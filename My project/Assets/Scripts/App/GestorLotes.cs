using System;
using System.Collections.Generic;
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
        public EstadoCampo Analisis() => AnalisisCampo.Analizar(LoteActual, svc.Reloj.UtcAhora);

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
        // Solo puede haber UNA simulación activa (es un único motor). Los lotes de Control Manual siguen activos
        // en paralelo (una por compostera física) hasta que se cosechan.
        void CerrarSimulacionesActivas(string exceptoId)
        {
            if (LoteActual != null && LoteActual.Modo == ModoLote.Simular && LoteActual.Id != exceptoId && LoteActual.Estado == EstadoLote.Activo)
            {
                LoteActual.Estado = EstadoLote.Cerrado;
                Guardar();
            }
            try
            {
                foreach (var l in svc.Lotes.Listar())
                    if (l.Modo == ModoLote.Simular && l.Estado == EstadoLote.Activo && l.Id != exceptoId && (LoteActual == null || l.Id != LoteActual.Id))
                    {
                        l.Estado = EstadoLote.Cerrado;
                        l.ActualizadoUtc = AhoraUtc.Ticks;
                        svc.Lotes.Guardar(l);
                    }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Notificador.Mostrar("No se pudo archivar la simulación anterior: " + e.Message, TipoMensaje.Aviso);
            }
        }

        public int SiguienteNumero()
        {
            int max = LoteActual?.Numero ?? 0;
            try { foreach (var l in svc.Lotes.Listar()) max = Math.Max(max, l.Numero); }
            catch (Exception e) { Debug.LogException(e); }
            return max + 1;
        }

        void CrearLoteSimulacion(SistemaComposta nuevo)
        {
            if (LoteActual != null && LoteActual.Modo == ModoLote.Manual) Guardar();     // la compostera manual sigue activa
            CerrarSimulacionesActivas(null);
            sistemaDelLote = nuevo;
            int numero = SiguienteNumero();
            LoteActual = Lote.Crear(nuevo.Perfil, sim.semilla, Usuario, svc.Reloj.UtcAhora, ModoLote.Simular);
            LoteActual.Numero = numero;
            // Estado inicial de los ajustes que afectan la simulación (necesario para reproducirla)
            Anotar(TipoComando.Automatico, sim.modoAutomatico ? 1f : 0f);
            Anotar(TipoComando.ProbFalla, sim.probFallaPorHora);
            Guardar();
            AlCambiarLote?.Invoke(LoteActual);
            Notificador.Mostrar($"Simulación iniciada: {LoteActual.Nombre}", TipoMensaje.Exito);
        }

        // Control Manual: sin simulador, sin datos sintéticos. El tablero queda "Esperando primer registro".
        // perfil.PesoKg = carga inicial; se registra como la primera aportación (material según el predominante elegido)
        public bool CrearLoteManual(PerfilComposta perfil, string compostera, out string error)
        {
            error = null;
            if (!svc.Sesion.Puede(Permiso.CrearLote)) { error = "Tu rol no permite crear lotes."; return false; }
            if (perfil == null) { error = "Falta el tipo de compostera."; return false; }
            string comp = EditorLote.LimpiarCompostera(compostera);
            error = EditorLote.ErrorCompostera(comp, ModoLote.Manual);
            if (error != null) return false;
            try
            {
                foreach (var otro in svc.Lotes.Listar())
                    if (otro.Estado == EstadoLote.Activo && otro.Modo == ModoLote.Manual &&
                        string.Equals(otro.Compostera, comp, StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"Ya hay un lote activo en «{comp}» ({otro.Etiqueta}). Cósechalo o usa otro nombre de compostera.";
                        return false;
                    }
            }
            catch (Exception e) { Debug.LogException(e); }

            var ahora = AhoraUtc;
            var nuevo = Lote.Crear(perfil, 0, Usuario, ahora, ModoLote.Manual);
            nuevo.Compostera = comp;
            nuevo.Numero = SiguienteNumero();
            nuevo.Nombre = $"{comp} · {perfil.Nombre}";
            var r = EditorLote.AgregarAporte(nuevo, perfil.PesoKg, Materiales.DesdeRelacionCN(perfil.RelacionCN), 0f, "Carga inicial",
                                             ahora, 0, Usuario, ahora, true);
            if (!r.Exito) { error = r.Mensaje; return false; }

            if (LoteActual != null) Guardar();          // el lote anterior (manual o simulación) queda como estaba
            var anterior = LoteActual;
            var sistemaAnterior = sistemaDelLote;
            LoteActual = nuevo;
            sistemaDelLote = null;
            if (!Guardar())
            {
                LoteActual = anterior;
                sistemaDelLote = sistemaAnterior;
                error = "No se pudo guardar el lote en disco.";
                return false;
            }
            sim.DetenerMotor();
            sim.modo = ModoLote.Manual;
            AlCambiarLote?.Invoke(LoteActual);
            Notificador.Mostrar($"{nuevo.Etiqueta} creado con {perfil.PesoKg:0.##} kg de carga inicial. Registra la primera medición.", TipoMensaje.Exito);
            return true;
        }

        public bool Reanudar(Lote lote, out string error)
        {
            error = null;
            if (!svc.Sesion.Activa) { error = "Inicia sesión para abrir un lote."; return false; }
            if (lote == null) { error = "No se encontró el lote."; return false; }
            if (LoteActual != null && LoteActual.Id != lote.Id) Guardar();      // no perder el avance del lote anterior
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
        // Fecha vacía = ahora. En una simulación la aportación entra en la hora simulada actual (la fecha no aplica).
        public bool RegistrarAporte(string kgTexto, MaterialEspecifico material, string humedadTexto, string fechaTexto, string nota, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.RegistrarAportes)) { mensaje = "Tu rol no permite registrar aportaciones."; return false; }
            if (LoteActual == null) { mensaje = "No hay un lote activo. Crea una composta primero."; return false; }
            if (!LeerAporte(kgTexto, humedadTexto, fechaTexto, null, out float kg, out float humedad, out DateTime fecha, out mensaje)) return false;
            int tick = TickActual;
            bool enSimulacion = sistemaDelLote != null && LoteActual.Modo == ModoLote.Simular;
            string idNuevo = null;
            bool ok = Modificar(l =>
            {
                var r = EditorLote.AgregarAporte(l, kg, material, humedad, nota, fecha, tick, Usuario, AhoraUtc);
                if (r.Exito && enSimulacion)
                    l.Comandos.Add(new Comando { Tick = tick, Tipo = TipoComando.Aporte, Valor = kg, Usuario = Usuario, FechaUtc = AhoraUtc.Ticks, Ref = r.IdEntidad });
                idNuevo = r.IdEntidad;
                return r;
            }, null, out mensaje);
            if (!ok) return false;
            if (enSimulacion) ReproductorLote.AplicarAporte(sistemaDelLote, LoteActual, idNuevo);   // mismo efecto que al reproducir
            AlCambiarAportes?.Invoke(LoteActual);
            AlCambiarRegistros?.Invoke(LoteActual);      // el balance y las recomendaciones dependen de las aportaciones
            return true;
        }

        public bool ActualizarAporte(string id, string kgTexto, MaterialEspecifico material, string humedadTexto, string fechaTexto, string nota, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite corregir aportaciones."; return false; }
            var actual = LoteActual?.Aportes?.Find(x => x != null && x.Id == id);
            if (actual == null) { mensaje = "La aportación ya no existe."; return false; }
            if (!LeerAporte(kgTexto, humedadTexto, fechaTexto, actual, out float kg, out float humedad, out DateTime fecha, out mensaje)) return false;
            if (!Modificar(l => EditorLote.EditarAporte(l, id, kg, material, humedad, nota, fecha, Usuario, AhoraUtc), null, out mensaje)) return false;
            DespuesDeCambiarAportes(ref mensaje);
            return true;
        }

        public bool EliminarAporte(string id, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite eliminar aportaciones."; return false; }
            if (!Modificar(l => EditorLote.EliminarAporte(l, id, Usuario, AhoraUtc), null, out mensaje)) return false;
            DespuesDeCambiarAportes(ref mensaje);
            return true;
        }

        // Una aportación corregida o eliminada cambia el pasado de una simulación: se recalcula desde la bitácora
        void DespuesDeCambiarAportes(ref string mensaje)
        {
            if (sistemaDelLote != null && LoteActual.Modo == ModoLote.Simular)
            {
                try
                {
                    var s = ReproductorLote.Reconstruir(LoteActual, out var ultimo);
                    sim.CargarSistema(s, ultimo, LoteActual.Semilla);
                    sistemaDelLote = s;
                    mensaje += " La simulación se recalculó con el cambio.";
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    mensaje += " No se pudo recalcular la simulación: " + e.Message;
                }
            }
            AlCambiarAportes?.Invoke(LoteActual);
            AlCambiarRegistros?.Invoke(LoteActual);
        }

        bool LeerAporte(string kgTexto, string humedadTexto, string fechaTexto, Aporte existente,
                        out float kg, out float humedad, out DateTime fecha, out string error)
        {
            humedad = 0f; fecha = AhoraUtc;
            if (!ValidadorAporte.IntentarLeerKg(kgTexto, out kg, out error)) return false;
            if (!Materiales.IntentarLeerHumedad(humedadTexto, out humedad, out error)) return false;
            bool simulacion = LoteActual != null && LoteActual.Modo == ModoLote.Simular;
            if (simulacion) { fecha = existente != null ? new DateTime(existente.FechaUtc, DateTimeKind.Utc) : AhoraUtc; return true; }
            string t = (fechaTexto ?? "").Trim();
            if (existente != null && t == ValidadorFechaMedicion.Formatear(existente.FechaUtc, Zona))
            {
                fecha = new DateTime(existente.FechaUtc, DateTimeKind.Utc);      // sin tocar: se conserva exacta
                return true;
            }
            return ValidadorFechaMedicion.Validar(t, Zona, AhoraUtc, out fecha, out error);
        }

        // ---------------- Datos del lote ----------------
        public bool ActualizarLote(string nombre, string compostera, TipoComposta tipo, float relacionCN, float aislamiento, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite modificar el lote."; return false; }
            return Modificar(l => EditorLote.EditarLote(l, nombre, compostera, tipo, relacionCN, aislamiento, Usuario, AhoraUtc), AlCambiarLote, out mensaje);
        }

        public bool BloquearMaduracion(bool bloquear, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite cambiar la fase del lote."; return false; }
            return Modificar(l => EditorLote.BloquearMaduracion(l, bloquear, Usuario, AhoraUtc), AlCambiarLote, out mensaje);
        }

        // Al cosechar, el lote sale del tablero: queda en el historial con su rendimiento y calidad
        public bool RegistrarCosecha(string pesoTexto, string fechaTexto, bool olorTierra, bool sinRestos, bool texturaSuelta, string nota,
                                     out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite registrar la cosecha."; return false; }
            if (LoteActual == null) { mensaje = "No hay un lote abierto."; return false; }
            if (!ValidadorAporte.IntentarLeerKg(pesoTexto, out float kg, out mensaje)) { mensaje = "Peso cosechado: " + mensaje; return false; }
            if (!ValidadorFechaMedicion.Validar(fechaTexto, Zona, AhoraUtc, out DateTime fecha, out mensaje)) return false;
            if (!Modificar(l => EditorLote.RegistrarCosecha(l, kg, fecha, olorTierra, sinRestos, texturaSuelta, nota, Usuario, AhoraUtc), null, out mensaje))
                return false;
            LoteActual = null;
            sistemaDelLote = null;
            AlCambiarLote?.Invoke(null);
            return true;
        }

        // Operaciones sobre un lote que NO está abierto (desde el historial): se guarda directamente
        public bool AnularCosecha(Lote lote, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.EditarRegistros)) { mensaje = "Tu rol no permite anular cosechas."; return false; }
            return ModificarGuardado(lote, l => EditorLote.AnularCosecha(l, Usuario, AhoraUtc), out mensaje);
        }

        bool ModificarGuardado(Lote lote, Func<Lote, ResultadoEdicion> operacion, out string mensaje)
        {
            if (lote == null) { mensaje = "No se encontró el lote."; return false; }
            if (LoteActual != null && LoteActual.Id == lote.Id) return Modificar(operacion, AlCambiarLote, out mensaje);
            var copia = EditorLote.Clonar(lote);
            ResultadoEdicion r;
            try { r = operacion(copia); }
            catch (Exception e) { Debug.LogException(e); mensaje = "Error inesperado: " + e.Message; return false; }
            mensaje = r.Mensaje;
            if (!r.Exito || r.SinCambios) return r.Exito;
            try { copia.ActualizadoUtc = AhoraUtc.Ticks; svc.Lotes.Guardar(copia); }
            catch (Exception e) { Debug.LogException(e); mensaje = "No se pudo guardar en disco: " + e.Message; return false; }
            return true;
        }

        // ---------------- Historial y datos de demostración ----------------
        // Todos los lotes guardados (el abierto, con su estado en memoria). Numera los lotes anteriores a la v1.1.2.
        public List<Lote> ListarLotes(out string aviso)
        {
            aviso = null;
            var lista = new List<Lote>();
            try
            {
                Guardar();
                foreach (var l in svc.Lotes.Listar())
                {
                    if (LoteActual != null && l.Id == LoteActual.Id) { lista.Add(LoteActual); continue; }
                    ReproductorLote.Sanear(l);
                    lista.Add(l);
                }
                var repo = svc.Lotes as Core.Persistencia.RepositorioLotesJson;
                if (repo != null && repo.ErroresDeLectura.Count > 0)
                    aviso = $"{repo.ErroresDeLectura.Count} archivo(s) de lote dañados no se pudieron leer.";
                AsegurarNumeros(lista);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                aviso = "No se pudieron leer los lotes guardados: " + e.Message;
            }
            return lista;
        }

        void AsegurarNumeros(List<Lote> lista)
        {
            int max = 0;
            foreach (var l in lista) max = Math.Max(max, l.Numero);
            var sinNumero = lista.FindAll(l => l.Numero <= 0);
            sinNumero.Sort((a, b) => a.CreadoUtc.CompareTo(b.CreadoUtc));
            foreach (var l in sinNumero)
            {
                l.Numero = ++max;
                try { svc.Lotes.Guardar(l); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public bool HayDemo(List<Lote> lotes) => lotes != null && lotes.Exists(l => l.Demo);

        public bool CargarDemo(out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.CrearLote)) { mensaje = "Tu rol no permite crear lotes."; return false; }
            var existentes = ListarLotes(out _);
            if (HayDemo(existentes)) { mensaje = "Los datos de demostración ya están cargados."; return false; }
            int max = 0;
            foreach (var l in existentes) max = Math.Max(max, l.Numero);
            try
            {
                var demo = DatosDemo.Generar(AhoraUtc, max + 1);
                foreach (var l in demo) svc.Lotes.Guardar(l);
                mensaje = $"Se cargaron {demo.Count} lotes de demostración (3 activos y 4 cosechados), marcados como DEMO.";
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mensaje = "No se pudieron crear los datos de demostración: " + e.Message;
                return false;
            }
        }

        public bool QuitarDemo(out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.CrearLote)) { mensaje = "Tu rol no permite quitar lotes."; return false; }
            int n = 0;
            try
            {
                foreach (var l in ListarLotes(out _))
                    if (l.Demo) { svc.Lotes.Eliminar(l.Id, AhoraUtc); n++; }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                mensaje = "No se pudieron quitar todos los datos de demostración: " + e.Message;
                return false;
            }
            if (LoteActual != null && LoteActual.Demo)
            {
                LoteActual = null;
                sistemaDelLote = null;
                AlCambiarLote?.Invoke(null);
            }
            mensaje = n == 0 ? "No había datos de demostración." : $"Se quitaron {n} lotes de demostración.";
            return true;
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
            if (LoteActual == null) { error = "No hay un lote para exportar."; return false; }
            var r = new ResultadoExportacion();
            if (!Guardar()) r.Avisos.Add("No se pudo guardar el lote antes de exportar: los archivos reflejan lo que ves en pantalla.");
            return ExportarLote(LoteActual, sistemaDelLote?.Historial, r, out resultado, out error);
        }

        // Cualquier lote del historial (no hace falta abrirlo). En una simulación se reconstruye su historial.
        public bool ExportarLote(Lote lote, out ResultadoExportacion resultado, out string error)
        {
            resultado = null; error = null;
            if (lote == null) { error = "No se encontró el lote."; return false; }
            if (LoteActual != null && lote.Id == LoteActual.Id) return Exportar(out resultado, out error);
            Historial historial = null;
            var r = new ResultadoExportacion();
            if (lote.Modo == ModoLote.Simular)
            {
                try
                {
                    var copia = EditorLote.Clonar(lote);
                    ReproductorLote.Sanear(copia);
                    historial = ReproductorLote.Reconstruir(copia, out _).Historial;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    r.Avisos.Add("No se pudo reconstruir el historial de la simulación: la hoja «Historial» va vacía.");
                }
            }
            return ExportarLote(lote, historial, r, out resultado, out error);
        }

        bool ExportarLote(Lote lote, Historial historial, ResultadoExportacion r, out ResultadoExportacion resultado, out string error)
        {
            resultado = null; error = null;
            if (!svc.Sesion.Puede(Permiso.Exportar)) { error = "Tu rol no permite exportar."; return false; }
            try
            {
                var ahora = AhoraUtc;
                string sello = Sello(ahora);
                string slug = Core.Exportacion.NombreArchivo.Slug((lote.Numero > 0 ? "lote-" + lote.Numero.ToString("000", CultureInfo.InvariantCulture) + " " : "") +
                                                                   (string.IsNullOrEmpty(lote.Compostera) ? lote.Nombre : lote.Compostera), 40);
                string carpetaClave = $"exportaciones/{sello}_{slug}/";
                r.NombreExcel = $"{slug}_{sello}.xlsx";

                var csv = new List<(string nombre, byte[] datos)>
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
                if (lote.NumeroAportaciones == 0)
                    r.Avisos.Add("No hay aportaciones posteriores a la carga inicial.");
                if (lote.Demo) r.Avisos.Add("Es un lote de DEMOSTRACIÓN: el archivo lo indica en el resumen.");

                svc.Almacen.EscribirBytes(carpetaClave + r.NombreExcel, excel);
                foreach (var (nombre, datos) in csv) svc.Almacen.EscribirBytes(carpetaClave + nombre, datos);
                r.RutaExcel = svc.Almacen.RutaAbsoluta(carpetaClave + r.NombreExcel);
                r.Carpeta = System.IO.Path.GetDirectoryName(r.RutaExcel);
                r.Hojas = lote.Modo == ModoLote.Manual ? "Resumen, Mediciones, Aportes y Cambios" : "Resumen, Bitácora, Historial, Aportes y Cambios";

                if (DescargasAndroid.Disponible) CopiarADescargas(r, excel, csv, $"Composta/CSV/{sello}_{slug}");
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

        // Bitácora consolidada: todos los lotes (activos, cosechados, cerrados) en un solo libro de Excel
        public bool ExportarConsolidado(out ResultadoExportacion resultado, out string error)
        {
            resultado = null; error = null;
            if (!svc.Sesion.Puede(Permiso.Exportar)) { error = "Tu rol no permite exportar."; return false; }
            try
            {
                var lotes = ListarLotes(out string aviso);
                var r = new ResultadoExportacion();
                if (aviso != null) r.Avisos.Add(aviso);
                if (lotes.Count == 0) r.Avisos.Add("No hay lotes guardados: el libro solo tiene los encabezados.");
                if (HayDemo(lotes)) r.Avisos.Add("Incluye lotes de DEMOSTRACIÓN (columna «Demo» = Sí).");
                var ahora = AhoraUtc;
                string sello = Sello(ahora);
                string clave = $"exportaciones/{sello}_consolidado/bitacora-consolidada_{sello}.xlsx";
                byte[] excel = Core.Exportacion.ExportadorExcel.GenerarConsolidado(lotes, Zona, ahora, Application.version);
                svc.Almacen.EscribirBytes(clave, excel);
                r.NombreExcel = $"bitacora-consolidada_{sello}.xlsx";
                r.RutaExcel = svc.Almacen.RutaAbsoluta(clave);
                r.Carpeta = System.IO.Path.GetDirectoryName(r.RutaExcel);
                r.Hojas = $"Lotes ({lotes.Count}), Mediciones, Aportes y Cambios";
                if (DescargasAndroid.Disponible) CopiarADescargas(r, excel, null, null);
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

        // Android: el libro en Descargas/Composta y los CSV en una subcarpeta, visibles en la app Archivos
        static void CopiarADescargas(ResultadoExportacion r, byte[] excel, List<(string nombre, byte[] datos)> csv, string subCsv)
        {
            if (!DescargasAndroid.Guardar("Composta", r.NombreExcel, Core.Exportacion.ExportadorExcel.TipoMime, excel, out string errDesc))
            {
                r.AvisoDescargas = "No se pudo copiar a Descargas (" + errDesc + "). Los archivos quedaron en la carpeta interna de la app.";
                return;
            }
            if (csv != null)
                foreach (var (nombre, datos) in csv)
                    if (!DescargasAndroid.Guardar(subCsv, nombre, "text/csv", datos, out errDesc))
                    {
                        r.AvisoDescargas = "El libro de Excel está en Descargas, pero los CSV no se pudieron copiar: " + errDesc;
                        break;
                    }
            r.Carpeta = "Descargas › Composta";
            r.EnDescargas = true;
        }

        string Sello(DateTime ahora)
        {
            DateTime local;
            try { local = TimeZoneInfo.ConvertTimeFromUtc(ahora, Zona); }
            catch (ArgumentException) { local = ahora; }
            return local.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }
    }

    public sealed class ResultadoExportacion
    {
        public string Carpeta;            // ruta en PC, o "Descargas › Composta" en Android
        public string RutaExcel;          // copia interna del .xlsx (en PC se puede abrir directamente)
        public string NombreExcel;
        public string Hojas = "";
        public bool EnDescargas;          // Android: el libro quedó en la carpeta pública Descargas
        public string AvisoDescargas;     // Android: algo no se pudo copiar a Descargas
        public readonly System.Collections.Generic.List<string> Avisos = new System.Collections.Generic.List<string>();
    }
}
