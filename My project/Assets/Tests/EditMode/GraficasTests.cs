using System.Collections.Generic;
using Composta.Core;
using NUnit.Framework;

namespace Composta.Tests
{
    // Datos que alimentan las gráficas de la simulación
    public class GraficasTests
    {
        static Lectura L(int tick, float temp) => new Lectura { Tick = tick, Dias = tick / 24f, Temperatura = temp, Humedad = 50f, PH = 7f, Oxigeno = 15f };

        [Test]
        public void Agregador_IgnoraValoresNoFinitos()
        {
            var lecturas = new List<Lectura> { L(1, 30f), L(2, float.NaN), L(3, float.PositiveInfinity), L(4, 40f), null };
            var s = Agregador.Construir(lecturas, Magnitud.Temperatura, Granularidad.Dia);
            Assert.AreEqual(1, s.Puntos.Count);
            Assert.AreEqual(35f, s.Puntos[0].Prom, 1e-5f);
            Assert.AreEqual(30f, s.Puntos[0].Min);
            Assert.AreEqual(40f, s.Puntos[0].Max);
        }

        [Test]
        public void Agregador_BloquesPorDia_YTickCeroEnElPrimero()
        {
            var lecturas = new List<Lectura> { L(0, 20f), L(1, 22f), L(24, 24f), L(25, 50f), L(48, 52f) };
            var s = Agregador.Construir(lecturas, Magnitud.Temperatura, Granularidad.Dia);
            Assert.AreEqual(2, s.Puntos.Count);
            Assert.AreEqual(0f, s.Puntos[0].X);
            Assert.AreEqual(22f, s.Puntos[0].Prom, 1e-5f);
            Assert.AreEqual(1f, s.Puntos[1].X);
            Assert.AreEqual(51f, s.Puntos[1].Prom, 1e-5f);
        }

        [Test]
        public void Agregador_ListaNulaOVacia()
        {
            Assert.AreEqual(0, Agregador.Construir(null, Magnitud.PH, Granularidad.Hora).Puntos.Count);
            Assert.AreEqual(0, Agregador.Construir(new List<Lectura>(), Magnitud.PH, Granularidad.Semana).Puntos.Count);
        }
    }
}
