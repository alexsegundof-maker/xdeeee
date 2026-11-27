using System;
using System.Collections.Generic;
using Composta.Core.Lotes;

namespace Composta.Core.Campo
{
    public enum NivelCN { SinDatos, ExcesoNitrogeno, AlgoBaja, Optima, AlgoAlta, ExcesoCarbono }

    // Balance de todo lo que entró al lote (carga inicial + aportaciones)
    public sealed class BalanceMezcla
    {
        public float PesoEntradaKg;          // base + aportaciones
        public float CargaBaseKg;            // carga inicial (o su equivalente en lotes anteriores)
        public float AportadoKg;             // aportaciones posteriores a la carga inicial
        public string OrigenBase = "";       // de dónde salió la carga base (para explicarlo)
        public double AguaKg, MateriaSecaKg, CarbonoKg, NitrogenoKg;
        public float RelacionCN = float.NaN; // NaN si no hay nitrógeno que medir
        public float HumedadMezclaPct = float.NaN;
        public float? HumedadActualPct;      // última medición + lo que entró después (si hay medición)
        public NivelCN Nivel = NivelCN.SinDatos;
        public Aporte UltimaAportacion;      // la más reciente (sin contar la carga inicial)

        public bool TieneDatos => PesoEntradaKg > 0f && !float.IsNaN(RelacionCN);
    }

    // Mezcla ponderada por MATERIA SECA: el agua no aporta carbono ni nitrógeno.
    //   seca = kg × (1 − h);  N = seca × %N;  C = N × (C:N)
    //   C:N de la mezcla = ΣC / ΣN;  humedad de la mezcla = Σ(kg × h) / Σkg
    public static class BalanceLote
    {
        public const float CNObjetivo = 30f;
        public const float CNBajaCritica = 20f, CNOptimaMin = 25f, CNOptimaMax = 35f, CNAltaCritica = 40f;
        const float CarbonoGenericoPct = 45f;     // % de C de la materia seca cuando solo se conoce la C:N

        public static NivelCN Clasificar(float cn)
        {
            if (float.IsNaN(cn) || float.IsInfinity(cn)) return NivelCN.SinDatos;
            if (cn < CNBajaCritica) return NivelCN.ExcesoNitrogeno;
            if (cn < CNOptimaMin) return NivelCN.AlgoBaja;
            if (cn <= CNOptimaMax) return NivelCN.Optima;
            if (cn <= CNAltaCritica) return NivelCN.AlgoAlta;
            return NivelCN.ExcesoCarbono;
        }

        public static string NombreNivel(NivelCN n)
        {
            switch (n)
            {
                case NivelCN.ExcesoNitrogeno: return "Exceso de nitrógeno";
                case NivelCN.AlgoBaja: return "Algo baja";
                case NivelCN.Optima: return "Óptima";
                case NivelCN.AlgoAlta: return "Algo alta";
                case NivelCN.ExcesoCarbono: return "Exceso de carbono";
                default: return "Sin datos";
            }
        }

        // hastaUtc: solo cuenta lo que entró hasta esa fecha (para reconstruir el pasado)
        public static BalanceMezcla Calcular(Lote l, long hastaUtc = long.MaxValue)
        {
            var b = new BalanceMezcla();
            if (l == null) return b;
            var aportes = AportesOrdenados(l);
            bool hayCargaInicial = aportes.Exists(a => a.EsCargaInicial);

            if (!hayCargaInicial)
            {
                // Lotes creados antes de registrar la carga inicial: se usa la primera medición (Control Manual)
                // o los parámetros del lote, con la C:N del material predominante elegido al crearlo.
                var regs = AnalisisCampo.Ordenados(l);
                float kg, h;
                if (l.Modo == ModoLote.Manual && regs.Count > 0) { kg = regs[0].PesoKg; h = regs[0].Humedad; b.OrigenBase = "primera medición"; }
                else { kg = l.PesoKg; h = l.HumedadInicial; b.OrigenBase = "datos del lote"; }
                if (kg > 0f)
                {
                    float n = CarbonoGenericoPct / Math.Max(1f, l.RelacionCN);
                    Sumar(b, kg, h, n, l.RelacionCN);
                    b.CargaBaseKg = kg;
                }
            }
            else b.OrigenBase = "carga inicial";

            foreach (var a in aportes)
            {
                if (a.FechaUtc > hastaUtc) continue;
                var d = Materiales.De(Materiales.Resolver(a));
                Sumar(b, a.Kg, Materiales.HumedadDe(a), d.NitrogenoPct, d.RelacionCN);
                if (a.EsCargaInicial) b.CargaBaseKg += a.Kg;
                else { b.AportadoKg += a.Kg; b.UltimaAportacion = a; }
            }

            b.PesoEntradaKg = b.CargaBaseKg + b.AportadoKg;
            if (b.NitrogenoKg > 1e-9) b.RelacionCN = (float)(b.CarbonoKg / b.NitrogenoKg);
            if (b.PesoEntradaKg > 0f) b.HumedadMezclaPct = (float)(b.AguaKg / b.PesoEntradaKg * 100.0);
            b.Nivel = Clasificar(b.RelacionCN);
            b.HumedadActualPct = HumedadActual(l, aportes, hastaUtc);
            return b;
        }

        static void Sumar(BalanceMezcla b, float kg, float humedadPct, float nitrogenoPct, float cn)
        {
            double agua = kg * humedadPct / 100.0;
            double seca = kg - agua;
            double n = seca * nitrogenoPct / 100.0;
            b.AguaKg += agua;
            b.MateriaSecaKg += seca;
            b.NitrogenoKg += n;
            b.CarbonoKg += n * cn;
        }

        // La última medición real de humedad manda; solo se mezcla lo que entró después de ella
        static float? HumedadActual(Lote l, List<Aporte> aportes, long hastaUtc)
        {
            InformeCampo ultima = null;
            foreach (var r in AnalisisCampo.Ordenados(l)) if (r.FechaUtc <= hastaUtc) ultima = r;
            if (ultima == null || ultima.PesoKg <= 0f) return null;
            double agua = ultima.PesoKg * ultima.Humedad / 100.0, masa = ultima.PesoKg;
            foreach (var a in aportes)
                if (!a.EsCargaInicial && a.FechaUtc > ultima.FechaUtc && a.FechaUtc <= hastaUtc)
                {
                    agua += a.Kg * Materiales.HumedadDe(a) / 100.0;
                    masa += a.Kg;
                }
            return (float)(agua / masa * 100.0);
        }

        // Kilos de un material que llevan la mezcla a la C:N objetivo. 0 si ese material no la acerca.
        //   (C + x·s·c) / (N + x·s·n) = objetivo  →  x = (objetivo·N − C) / (s·(c − objetivo·n))
        public static float KgParaObjetivo(BalanceMezcla b, MaterialEspecifico material, float objetivo = CNObjetivo)
        {
            if (b == null || b.NitrogenoKg <= 0) return 0f;
            var d = Materiales.De(material);
            double s = 1.0 - d.HumedadTipica / 100.0;
            double n = d.NitrogenoPct / 100.0, c = n * d.RelacionCN;
            double denominador = s * (c - objetivo * n);
            double numerador = objetivo * b.NitrogenoKg - b.CarbonoKg;
            if (Math.Abs(denominador) < 1e-12) return 0f;
            double x = numerador / denominador;
            return x > 0 ? (float)Math.Ceiling(x * 10.0) / 10f : 0f;     // redondeo hacia arriba a 0.1 kg
        }

        public static List<Aporte> AportesOrdenados(Lote l)
        {
            var lista = new List<Aporte>();
            if (l?.Aportes != null) foreach (var a in l.Aportes) if (a != null && a.Kg > 0f) lista.Add(a);
            // Estable: a igual fecha se respeta el orden en que se registraron
            var idx = new Dictionary<Aporte, int>();
            for (int i = 0; i < lista.Count; i++) idx[lista[i]] = i;
            lista.Sort((x, y) => x.FechaUtc != y.FechaUtc ? x.FechaUtc.CompareTo(y.FechaUtc) : idx[x].CompareTo(idx[y]));
            return lista;
        }
    }
}
