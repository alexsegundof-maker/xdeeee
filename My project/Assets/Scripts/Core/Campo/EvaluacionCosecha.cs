using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core.Lotes;

namespace Composta.Core.Campo
{
    public sealed class CriterioCalidad
    {
        public string Nombre;
        public bool Cumple;
        public string Detalle;
    }

    public sealed class ResultadoCosecha
    {
        public float PesoEntradaKg, PesoCosechadoKg, RendimientoPct;
        public int Puntos, PuntosMaximos;
        public char Calidad = '—';
        public float DuracionDias;
        public readonly List<CriterioCalidad> Criterios = new List<CriterioCalidad>();
    }

    // Rendimiento = peso cosechado / peso de entrada total. Calidad A/B/C por 6 criterios comprobables:
    // 3 de la revisión física al cosechar + temperatura y humedad de la última medición + rendimiento esperado.
    public static class EvaluacionCosecha
    {
        public const float RendimientoMin = 30f, RendimientoMax = 60f;
        public const float HumedadFinalMin = 35f, HumedadFinalMax = 55f;
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static ResultadoCosecha Evaluar(Lote l) => l?.Cosecha == null ? new ResultadoCosecha() : Evaluar(l, l.Cosecha);

        public static ResultadoCosecha Evaluar(Lote l, CosechaLote c)
        {
            var r = new ResultadoCosecha();
            if (l == null || c == null) return r;
            var balance = BalanceLote.Calcular(l, c.FechaUtc > 0 ? c.FechaUtc : long.MaxValue);
            r.PesoEntradaKg = balance.PesoEntradaKg;
            r.PesoCosechadoKg = c.PesoKg;
            r.RendimientoPct = r.PesoEntradaKg > 0f ? c.PesoKg / r.PesoEntradaKg * 100f : 0f;
            if (c.FechaUtc > 0) r.DuracionDias = (float)new TimeSpan(c.FechaUtc - Maduracion.Estimar(l, new DateTime(c.FechaUtc, DateTimeKind.Utc)).InicioUtc.Ticks).TotalDays;

            void Criterio(string nombre, bool cumple, string detalle) => r.Criterios.Add(new CriterioCalidad { Nombre = nombre, Cumple = cumple, Detalle = detalle });
            Criterio("Olor a tierra", c.OlorTierra, c.OlorTierra ? "Sin olor a podrido ni a amoniaco." : "Todavía huele a material en descomposición.");
            Criterio("Sin restos reconocibles", c.SinRestos, c.SinRestos ? "No se distinguen los residuos originales." : "Aún se reconocen restos: le faltaba maduración.");
            Criterio("Textura suelta", c.TexturaSuelta, c.TexturaSuelta ? "Se desmorona en la mano." : "Está compacto o pegajoso.");

            InformeCampo final = null;
            foreach (var m in AnalisisCampo.Ordenados(l)) if (c.FechaUtc <= 0 || m.FechaUtc <= c.FechaUtc) final = m;
            float limiteTemp = Math.Max(30f, l.TempAmbiente + 8f);
            if (final == null)
            {
                Criterio("Temperatura estabilizada", false, "No hay mediciones para comprobarlo.");
                Criterio("Humedad final adecuada", false, "No hay mediciones para comprobarlo.");
            }
            else
            {
                Criterio("Temperatura estabilizada", final.Temperatura <= limiteTemp,
                         string.Format(C, "Última medición: {0:F1} °C (debe ser ≤ {1:F0} °C, cerca de la ambiente).", final.Temperatura, limiteTemp));
                bool humOk = final.Humedad >= HumedadFinalMin && final.Humedad <= HumedadFinalMax;
                Criterio("Humedad final adecuada", humOk,
                         string.Format(C, "Última medición: {0:F0} % (ideal {1:F0}–{2:F0} %).", final.Humedad, HumedadFinalMin, HumedadFinalMax));
            }
            bool rendOk = r.RendimientoPct >= RendimientoMin && r.RendimientoPct <= RendimientoMax;
            Criterio("Rendimiento esperado", rendOk,
                     string.Format(C, "{0:F0} % del peso de entrada (esperado {1:F0}–{2:F0} %).", r.RendimientoPct, RendimientoMin, RendimientoMax));

            r.PuntosMaximos = r.Criterios.Count;
            foreach (var x in r.Criterios) if (x.Cumple) r.Puntos++;
            r.Calidad = r.Puntos >= 5 ? 'A' : r.Puntos >= 3 ? 'B' : 'C';
            return r;
        }

        public static string DescribirCalidad(char calidad)
        {
            switch (calidad)
            {
                case 'A': return "A · listo para usar";
                case 'B': return "B · usable, conviene cribar o dejar madurar más";
                case 'C': return "C · inmaduro: dejar madurar antes de usar";
                default: return "Sin cosecha";
            }
        }
    }
}
