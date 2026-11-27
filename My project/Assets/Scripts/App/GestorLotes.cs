using System;
using System.Globalization;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Seguridad;
using UnityEngine;

namespace Composta.App
{
    // Persistencia y ciclo de vida de los lotes. Dos flujos separados:
    //  · SIMULACIÓN: el lote nace del simulador (AlReiniciar), anota cada acción humana en la bitácora,
    //    se autoguarda y se reanuda reproduciendo la bitácora.
    //  · CONTROL MANUAL: el lote NO usa el simulador. Solo guarda mediciones reales (registros de campo);
    //    el motor queda detenido mientras este lote está abierto.
    public sealed class GestorLotes : IDisposable
    {
        const int AutoguardadoCadaTicks = 24;      // una vez por día simulado
        const float SegundosEntreErrores = 30f;

        readonly SimuladorComposta sim;
        readonly ServiciosApp svc;
        SistemaComposta sistemaDelLote;            // solo en lotes de Simulación
        float ultimoError = -999f;

        public Lote LoteActual { get; private set; }
        public event Action<Lote> AlCambiarLote;
        public event Action<Lote> AlCambiarAportes;
        public event Action<Lote> AlCambiarRegistros;

        public bool EsLoteManual => LoteActual != null && LoteActual.Modo == ModoLote.Manual;
        public SituacionInforme Situacion() => PlanInformes.Evaluar(LoteActual, svc.Reloj.UtcAhora);
        public EstadoCampo Analisis() => AnalisisCampo.Analizar(LoteActual);

        public GestorLotes(SimuladorComposta simulador, ServiciosApp servicios)
        {
            sim = simulador ? simulador : throw new ArgumentNullException(nameof(simulador));
            svc = servicios ?? throw new ArgumentNullException(nameof(servicios));
            sim.AlReiniciar += CrearLoteSimulacion;
            sim.AlComando += RegistrarComando;
            sim.AlTick += AlTick;
        }

        public void Dispose()
        {
            if (sim == null) return;
            sim.AlReiniciar -= CrearLoteSimulacion;
            sim.AlComando -= RegistrarComando;
            sim.AlTick -= AlTick;
        }

        string Usuario => svc.Sesion.NombreActual;
        int TickActual => sistemaDelLote?.Modelo.Ticks ?? 0;

        // ---------------- Ciclo de vida ----------------
        void CerrarLoteActual()
        {
            if (LoteActual == null) return;
            LoteActual.Estado = EstadoLote.Cerrado;
            Guardar();
        }

        void CrearLoteSimulacion(SistemaComposta nuevo)
        {
            CerrarLoteActual();
            sistemaDelLote = nuevo;
            LoteActual = Lote.Crear(nuevo.Perfil, sim.semilla, Usuario, svc.Reloj.UtcAhora, ModoLote.Simular);
            // Estado inicial de los ajustes que afectan la simulación (necesario para reproducirla)
            Anotar(TipoComando.Automatico, sim.modoAutomatico ? 1f : 0f);
            Anotar(TipoComando.ProbFalla, sim.probFallaPorHora);
            Guardar();
            AlCambiarLote?.Invoke(LoteActual);
            Notificador.Mostrar($"Simulación iniciada: {LoteActual.Nombre}", TipoMensaje.Exito);
        }

        // Control Manual: sin simulador, sin datos sintéticos. El tablero queda "Esperando primer registro".
        public bool CrearLoteManual(PerfilComposta perfil, out string error)
        {
            error = null;
            if (!svc.Sesion.Puede(Permiso.CrearLote)) { error = "Tu rol no permite crear lotes."; return false; }
            if (perfil == null) { error = "Falta el tipo de compostera."; return false; }
            CerrarLoteActual();
            sim.DetenerMotor();
            sim.modo = ModoLote.Manual;
            sistemaDelLote = null;
            LoteActual = Lote.Crear(perfil, 0, Usuario, svc.Reloj.UtcAhora, ModoLote.Manual);
            LoteActual.Nombre = $"{perfil.Nombre} · control manual · {svc.Reloj.UtcAhora.ToLocalTime():yyyy-MM-dd HH:mm}";
            if (!Guardar()) { error = "No se pudo guardar el lote en disco."; return false; }
            AlCambiarLote?.Invoke(LoteActual);
            Notificador.Mostrar("Lote de control manual creado. Registra la primera medición.", TipoMensaje.Exito);
            return true;
        }

        public bool Reanudar(Lote lote, out string error)
        {
            error = null;
            if (!svc.Sesion.Activa) { error = "Inicia sesión para abrir un lote."; return false; }
            try
            {
                var avisos = ReproductorLote.Sanear(lote);
                if (lote.Modo == ModoLote.Manual)
                {
                    // Nada que reproducir: los datos son las mediciones guardadas
                    sim.DetenerMotor();
                    sim.modo = ModoLote.Manual;
                    sistemaDelLote = null;
                }
                else
                {
                    var s = ReproductorLote.Reconstruir(lote, out var ultimo);
                    sim.modo = ModoLote.Simular;          // antes de CargarSistema: los oyentes de AlCargar lo leen
                    sim.CargarSistema(s, ultimo, lote.Semilla);
                    sistemaDelLote = s;
                }
                LoteActual = lote;
                if (avisos.Count > 0)
                {
                    Debug.LogWarning($"Lote {lote.Id} saneado al cargar: {string.Join("; ", avisos)}");
                    Notificador.Mostrar("El lote tenía datos inconsistentes y se corrigieron al cargarlo.", TipoMensaje.Aviso);
                }
                AlCambiarLote?.Invoke(LoteActual);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
                return false;
            }
        }

        // ---------------- Bitácora de la simulación ----------------
        void RegistrarComando(TipoComando tipo, float valor)
        {
            if (LoteActual == null || sistemaDelLote == null || sim.Sistema != sistemaDelLote) return;
            Anotar(tipo, valor);
            if (tipo != TipoComando.ProbFalla) Guardar();   // ProbFalla puede cambiar muchas veces desde el Inspector
        }

        void Anotar(TipoComando tipo, float valor)
        {
            LoteActual.Comandos.Add(new Comando
            {
                Tick = sistemaDelLote.Modelo.Ticks,
                Tipo = tipo,
                Valor = valor,
                Usuario = Usuario,
                FechaUtc = svc.Reloj.UtcAhora.Ticks
            });
        }

        void AlTick(Lectura l)
        {
            if (LoteActual != null && sistemaDelLote != null && l.Tick % AutoguardadoCadaTicks == 0) Guardar();
        }

        // ---------------- Control Manual: mediciones reales ----------------
        // Zona horaria del dispositivo: las fechas escritas a mano se interpretan en hora local
        public TimeZoneInfo Zona => TimeZoneInfo.Local;
        public DateTime AhoraUtc => svc.Reloj.UtcAhora;
        public bool Puede(Permiso p) => svc.Sesion.Puede(p);

        public bool RegistrarMedicion(float temperatura, float humedad, float pesoKg, string nota, DateTime fechaUtc, out string error)
        {
            if (!svc.Sesion.Puede(Permiso.RegistrarInformes)) { error = "Tu rol no permite registrar mediciones."; return false; }
            return Modificar(l => EditorLote.AgregarMedicion(l, temperatura, humedad, pesoKg, nota, fechaUtc, Usuario, AhoraUtc),
                             AlCambiarRegistros, out error);
        }

        public bool ActualizarMedicion(string id, float temperatura, float humedad, float pesoKg, string nota, DateTime fechaUtc, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite corregir mediciones."; return false; }
            return Modificar(l => EditorLote.EditarMedicion(l, id, temperatura, humedad, pesoKg, nota, fechaUtc, Usuario, AhoraUtc),
                             AlCambiarRegistros, out mensaje);
        }

        public bool EliminarMedicion(string id, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite eliminar mediciones."; return false; }
            return Modificar(l => EditorLote.EliminarMedicion(l, id, Usuario, AhoraUtc), AlCambiarRegistros, out mensaje);
        }

        // ---------------- Aportes de residuos (ambos modos) ----------------
        public bool RegistrarAporte(string kgTexto, TipoMaterial material, string nota, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.RegistrarAportes)) { mensaje = "Tu rol no permite registrar aportes."; return false; }
            if (LoteActual == null) { mensaje = "No hay un lote activo. Crea una composta primero."; return false; }
            if (!ValidadorAporte.IntentarLeerKg(kgTexto, out float kg, out mensaje)) return false;
            int tick = TickActual;
            return Modificar(l => EditorLote.AgregarAporte(l, kg, material, nota, tick, Usuario, AhoraUtc), AlCambiarAportes, out mensaje);
        }

        public bool ActualizarAporte(string id, string kgTexto, TipoMaterial material, string nota, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite corregir aportes."; return false; }
            if (!ValidadorAporte.IntentarLeerKg(kgTexto, out float kg, out mensaje)) return false;
            return Modificar(l => EditorLote.EditarAporte(l, id, kg, material, nota, Usuario, AhoraUtc), AlCambiarAportes, out mensaje);
        }

        public bool EliminarAporte(string id, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite eliminar aportes."; return false; }
            return Modificar(l => EditorLote.EliminarAporte(l, id, Usuario, AhoraUtc), AlCambiarAportes, out mensaje);
        }

        // ---------------- Datos del lote ----------------
        public bool ActualizarLote(string nombre, TipoComposta tipo, float relacionCN, float aislamiento, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite modificar el lote."; return false; }
            return Modificar(l => EditorLote.EditarLote(l, nombre, tipo, relacionCN, aislamiento, Usuario, AhoraUtc), AlCambiarLote, out mensaje);
        }

        // Manda el lote abierto a la papelera de la carpeta de datos (recuperable copiando el archivo de vuelta).
        // Antes se guarda con la anotación de quién lo eliminó, para que la copia en la papelera lo diga.
        public bool EliminarLoteActual(out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EliminarLotes)) { mensaje = "Solo administración puede eliminar lotes."; return false; }
            if (LoteActual == null) { mensaje = "No hay un lote abierto."; return false; }
            var respaldo = EditorLote.Clonar(LoteActual);
            string nombre = LoteActual.Nombre;
            try
            {
                if (sistemaDelLote != null) LoteActual.TicksSimulados = sistemaDelLote.Modelo.Ticks;
                EditorLote.AnotarEliminacionLote(LoteActual, Usuario, AhoraUtc);
                svc.Lotes.Guardar(LoteActual);
                svc.Lotes.Eliminar(LoteActual.Id, AhoraUtc);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                LoteActual = respaldo;
                mensaje = "No se pudo eliminar el lote: " + e.Message;
                return false;
            }
            sim.DetenerMotor();
            sistemaDelLote = null;
            LoteActual = null;
            AlCambiarLote?.Invoke(null);
            mensaje = $"Lote «{nombre}» eliminado. Se guardó una copia en la papelera de la carpeta de datos.";
            return true;
        }

        // Aplica una modificación de forma transaccional: si no se puede guardar en disco, el lote vuelve
        // exactamente a como estaba (nunca se muestra como guardado algo que no llegó al archivo).
        bool Modificar(Func<Lote, ResultadoEdicion> operacion, Action<Lote> alTerminar, out string mensaje)
        {
            if (LoteActual == null) { mensaje = "No hay un lote abierto."; return false; }
            var respaldo = EditorLote.Clonar(LoteActual);
            ResultadoEdicion r;
            try { r = operacion(LoteActual); }
            catch (Exception e)
            {
                Debug.LogException(e);
                LoteActual = respaldo;
                mensaje = "Error inesperado: " + e.Message;
                return false;
            }
            mensaje = r.Mensaje;
            if (!r.Exito) { LoteActual = respaldo; return false; }
            if (r.SinCambios) return true;
            if (!Guardar())
            {
                LoteActual = respaldo;
                mensaje = "No se pudo guardar en disco. Revisa el espacio o los permisos; no se aplicó ningún cambio.";
                return false;
            }
            alTerminar?.Invoke(LoteActual);
            return true;
        }

        // ---------------- Persistencia ----------------
        public bool Guardar()
        {
            if (LoteActual == null) return true;
            try
            {
                // Simulación: siempre la hora actual, así ninguna acción queda "en el futuro" respecto a lo guardado
                if (sistemaDelLote != null) LoteActual.TicksSimulados = sistemaDelLote.Modelo.Ticks;
                LoteActual.ActualizadoUtc = svc.Reloj.UtcAhora.Ticks;
                svc.Lotes.Guardar(LoteActual);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Time.unscaledTime - ultimoError > SegundosEntreErrores)
                {
                    ultimoError = Time.unscaledTime;
                    Notificador.Mostrar("No se pudo guardar el lote: " + e.Message, TipoMensaje.Error);
                }
                return false;
            }
        }

        // Exporta el lote abierto: un libro de Excel (.xlsx) para personas y los CSV para otros programas.
        // Nunca falla por "no hay datos": un lote vacío se exporta con sus encabezados y se avisa.
        public bool Exportar(out ResultadoExportacion resultado, out string error)
        {
            resultado = null; error = null;
            if (!svc.Sesion.Puede(Permiso.Exportar)) { error = "Tu rol no permite exportar."; return false; }
            if (LoteActual == null) { error = "No hay un lote para exportar."; return false; }
            try
            {
                var r = new ResultadoExportacion();
                if (!Guardar()) r.Avisos.Add("No se pudo guardar el lote antes de exportar: los archivos reflejan lo que ves en pantalla.");
                var lote = LoteActual;
                var historial = sistemaDelLote?.Historial;
                var ahora = AhoraUtc;
                DateTime ahoraLocal;
                try { ahoraLocal = TimeZoneInfo.ConvertTimeFromUtc(ahora, Zona); }
                catch (ArgumentException) { ahoraLocal = ahora; }
                string sello = ahoraLocal.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string slug = Core.Exportacion.NombreArchivo.Slug(lote.Nombre, 40);
                string carpetaClave = $"exportaciones/{sello}_{slug}/";
                r.NombreExcel = $"{slug}_{sello}.xlsx";

                var csv = new System.Collections.Generic.List<(string nombre, byte[] datos)>
                {
                    ("resumen.csv", ExportadorCsv.ABytes(ExportadorCsv.Resumen(lote))),
                    ("aportes.csv", ExportadorCsv.ABytes(ExportadorCsv.Aportes(lote))),
                    ("cambios.csv", ExportadorCsv.ABytes(ExportadorCsv.Cambios(lote)))
                };
                if (lote.Modo == ModoLote.Manual) csv.Add(("mediciones.csv", ExportadorCsv.ABytes(ExportadorCsv.Mediciones(lote))));
                else
                {
                    csv.Add(("bitacora.csv", ExportadorCsv.ABytes(ExportadorCsv.Bitacora(lote))));
                    csv.Add(("historial.csv", ExportadorCsv.ABytes(historial != null ? historial.ToCsv() : "")));
                }
                byte[] excel = Core.Exportacion.ExportadorExcel.Generar(lote, historial, Zona, ahora, Application.version);

                // Lotes sin datos: se exportan igual (con encabezados), pero se dice claramente
                if (lote.Modo == ModoLote.Manual && (lote.Informes == null || lote.Informes.Count == 0))
                    r.Avisos.Add("El lote todavía no tiene mediciones: la hoja «Mediciones» solo tiene los encabezados.");
                if (lote.Modo != ModoLote.Manual && (historial == null || historial.Lecturas.Count == 0))
                    r.Avisos.Add("La simulación aún no ha avanzado: la hoja «Historial» está vacía.");
                if (lote.Aportes == null || lote.Aportes.Count == 0)
                    r.Avisos.Add("No hay aportes registrados: la hoja «Aportes» solo tiene los encabezados.");

                // Copia interna (respaldo de la app; en PC es la carpeta que se abre)
                svc.Almacen.EscribirBytes(carpetaClave + r.NombreExcel, excel);
                foreach (var (nombre, datos) in csv) svc.Almacen.EscribirBytes(carpetaClave + nombre, datos);
                r.RutaExcel = svc.Almacen.RutaAbsoluta(carpetaClave + r.NombreExcel);
                r.Carpeta = System.IO.Path.GetDirectoryName(r.RutaExcel);

                // Android: el libro en Descargas/Composta y los CSV en una subcarpeta, visibles en la app Archivos
                if (DescargasAndroid.Disponible)
                {
                    if (!DescargasAndroid.Guardar("Composta", r.NombreExcel, Core.Exportacion.ExportadorExcel.TipoMime, excel, out string errDesc))
                        r.AvisoDescargas = "No se pudo copiar a Descargas (" + errDesc + "). Los archivos quedaron en la carpeta interna de la app.";
                    else
                    {
                        string sub = $"Composta/CSV/{sello}_{slug}";
                        foreach (var (nombre, datos) in csv)
                            if (!DescargasAndroid.Guardar(sub, nombre, "text/csv", datos, out errDesc))
                            {
                                r.AvisoDescargas = "El libro de Excel está en Descargas, pero los CSV no se pudieron copiar: " + errDesc;
                                break;
                            }
                        r.Carpeta = "Descargas › Composta";
                        r.EnDescargas = true;
                    }
                }
                resultado = r;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                error = e.Message;
                return false;
            }
        }
    }

    public sealed class ResultadoExportacion
    {
        public string Carpeta;            // ruta en PC, o "Descargas › Composta" en Android
        public string RutaExcel;          // copia interna del .xlsx (en PC se puede abrir directamente)
        public string NombreExcel;
        public bool EnDescargas;          // Android: el libro quedó en la carpeta pública Descargas
        public string AvisoDescargas;     // Android: algo no se pudo copiar a Descargas
        public readonly System.Collections.Generic.List<string> Avisos = new System.Collections.Generic.List<string>();
    }
}
