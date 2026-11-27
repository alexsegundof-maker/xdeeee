using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core.Lotes;

namespace Composta.Core.Campo
{
    public enum NivelEstado { Ok, Aviso, Critico, SinDato }

    public sealed class Recomendacion
    {
        public NivelEstado Nivel;
        public string Titulo;
        public string Detalle;
    }

    // Resultado del análisis: SOLO derivado de mediciones reales (nada simulado)
    public sealed class EstadoCampo
    {
        public int Registros;
        public InformeCampo Ultimo, Anterior;
        public float DiasDesdeInicio;              // del primer al último registro
        public Etapa? Fase;                        // null = sin datos suficientes
        public string ExplicacionFase = "";
        public float? DeltaTemp, DeltaHum;          // frente al registro anterior
        public float? PerdidaPesoTotalPct;         // desde el primer registro
        public float? PerdidaSemanalPct;           // ritmo del último tramo, normalizado a 7 días
        public NivelEstado NivelTemp = NivelEstado.SinDato, NivelHum = NivelEstado.SinDato, NivelPeso = NivelEstado.SinDato;
        public readonly List<Recomendacion> Recomendaciones = new List<Recomendacion>();
        public BalanceMezcla Balance = new BalanceMezcla();       // C:N, humedad y peso de entrada (con aportaciones)
        public EstimacionMaduracion Madurez;                       // null si no se indicó la fecha actual
        public bool Estabilizado;                                  // listo para iniciar la maduración
    }

    // Interpreta los registros de campo con reglas de compostaje conocidas.
    // Separado del simulador a propósito (SRP): el modo Control Manual nunca genera datos sintéticos.
    public static class AnalisisCampo
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static List<InformeCampo> Ordenados(Lote l)
        {
            var lista = new List<InformeCampo>(l?.Informes ?? new List<InformeCampo>());
            lista.RemoveAll(r => r == null);
            lista.Sort((a, b) => a.FechaUtc.CompareTo(b.FechaUtc));
            return lista;
        }

        public static float Dias(InformeCampo desde, InformeCampo hasta)
            => (float)new TimeSpan(hasta.FechaUtc - desde.FechaUtc).TotalDays;

        public static EstadoCampo Analizar(Lote lote) => Analizar(lote, null);

        // ahoraUtc: necesario para los días que faltan a la madurez
        public static EstadoCampo Analizar(Lote lote, DateTime? ahoraUtc)
        {
            var e = new EstadoCampo();
            var regs = Ordenados(lote);
            e.Registros = regs.Count;
            if (lote == null) return e;
            e.Balance = BalanceLote.Calcular(lote);
            if (ahoraUtc.HasValue) e.Madurez = Maduracion.Estimar(lote, ahoraUtc.Value);
            if (regs.Count == 0)
            {
                RecomendarBalance(e, PerfilComposta.Crear(lote.Tipo, lote.PesoKg, true), lote);
                return e;
            }

            var perfil = PerfilComposta.Crear(lote.Tipo, lote.PesoKg, true);
            var primero = regs[0];
            e.Ultimo = regs[regs.Count - 1];
            e.Anterior = regs.Count > 1 ? regs[regs.Count - 2] : null;
            e.DiasDesdeInicio = Dias(primero, e.Ultimo);

            if (e.Anterior != null)
            {
                e.DeltaTemp = e.Ultimo.Temperatura - e.Anterior.Temperatura;
                e.DeltaHum = e.Ultimo.Humedad - e.Anterior.Humedad;
                float dias = Math.Max(0.5f, Dias(e.Anterior, e.Ultimo));
                // Lo que se aportó entre dos mediciones no es "ganancia": se suma a la referencia
                float refAnterior = e.Anterior.PesoKg + AportadoEntre(lote, e.Anterior.FechaUtc, e.Ultimo.FechaUtc);
                if (refAnterior > 0f)
                    e.PerdidaSemanalPct = (refAnterior - e.Ultimo.PesoKg) / refAnterior * 100f * 7f / dias;
            }
            float refInicio = primero.PesoKg + AportadoEntre(lote, primero.FechaUtc, e.Ultimo.FechaUtc);
            if (refInicio > 0f) e.PerdidaPesoTotalPct = (refInicio - e.Ultimo.PesoKg) / refInicio * 100f;

            EvaluarNiveles(e, perfil);
            DeducirFase(e, regs, perfil);
            Recomendar(e, perfil, lote);
            return e;
        }

        // kg aportados (sin la carga inicial) con fecha en (desde, hasta]
        public static float AportadoEntre(Lote l, long desde, long hasta)
        {
            float kg = 0f;
            if (l?.Aportes != null)
                foreach (var a in l.Aportes)
                    if (a != null && !a.EsCargaInicial && a.FechaUtc > desde && a.FechaUtc <= hasta) kg += a.Kg;
            return kg;
        }

        static void EvaluarNiveles(EstadoCampo e, PerfilComposta p)
        {
            var u = e.Ultimo;
            e.NivelTemp = Nivel(u.Temperatura, p.LimitesDe(Magnitud.Temperatura));
            e.NivelHum = Nivel(u.Humedad, p.LimitesDe(Magnitud.Humedad));
            var np = p.EvaluarPeso(u.PesoKg);
            e.NivelPeso = np == Core.NivelPeso.Exagerado ? NivelEstado.Critico
                        : np == Core.NivelPeso.FueraDeOptimo ? NivelEstado.Aviso : NivelEstado.Ok;
        }

        static NivelEstado Nivel(float v, Limite l)
            => v < l.CriticoMin || v > l.CriticoMax ? NivelEstado.Critico
             : v < l.AvisoMin || v > l.AvisoMax ? NivelEstado.Aviso : NivelEstado.Ok;

        // Fase estimada con reglas: temperatura actual, si ya hubo fase caliente, tiempo y ritmo de pérdida de peso
        static void DeducirFase(EstadoCampo e, List<InformeCampo> regs, PerfilComposta p)
        {
            float t = e.Ultimo.Temperatura;
            bool huboCaliente = false;
            foreach (var r in regs) if (r.Temperatura >= 45f) huboCaliente = true;
            bool perdidaLenta = e.PerdidaSemanalPct.HasValue && e.PerdidaSemanalPct.Value < 1.5f;
            bool vermi = p.Tipo == TipoComposta.Vermicomposta;

            if (!vermi && t >= 45f)
            {
                e.Fase = Etapa.Termofilica;
                e.ExplicacionFase = "Más de 45 °C: los microbios termófilos están trabajando a pleno.";
            }
            else if (regs.Count >= 2 && e.DiasDesdeInicio >= 21f && perdidaLenta && t < 35f)
            {
                e.Fase = Etapa.Maduracion;
                e.ExplicacionFase = "Temperatura baja y el peso casi no cambia: el material se está estabilizando.";
            }
            else if (huboCaliente)
            {
                e.Fase = Etapa.Enfriamiento;
                e.ExplicacionFase = "Ya pasó por la fase caliente y ahora se está enfriando.";
            }
            else
            {
                e.Fase = Etapa.Mesofilica;
                e.ExplicacionFase = vermi
                    ? "Vermicomposta: proceso frío; las lombrices trabajan entre 15 y 25 °C."
                    : regs.Count == 1 ? "Primer registro: se confirmará la fase con la próxima medición."
                                      : "Todavía no supera 45 °C: fase inicial de calentamiento.";
            }
        }

        static void Recomendar(EstadoCampo e, PerfilComposta p, Lote lote)
        {
            var u = e.Ultimo;
            var lh = p.LimitesDe(Magnitud.Humedad);
            var lt = p.LimitesDe(Magnitud.Temperatura);
            void Add(NivelEstado n, string titulo, string detalle) => e.Recomendaciones.Add(new Recomendacion { Nivel = n, Titulo = titulo, Detalle = detalle });

            if (u.Humedad < lh.AvisoMin)
                Add(u.Humedad < lh.CriticoMin ? NivelEstado.Critico : NivelEstado.Aviso, "Regar",
                    string.Format(C, "Humedad {0:F0} % (ideal {1:F0}–{2:F0} %). Sin agua los microbios se detienen.", u.Humedad, lh.AvisoMin, lh.AvisoMax));
            else if (u.Humedad > lh.AvisoMax)
                Add(u.Humedad > lh.CriticoMax ? NivelEstado.Critico : NivelEstado.Aviso, "Voltear y agregar material seco",
                    string.Format(C, "Humedad {0:F0} %: falta oxígeno y aparecen malos olores. Agrega hojas secas o cartón.", u.Humedad));

            if (u.Temperatura > lt.AvisoMax)
                Add(u.Temperatura > lt.CriticoMax ? NivelEstado.Critico : NivelEstado.Aviso, "Voltear para bajar la temperatura",
                    string.Format(C, "{0:F0} °C supera el máximo recomendado ({1:F0} °C) para {2}.", u.Temperatura, lt.AvisoMax, p.Nombre));

            if (p.Tipo != TipoComposta.Vermicomposta && e.Fase == Etapa.Mesofilica && e.Registros >= 2 && e.DiasDesdeInicio >= 10f && u.Temperatura < 35f)
                Add(NivelEstado.Aviso, "La pila no se calienta",
                    "Tras más de 10 días sigue fría. Agrega material verde (restos de cocina, pasto), revisa la humedad y voltea.");

            if (e.PerdidaSemanalPct.HasValue && e.PerdidaSemanalPct.Value < 0.5f && e.Fase != Etapa.Maduracion && u.Temperatura < 40f)
                Add(NivelEstado.Aviso, "El proceso parece estancado",
                    "El peso casi no bajó en la última semana. Voltear airea la pila y reactiva la descomposición.");

            if (e.NivelPeso == NivelEstado.Critico)
                Add(NivelEstado.Critico, "PESO EXAGERADO",
                    string.Format(C, "{0:0.#} kg está fuera de los límites físicos de {1} ({2:0.#}–{3:0.#} kg). Revisa la medición o el tipo de compostera.",
                                  u.PesoKg, p.Nombre, p.ExtremoMin, p.ExtremoMax));
            else if (e.NivelPeso == NivelEstado.Aviso)
                Add(NivelEstado.Ok, "Peso fuera del rango ideal",
                    string.Format(C, "{0:0.#} kg funciona, pero el rango ideal para {1} es {2:0.#}–{3:0.#} kg.", u.PesoKg, p.Nombre, p.PesoMin, p.PesoMax));

            e.Estabilizado = e.Fase == Etapa.Maduracion;
            if (e.Estabilizado && !lote.BloqueadoMaduracion)
                Add(NivelEstado.Aviso, "Lote estabilizado: evita nuevas aportaciones",
                    "La temperatura bajó y el peso ya casi no cambia. Toca «Iniciar maduración» para que no entren más residuos.");
            else if (lote.BloqueadoMaduracion)
                Add(NivelEstado.Ok, "En maduración",
                    "Deja reposar 2–4 semanas sin voltear. Cuando huela a tierra, no se distingan los restos y la temperatura sea la ambiente, toca «Registrar cosecha».");

            RecomendarBalance(e, p, lote);

            bool termofilaOptima = p.Tipo != TipoComposta.Vermicomposta && u.Temperatura >= 55f && u.Temperatura <= 65f &&
                                   u.Humedad >= lh.AvisoMin && u.Humedad <= lh.AvisoMax &&
                                   (e.Balance.Nivel == NivelCN.Optima || e.Balance.Nivel == NivelCN.AlgoBaja || e.Balance.Nivel == NivelCN.AlgoAlta);
            if (termofilaOptima)
                Add(NivelEstado.Ok, "Temperatura óptima termófila",
                    string.Format(C, "{0:F0} °C con C:N ≈ {1:F0}: mantén la humedad entre {2:F0} y {3:F0} % y voltea si pasa de {4:F0} °C.",
                                  u.Temperatura, e.Balance.RelacionCN, lh.AvisoMin, lh.AvisoMax, lt.AvisoMax));

            if (e.Recomendaciones.Count == 0)
                Add(NivelEstado.Ok, "Todo en orden", "Los valores medidos están dentro de lo recomendado. Sigue registrando cada semana.");
        }

        // Recomendaciones que salen de lo que ENTRÓ al lote (carga inicial + aportaciones)
        static void RecomendarBalance(EstadoCampo e, PerfilComposta p, Lote lote)
        {
            var b = e.Balance;
            void Add(NivelEstado n, string titulo, string detalle) => e.Recomendaciones.Add(new Recomendacion { Nivel = n, Titulo = titulo, Detalle = detalle });
            if (!b.TieneDatos) return;

            string causa = "";
            var ua = b.UltimaAportacion;
            if (ua != null)
            {
                var d = Materiales.De(Materiales.Resolver(ua));
                causa = string.Format(C, " tras la última aportación ({0:0.#} kg de {1})", ua.Kg, d.Nombre.ToLowerInvariant());
            }

            if (b.Nivel == NivelCN.ExcesoNitrogeno)
            {
                float hojas = BalanceLote.KgParaObjetivo(b, MaterialEspecifico.Hojarasca);
                float carton = BalanceLote.KgParaObjetivo(b, MaterialEspecifico.Carton);
                Add(b.RelacionCN < 15f ? NivelEstado.Critico : NivelEstado.Aviso, string.Format(C, "Exceso de nitrógeno (C:N ≈ {0:F0})", b.RelacionCN),
                    string.Format(C, "La mezcla quedó baja en carbono{0}. Agrega {1:0.#} kg de hojarasca seca (o {2:0.#} kg de cartón) y voltea para llegar a C:N 30 y evitar olor a amoniaco.",
                                  causa, hojas, carton));
            }
            else if (b.Nivel == NivelCN.ExcesoCarbono)
            {
                float verde = BalanceLote.KgParaObjetivo(b, MaterialEspecifico.RestosCocina);
                Add(NivelEstado.Aviso, string.Format(C, "Exceso de carbono (C:N ≈ {0:F0})", b.RelacionCN),
                    string.Format(C, "Hay mucho material café{0}: la pila tardará en calentarse. Agrega {1:0.#} kg de restos de cocina o césped y humedece.",
                                  causa, verde));
            }

            // Humedad de lo que entró después de la última medición
            if (b.HumedadActualPct.HasValue && b.UltimaAportacion != null && e.Ultimo != null && b.UltimaAportacion.FechaUtc > e.Ultimo.FechaUtc)
            {
                var lh = p.LimitesDe(Magnitud.Humedad);
                float h = b.HumedadActualPct.Value;
                if (h > lh.AvisoMax)
                    Add(NivelEstado.Aviso, string.Format(C, "Las aportaciones humedecieron la pila (≈{0:F0} %)", h),
                        "Agrega material seco (hojarasca, cartón) junto con los residuos húmedos y confirma con la próxima medición.");
                else if (h < lh.AvisoMin)
                    Add(NivelEstado.Aviso, string.Format(C, "Las aportaciones secaron la pila (≈{0:F0} %)", h),
                        "Riega un poco al mezclar el material seco y confirma con la próxima medición.");
            }

            if (e.Madurez != null && e.Madurez.Reajustada && !lote.BloqueadoMaduracion)
                Add(NivelEstado.Ok, "Fecha de madurez recorrida", e.Madurez.Motivo +
                    string.Format(C, " Nueva fecha estimada: {0:dd/MM/yyyy}.", e.Madurez.FechaMadurezUtc));
        }
    }
}
