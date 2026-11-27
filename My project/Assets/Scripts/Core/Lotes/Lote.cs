using System;
using System.Collections.Generic;
using System.Globalization;

namespace Composta.Core.Lotes
{
    public enum EstadoLote { Activo = 0, Cerrado = 1 }

    // Acciones hechas por personas. Las del control automático NO se guardan:
    // se vuelven a producir solas al reproducir el lote (el modelo es determinista).
    public enum TipoComando { Voltear = 0, Regar = 1, Automatico = 2, ProbFalla = 3, InyectarFalla = 4, RepararSensores = 5 }

    public enum TipoMaterial { Verde = 0, Cafe = 1, Mixto = 2 }

    [Serializable]
    public class Comando
    {
        public int Tick;              // hora simulada en la que se ejecutó (antes del siguiente paso)
        public TipoComando Tipo;
        public float Valor;           // litros (Regar, -1 = estándar), 0/1 (Automatico), probabilidad (ProbFalla)
        public string Usuario;
        public long FechaUtc;
    }

    // Residuo orgánico pesado que entra al lote (bitácora comunitaria)
    [Serializable]
    public class Aporte
    {
        public string Id;
        public long FechaUtc;
        public int Tick;
        public float Kg;
        public TipoMaterial Material;
        public string Nota;
        public string Usuario;
    }

    [Serializable]
    public class Lote
    {
        public const int VersionActual = 1;

        public int Version = VersionActual;
        public string Id;
        public string Nombre;
        public EstadoLote Estado;

        // Parámetros del perfil YA resueltos (nunca -1), para poder reconstruirlo idéntico
        public TipoComposta Tipo;
        public float PesoKg, RelacionCN, HumedadInicial, Aislamiento, TempAmbiente;
        public int Semilla;

        public string CreadoPor;
        public long CreadoUtc, ActualizadoUtc;
        public int TicksSimulados;

        public List<Comando> Comandos = new List<Comando>();
        public List<Aporte> Aportes = new List<Aporte>();

        public float DiasSimulados => TicksSimulados / 24f;

        public float TotalAportadoKg
        {
            get
            {
                float t = 0f;
                if (Aportes != null) foreach (var a in Aportes) t += a.Kg;
                return t;
            }
        }

        public static Lote Crear(PerfilComposta perfil, int semilla, string usuario, DateTime ahoraUtc)
        {
            if (perfil == null) throw new ArgumentNullException(nameof(perfil));
            return new Lote
            {
                Id = Guid.NewGuid().ToString("N"),
                Nombre = string.Format(CultureInfo.InvariantCulture, "{0} {1:F0} kg · {2:yyyy-MM-dd HH:mm}",
                                       perfil.Nombre, perfil.PesoKg, ahoraUtc.ToLocalTime()),
                Estado = EstadoLote.Activo,
                Tipo = perfil.Tipo,
                PesoKg = perfil.PesoKg,
                RelacionCN = perfil.RelacionCN,
                HumedadInicial = perfil.HumedadInicial,
                Aislamiento = perfil.Aislamiento,
                TempAmbiente = perfil.TempAmbiente,
                Semilla = semilla,
                CreadoPor = usuario,
                CreadoUtc = ahoraUtc.Ticks,
                ActualizadoUtc = ahoraUtc.Ticks
            };
        }
    }
}
