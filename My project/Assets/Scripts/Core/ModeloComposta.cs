using System;

namespace Composta.Core
{
    public class ModeloComposta
    {
        public readonly PerfilComposta Perfil;

        // ---- Estado ----
        public float Temperatura = 22f;
        public float PH = 6.5f;
        public float Humedad;
        public float Oxigeno = 20f;
        public int Ticks = 0;                 // 1 tick = 1 hora simulada
        public float Dias = 0f;
        public Etapa Etapa = Etapa.Mesofilica;
        public float TempAmbiente = 22f;

        // ---- Perillas (se derivan del perfil: peso, aislamiento y C/N) ----
        public float KTemp, KPH = 0.03f, Evaporacion, ConsumoO2;
        public float MaxCambioTemp = 3f;
        public float TempMax = 75f;

        readonly float calor, cubica;
        readonly System.Random rnd;

        // Temperatura a la que tiende la fase caliente con este peso y este aislamiento
        public float TempPlateau => TempAmbiente + Math.Max(0f, Perfil.CalorMax - TempAmbiente) * calor;
        public bool FaseCalientePosible => TempPlateau >= 45f;

        public ModeloComposta(PerfilComposta perfil, int semilla = 1234)
        {
            Perfil = perfil;
            rnd = new System.Random(semilla);
            Humedad = perfil.HumedadInicial;
            TempAmbiente = perfil.TempAmbiente;
            Temperatura = perfil.TempAmbiente;

            // Relación superficie/volumen ~ 1 / (peso^(1/3)); 500 kg es la pila de referencia
            cubica = (float)Math.Pow(perfil.PesoKg / 500f, 1.0 / 3.0);
            float act = perfil.Actividad();

            calor = Clamp(cubica * perfil.Aislamiento * act, 0.2f, 1.1f);
            KTemp = Clamp(0.06f / cubica, 0.03f, 0.15f);                                  // más masa = más lenta
            Evaporacion = Clamp(0.03f / (cubica * perfil.Aislamiento), 0.01f, 0.12f);    // más chica = se seca más
            ConsumoO2 = 0.08f * Clamp(act, 0.4f, 1.4f);
        }

        public ModeloComposta(int semilla = 1234)
            : this(PerfilComposta.Crear(TipoComposta.PilaAbierta), semilla) { }

        // Avanza EXACTAMENTE 1 hora simulada (no depende de los FPS)
        public void AvanzarHora()
        {
            Ticks++;
            Dias = Ticks / 24f;
            ActualizarEtapa();

            // Temperatura: persigue un objetivo; con poco O2 el calor se frena
            float factorO2 = Clamp(Oxigeno / 12f, 0.2f, 1f);
            float objetivo = TempAmbiente + (TempObjetivo() - TempAmbiente) * factorO2;
            float dT = (objetivo - Temperatura) * KTemp + Ruido(0.15f);
            Temperatura += Clamp(dT, -MaxCambioTemp, MaxCambioTemp);

            // pH
            PH += (PHObjetivo() - PH) * KPH + Ruido(0.01f);

            // Humedad: se evapora más con calor
            Humedad -= Evaporacion * (Temperatura / 60f);

            // Oxígeno: lo consumen los microbios; muy húmedo = más compacto
            float consumo = ConsumoO2 * (Temperatura > 30f ? 1f : 0.5f);
            if (Humedad > Perfil.LimitesDe(Magnitud.Humedad).AvisoMax + 5f) consumo *= 1.5f;
            Oxigeno -= consumo;

            Limitar();
        }

        // ---- Acciones del usuario ----
        public void Voltear()
        {
            Oxigeno = 20f;
            Temperatura -= Clamp(5f / cubica, 2.5f, 7.5f);   // una pila grande se enfría menos
            Humedad -= 2f;
            Limitar();
        }

        // Ajuste manual durante el monitoreo (cambio de clima, traslado de la pila...)
        public void EstablecerTempAmbiente(float grados)
        {
            if (float.IsNaN(grados) || float.IsInfinity(grados)) return;
            TempAmbiente = grados;
            Limitar();
        }

        // litros < 0 = riego estándar del perfil (8% del peso). El efecto depende del peso.
        public void Regar(float litros = -1f)
        {
            if (float.IsNaN(litros) || float.IsInfinity(litros) || litros < 0f) litros = Perfil.LitrosPorRiego;
            Humedad += litros / Perfil.PesoKg * 100f;
            Temperatura -= 1f;
            Limitar();
        }

        // ---- Parámetros por etapa (relativos a la fase caliente del perfil) ----
        float TempObjetivo()
        {
            float span = TempPlateau - TempAmbiente;
            switch (Etapa)
            {
                case Etapa.Mesofilica: return TempAmbiente + 0.70f * span;
                case Etapa.Termofilica: return TempPlateau;
                case Etapa.Enfriamiento: return TempAmbiente + 0.325f * span;
                default: return TempAmbiente + 3f;
            }
        }

        float PHObjetivo()
        {
            float baseEtapa;
            switch (Etapa)
            {
                case Etapa.Mesofilica: baseEtapa = 6.0f; break;
                case Etapa.Termofilica: baseEtapa = 7.8f; break;
                case Etapa.Enfriamiento: baseEtapa = 7.4f; break;
                default: baseEtapa = 7.0f; break;
            }
            float v = 7f + (baseEtapa - 7f) * Perfil.AmplitudPH;
            if (Etapa == Etapa.Termofilica)
                v += Clamp((28f - Perfil.RelacionCN) * 0.02f, -0.4f, 0.4f);   // poco carbono = más amoníaco
            return v;
        }

        void ActualizarEtapa()
        {
            float span = TempPlateau - TempAmbiente;
            switch (Etapa)
            {
                case Etapa.Mesofilica:
                    if (FaseCalientePosible && Temperatura >= TempAmbiente + 0.575f * span) Etapa = Etapa.Termofilica;
                    else if (Dias >= 15f) Etapa = Etapa.Enfriamiento;   // pilas frías saltan la fase caliente
                    break;
                case Etapa.Termofilica:
                    if (Dias >= 15f) Etapa = Etapa.Enfriamiento;
                    break;
                case Etapa.Enfriamiento:
                    if ((Dias >= 30f && Temperatura < TempAmbiente + 0.4f * span) || Dias >= 45f)
                        Etapa = Etapa.Maduracion;
                    break;
            }
        }

        public Lectura Capturar() => new Lectura
        {
            Tick = Ticks,
            Dias = Dias,
            Temperatura = Temperatura,
            PH = PH,
            Humedad = Humedad,
            Oxigeno = Oxigeno,
            Etapa = Etapa
        };

        // ---- Utilidades ----
        void Limitar()
        {
            Temperatura = Clamp(Temperatura, TempAmbiente, TempMax);
            Humedad = Clamp(Humedad, 0f, 100f);
            Oxigeno = Clamp(Oxigeno, 2f, 21f);
            PH = Clamp(PH, 3f, 10f);
        }

        float Ruido(float amp) => (float)(rnd.NextDouble() * 2 - 1) * amp;
        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}