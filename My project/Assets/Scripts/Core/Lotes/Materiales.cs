using System;
using System.Collections.Generic;

namespace Composta.Core.Lotes
{
    // Los valores numéricos se guardan en disco: solo se agregan al final
    public enum MaterialEspecifico
    {
        SinEspecificar = 0, RestosCocina = 1, Cesped = 2, PososCafe = 3, Estiercol = 4,
        Hojarasca = 5, Paja = 6, Aserrin = 7, Carton = 8, Papel = 9, MezclaGenerica = 10
    }

    // Composición típica de un material (base: materia seca). Valores de referencia de manuales de compostaje
    // (Cornell Waste Management Institute, On-Farm Composting Handbook NRAES-54): C ≈ 40–50 % de la materia seca.
    public readonly struct DatosMaterial
    {
        public readonly MaterialEspecifico Id;
        public readonly string Nombre;
        public readonly TipoMaterial Grupo;
        public readonly float RelacionCN;       // carbono / nitrógeno
        public readonly float NitrogenoPct;     // % de N en la materia seca
        public readonly float HumedadTipica;    // % de agua al momento de agregarlo

        public DatosMaterial(MaterialEspecifico id, string nombre, TipoMaterial grupo, float cn, float n, float humedad)
        { Id = id; Nombre = nombre; Grupo = grupo; RelacionCN = cn; NitrogenoPct = n; HumedadTipica = humedad; }

        public float CarbonoPct => NitrogenoPct * RelacionCN;
    }

    public static class Materiales
    {
        public const float HumedadMin = 5f, HumedadMax = 95f;

        static readonly DatosMaterial[] Tabla =
        {
            new DatosMaterial(MaterialEspecifico.RestosCocina, "Restos de cocina", TipoMaterial.Verde, 15f, 2.5f, 80f),
            new DatosMaterial(MaterialEspecifico.Cesped, "Césped recién cortado", TipoMaterial.Verde, 17f, 2.8f, 80f),
            new DatosMaterial(MaterialEspecifico.PososCafe, "Posos de café", TipoMaterial.Verde, 20f, 2.0f, 60f),
            new DatosMaterial(MaterialEspecifico.Estiercol, "Estiércol", TipoMaterial.Verde, 20f, 2.2f, 75f),
            new DatosMaterial(MaterialEspecifico.Hojarasca, "Hojarasca seca", TipoMaterial.Cafe, 55f, 0.9f, 20f),
            new DatosMaterial(MaterialEspecifico.Paja, "Paja", TipoMaterial.Cafe, 80f, 0.6f, 12f),
            new DatosMaterial(MaterialEspecifico.Aserrin, "Aserrín", TipoMaterial.Cafe, 400f, 0.12f, 25f),
            new DatosMaterial(MaterialEspecifico.Carton, "Cartón", TipoMaterial.Cafe, 350f, 0.13f, 8f),
            new DatosMaterial(MaterialEspecifico.Papel, "Papel periódico", TipoMaterial.Cafe, 400f, 0.11f, 8f),
            new DatosMaterial(MaterialEspecifico.MezclaGenerica, "Mezcla (sin separar)", TipoMaterial.Mixto, 30f, 1.5f, 55f),
        };

        // Orden en que se muestran: primero los verdes, luego los cafés
        public static IReadOnlyList<DatosMaterial> Todos => Tabla;

        public static DatosMaterial De(MaterialEspecifico m)
        {
            foreach (var d in Tabla) if (d.Id == m) return d;
            return De(MaterialEspecifico.MezclaGenerica);
        }

        public static bool EsValido(MaterialEspecifico m) => m != MaterialEspecifico.SinEspecificar && Enum.IsDefined(typeof(MaterialEspecifico), m);

        // Aportes guardados antes de existir el material específico: se usa el representante de su grupo
        public static MaterialEspecifico Resolver(Aporte a)
        {
            if (a == null) return MaterialEspecifico.MezclaGenerica;
            if (EsValido(a.Especifico)) return a.Especifico;
            switch (a.Material)
            {
                case TipoMaterial.Verde: return MaterialEspecifico.RestosCocina;
                case TipoMaterial.Cafe: return MaterialEspecifico.Hojarasca;
                default: return MaterialEspecifico.MezclaGenerica;
            }
        }

        public static float HumedadDe(Aporte a)
            => a != null && a.HumedadPct >= HumedadMin && a.HumedadPct <= HumedadMax ? a.HumedadPct : De(Resolver(a)).HumedadTipica;

        public static string NombreDe(Aporte a) => De(Resolver(a)).Nombre;

        // Material predominante elegido al crear un lote de Control Manual (relación C/N aproximada)
        public static MaterialEspecifico DesdeRelacionCN(float cn)
            => cn <= 23f ? MaterialEspecifico.RestosCocina : cn >= 34f ? MaterialEspecifico.Hojarasca : MaterialEspecifico.MezclaGenerica;

        // Humedad escrita a mano: vacía = típica del material
        public static bool IntentarLeerHumedad(string texto, out float humedad, out string error)
        {
            humedad = 0f; error = null;
            string t = (texto ?? "").Trim().TrimEnd('%').Trim();
            if (t.Length == 0) return true;
            if (!RangosParametros.IntentarLeer(t, out humedad) || humedad < HumedadMin || humedad > HumedadMax)
            {
                error = $"La humedad del material debe estar entre {HumedadMin:F0} y {HumedadMax:F0} % (o déjala vacía para usar la típica).";
                humedad = 0f;
                return false;
            }
            humedad = (float)Math.Round(humedad, 1);
            return true;
        }
    }
}
