using System;
using System.Globalization;
using Composta.Core;
using Composta.Core.Lotes;
using Composta.Core.Seguridad;
using UnityEngine;

namespace Composta.App
{
    // Une el simulador con la persistencia: crea el lote al iniciar una composta, anota en la
    // bitácora cada acción humana (con usuario y hora), guarda automáticamente y permite reanudar.
    public sealed class GestorLotes : IDisposable
    {
        const int AutoguardadoCadaTicks = 24;      // una vez por día simulado
        const float SegundosEntreErrores = 30f;

        readonly SimuladorComposta sim;
        readonly ServiciosApp svc;
        SistemaComposta sistemaDelLote;            // el Sistema al que corresponde LoteActual
        float ultimoError = -999f;

        public Lote LoteActual { get; private set; }
        public event Action<Lote> AlCambiarLote;
        public event Action<Lote> AlCambiarAportes;

        public GestorLotes(SimuladorComposta simulador, ServiciosApp servicios)
        {
            sim = simulador ? simulador : throw new ArgumentNullException(nameof(simulador));
            svc = servicios ?? throw new ArgumentNullException(nameof(servicios));
            sim.AlReiniciar += CrearLoteNuevo;
            sim.AlComando += RegistrarComando;
            sim.AlTick += AlTick;
        }

        public void Dispose()
        {
            if (sim == null) return;
            sim.AlReiniciar -= CrearLoteNuevo;
            sim.AlComando -= RegistrarComando;
            sim.AlTick -= AlTick;
        }

        string Usuario => svc.Sesion.NombreActual;

        // ---------------- Ciclo de vida del lote ----------------
        void CrearLoteNuevo(SistemaComposta nuevo)
        {
            if (LoteActual != null)
            {
                LoteActual.Estado = EstadoLote.Cerrado;
                Guardar();
            }

            sistemaDelLote = nuevo;
            LoteActual = Lote.Crear(nuevo.Perfil, sim.semilla, Usuario, svc.Reloj.UtcAhora);
            // Estado inicial de los ajustes que afectan la simulación (necesario para reproducirla)
            Anotar(TipoComando.Automatico, sim.modoAutomatico ? 1f : 0f);
            Anotar(TipoComando.ProbFalla, sim.probFallaPorHora);
            Guardar();
            AlCambiarLote?.Invoke(LoteActual);
            Notificador.Mostrar($"Lote creado: {LoteActual.Nombre}", TipoMensaje.Exito);
        }

        public bool Reanudar(Lote lote, out string error)
        {
            error = null;
            try
            {
                var avisos = ReproductorLote.Sanear(lote);
                var s = ReproductorLote.Reconstruir(lote, out var ultimo);
                sim.CargarSistema(s, ultimo, lote.Semilla);
                sistemaDelLote = s;
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

        // ---------------- Bitácora ----------------
        void RegistrarComando(TipoComando tipo, float valor)
        {
            if (LoteActual == null || sim.Sistema != sistemaDelLote) return;
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
            if (LoteActual != null && l.Tick % AutoguardadoCadaTicks == 0) Guardar();
        }

        // ---------------- Aportes de residuos ----------------
        public bool RegistrarAporte(string kgTexto, TipoMaterial material, string nota, out string mensaje)
        {
            if (!svc.Sesion.Puede(Permiso.RegistrarAportes)) { mensaje = "Tu rol no permite registrar aportes."; return false; }
            if (LoteActual == null) { mensaje = "No hay un lote activo. Crea una composta primero."; return false; }
            if (LoteActual.Estado != EstadoLote.Activo) { mensaje = "El lote está cerrado."; return false; }
            if (!Enum.IsDefined(typeof(TipoMaterial), material)) { mensaje = "Tipo de material no válido."; return false; }
            if (!ValidadorAporte.IntentarLeerKg(kgTexto, out float kg, out mensaje)) return false;

            var aporte = new Aporte
            {
                Id = Guid.NewGuid().ToString("N"),
                FechaUtc = svc.Reloj.UtcAhora.Ticks,
                Tick = sistemaDelLote.Modelo.Ticks,
                Kg = kg,
                Material = material,
                Nota = ValidadorAporte.LimpiarNota(nota),
                Usuario = Usuario
            };
            LoteActual.Aportes.Add(aporte);
            if (!Guardar())
            {
                LoteActual.Aportes.Remove(aporte);     // no mostrar como guardado algo que no llegó a disco
                mensaje = "No se pudo guardar el aporte en disco. Revisa el espacio o los permisos.";
                return false;
            }
            mensaje = string.Format(CultureInfo.InvariantCulture, "Aporte registrado: {0:0.##} kg ({1}).", kg, ValidadorAporte.Nombre(material));
            AlCambiarAportes?.Invoke(LoteActual);
            return true;
        }

        // ---------------- Persistencia ----------------
        public bool Guardar()
        {
            if (LoteActual == null || sistemaDelLote == null) return true;
            try
            {
                // Siempre la hora actual: así ninguna acción queda "en el futuro" respecto a lo guardado
                LoteActual.TicksSimulados = sistemaDelLote.Modelo.Ticks;
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

        // Exporta resumen, bitácora, aportes e historial en una carpeta con fecha
        public bool Exportar(out string carpeta, out string error)
        {
            carpeta = null; error = null;
            if (!svc.Sesion.Puede(Permiso.Exportar)) { error = "Tu rol no permite exportar."; return false; }
            if (LoteActual == null) { error = "No hay un lote para exportar."; return false; }
            try
            {
                Guardar();
                string sello = svc.Reloj.UtcAhora.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string baseClave = $"exportaciones/{LoteActual.Id}_{sello}/";
                svc.Almacen.Escribir(baseClave + "resumen.csv", ExportadorCsv.Resumen(LoteActual));
                svc.Almacen.Escribir(baseClave + "bitacora.csv", ExportadorCsv.Bitacora(LoteActual));
                svc.Almacen.Escribir(baseClave + "aportes.csv", ExportadorCsv.Aportes(LoteActual));
                svc.Almacen.Escribir(baseClave + "historial.csv", sistemaDelLote.Historial.ToCsv());
                carpeta = System.IO.Path.GetDirectoryName(svc.Almacen.RutaAbsoluta(baseClave + "resumen.csv"));
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
}
