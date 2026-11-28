using System;
using System.Linq;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Pronostico;
using NUnit.Framework;

namespace Composta.Tests
{
    // Diagnóstico y recomendaciones de la ventana de Predicción, a partir de los datos reales del lote
    public class DiagnosticoTests
    {
        static readonly DateTime Ahora = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        static string Dia(float d) => "DIA" + Math.Round(d).ToString(System.Globalization.CultureInfo.InvariantCulture);

        static Lote LoteManual(float carga, MaterialEspecifico m, TipoComposta tipo = TipoComposta.Cerrada)
        {
            var l = Lote.Crear(PerfilComposta.Crear(tipo, carga, true), 0, "ana", Ahora.AddDays(-10), ModoLote.Manual);
            l.Compostera = "Compostero B";
            Assert.IsTrue(EditorLote.AgregarAporte(l, carga, m, 0f, "", Ahora.AddDays(-10), 0, "ana", Ahora.AddDays(-10), true).Exito);
            return l;
        }

        static void Medir(Lote l, float t, float h, float p, DateTime f)
            => Assert.IsTrue(EditorLote.AgregarMedicion(l, t, h, p, "", f, "ana", f).Exito);

        [Test]
        public void Plan_SinLote_EvaluaParametrosElegidos()
        {
            var perfil = PerfilComposta.Crear(TipoComposta.Cerrada, 88f, true);
            var p = MotorPronostico.DesdePerfil(perfil);
            var d = GeneradorDiagnostico.Generar(p, null, Ahora, Dia);
            Assert.IsFalse(d.ConDatosReales);
            StringAssert.Contains("parámetros elegidos", d.Origen);
            CollectionAssert.AreEqual(new[] { "Peso", "Humedad", "Temperatura", "Balance C:N" }, d.Estado.Select(e => e.Nombre));
            Assert.AreEqual("88 kg", d.Estado[0].Valor);
            Assert.LessOrEqual(d.Acciones.Count, DiagnosticoPronostico.MaxAcciones);
        }

        [Test]
        public void Lote_UsaLaUltimaMedicionReal()
        {
            var l = LoteManual(90f, MaterialEspecifico.MezclaGenerica);
            Medir(l, 45f, 53f, 88f, Ahora.AddDays(-1));
            var d = GeneradorDiagnostico.Generar(MotorPronostico.DesdeMediciones(l), l, Ahora, Dia);
            Assert.IsTrue(d.ConDatosReales);
            StringAssert.Contains("última medición (DIA-1)", d.Origen);
            Assert.AreEqual("88 kg", d.Estado[0].Valor);
            Assert.AreEqual("53 %", d.Estado[1].Valor);
            Assert.AreEqual(NivelEstado.Ok, d.Estado[1].Nivel);
            StringAssert.StartsWith("45 °C hoy", d.Estado[2].Valor);
        }

        [Test]
        public void HumedadCritica_PrimeraAccionYAlerta()
        {
            var l = LoteManual(90f, MaterialEspecifico.MezclaGenerica);
            Medir(l, 40f, 25f, 88f, Ahora.AddDays(-1));
            var d = GeneradorDiagnostico.Generar(MotorPronostico.DesdeMediciones(l), l, Ahora, Dia);
            Assert.AreEqual(NivelEstado.Critico, d.Estado[1].Nivel);
            StringAssert.StartsWith("Riega hoy", d.Acciones[0].Texto);
            Assert.IsTrue(d.Alertas.Any(a => a.Nivel == NivelEstado.Critico && a.Texto.Contains("25 %")));
        }

        [Test]
        public void ExcesoDeNitrogeno_AccionConKilos()
        {
            var l = LoteManual(15f, MaterialEspecifico.RestosCocina, TipoComposta.Domestica);
            Medir(l, 50f, 55f, 15f, Ahora.AddDays(-1));
            var d = GeneradorDiagnostico.Generar(MotorPronostico.DesdeMediciones(l), l, Ahora, Dia);
            float kg = BalanceLote.KgParaObjetivo(BalanceLote.Calcular(l), MaterialEspecifico.Hojarasca);
            Assert.IsTrue(d.Acciones.Any(a => a.Texto.Contains(kg.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kg de hojarasca seca")),
                          string.Join(" | ", d.Acciones.Select(a => a.Texto)));
            Assert.AreEqual(NivelEstado.Critico, d.Estado[3].Nivel);
        }

        [Test]
        public void TemperaturaPrevistaBaja_SeExplica()
        {
            // Pila pequeña con mucho material café: el pronóstico no llega a la zona óptima
            var l = LoteManual(60f, MaterialEspecifico.Paja, TipoComposta.PilaAbierta);
            Medir(l, 22f, 50f, 60f, Ahora.AddDays(-1));
            var p = MotorPronostico.DesdeMediciones(l);
            var d = GeneradorDiagnostico.Generar(p, l, Ahora, Dia);
            var zona = p.Perfil.ZonaIdeal(Magnitud.Temperatura);
            Assume.That(p.TempMaxima < zona.min, $"máx {p.TempMaxima} zona {zona.min} cn {p.Perfil.RelacionCN}");
            Assert.AreNotEqual(NivelEstado.Ok, d.Estado[2].Nivel);
            StringAssert.Contains("por debajo de la zona óptima", d.Estado[2].Explicacion);
        }

        [Test]
        public void AportacionReciente_GeneraAlertaDeBalance()
        {
            var l = LoteManual(80f, MaterialEspecifico.MezclaGenerica);
            Medir(l, 50f, 55f, 80f, Ahora.AddDays(-5));
            Assert.IsTrue(EditorLote.AgregarAporte(l, 25f, MaterialEspecifico.RestosCocina, 0f, "", Ahora.AddDays(-2), 0, "luis", Ahora.AddDays(-2)).Exito);
            var d = GeneradorDiagnostico.Generar(MotorPronostico.DesdeMediciones(l), l, Ahora, Dia);
            Assert.IsTrue(d.Alertas.Any(a => a.Texto.StartsWith("La aportación reciente (25 kg de restos de cocina) cambió la C:N")),
                          string.Join(" | ", d.Alertas.Select(a => a.Texto)));
        }

        [Test]
        public void VolteoUsaLaFechaFormateada()
        {
            var l = LoteManual(90f, MaterialEspecifico.MezclaGenerica);
            Medir(l, 45f, 53f, 88f, Ahora.AddDays(-1));
            var p = MotorPronostico.DesdeMediciones(l);
            var volteo = p.Hitos.FirstOrDefault(h => h.Tipo == TipoHito.Volteo);
            Assume.That(volteo, Is.Not.Null);
            var d = GeneradorDiagnostico.Generar(p, l, Ahora, Dia);
            Assert.IsTrue(d.Acciones.Any(a => a.Texto.StartsWith("Voltea el " + Dia(volteo.DiasDesdeAhora))) || d.Acciones.Count == DiagnosticoPronostico.MaxAcciones);
        }
    }
}
