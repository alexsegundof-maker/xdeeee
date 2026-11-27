using System;
using System.Globalization;
using Composta.Core.Lotes;

namespace Composta.Core.Campo
{
    public sealed class EstimacionMaduracion
    {
        public DateTime InicioUtc;
        public DateTime FechaMadurezUtc;
        public int DiasBase;
        public float DiasRestantes;          // 0 si ya debería estar madura
        public bool Reajustada;              // una aportación de verde recorrió la fecha
        public string Motivo = "";           // explicación del último reajuste
    }

    // Cuándo debería estar listo el compost. Duración base por tipo de compostera; el material verde fresco
    // que entra después recorre la fecha en proporción a lo que pesa frente a la pila:
    //   f = kg verde / peso actual estimado;  candidato = mín(base, f / 0.30 × fase activa)
    //   si lo que faltaba es menor que el candidato, la madurez pasa a (fecha de la aportación + candidato)
    public static class Maduracion
    {
        public const float FraccionReinicio = 0.30f;     // 30 % de material nuevo reinicia la fase activa
        public const float FraccionFaseActiva = 0.5f;    // mesófila + termófila ≈ la mitad del proceso
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static int DuracionBaseDias(TipoComposta t)
        {
            switch (t)
            {
                case TipoComposta.Domestica: return 84;      // 12 semanas
                case TipoComposta.Cerrada: return 70;
                case TipoComposta.PilaAbierta: return 112;
                case TipoComposta.Industrial: return 63;
                default: return 90;                          // vermicomposta
            }
        }

        public static EstimacionMaduracion Estimar(Lote l, DateTime ahoraUtc)
        {
            var e = new EstimacionMaduracion();
            if (l == null) return e;
            e.DiasBase = DuracionBaseDias(l.Tipo);
            e.InicioUtc = Inicio(l);
            e.FechaMadurezUtc = e.InicioUtc.AddDays(e.DiasBase);
            double faseActiva = e.DiasBase * FraccionFaseActiva;

            var regs = AnalisisCampo.Ordenados(l);
            foreach (var a in BalanceLote.AportesOrdenados(l))
            {
                if (a.EsCargaInicial) continue;
                var d = Materiales.De(Materiales.Resolver(a));
                float kgVerde = d.Grupo == TipoMaterial.Verde ? a.Kg : d.Grupo == TipoMaterial.Mixto ? a.Kg * 0.5f : 0f;
                if (kgVerde <= 0f) continue;

                float masa = MasaAntesDe(l, regs, a);
                if (masa <= 0f) continue;
                float f = kgVerde / masa;
                double candidatoDias = Math.Min(e.DiasBase, f / FraccionReinicio * faseActiva);
                var t = new DateTime(a.FechaUtc, DateTimeKind.Utc);
                if ((e.FechaMadurezUtc - t).TotalDays < candidatoDias)
                {
                    e.FechaMadurezUtc = t.AddDays(candidatoDias);
                    e.Reajustada = true;
                    e.Motivo = string.Format(C, "La aportación del {0:dd/MM/yyyy} ({1:0.#} kg de {2}, {3:F0} % de la pila) recorrió la fecha de madurez.",
                                             t, a.Kg, d.Nombre.ToLowerInvariant(), f * 100f);
                }
            }
            e.DiasRestantes = (float)Math.Max(0.0, (e.FechaMadurezUtc - ahoraUtc).TotalDays);
            return e;
        }

        static DateTime Inicio(Lote l)
        {
            long inicio = l.CreadoUtc;
            if (l.Aportes != null)
                foreach (var a in l.Aportes) if (a != null && a.EsCargaInicial && a.FechaUtc > 0 && a.FechaUtc < inicio) inicio = a.FechaUtc;
            var regs = AnalisisCampo.Ordenados(l);
            if (regs.Count > 0 && regs[0].FechaUtc > 0 && regs[0].FechaUtc < inicio) inicio = regs[0].FechaUtc;
            return new DateTime(Math.Max(inicio, 1), DateTimeKind.Utc);
        }

        // Peso de la pila justo antes de la aportación: la medición previa más lo que entró desde entonces;
        // sin mediciones, todo lo que había entrado hasta ese momento.
        static float MasaAntesDe(Lote l, System.Collections.Generic.List<InformeCampo> regs, Aporte objetivo)
        {
            InformeCampo previa = null;
            foreach (var r in regs) if (r.FechaUtc <= objetivo.FechaUtc) previa = r;
            if (previa == null) return BalanceLote.Calcular(l, objetivo.FechaUtc - 1).PesoEntradaKg;
            float masa = previa.PesoKg;
            foreach (var a in BalanceLote.AportesOrdenados(l))
            {
                if (ReferenceEquals(a, objetivo)) break;
                if (!a.EsCargaInicial && a.FechaUtc > previa.FechaUtc && a.FechaUtc <= objetivo.FechaUtc) masa += a.Kg;
            }
            return masa;
        }
    }
}
