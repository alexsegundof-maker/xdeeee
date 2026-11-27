using System;
using System.Collections.Generic;

namespace Composta.Core.Lotes
{
    // Lotes de DEMOSTRACIÓN para probar el historial, las recomendaciones y las bitácoras.
    // Se construyen con las mismas operaciones validadas que usan las personas (EditorLote): si un dato de
    // ejemplo no pasara las validaciones, la generación falla en vez de guardar algo inconsistente.
    // Todas las fechas son relativas a "ahora" y quedan dentro del último año.
    public static class DatosDemo
    {
        public const string UsuarioA = "ana.demo", UsuarioB = "luis.demo";

        sealed class Plan
        {
            public string Compostera, Nombre;
            public TipoComposta Tipo;
            public float RelacionCN = 28f, Aislamiento = 1f;
            public int DiasAtras;                                         // inicio del lote
            public (float kg, MaterialEspecifico m) Carga;
            public readonly List<(int dia, float kg, MaterialEspecifico m, string nota)> Aportes = new List<(int, float, MaterialEspecifico, string)>();
            public readonly List<(int dia, float t, float h, float p, string nota)> Mediciones = new List<(int, float, float, float, string)>();
            public readonly List<(int dia, float kg, MaterialEspecifico m)> CargaExtra = new List<(int, float, MaterialEspecifico)>();
            public bool Madurando;
            public (int dia, float kg, bool olor, bool restos, bool textura)? Cosecha;
        }

        public static List<Lote> Generar(DateTime ahoraUtc, int numeroInicial)
        {
            var lotes = new List<Lote>();
            int n = numeroInicial;
            foreach (var p in Planes()) lotes.Add(Construir(p, ahoraUtc, n++));
            return lotes;
        }

        static IEnumerable<Plan> Planes()
        {
            // 1 · Activa con exceso de nitrógeno tras una aportación grande de restos de cocina
            var a = new Plan { Compostera = "Cubeta A", Nombre = "Cubeta A · patio de la escuela", Tipo = TipoComposta.Domestica, RelacionCN = 18f, DiasAtras = 35 };
            a.Carga = (15f, MaterialEspecifico.RestosCocina);
            a.CargaExtra.Add((0, 2f, MaterialEspecifico.Hojarasca));
            a.Aportes.Add((5, 4f, MaterialEspecifico.RestosCocina, "Restos del comedor"));
            a.Aportes.Add((33, 8f, MaterialEspecifico.RestosCocina, "Fruta y verdura del mercado"));
            a.Mediciones.Add((1, 28f, 62f, 17f, ""));
            a.Mediciones.Add((8, 54f, 58f, 19f, "Ya se siente caliente"));
            a.Mediciones.Add((15, 61f, 56f, 17.6f, "Se volteó"));
            a.Mediciones.Add((22, 58f, 55f, 16.3f, ""));
            a.Mediciones.Add((29, 56f, 57f, 15.2f, ""));
            yield return a;

            // 2 · Activa en fase termófila óptima, C:N equilibrada
            var b = new Plan { Compostera = "Compostero B", Nombre = "Compostero B · huerto comunitario", Tipo = TipoComposta.Cerrada, RelacionCN = 28f, Aislamiento = 1.3f, DiasAtras = 20 };
            b.Carga = (80f, MaterialEspecifico.MezclaGenerica);
            b.Aportes.Add((6, 10f, MaterialEspecifico.Hojarasca, "Hojas del parque"));
            b.Aportes.Add((12, 12f, MaterialEspecifico.RestosCocina, ""));
            b.Mediciones.Add((1, 32f, 58f, 80f, ""));
            b.Mediciones.Add((7, 58f, 55f, 86.5f, ""));
            b.Mediciones.Add((14, 62f, 54f, 92f, "Se volteó"));
            b.Mediciones.Add((19, 60f, 53f, 88f, ""));
            yield return b;

            // 3 · Activa y estabilizada: la app sugiere iniciar la maduración
            var c = new Plan { Compostera = "Tambor C", Nombre = "Tambor C · centro vecinal", Tipo = TipoComposta.PilaAbierta, RelacionCN = 28f, DiasAtras = 70 };
            c.Carga = (300f, MaterialEspecifico.MezclaGenerica);
            c.Aportes.Add((7, 40f, MaterialEspecifico.Hojarasca, "Poda de otoño"));
            c.Aportes.Add((19, 30f, MaterialEspecifico.RestosCocina, ""));
            float[] tc = { 30f, 55f, 64f, 62f, 58f, 50f, 42f, 36f, 31f, 29f };
            float[] hc = { 60f, 57f, 55f, 54f, 52f, 50f, 49f, 48f, 47f, 46f };
            float[] pc = { 300f, 330f, 318f, 335f, 318f, 300f, 285f, 276f, 270f, 266f };
            for (int i = 0; i < tc.Length; i++) c.Mediciones.Add((1 + i * 7, tc[i], hc[i], pc[i], i == 4 ? "Se volteó con pala" : ""));
            yield return c;

            // 4 · Cosechada, calidad A
            var d = new Plan { Compostera = "Cubeta D", Nombre = "Cubeta D · primavera", Tipo = TipoComposta.Domestica, RelacionCN = 28f, DiasAtras = 220 };
            d.Carga = (20f, MaterialEspecifico.RestosCocina);
            d.CargaExtra.Add((0, 6f, MaterialEspecifico.Hojarasca));
            d.Aportes.Add((10, 4f, MaterialEspecifico.RestosCocina, ""));
            d.Aportes.Add((12, 2f, MaterialEspecifico.Carton, "Cartón picado"));
            d.Aportes.Add((25, 2f, MaterialEspecifico.PososCafe, ""));
            float[] td = { 26f, 50f, 60f, 57f, 49f, 41f, 33f, 28f, 25f, 24f, 24f, 23f, 24f };
            float[] hd = { 60f, 58f, 55f, 54f, 52f, 50f, 48f, 47f, 46f, 45f, 45f, 44f, 45f };
            float[] pd = { 26f, 27.5f, 29f, 27f, 24.5f, 22f, 20f, 18.4f, 17f, 16f, 15.2f, 14.6f, 14.3f };
            for (int i = 0; i < td.Length; i++) d.Mediciones.Add((1 + i * 7, td[i], hd[i], pd[i], ""));
            d.Madurando = true;
            d.Cosecha = (90, 14f, true, true, true);
            yield return d;

            // 5 · Cosechada, calidad B: rendimiento bajo (se secó demasiado y se tamizó mucho)
            var e = new Plan { Compostera = "Compostero E", Nombre = "Compostero E · invierno", Tipo = TipoComposta.Cerrada, RelacionCN = 28f, Aislamiento = 1.3f, DiasAtras = 300 };
            e.Carga = (90f, MaterialEspecifico.MezclaGenerica);
            e.Aportes.Add((9, 15f, MaterialEspecifico.RestosCocina, ""));
            e.Aportes.Add((16, 15f, MaterialEspecifico.Hojarasca, ""));
            float[] te = { 30f, 55f, 60f, 54f, 45f, 37f, 31f, 28f, 26f, 26f, 25f, 26f };
            float[] he = { 58f, 55f, 52f, 50f, 48f, 46f, 45f, 44f, 43f, 42f, 41f, 40f };
            float[] pe = { 90f, 98f, 104f, 92f, 80f, 70f, 61f, 54f, 48f, 44f, 41f, 39f };
            for (int i = 0; i < te.Length; i++) e.Mediciones.Add((1 + i * 7, te[i], he[i], pe[i], ""));
            e.Madurando = true;
            e.Cosecha = (88, 30f, true, true, false);
            yield return e;

            // 6 · Cosechada, calidad A: pila grande
            var f = new Plan { Compostera = "Tambor F", Nombre = "Tambor F · jornada vecinal", Tipo = TipoComposta.PilaAbierta, RelacionCN = 28f, DiasAtras = 330 };
            f.Carga = (350f, MaterialEspecifico.MezclaGenerica);
            f.Aportes.Add((8, 50f, MaterialEspecifico.Paja, "Paja de la feria"));
            f.Aportes.Add((15, 50f, MaterialEspecifico.Estiercol, "Estiércol de caballo"));
            float[] tf = { 28f, 56f, 65f, 63f, 60f, 54f, 47f, 40f, 34f, 30f, 28f, 27f, 26f, 26f, 25f, 25f, 24f, 25f, 24f };
            for (int i = 0; i < tf.Length; i++)
                f.Mediciones.Add((1 + i * 7, tf[i], Math.Max(44f, 60f - i * 0.9f), Math.Max(200f, 350f + (i >= 2 ? 100f : i == 1 ? 50f : 0f) - i * 14f), ""));
            f.Madurando = true;
            f.Cosecha = (130, 200f, true, true, true);
            yield return f;

            // 7 · Cosechada, calidad C: vermicomposta húmeda e inmadura
            var g = new Plan { Compostera = "Cubeta G", Nombre = "Cubeta G · lombricomposta", Tipo = TipoComposta.Vermicomposta, RelacionCN = 28f, DiasAtras = 180 };
            g.Carga = (18f, MaterialEspecifico.RestosCocina);
            g.CargaExtra.Add((0, 3f, MaterialEspecifico.Papel));
            g.Aportes.Add((14, 4f, MaterialEspecifico.RestosCocina, ""));
            float[] tg = { 21f, 23f, 24f, 23f, 22f, 22f, 23f, 22f, 22f, 22f, 22f };
            float[] hg = { 70f, 68f, 67f, 66f, 65f, 64f, 64f, 63f, 63f, 62f, 62f };
            float[] pg = { 21f, 20.5f, 23f, 22f, 21f, 20f, 19f, 18.2f, 17.5f, 17f, 16.6f };
            for (int i = 0; i < tg.Length; i++) g.Mediciones.Add((1 + i * 7, tg[i], hg[i], pg[i], ""));
            g.Cosecha = (75, 16f, false, false, true);
            yield return g;
        }

        static Lote Construir(Plan p, DateTime ahoraUtc, int numero)
        {
            DateTime Dia(int d, int hora = 10) => ahoraUtc.Date.AddDays(-p.DiasAtras + d).AddHours(hora);
            var perfil = PerfilComposta.Crear(p.Tipo, p.Carga.kg, true);
            perfil.RelacionCN = p.RelacionCN;
            perfil.Aislamiento = p.Aislamiento;
            // Horas en UTC pensadas para caer de día en América (UTC−5 a UTC−7)
            var inicio = Dia(0, 15);
            var l = Lote.Crear(perfil, 0, UsuarioA, inicio, ModoLote.Manual);
            l.Nombre = "Demo · " + p.Nombre;
            l.Compostera = p.Compostera;
            l.Numero = numero;
            l.Demo = true;

            void Ok(ResultadoEdicion r, string que)
            {
                if (!r.Exito) throw new InvalidOperationException($"Datos de demostración inválidos ({p.Compostera}, {que}): {r.Mensaje}");
            }

            Ok(EditorLote.AgregarAporte(l, p.Carga.kg, p.Carga.m, 0f, "Carga inicial", inicio, 0, UsuarioA, inicio, true), "carga inicial");
            foreach (var x in p.CargaExtra) Ok(EditorLote.AgregarAporte(l, x.kg, x.m, 0f, "Material de la carga inicial", Dia(x.dia, 15), 0, UsuarioA, Dia(x.dia, 15)), "carga");

            // Aportaciones y mediciones en orden cronológico (como se registrarían en la vida real)
            var eventos = new List<(DateTime t, Action accion)>();
            foreach (var x in p.Aportes)
            {
                var t = Dia(x.dia, 22);
                var copia = x;
                eventos.Add((t, () => Ok(EditorLote.AgregarAporte(l, copia.kg, copia.m, 0f, copia.nota, t, 0, UsuarioB, t), "aportación")));
            }
            for (int i = 0; i < p.Mediciones.Count; i++)
            {
                var x = p.Mediciones[i];
                var t = Dia(x.dia, 16);
                string usuario = i % 2 == 0 ? UsuarioA : UsuarioB;
                eventos.Add((t, () => Ok(EditorLote.AgregarMedicion(l, x.t, x.h, x.p, x.nota, t, usuario, t), "medición")));
            }
            eventos.Sort((x, y) => x.t.CompareTo(y.t));
            foreach (var ev in eventos) ev.accion();

            if (p.Cosecha.HasValue)
            {
                var c = p.Cosecha.Value;
                var t = Dia(c.dia, 18);
                if (p.Madurando) Ok(EditorLote.BloquearMaduracion(l, true, UsuarioA, Dia(c.dia - 21, 18)), "maduración");
                Ok(EditorLote.RegistrarCosecha(l, c.kg, t, c.olor, c.restos, c.textura, "Cosecha de demostración", UsuarioA, t), "cosecha");
            }
            else if (p.Madurando) Ok(EditorLote.BloquearMaduracion(l, true, UsuarioA, ahoraUtc), "maduración");

            l.ActualizadoUtc = Math.Min(ahoraUtc.Ticks, Math.Max(l.ActualizadoUtc, inicio.Ticks));
            return l;
        }
    }
}
