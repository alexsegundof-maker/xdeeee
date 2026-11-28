using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core.Campo;
using Composta.Core.Lotes;

namespace Composta.Core.Pronostico
{
    // Renglón de «Estado actual vs. ideal»
    public sealed class LineaEstado
    {
        public string Nombre;          // "Peso", "Humedad"...
        public string Valor;           // "88 kg"
        public string Ideal;           // "50–200 kg"
        public NivelEstado Nivel;
        public string Explicacion;     // frase en lenguaje sencillo
    }

    public sealed class AccionPrioritaria
    {
        public int Prioridad;          // menor = más urgente
        public NivelEstado Nivel;
        public string Texto;
    }

    public sealed class AlertaRiesgo
    {
        public NivelEstado Nivel;
        public string Texto;
    }

    public sealed class DiagnosticoPronostico
    {
        public const int MaxAcciones = 3;
        public bool ConDatosReales;          // hay mediciones registradas (Control Manual)
        public string Origen = "";           // de dónde salen los valores "actuales"
        public readonly List<LineaEstado> Estado = new List<LineaEstado>();
        public readonly List<AccionPrioritaria> Acciones = new List<AccionPrioritaria>();
        public readonly List<AlertaRiesgo> Alertas = new List<AlertaRiesgo>();
    }

    // Cruza el pronóstico (manejo ideal) con lo que de verdad se registró en el lote: última medición, carga
    // inicial, aportaciones, balance C:N, madurez estimada y plan de mediciones. Sin Unity: se prueba aparte.
    // formatearDia: convierte "días desde ahora" en una fecha legible ("mié 2 dic") con la zona del dispositivo.
    public static class GeneradorDiagnostico
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        public const float VentanaAportacionReciente = 7f;     // días
        public const float DiferenciaCosechaRelevante = 7f;    // días

        public static DiagnosticoPronostico Generar(ResultadoPronostico p, Lote lote, DateTime ahoraUtc, Func<float, string> formatearDia)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            formatearDia = formatearDia ?? (d => string.Format(C, "día {0:F0}", d));
            var d = new DiagnosticoPronostico();
            var perfil = p.Perfil;
            bool manual = lote != null && lote.Modo == ModoLote.Manual;
            var analisis = manual ? AnalisisCampo.Analizar(lote, ahoraUtc) : null;
            var ultima = analisis?.Ultimo;
            d.ConDatosReales = ultima != null;

            // ---- Valores "actuales": medición real > estado simulado > parámetros elegidos ----
            float peso, humedad;
            float? temp = null;
            if (ultima != null)
            {
                peso = ultima.PesoKg; humedad = ultima.Humedad; temp = ultima.Temperatura;
                d.Origen = $"Según tu última medición ({formatearDia((float)(new DateTime(ultima.FechaUtc, DateTimeKind.Utc) - ahoraUtc).TotalDays)}).";
            }
            else if (p.Lecturas.Count > 0 && p.DiaSimuladoInicial > 0.01f)
            {
                var l0 = p.Lecturas[0];
                peso = l0.MasaKg > 0f ? l0.MasaKg : perfil.PesoKg; humedad = l0.Humedad; temp = l0.Temperatura;
                d.Origen = "Según el estado actual de la simulación (no hay mediciones reales).";
            }
            else
            {
                peso = perfil.PesoKg; humedad = perfil.HumedadInicial;
                d.Origen = manual ? "Todavía no hay mediciones: se evalúan los datos con que creaste el lote."
                                  : "Sin datos registrados: se evalúan los parámetros elegidos.";
            }

            var zonaT = perfil.ZonaIdeal(Magnitud.Temperatura);
            var limH = perfil.LimitesDe(Magnitud.Humedad);
            bool vermi = perfil.Tipo == TipoComposta.Vermicomposta;

            // ---- Estado actual vs. ideal ----
            var np = perfil.EvaluarPeso(peso);
            d.Estado.Add(new LineaEstado
            {
                Nombre = "Peso", Valor = Kg(peso), Ideal = $"{Kg(perfil.PesoMin)}–{Kg(perfil.PesoMax)}",
                Nivel = np == NivelPeso.Optimo ? NivelEstado.Ok : np == NivelPeso.FueraDeOptimo ? NivelEstado.Aviso : NivelEstado.Critico,
                Explicacion = np == NivelPeso.Exagerado ? $"Fuera de los límites físicos de {perfil.Nombre}: revisa el peso o el tipo de compostera."
                            : peso < perfil.PesoMin ? "Poca masa: la pila pierde calor rápido y le cuesta llegar a la fase caliente."
                            : peso > perfil.PesoMax ? "Mucha masa para este recipiente: voltea con más frecuencia para que entre aire al centro."
                            : "Masa suficiente para conservar el calor que generan los microbios."
            });
            string hTexto = humedad < limH.AvisoMin ? "Seca: los microbios se frenan. Riega al voltear."
                          : humedad > limH.AvisoMax ? "Muy húmeda: falta aire y aparecen malos olores. Agrega material seco y voltea."
                          : "Justo en el rango ideal.";
            d.Estado.Add(new LineaEstado
            {
                Nombre = "Humedad", Valor = Pct(humedad), Ideal = $"{limH.AvisoMin:F0}–{limH.AvisoMax:F0} %",
                Nivel = humedad < limH.CriticoMin || humedad > limH.CriticoMax ? NivelEstado.Critico
                      : humedad < limH.AvisoMin || humedad > limH.AvisoMax ? NivelEstado.Aviso : NivelEstado.Ok,
                Explicacion = hTexto
            });

            NivelEstado nivelT;
            string tTexto;
            if (vermi)
            {
                nivelT = p.TempMaxima > 35f ? NivelEstado.Critico : NivelEstado.Ok;
                tTexto = p.TempMaxima > 35f ? string.Format(C, "El pronóstico llega a {0:F0} °C: más de 35 °C es letal para las lombrices.", p.TempMaxima)
                                            : "Proceso frío, como necesitan las lombrices (15–25 °C).";
            }
            else if (p.TempMaxima < zonaT.min)
            {
                nivelT = p.AlcanzaFaseCaliente ? NivelEstado.Aviso : NivelEstado.Critico;
                tTexto = string.Format(C, "El pronóstico llega a {0:F0} °C, por debajo de la zona óptima ({1:F0}–{2:F0} °C): la fase caliente será {3}.",
                                       p.TempMaxima, zonaT.min, zonaT.max, p.AlcanzaFaseCaliente ? "débil y no eliminará bien patógenos ni semillas" : "inexistente");
            }
            else
            {
                nivelT = p.TempMaxima > perfil.LimitesDe(Magnitud.Temperatura).AvisoMax ? NivelEstado.Aviso : NivelEstado.Ok;
                tTexto = string.Format(C, "El pronóstico alcanza {0:F0} °C: la fase caliente higieniza el compost.", p.TempMaxima);
            }
            d.Estado.Add(new LineaEstado
            {
                Nombre = "Temperatura", Valor = temp.HasValue ? string.Format(C, "{0:F0} °C hoy · máx. {1:F0} °C", temp.Value, p.TempMaxima)
                                                              : string.Format(C, "máx. prevista {0:F0} °C", p.TempMaxima),
                Ideal = vermi ? "15–25 °C" : $"{zonaT.min:F0}–{zonaT.max:F0} °C",
                Nivel = nivelT, Explicacion = tTexto
            });

            BalanceMezcla balance = lote != null ? BalanceLote.Calcular(lote) : null;
            float cn = balance != null && balance.TieneDatos ? balance.RelacionCN : perfil.RelacionCN;
            var nivelCn = BalanceLote.Clasificar(cn);
            d.Estado.Add(new LineaEstado
            {
                Nombre = "Balance C:N", Valor = string.Format(C, "{0:F0}", cn), Ideal = "25–35",
                Nivel = nivelCn == NivelCN.Optima ? NivelEstado.Ok : nivelCn == NivelCN.ExcesoNitrogeno || nivelCn == NivelCN.ExcesoCarbono ? NivelEstado.Critico : NivelEstado.Aviso,
                Explicacion = nivelCn == NivelCN.Optima ? "Mezcla equilibrada de material verde y café."
                            : nivelCn == NivelCN.ExcesoNitrogeno || nivelCn == NivelCN.AlgoBaja ? "Falta material café (carbono): riesgo de olor a amoniaco."
                            : "Falta material verde (nitrógeno): la pila tardará en calentarse."
            });

            // ---- Acciones prioritarias ----
            var acciones = new List<AccionPrioritaria>();
            void Accion(int prioridad, NivelEstado nivel, string texto) => acciones.Add(new AccionPrioritaria { Prioridad = prioridad, Nivel = nivel, Texto = texto });

            if (manual)
            {
                var sit = PlanInformes.Evaluar(lote, ahoraUtc);
                if (sit.Estado == EstadoInforme.SinRegistros) Accion(0, NivelEstado.Aviso, "Registra la primera medición (temperatura, humedad y peso): el diagnóstico será mucho más preciso.");
                else if (sit.Estado == EstadoInforme.Atrasado) Accion(0, NivelEstado.Critico, "Registra hoy la medición semanal: " + sit.Texto().ToLowerInvariant() + ".");
            }
            if (humedad < limH.AvisoMin) Accion(1, humedad < limH.CriticoMin ? NivelEstado.Critico : NivelEstado.Aviso,
                                                string.Format(C, "Riega hoy: la humedad está en {0:F0} % y el ideal es {1:F0}–{2:F0} %.", humedad, limH.AvisoMin, limH.AvisoMax));
            else if (humedad > limH.AvisoMax) Accion(1, humedad > limH.CriticoMax ? NivelEstado.Critico : NivelEstado.Aviso,
                                                     string.Format(C, "Agrega material seco y voltea hoy: la humedad está en {0:F0} %.", humedad));

            if (balance != null && balance.TieneDatos && (nivelCn == NivelCN.ExcesoNitrogeno || nivelCn == NivelCN.ExcesoCarbono))
            {
                bool faltaCarbono = nivelCn == NivelCN.ExcesoNitrogeno;
                float kg = BalanceLote.KgParaObjetivo(balance, faltaCarbono ? MaterialEspecifico.Hojarasca : MaterialEspecifico.RestosCocina);
                Accion(2, NivelEstado.Aviso, string.Format(C, "Esta semana agrega {0:0.#} kg de {1} y mezcla bien para llevar la C:N de {2:F0} a 30.",
                                                           kg, faltaCarbono ? "hojarasca seca" : "restos de cocina", cn));
            }
            else if (!vermi && p.TempMaxima < zonaT.min && (nivelCn == NivelCN.AlgoAlta || nivelCn == NivelCN.ExcesoCarbono))
                Accion(2, NivelEstado.Aviso, "Agrega material verde (restos de cocina, césped) para que la pila alcance la fase caliente.");

            // Primer volteo y cuánto bajaría el oxígeno antes de él
            Hito volteo = null;
            foreach (var h in p.Hitos) if (h.Tipo == TipoHito.Volteo) { volteo = h; break; }
            if (volteo != null)
            {
                float minO2 = float.MaxValue;
                foreach (var l in p.Lecturas) if (l.Dias <= volteo.DiasDesdeAhora) minO2 = Math.Min(minO2, l.Oxigeno);
                float umbral = perfil.LimitesDe(Magnitud.Oxigeno).AvisoMin;
                string motivo = minO2 < float.MaxValue && minO2 < umbral + 2f
                    ? string.Format(C, ": el oxígeno bajaría a {0:F0} % (mínimo recomendado {1:F0} %)", minO2, umbral)
                    : ": airea la pila y reparte el calor";
                Accion(3, NivelEstado.Ok, $"Voltea el {formatearDia(volteo.DiasDesdeAhora)}{motivo}.");
            }

            // Hasta cuándo conviene aportar material verde
            Hito finCaliente = p.Hitos.Find(h => h.Tipo == TipoHito.FinFaseCaliente);
            if (finCaliente != null && !(lote?.BloqueadoMaduracion ?? false))
                Accion(4, NivelEstado.Ok, $"Evita agregar residuos verdes después del {formatearDia(finCaliente.DiasDesdeAhora)}: reiniciarían la descomposición y retrasarían la cosecha.");

            if (analisis != null && analisis.Estabilizado && !lote.BloqueadoMaduracion)
                Accion(1, NivelEstado.Aviso, "Inicia la maduración: la pila ya se estabilizó y no conviene agregar más residuos.");

            acciones.Sort((a, b) => a.Prioridad != b.Prioridad ? a.Prioridad.CompareTo(b.Prioridad) : b.Nivel.CompareTo(a.Nivel));
            for (int i = 0; i < acciones.Count && d.Acciones.Count < DiagnosticoPronostico.MaxAcciones; i++) d.Acciones.Add(acciones[i]);

            // ---- Alertas de riesgo ----
            void Alerta(NivelEstado n, string t) => d.Alertas.Add(new AlertaRiesgo { Nivel = n, Texto = t });
            if (vermi && p.TempMaxima > 35f) Alerta(NivelEstado.Critico, "Riesgo para las lombrices: la temperatura prevista supera 35 °C. Reduce el material verde y airea.");
            if (ultima != null && (ultima.Humedad < limH.CriticoMin || ultima.Humedad > limH.CriticoMax))
                Alerta(NivelEstado.Critico, string.Format(C, "La humedad registrada ({0:F0} %) está en nivel crítico: el proceso se detiene o se pudre.", ultima.Humedad));
            if (manual)
            {
                var sit = PlanInformes.Evaluar(lote, ahoraUtc);
                if (sit.Estado == EstadoInforme.Atrasado) Alerta(NivelEstado.Aviso, "Medición atrasada: el pronóstico parte de datos viejos y puede desviarse.");
            }
            if (balance != null && balance.UltimaAportacion != null)
            {
                var ua = balance.UltimaAportacion;
                double hace = (ahoraUtc - new DateTime(ua.FechaUtc, DateTimeKind.Utc)).TotalDays;
                if (hace >= 0 && hace <= VentanaAportacionReciente)
                {
                    var antes = BalanceLote.Calcular(lote, ua.FechaUtc - 1);
                    if (antes.TieneDatos && Math.Abs(antes.RelacionCN - balance.RelacionCN) >= 2f)
                        Alerta(BalanceLote.Clasificar(balance.RelacionCN) == NivelCN.Optima ? NivelEstado.Ok : NivelEstado.Aviso,
                               string.Format(C, "La aportación reciente ({0:0.#} kg de {1}) cambió la C:N de {2:F0} a {3:F0} ({4}).", ua.Kg,
                                             Materiales.NombreDe(ua).ToLowerInvariant(), antes.RelacionCN, balance.RelacionCN,
                                             BalanceLote.NombreNivel(balance.Nivel).ToLowerInvariant()));
                }
            }
            if (manual)
            {
                var m = Maduracion.Estimar(lote, ahoraUtc);
                if (m.Reajustada) Alerta(NivelEstado.Aviso, m.Motivo);
                Hito cosecha = p.Hitos.Find(h => h.Tipo == TipoHito.Cosecha);
                if (cosecha != null)
                {
                    double dif = (ahoraUtc.AddDays(cosecha.DiasDesdeAhora) - m.FechaMadurezUtc).TotalDays;
                    if (Math.Abs(dif) >= DiferenciaCosechaRelevante)
                        Alerta(NivelEstado.Ok, string.Format(C, "El pronóstico estima la cosecha el {0}; con tus registros, la madurez sería {1} días {2}. " +
                                                                "El pronóstico supone manejo ideal; tus registros reflejan lo que pasó.",
                                                             formatearDia(cosecha.DiasDesdeAhora), Math.Abs(Math.Round(dif)), dif > 0 ? "antes" : "después"));
                }
            }
            if (!d.ConDatosReales && manual)
                Alerta(NivelEstado.Ok, "Sin mediciones registradas: el pronóstico parte de valores típicos del tipo de compostera.");
            return d;
        }

        static string Kg(float v) => v.ToString(v >= 100f ? "F0" : "0.#", C) + " kg";
        static string Pct(float v) => v.ToString("F0", C) + " %";
    }
}
