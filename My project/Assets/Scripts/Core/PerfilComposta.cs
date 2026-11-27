using System;

namespace Composta.Core
{
    public enum TipoComposta { Domestica, Cerrada, PilaAbierta, Industrial, Vermicomposta }

    // Una "build" de composta: tipo, peso y rangos propios
    public class PerfilComposta
    {
        public TipoComposta Tipo { get; private set; }
        public string Nombre { get; private set; }
        public float PesoMin { get; private set; }
        public float PesoMax { get; private set; }
        public float PesoDefecto { get; private set; }
        public float PesoKg { get; private set; }

        public float RelacionCN  = 28f;      // ideal ~25-30
        public float TempAmbiente = 22f;
        public float HumedadInicial = 55f;
        public float Aislamiento = 1f;      // >1 retiene más calor y evapora menos
        public float CalorMax = 62f;        // temperatura de la fase caliente en una pila de 500 kg
        public float AmplitudPH = 1f;       // 1 = cambios de pH normales; <1 = más estable

        public bool PesoExagerado => PesoKg < PesoMin || PesoKg > PesoMax;
        public float LitrosPorRiego => 0.08f * PesoKg;   // riego estándar: agua = 8% del peso

        readonly float[,] lim = new float[4, 4];    // [magnitud]: avisoMin, avisoMax, criticoMin, criticoMax
        readonly float[,] zona = new float[4, 2];   // zona ideal para las gráficas

        PerfilComposta(TipoComposta tipo)
        {
            Tipo = tipo;
            for (int m = 0; m < 4; m++)
            {
                var b = Reglas.Base((Magnitud)m);
                lim[m, 0] = b.AvisoMin; lim[m, 1] = b.AvisoMax;
                lim[m, 2] = b.CriticoMin; lim[m, 3] = b.CriticoMax;
                var z = Agregador.ZonaIdeal((Magnitud)m);
                zona[m, 0] = z.min; zona[m, 1] = z.max;
            }
        }

        public static PerfilComposta Crear(TipoComposta tipo, float pesoKg = -1f, bool permitirExagerado = false)
        {
            var p = new PerfilComposta(tipo);
            switch (tipo)
            {
                case TipoComposta.Domestica:
                    p.Nombre = "Compostera doméstica";
                    p.FijarRango(40f, 50f, 45f);
                    p.FijarLimites(Magnitud.Temperatura, 5f, 50f, 0f, 60f);
                    p.FijarZona(Magnitud.Temperatura, 25f, 45f);
                    break;

                case TipoComposta.Cerrada:
                    p.Nombre = "Compostera cerrada";
                    p.FijarRango(80f, 150f, 100f);
                    p.Aislamiento = 1.3f;
                    p.FijarZona(Magnitud.Temperatura, 45f, 60f);
                    break;

                case TipoComposta.PilaAbierta:
                    p.Nombre = "Pila abierta";
                    p.FijarRango(300f, 600f, 500f);
                    break;

                case TipoComposta.Industrial:
                    p.Nombre = "Hilera industrial";
                    p.FijarRango(2000f, 10000f, 5000f);
                    p.FijarLimites(Magnitud.Temperatura, 10f, 70f, 0f, 75f);
                    break;

                case TipoComposta.Vermicomposta:
                    p.Nombre = "Vermicomposta";
                    p.FijarRango(5f, 35f, 20f);
                    p.CalorMax = 28f;
                    p.AmplitudPH = 0.3f;
                    p.HumedadInicial = 75f;
                    p.FijarLimites(Magnitud.Temperatura, 10f, 28f, 5f, 32f);
                    p.FijarLimites(Magnitud.PH, 6.2f, 7.6f, 5.5f, 8.2f);
                    p.FijarLimites(Magnitud.Humedad, 65f, 85f, 55f, 90f);
                    p.FijarZona(Magnitud.Temperatura, 15f, 25f);
                    p.FijarZona(Magnitud.PH, 6.5f, 7.5f);
                    p.FijarZona(Magnitud.Humedad, 70f, 80f);
                    break;
            }
            p.EstablecerPeso(pesoKg > 0f ? pesoKg : p.PesoDefecto, permitirExagerado);
            return p;
        }

        // Sin permitirExagerado, el peso se limita al rango del perfil
        public void EstablecerPeso(float kg, bool permitirExagerado = false)
        {
            // NaN/Infinito no sobreviven a Math.Min/Max: se reemplazan por el peso por defecto
            if (float.IsNaN(kg) || float.IsInfinity(kg)) kg = PesoDefecto;
            // Peso libre: dentro de los límites físicos del modelo (modo Manual). Antes el tope era 1-20000 kg.
            PesoKg = permitirExagerado
                ? Math.Max(RangosParametros.PesoAbsMin, Math.Min(kg, RangosParametros.PesoAbsMax))
                : Math.Max(PesoMin, Math.Min(kg, PesoMax));
        }

        // Más nitrógeno (C/N bajo) = más activa; demasiado carbono = lenta
        public float Actividad()
        {
            float cn = RelacionCN;
            if (cn < 25f) return Math.Min(1.3f, Math.Max(1f, 1f + (25f - cn) * 0.02f));
            if (cn > 30f) return Math.Min(1f, Math.Max(0.4f, 1f - (cn - 30f) * 0.015f));
            return 1f;
        }

        public Limite LimitesDe(Magnitud m)
        {
            var b = Reglas.Base(m);          // trae unidad y acciones
            int i = (int)m;
            b.AvisoMin = lim[i, 0]; b.AvisoMax = lim[i, 1];
            b.CriticoMin = lim[i, 2]; b.CriticoMax = lim[i, 3];
            return b;
        }

        public (float min, float max) ZonaIdeal(Magnitud m) => (zona[(int)m, 0], zona[(int)m, 1]);

        void FijarRango(float min, float max, float defecto) { PesoMin = min; PesoMax = max; PesoDefecto = defecto; }

        void FijarLimites(Magnitud m, float aMin, float aMax, float cMin, float cMax)
        {
            int i = (int)m;
            lim[i, 0] = aMin; lim[i, 1] = aMax; lim[i, 2] = cMin; lim[i, 3] = cMax;
        }

        void FijarZona(Magnitud m, float min, float max)
        {
            zona[(int)m, 0] = min; zona[(int)m, 1] = max;
        }
    }
}