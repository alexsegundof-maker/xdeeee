using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Exportacion;
using Composta.Core.Lotes;
using Composta.Core.Persistencia;
using NUnit.Framework;

namespace Composta.Tests
{
    // Aportaciones como motor de cálculo: balance C:N, humedad, madurez, recomendaciones, cosecha y datos DEMO
    public class AportacionesTests
    {
        static readonly DateTime Ahora = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        static Lote LoteConCarga(float kg, MaterialEspecifico m, DateTime inicio, TipoComposta tipo = TipoComposta.Domestica)
        {
            var l = Lote.Crear(PerfilComposta.Crear(tipo, kg, true), 0, "ana", inicio, ModoLote.Manual);
            l.Compostera = "Cubeta A";
            var r = EditorLote.AgregarAporte(l, kg, m, 0f, "Carga inicial", inicio, 0, "ana", inicio, true);
            Assert.IsTrue(r.Exito, r.Mensaje);
            return l;
        }

        static void Aportar(Lote l, float kg, MaterialEspecifico m, DateTime t, float humedad = 0f)
        {
            var r = EditorLote.AgregarAporte(l, kg, m, humedad, "", t, 0, "luis", t);
            Assert.IsTrue(r.Exito, r.Mensaje);
        }

        static void Medir(Lote l, float t, float h, float p, DateTime fecha)
        {
            var r = EditorLote.AgregarMedicion(l, t, h, p, "", fecha, "ana", fecha);
            Assert.IsTrue(r.Exito, r.Mensaje);
        }

        // ---------------- Materiales ----------------
        [Test]
        public void Materiales_AportesAnterioresSeResuelvenPorGrupo()
        {
            Assert.AreEqual(MaterialEspecifico.RestosCocina, Materiales.Resolver(new Aporte { Material = TipoMaterial.Verde }));
            Assert.AreEqual(MaterialEspecifico.Hojarasca, Materiales.Resolver(new Aporte { Material = TipoMaterial.Cafe }));
            Assert.AreEqual(MaterialEspecifico.MezclaGenerica, Materiales.Resolver(new Aporte { Material = TipoMaterial.Mixto }));
            Assert.AreEqual(80f, Materiales.HumedadDe(new Aporte { Especifico = MaterialEspecifico.RestosCocina }));
            Assert.AreEqual(66f, Materiales.HumedadDe(new Aporte { Especifico = MaterialEspecifico.RestosCocina, HumedadPct = 66f }));
            foreach (var d in Materiales.Todos) Assert.That(d.CarbonoPct, Is.InRange(35f, 60f), d.Nombre);
        }

        [TestCase("", true, 0f)]
        [TestCase("55", true, 55f)]
        [TestCase("62,5 %", true, 62.5f)]
        [TestCase("2", false, 0f)]
        [TestCase("120", false, 0f)]
        [TestCase("mucha", false, 0f)]
        public void Materiales_HumedadEscrita(string texto, bool ok, float esperado)
        {
            Assert.AreEqual(ok, Materiales.IntentarLeerHumedad(texto, out float h, out string e), e);
            Assert.AreEqual(esperado, h, 1e-4f);
        }

        // ---------------- Balance ----------------
        [Test]
        public void Balance_PonderadoPorMateriaSeca()
        {
            var l = LoteConCarga(10f, MaterialEspecifico.RestosCocina, Ahora.AddDays(-10));
            Aportar(l, 2f, MaterialEspecifico.Hojarasca, Ahora.AddDays(-9));
            var b = BalanceLote.Calcular(l);
            // restos: seca 2 kg → N 0.05, C 0.75; hojarasca: seca 1.6 kg → N 0.0144, C 0.792
            Assert.AreEqual(1.542 / 0.0644, b.RelacionCN, 1e-3);
            Assert.AreEqual(8.4 / 12.0 * 100.0, b.HumedadMezclaPct, 1e-3);
            Assert.AreEqual(12f, b.PesoEntradaKg, 1e-5f);
            Assert.AreEqual(10f, b.CargaBaseKg, 1e-5f);
            Assert.AreEqual(2f, b.AportadoKg, 1e-5f);
            Assert.AreEqual(NivelCN.AlgoBaja, b.Nivel);
        }

        [TestCase(MaterialEspecifico.Hojarasca)]
        [TestCase(MaterialEspecifico.Carton)]
        [TestCase(MaterialEspecifico.Aserrin)]
        public void Balance_KilosSugeridosLlevanAC_N30(MaterialEspecifico cafe)
        {
            var l = LoteConCarga(12f, MaterialEspecifico.RestosCocina, Ahora.AddDays(-5));
            var b = BalanceLote.Calcular(l);
            Assert.AreEqual(NivelCN.ExcesoNitrogeno, b.Nivel);
            float kg = BalanceLote.KgParaObjetivo(b, cafe);
            Assert.Greater(kg, 0f);
            Aportar(l, kg, cafe, Ahora.AddDays(-4));
            Assert.That(BalanceLote.Calcular(l).RelacionCN, Is.InRange(30f, 31f));
        }

        [Test]
        public void Balance_SugerenciaNoAplicaSiElMaterialNoAyuda()
        {
            var l = LoteConCarga(12f, MaterialEspecifico.RestosCocina, Ahora.AddDays(-5));
            Assert.AreEqual(0f, BalanceLote.KgParaObjetivo(BalanceLote.Calcular(l), MaterialEspecifico.Cesped));
        }

        [Test]
        public void Balance_LoteAnteriorSinCargaUsaLaPrimeraMedicion()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 0, "ana", Ahora.AddDays(-20), ModoLote.Manual);
            l.RelacionCN = 28f;
            Medir(l, 30f, 60f, 25f, Ahora.AddDays(-19));
            var b = BalanceLote.Calcular(l);
            Assert.AreEqual(25f, b.PesoEntradaKg, 1e-5f);
            Assert.AreEqual(28f, b.RelacionCN, 1e-3f);
            Assert.AreEqual("primera medición", b.OrigenBase);
        }

        [Test]
        public void Balance_HumedadActualParteDeLaUltimaMedicion()
        {
            var l = LoteConCarga(20f, MaterialEspecifico.MezclaGenerica, Ahora.AddDays(-10));
            Medir(l, 50f, 50f, 20f, Ahora.AddDays(-5));
            Aportar(l, 5f, MaterialEspecifico.RestosCocina, Ahora.AddDays(-2));
            Assert.AreEqual((10.0 + 4.0) / 25.0 * 100.0, BalanceLote.Calcular(l).HumedadActualPct.Value, 1e-3);
        }

        [Test]
        public void Balance_SinAportesNiPeso_SinDatos()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 0, "ana", Ahora, ModoLote.Manual);
            l.PesoKg = 0f;
            Assert.IsFalse(BalanceLote.Calcular(l).TieneDatos);
        }

        // ---------------- Madurez ----------------
        [Test]
        public void Madurez_ElVerdeTardioRecorreLaFecha()
        {
            var inicio = Ahora.AddDays(-80);
            var l = LoteConCarga(20f, MaterialEspecifico.MezclaGenerica, inicio);
            var antes = Maduracion.Estimar(l, Ahora);
            Assert.AreEqual(inicio.AddDays(84), antes.FechaMadurezUtc);
            Assert.IsFalse(antes.Reajustada);

            Aportar(l, 0.5f, MaterialEspecifico.RestosCocina, Ahora.AddDays(-10));      // 2.5 % de la pila: no cambia
            Assert.IsFalse(Maduracion.Estimar(l, Ahora).Reajustada);

            var t = Ahora.AddDays(-5);
            Aportar(l, 6.15f, MaterialEspecifico.RestosCocina, t);                       // 30 % de 20.5 kg: reinicia la fase activa
            var despues = Maduracion.Estimar(l, Ahora);
            Assert.IsTrue(despues.Reajustada);
            Assert.AreEqual(t.AddDays(42).Ticks, despues.FechaMadurezUtc.Ticks, TimeSpan.TicksPerMinute);
            Assert.AreEqual(37f, despues.DiasRestantes, 0.01f);
            StringAssert.Contains("recorrió", despues.Motivo);
        }

        [Test]
        public void Madurez_MaterialCafeNoRecorreLaFecha()
        {
            var l = LoteConCarga(20f, MaterialEspecifico.MezclaGenerica, Ahora.AddDays(-80));
            Aportar(l, 15f, MaterialEspecifico.Hojarasca, Ahora.AddDays(-5));
            Assert.IsFalse(Maduracion.Estimar(l, Ahora).Reajustada);
        }

        // ---------------- Bloqueo de maduración ----------------
        [Test]
        public void Maduracion_BloqueaAportaciones_PeroPermiteCorregir()
        {
            var l = LoteConCarga(20f, MaterialEspecifico.MezclaGenerica, Ahora.AddDays(-30));
            Aportar(l, 2f, MaterialEspecifico.Hojarasca, Ahora.AddDays(-20));
            Assert.IsTrue(EditorLote.BloquearMaduracion(l, true, "ana", Ahora).Exito);
            var r = EditorLote.AgregarAporte(l, 1f, MaterialEspecifico.RestosCocina, 0f, "", Ahora, 0, "ana", Ahora);
            Assert.IsFalse(r.Exito);
            StringAssert.Contains("maduración", r.Mensaje);
            var a = l.Aportes.First(x => !x.EsCargaInicial);
            Assert.IsTrue(EditorLote.EditarAporte(l, a.Id, 2.5f, MaterialEspecifico.Hojarasca, 0f, "", new DateTime(a.FechaUtc, DateTimeKind.Utc), "ana", Ahora).Exito);
            Assert.IsTrue(EditorLote.BloquearMaduracion(l, false, "ana", Ahora).Exito);
            Assert.IsTrue(EditorLote.AgregarAporte(l, 1f, MaterialEspecifico.RestosCocina, 0f, "", Ahora, 0, "ana", Ahora).Exito);
        }

        [Test]
        public void CargaInicial_NoSeEliminaNiSeDuplica()
        {
            var l = LoteConCarga(20f, MaterialEspecifico.MezclaGenerica, Ahora.AddDays(-3));
            var carga = l.Aportes[0];
            Assert.IsFalse(EditorLote.EliminarAporte(l, carga.Id, "ana", Ahora).Exito);
            Assert.IsFalse(EditorLote.AgregarAporte(l, 5f, MaterialEspecifico.RestosCocina, 0f, "", Ahora, 0, "ana", Ahora, true).Exito);
            Assert.IsTrue(EditorLote.EditarAporte(l, carga.Id, 22f, MaterialEspecifico.MezclaGenerica, 60f, "", Ahora.AddDays(-3), "ana", Ahora).Exito);
            Assert.AreEqual(22f, BalanceLote.Calcular(l).PesoEntradaKg, 1e-4f);
            Assert.AreEqual(0f, l.TotalAportadoKg, "la carga inicial no cuenta como aportación");
        }

        [TestCase(1f)]
        [TestCase(96f)]
        [TestCase(float.NaN)]
        public void Aportacion_HumedadFueraDeRango_SeRechaza(float h)
        {
            var l = LoteConCarga(20f, MaterialEspecifico.MezclaGenerica, Ahora.AddDays(-3));
            Assert.IsFalse(EditorLote.AgregarAporte(l, 2f, MaterialEspecifico.Hojarasca, h, "", Ahora, 0, "ana", Ahora).Exito);
        }

        [Test]
        public void Aportacion_FechaFutura_SeRechaza()
            => Assert.IsFalse(EditorLote.AgregarAporte(LoteConCarga(20f, MaterialEspecifico.MezclaGenerica, Ahora.AddDays(-3)),
                                                       2f, MaterialEspecifico.Hojarasca, 0f, "", Ahora.AddHours(2), 0, "ana", Ahora).Exito);

        // ---------------- Cosecha ----------------
        static Lote LoteMaduro()
        {
            var l = LoteConCarga(30f, MaterialEspecifico.MezclaGenerica, Ahora.AddDays(-100));
            Aportar(l, 4f, MaterialEspecifico.RestosCocina, Ahora.AddDays(-90));
            Medir(l, 58f, 55f, 32f, Ahora.AddDays(-80));
            Medir(l, 26f, 45f, 16f, Ahora.AddDays(-3));
            return l;
        }

        [Test]
        public void Cosecha_RendimientoYCalidadA()
        {
            var l = LoteMaduro();
            var r = EditorLote.RegistrarCosecha(l, 15f, Ahora, true, true, true, "", "ana", Ahora);
            Assert.IsTrue(r.Exito, r.Mensaje);
            Assert.AreEqual(EstadoLote.Cosechado, l.Estado);
            var ev = EvaluacionCosecha.Evaluar(l);
            Assert.AreEqual(15f / 34f * 100f, ev.RendimientoPct, 1e-3f);
            Assert.AreEqual('A', ev.Calidad);
            Assert.AreEqual(6, ev.Puntos);
            Assert.AreEqual(100f, ev.DuracionDias, 0.01f);
            Assert.IsFalse(EditorLote.AgregarMedicion(l, 25f, 45f, 15f, "", Ahora, "ana", Ahora).Exito, "un lote cosechado no se modifica");
        }

        [Test]
        public void Cosecha_CalidadBajaConCriteriosIncumplidos()
        {
            var l = LoteMaduro();
            Assert.IsTrue(EditorLote.RegistrarCosecha(l, 30f, Ahora, false, false, true, "", "ana", Ahora).Exito);   // 88 % de rendimiento
            var ev = EvaluacionCosecha.Evaluar(l);
            Assert.AreEqual(3, ev.Puntos);       // textura + temperatura + humedad
            Assert.AreEqual('B', ev.Calidad);
            Assert.IsFalse(ev.Criterios.Single(c => c.Nombre == "Rendimiento esperado").Cumple);
        }

        [Test]
        public void Cosecha_Validaciones()
        {
            var l = LoteMaduro();
            Assert.IsFalse(EditorLote.RegistrarCosecha(l, 50f, Ahora, true, true, true, "", "ana", Ahora).Exito, "más de lo que entró");
            Assert.IsFalse(EditorLote.RegistrarCosecha(l, 0f, Ahora, true, true, true, "", "ana", Ahora).Exito);
            Assert.IsFalse(EditorLote.RegistrarCosecha(l, 10f, Ahora.AddDays(1), true, true, true, "", "ana", Ahora).Exito, "fecha futura");
            var sim = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 1, "ana", Ahora, ModoLote.Simular);
            Assert.IsFalse(EditorLote.RegistrarCosecha(sim, 10f, Ahora, true, true, true, "", "ana", Ahora).Exito);
            Assert.AreEqual(EstadoLote.Activo, l.Estado);
        }

        [Test]
        public void Cosecha_SeAnulaYElLoteVuelveAMaduracion()
        {
            var l = LoteMaduro();
            EditorLote.RegistrarCosecha(l, 15f, Ahora, true, true, true, "", "ana", Ahora);
            Assert.IsTrue(EditorLote.AnularCosecha(l, "luis", Ahora).Exito);
            Assert.AreEqual(EstadoLote.Activo, l.Estado);
            Assert.IsTrue(l.BloqueadoMaduracion);
            Assert.IsFalse(l.TieneCosecha);
            Assert.AreEqual(AccionCambio.Eliminacion, l.Cambios.Last().Accion);
        }

        // ---------------- Recomendaciones y análisis ----------------
        [Test]
        public void Recomendacion_ExcesoDeNitrogenoConKilos()
        {
            var l = LoteConCarga(15f, MaterialEspecifico.RestosCocina, Ahora.AddDays(-10));
            Medir(l, 50f, 60f, 15f, Ahora.AddDays(-9));
            Aportar(l, 6f, MaterialEspecifico.RestosCocina, Ahora.AddDays(-1));
            var e = AnalisisCampo.Analizar(l, Ahora);
            var rec = e.Recomendaciones.Single(r => r.Titulo.StartsWith("Exceso de nitrógeno"));
            float kg = BalanceLote.KgParaObjetivo(e.Balance, MaterialEspecifico.Hojarasca);
            StringAssert.Contains(kg.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kg de hojarasca", rec.Detalle);
            StringAssert.Contains("última aportación (6 kg de restos de cocina)", rec.Detalle);
            Assert.IsTrue(e.Recomendaciones.Any(r => r.Titulo.StartsWith("Las aportaciones humedecieron")));
        }

        [Test]
        public void Recomendacion_ExcesoDeCarbono()
        {
            var l = LoteConCarga(10f, MaterialEspecifico.Paja, Ahora.AddDays(-3));
            var e = AnalisisCampo.Analizar(l, Ahora);
            Assert.IsTrue(e.Recomendaciones.Any(r => r.Titulo.StartsWith("Exceso de carbono") && r.Detalle.Contains("restos de cocina")));
        }

        [Test]
        public void Recomendacion_TermofilaOptima()
        {
            var l = LoteConCarga(80f, MaterialEspecifico.MezclaGenerica, Ahora.AddDays(-10), TipoComposta.Cerrada);
            Medir(l, 60f, 55f, 78f, Ahora.AddDays(-1));
            var e = AnalisisCampo.Analizar(l, Ahora);
            Assert.IsTrue(e.Recomendaciones.Any(r => r.Titulo == "Temperatura óptima termófila"));
        }

        [Test]
        public void Analisis_PerdidaDePesoDescuentaAportaciones()
        {
            var l = LoteConCarga(20f, MaterialEspecifico.MezclaGenerica, Ahora.AddDays(-15));
            Medir(l, 50f, 55f, 20f, Ahora.AddDays(-14));
            Aportar(l, 10f, MaterialEspecifico.RestosCocina, Ahora.AddDays(-10));
            Medir(l, 55f, 55f, 27f, Ahora.AddDays(-7));
            var e = AnalisisCampo.Analizar(l, Ahora);
            Assert.AreEqual(10f, e.PerdidaPesoTotalPct.Value, 1e-3f, "(20 + 10 − 27) / 30");
        }

        // ---------------- Simulación ----------------
        [Test]
        public void Simulacion_AportacionSeReproduceIgualQueEnVivo()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 4321, "ana", Ahora, ModoLote.Simular);
            var vivo = new SistemaComposta(ReproductorLote.CrearPerfil(l), l.Semilla);
            for (int t = 0; t < 120; t++)
            {
                if (t == 60)
                {
                    var r = EditorLote.AgregarAporte(l, 40f, MaterialEspecifico.RestosCocina, 0f, "", Ahora, vivo.Modelo.Ticks, "ana", Ahora);
                    Assert.IsTrue(r.Exito, r.Mensaje);
                    l.Comandos.Add(new Comando { Tick = vivo.Modelo.Ticks, Tipo = TipoComando.Aporte, Valor = 40f, Usuario = "ana", FechaUtc = Ahora.Ticks, Ref = r.IdEntidad });
                    float masaAntes = vivo.Modelo.MasaKg;
                    ReproductorLote.AplicarAporte(vivo, l, r.IdEntidad);
                    Assert.AreEqual(masaAntes + 40f, vivo.Modelo.MasaKg, 1e-3f);
                }
                vivo.Paso();
            }
            l.TicksSimulados = vivo.Modelo.Ticks;
            var copia = EditorLote.Clonar(l);
            ReproductorLote.Sanear(copia);
            var rep = ReproductorLote.Reconstruir(copia, out _);
            Assert.AreEqual(vivo.Modelo.Temperatura, rep.Modelo.Temperatura, 1e-4f);
            Assert.AreEqual(vivo.Modelo.MasaKg, rep.Modelo.MasaKg, 1e-4f);
            Assert.AreEqual(vivo.Modelo.Humedad, rep.Modelo.Humedad, 1e-4f);

            // Si se elimina la aportación, la reconstrucción ya no la incluye
            Assert.IsTrue(EditorLote.EliminarAporte(copia, copia.Aportes[0].Id, "ana", Ahora).Exito);
            var sin = ReproductorLote.Reconstruir(copia, out _);
            Assert.Less(sin.Modelo.MasaKg, rep.Modelo.MasaKg - 30f);
        }

        // ---------------- Datos de demostración ----------------
        [Test]
        public void Demo_SieteLotesValidosConLosEscenariosPrometidos()
        {
            var lotes = DatosDemo.Generar(Ahora, 5);
            Assert.AreEqual(7, lotes.Count);
            Assert.IsTrue(lotes.All(l => l.Demo && l.Modo == ModoLote.Manual && !string.IsNullOrEmpty(l.Compostera)));
            CollectionAssert.AreEqual(Enumerable.Range(5, 7), lotes.Select(l => l.Numero));
            var activos = lotes.Where(l => l.Estado == EstadoLote.Activo).ToList();
            var cosechados = lotes.Where(l => l.TieneCosecha).ToList();
            Assert.AreEqual(3, activos.Count);
            Assert.AreEqual(4, cosechados.Count);
            CollectionAssert.AreEquivalent(new[] { 'A', 'B', 'A', 'C' }, cosechados.Select(l => EvaluacionCosecha.Evaluar(l).Calidad));

            var a = AnalisisCampo.Analizar(activos[0], Ahora);
            Assert.AreEqual(NivelCN.ExcesoNitrogeno, a.Balance.Nivel);
            Assert.IsTrue(a.Recomendaciones.Any(r => r.Titulo.StartsWith("Exceso de nitrógeno")));
            Assert.IsTrue(AnalisisCampo.Analizar(activos[1], Ahora).Recomendaciones.Any(r => r.Titulo == "Temperatura óptima termófila"));
            var c = AnalisisCampo.Analizar(activos[2], Ahora);
            Assert.IsTrue(c.Estabilizado);
            Assert.IsTrue(c.Recomendaciones.Any(r => r.Titulo.StartsWith("Lote estabilizado")));
        }

        [Test]
        public void Demo_SeGuardaYSeLeeSinPerderDatos()
        {
            var repo = new RepositorioLotesJson(new AlmacenMemoria(), new SerializadorPruebas());
            foreach (var l in DatosDemo.Generar(Ahora, 1)) repo.Guardar(l);
            var leidos = repo.Listar();
            Assert.AreEqual(7, leidos.Count);
            var g = leidos.Single(l => l.Compostera == "Cubeta G");
            Assert.IsTrue(g.TieneCosecha);
            Assert.AreEqual('C', EvaluacionCosecha.Evaluar(g).Calidad);
            Assert.IsTrue(g.Aportes.Any(x => x.EsCargaInicial && x.Especifico == MaterialEspecifico.RestosCocina));
        }

        [Test]
        public void Consolidado_IncluyeTodosLosLotes()
        {
            var lotes = DatosDemo.Generar(Ahora, 1);
            var bytes = ExportadorExcel.GenerarConsolidado(lotes, TimeZoneInfo.Utc, Ahora, "1.1.2");
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                XDocument Leer(string n) { using (var s = zip.GetEntry(n).Open()) return XDocument.Load(s); }
                var hojas = Leer("xl/workbook.xml").Descendants(m + "sheet").Select(s => (string)s.Attribute("name")).ToArray();
                CollectionAssert.AreEqual(new[] { "Lotes", "Mediciones", "Aportes", "Cambios", "Sobre este archivo" }, hojas);
                var hojaLotes = Leer("xl/worksheets/sheet1.xml");
                Assert.AreEqual(8, hojaLotes.Descendants(m + "row").Count());
                var estados = hojaLotes.Descendants(m + "c").Where(c => ((string)c.Attribute("r")).StartsWith("E") && (string)c.Attribute("r") != "E1")
                                       .Select(c => c.Value).ToList();
                Assert.AreEqual(4, estados.Count(e => e == "Cosechado"), string.Join(", ", estados));
                Assert.AreEqual(3, estados.Count(e => e.StartsWith("Activo")));
                int aportes = lotes.Sum(l => l.Aportes.Count);
                Assert.AreEqual(aportes + 1, Leer("xl/worksheets/sheet3.xml").Descendants(m + "row").Count());
            }
        }
    }
}
