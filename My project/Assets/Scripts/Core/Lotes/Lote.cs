using System;
using System.Collections.Generic;
using System.Globalization;

namespace Composta.Core.Lotes
{
    // Cerrado = archivado sin cosecha (p. ej. una simulación reemplazada); Cosechado = terminó con cosecha registrada
    public enum EstadoLote { Activo = 0, Cerrado = 1, Cosechado = 2 }

    // Acciones hechas por personas. Las del control automático NO se guardan:
    // se vuelven a producir solas al reproducir el lote (el modelo es determinista).
    // Los valores numéricos se guardan en disco: solo se agregan al final
    public enum TipoComando { Voltear = 0, Regar = 1, Automatico = 2, ProbFalla = 3, InyectarFalla = 4, RepararSensores = 5, TempAmbiente = 6, Informe = 7, Aporte = 8 }

    public enum TipoMaterial { Verde = 0, Cafe = 1, Mixto = 2 }

    [Serializable]
    public class Comando
    {
        public int Tick;              // hora simulada en la que se ejecutó (antes del siguiente paso)
        public TipoComando Tipo;
        public float Valor;           // litros (Regar, -1 = estándar), 0/1 (Automatico), probabilidad (ProbFalla)
        public string Usuario;
        public long FechaUtc;
        public string Ref;            // Aporte: Id del aporte que se incorporó a la simulación en esa hora
    }

    // Residuo orgánico pesado que entra al lote (bitácora comunitaria)
    [Serializable]
    public class Aporte
    {
        public string Id;
        public long FechaUtc;
        public int Tick;
        public float Kg;
        public TipoMaterial Material;           // grupo (verde/café/mixto); se deduce del material específico
        public MaterialEspecifico Especifico;   // SinEspecificar en aportes anteriores a la v1.1.2
        public float HumedadPct;                // 0 = usar la humedad típica del material
        public bool EsCargaInicial;             // lo que se puso al crear el lote
        public string Nota;
        public string Usuario;
        public string EditadoPor;     // última edición (vacío si nunca se editó)
        public long EditadoUtc;
    }

    // Cosecha: cierra el lote y permite calcular rendimiento y calidad
    [Serializable]
    public class CosechaLote
    {
        public long FechaUtc;         // 0 = sin cosecha
        public float PesoKg;
        public bool OlorTierra, SinRestos, TexturaSuelta;
        public string Usuario;
        public string Nota;
    }

    public enum AccionCambio { Creacion = 0, Edicion = 1, Eliminacion = 2 }
    public enum EntidadCambio { Medicion = 0, Aporte = 1, Lote = 2 }

    // Bitácora de auditoría: quién cambió qué y cuándo (nunca se borra)
    [Serializable]
    public class CambioLote
    {
        public long FechaUtc;
        public string Usuario;
        public AccionCambio Accion;
        public EntidadCambio Entidad;
        public string IdEntidad;
        public string Detalle;
    }

    [Serializable]
    public class Lote
    {
        public const int VersionActual = 1;

        public int Version = VersionActual;
        public string Id;
        public string Nombre;
        public int Numero;            // número consecutivo visible ("Lote #003"); 0 = lote anterior sin número
        public string Compostera;     // recipiente o lugar físico: "Cubeta A", "Tambor D"...
        public bool Demo;             // datos de demostración: se marcan en todas partes y se pueden quitar
        public EstadoLote Estado;
        public bool BloqueadoMaduracion;      // en maduración ya no se admiten aportaciones
        public long BloqueoUtc;
        public CosechaLote Cosecha = new CosechaLote();
        public ModoLote Modo;         // lotes guardados antes de existir este campo se leen como Simular (0)
        public bool RitmoReal;        // Manual: 1 hora simulada = 1 hora de calendario

        // Parámetros del perfil YA resueltos (nunca -1), para poder reconstruirlo idéntico
        public TipoComposta Tipo;
        public float PesoKg, RelacionCN, HumedadInicial, Aislamiento, TempAmbiente;
        public int Semilla;

        public string CreadoPor;
        public long CreadoUtc, ActualizadoUtc;
        public int TicksSimulados;

        public List<Comando> Comandos = new List<Comando>();
        public List<Aporte> Aportes = new List<Aporte>();
        public List<Campo.InformeCampo> Informes = new List<Campo.InformeCampo>();   // informes semanales (Manual)
        public List<CambioLote> Cambios = new List<CambioLote>();                      // auditoría de ediciones y borrados

        public float DiasSimulados => TicksSimulados / 24f;

        // Residuos agregados después de la carga inicial
        public float TotalAportadoKg
        {
            get
            {
                float t = 0f;
                if (Aportes != null) foreach (var a in Aportes) if (a != null && !a.EsCargaInicial) t += a.Kg;
                return t;
            }
        }

        public int NumeroAportaciones
        {
            get
            {
                int n = 0;
                if (Aportes != null) foreach (var a in Aportes) if (a != null && !a.EsCargaInicial) n++;
                return n;
            }
        }

        public bool TieneCosecha => Estado == EstadoLote.Cosechado && Cosecha != null && Cosecha.FechaUtc > 0;
        public string Etiqueta => (Numero > 0 ? "Lote #" + Numero.ToString("000", CultureInfo.InvariantCulture) : "Lote") +
                                  (string.IsNullOrEmpty(Compostera) ? "" : " · " + Compostera);

        public static Lote Crear(PerfilComposta perfil, int semilla, string usuario, DateTime ahoraUtc,
                                 ModoLote modo = ModoLote.Simular)
        {
            if (perfil == null) throw new ArgumentNullException(nameof(perfil));
            return new Lote
            {
                Modo = modo,
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
