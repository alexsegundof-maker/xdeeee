using System;
using System.Collections.Generic;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Persistencia;
using NUnit.Framework;

namespace Composta.Tests
{
    public class CampoTests
    {
        // Valores del motor ANTES de agregar el modelo de campo: el modo Simular no debe cambiar.
        // (Tolerancia pequeña: Mono y .NET pueden diferir en el último bit de Math.Pow/Exp)
        [TestCase(TipoComposta.Domestica, 24.905235f, 51.610497f, 16.719925f, Etapa.Maduracion, 6, 5)]
        [TestCase(TipoComposta.PilaAbierta, 24.990349f, 53.106163f, 13.319912f, Etapa.Maduracion, 7, 4)]
        [TestCase(TipoComposta.Vermicomposta, 24.893705f, 72.08528f, 18.039955f, Etapa.Maduracion, 5, 5)]
        public void ModoSimular_DaLosMismosResultadosQueAntes(TipoComposta t, float temp, float hum, float o2, Etapa etapa, int volteos, int riegos)
        {
            var s = new SistemaComposta(PerfilComposta.Crear(t), 4321);
            s.Controlador.Activo = true;
            s.Sensores.ProbFalla = 0.01f;
            for (int i = 0; i < 24 * 40; i++) { if (i == 100) s.Regar(); if (i == 300) s.Voltear(); s.Paso(); }
            Assert.AreEqual(temp, s.Modelo.Temperatura, 1e-3f);
            Assert.AreEqual(hum, s.Modelo.Humedad, 1e-3f);
            Assert.AreEqual(o2, s.Modelo.Oxigeno, 1e-3f);
            Assert.AreEqual(etapa, s.Modelo.Etapa);
            Assert.AreEqual(volteos, s.Metricas.Volteos);
            Assert.AreEqual(riegos, s.Metricas.Riegos);
        }

        [Test]
        public void ModeloCampo_PierdeMasaYNoUsaSensores()
        {
            var s = new SistemaComposta(PerfilComposta.Crear(TipoComposta.PilaAbierta), 1, true);
            Assert.IsTrue(s.SinSensores);
            ResultadoPaso r = null;
            for (int i = 0; i < 24 * 14; i++) r = s.Paso();
            Assert.Less(s.Modelo.MasaKg, 500f);
            Assert.Greater(s.Modelo.MasaKg, 250f);                       // como mucho se pierde la fracción degradable
            Assert.AreEqual(r.Real.Temperatura, r.Medida.Temperatura);    // "lo medido" es la estimación
            foreach (var m in r.Mediciones) Assert.IsTrue(m.Confiable);
        }

        [Test]
        public void Ganancia_CreceConElTiempoSinMedir()
        {
            Assert.AreEqual(0.7f, CalibracionCampo.Ganancia(168), 0.01f);
            Assert.Greater(CalibracionCampo.Ganancia(672), 0.9f);
            Assert.Less(CalibracionCampo.Ganancia(0), 0.55f);                 // informe muy temprano: pesa menos
            Assert.GreaterOrEqual(CalibracionCampo.Ganancia(0), CalibracionCampo.GananciaMin);
            Assert.LessOrEqual(CalibracionCampo.Ganancia(100000), CalibracionCampo.GananciaMax);
        }

        [Test]
        public void Informe_AcercaElEstadoALoMedido_YAjustaPerillasConTope()
        {
            var s = new SistemaComposta(PerfilComposta.Crear(TipoComposta.PilaAbierta), 3, true);
            for (int i = 0; i < 168; i++) s.Paso();
            var m = s.Modelo;
            float tAntes = m.Temperatura, hAntes = m.Humedad;
            var r = s.Calibracion.Aplicar(new InformeCampo { Temperatura = tAntes - 20f, Humedad = hAntes - 15f, PesoKg = m.MasaKg * 0.9f });

            Assert.AreEqual(tAntes - 20f * r.Ganancia, m.Temperatura, 0.6f);   // pudo cambiar la ambiente/limitar
            Assert.Less(m.Humedad, hAntes);
            Assert.Greater(m.FactorEvaporacion, 1f);                          // se secó más de lo esperado
            Assert.LessOrEqual(m.FactorEvaporacion, 1.25f + 1e-4f);           // tope ±25 % por informe
            Assert.GreaterOrEqual(m.FactorActividad, 0.8f - 1e-4f);
            Assert.AreEqual(1, s.Calibracion.CantidadInformes);
        }

        [Test]
        public void Informe_SuavizaElHistorialSinSaltos()
        {
            var s = new SistemaComposta(PerfilComposta.Crear(TipoComposta.PilaAbierta), 3, true);
            for (int i = 0; i < 168; i++) s.Paso();
            float tFinal = s.Modelo.Temperatura;
            s.Calibracion.Aplicar(new InformeCampo { Temperatura = tFinal + 10f, Humedad = s.Modelo.Humedad, PesoKg = s.Modelo.MasaKg });
            var h = s.Historial.Lecturas;
            Assert.AreEqual(s.Modelo.Temperatura, h[h.Count - 1].Temperatura, 0.01f);   // la última lectura ya coincide
            Assert.Less(Math.Abs(h[83].Temperatura - h[84].Temperatura), 3.5f);         // sin escalón a mitad de tramo
        }

        [Test]
        public void Calibracion_ConvergeHaciaUnaPilaConParametrosDistintos()
        {
            var perfil = PerfilComposta.Crear(TipoComposta.PilaAbierta);
            var real = new SistemaComposta(perfil, 99, true);
            real.Modelo.FactorEvaporacion = 1.6f; real.Modelo.FactorDescomposicion = 1.5f; real.Modelo.FactorActividad = 1.4f;
            var mod = new SistemaComposta(perfil, 7, true);
            var sin = new SistemaComposta(perfil, 7, true);
            float errCal = 0, errSin = 0;
            for (int sem = 1; sem <= 8; sem++)
            {
                for (int h = 0; h < 168; h++)
                {
                    if (h > 0 && h % 48 == 0) { real.Voltear(); mod.Voltear(); sin.Voltear(); }
                    real.Paso(); mod.Paso(); sin.Paso();
                }
                if (sem >= 6)
                {
                    errCal += Math.Abs(mod.Modelo.Humedad - real.Modelo.Humedad) + Math.Abs(mod.Modelo.Temperatura - real.Modelo.Temperatura);
                    errSin += Math.Abs(sin.Modelo.Humedad - real.Modelo.Humedad) + Math.Abs(sin.Modelo.Temperatura - real.Modelo.Temperatura);
                }
                var R = real.Modelo;
                mod.Calibracion.Aplicar(new InformeCampo { Temperatura = R.Temperatura, Humedad = R.Humedad, PesoKg = R.MasaKg });
            }
            Assert.Less(errCal, errSin * 0.5f, $"calibrado {errCal:F1} vs sin calibrar {errSin:F1}");
            Assert.Greater(mod.Modelo.FactorEvaporacion, 1.2f);
        }

        [Test]
        public void LoteManual_ConInformes_SeReproduceIgual()
        {
            var lote = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 11, "x", DateTime.UtcNow, ModoLote.Manual);
            var vivo = new SistemaComposta(ReproductorLote.CrearPerfil(lote), lote.Semilla, true);
            for (int t = 0; t < 24 * 21; t++)
            {
                if (t > 0 && t % 168 == 0)
                {
                    var inf = new InformeCampo { Temperatura = vivo.Modelo.Temperatura + 5f, Humedad = vivo.Modelo.Humedad - 6f, PesoKg = vivo.Modelo.MasaKg * 0.95f };
                    lote.Informes.Add(inf);
                    var c = new Comando { Tick = vivo.Modelo.Ticks, Tipo = TipoComando.Informe, Valor = lote.Informes.Count - 1 };
                    lote.Comandos.Add(c);
                    ReproductorLote.Aplicar(vivo, c, lote);
                }
                vivo.Paso();
            }
            lote.TicksSimulados = vivo.Modelo.Ticks;

            // Ida y vuelta por JSON, como al guardar y reabrir
            var repo = new RepositorioLotesJson(new AlmacenMemoria(), new SerializadorPruebas());
            repo.Guardar(lote);
            var cargado = repo.Cargar(lote.Id);
            Assert.IsEmpty(ReproductorLote.Sanear(cargado));
            var copia = ReproductorLote.Reconstruir(cargado, out _);

            Assert.AreEqual(vivo.Modelo.Temperatura, copia.Modelo.Temperatura);
            Assert.AreEqual(vivo.Modelo.MasaKg, copia.Modelo.MasaKg);
            Assert.AreEqual(vivo.Modelo.FactorEvaporacion, copia.Modelo.FactorEvaporacion);
            Assert.AreEqual(2, copia.Calibracion.CantidadInformes);
        }

        [Test]
        public void Sanear_DescartaInformesQueNoExisten()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 1, "x", DateTime.UtcNow, ModoLote.Manual);
            l.TicksSimulados = 10;
            l.Comandos.Add(new Comando { Tick = 5, Tipo = TipoComando.Informe, Valor = 3 });
            ReproductorLote.Sanear(l);
            Assert.IsEmpty(l.Comandos);
        }

        [Test]
        public void Plan_EstadosSegunCalendario()
        {
            var ahora = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 1, "x", ahora, ModoLote.Manual);
            l.RitmoReal = true;
            Assert.AreEqual(EstadoInforme.AlDia, PlanInformes.Evaluar(l, ahora.AddDays(3), 0).Estado);
            Assert.AreEqual(EstadoInforme.Pendiente, PlanInformes.Evaluar(l, ahora.AddDays(6.5), 0).Estado);
            Assert.AreEqual(EstadoInforme.Atrasado, PlanInformes.Evaluar(l, ahora.AddDays(10), 0).Estado);

            l.Informes.Add(new InformeCampo { FechaUtc = ahora.AddDays(9).Ticks });
            Assert.AreEqual(EstadoInforme.AlDia, PlanInformes.Evaluar(l, ahora.AddDays(10), 0).Estado);
        }

        [Test]
        public void Plan_EnDemostracionUsaHorasSimuladas()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 1, "x", DateTime.UtcNow, ModoLote.Manual);
            Assert.AreEqual(EstadoInforme.AlDia, PlanInformes.Evaluar(l, DateTime.UtcNow.AddYears(1), 100).Estado);
            Assert.AreEqual(EstadoInforme.Atrasado, PlanInformes.Evaluar(l, DateTime.UtcNow, 200).Estado);
        }

        [Test]
        public void Validador_ErroresYAdvertencias()
        {
            var m = new ModeloComposta(PerfilComposta.Crear(TipoComposta.PilaAbierta), 1, true);
            var adv = new List<string>();
            Assert.IsFalse(ValidadorInforme.Validar("abc", "50", "400", m, 500f, out _, out _, out _, out var e1, adv));
            Assert.IsNotNull(e1);
            Assert.IsFalse(ValidadorInforme.Validar("50", "120", "400", m, 500f, out _, out _, out _, out _, adv));
            Assert.IsTrue(ValidadorInforme.Validar("60,5", "55", "480", m, 500f, out var t, out _, out _, out _, adv));
            Assert.AreEqual(60.5f, t, 1e-4f);
            Assert.IsTrue(adv.Count > 0);                // 60 °C frente a 22 estimados: pide confirmación
            adv.Clear();
            Assert.IsTrue(ValidadorInforme.Validar("25", "55", "650", m, 500f, out _, out _, out _, out _, adv));
            Assert.IsTrue(adv.Exists(a => a.Contains("subió")));
        }
    }
}
