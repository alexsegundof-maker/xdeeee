using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Composta.Core;
using Composta.Core.Exportacion;
using Composta.Core.Lotes;
using Composta.Core.Persistencia;
using NUnit.Framework;

namespace Composta.Tests
{
    // Libro .xlsx: se abre con el lector ZIP estándar y se valida el XML de cada parte
    public class ExcelTests
    {
        static readonly XNamespace M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly DateTime Ahora = new DateTime(2026, 6, 15, 18, 0, 0, DateTimeKind.Utc);
        static readonly TimeZoneInfo Mexico = TimeZoneInfo.CreateCustomTimeZone("Prueba-6", TimeSpan.FromHours(-6), "UTC-6", "UTC-6");

        static Dictionary<string, byte[]> Abrir(byte[] zip)
        {
            var r = new Dictionary<string, byte[]>();
            using (var a = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                foreach (var e in a.Entries)
                    using (var s = e.Open())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        r[e.FullName] = ms.ToArray();
                    }
            return r;
        }

        static XDocument Xml(Dictionary<string, byte[]> partes, string nombre)
        {
            Assert.IsTrue(partes.ContainsKey(nombre), "falta " + nombre);
            return XDocument.Parse(Encoding.UTF8.GetString(partes[nombre]));
        }

        static string[] NombresDeHojas(Dictionary<string, byte[]> partes)
            => Xml(partes, "xl/workbook.xml").Descendants(M + "sheet").Select(s => (string)s.Attribute("name")).ToArray();

        static XElement Celda(XDocument hoja, string referencia)
            => hoja.Descendants(M + "c").FirstOrDefault(c => (string)c.Attribute("r") == referencia);

        static string Texto(XDocument hoja, string referencia) => Celda(hoja, referencia)?.Element(M + "is")?.Element(M + "t")?.Value;
        static double Numero(XDocument hoja, string referencia)
            => double.Parse(Celda(hoja, referencia).Element(M + "v").Value, System.Globalization.CultureInfo.InvariantCulture);

        static Lote LoteManual()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 0, "ana", Ahora.AddDays(-20), ModoLote.Manual);
            l.Nombre = "Huerto Ñandú & <amigos>";
            return l;
        }

        [Test]
        public void Zip_SeLeeConElLectorEstandar_YConservaUtf8()
        {
            var z = new ZipSinCompresion(new DateTime(2026, 6, 15, 10, 30, 0));
            z.Agregar("a.txt", "año, acción y pingüino");
            z.Agregar("carpeta/vacío.bin", new byte[0]);
            var datos = Abrir(z.Terminar());
            Assert.AreEqual("año, acción y pingüino", Encoding.UTF8.GetString(datos["a.txt"]));
            Assert.AreEqual(0, datos["carpeta/vacío.bin"].Length);
        }

        [Test]
        public void Zip_RechazaRutasPeligrosasYRepetidas()
        {
            var z = new ZipSinCompresion(DateTime.Now);
            Assert.Throws<ArgumentException>(() => z.Agregar("../x", "a"));
            Assert.Throws<ArgumentException>(() => z.Agregar("/x", "a"));
            Assert.Throws<ArgumentException>(() => z.Agregar("a\\b", "a"));
            z.Agregar("x", "a");
            Assert.Throws<ArgumentException>(() => z.Agregar("x", "b"));
        }

        [Test]
        public void Crc32_ValorConocido()
            => Assert.AreEqual(0xCBF43926u, Crc32.Calcular(Encoding.ASCII.GetBytes("123456789")));

        [TestCase(0, "A")]
        [TestCase(25, "Z")]
        [TestCase(26, "AA")]
        [TestCase(701, "ZZ")]
        [TestCase(702, "AAA")]
        public void Columna(int i, string esperado) => Assert.AreEqual(esperado, LibroExcel.Columna(i));

        [Test]
        public void NombresDeHoja_SeLimpianYNoSeRepiten()
        {
            var libro = new LibroExcel();
            Assert.AreEqual("ab", libro.AgregarHoja("a/b").Nombre);
            Assert.AreEqual("Hoja", libro.AgregarHoja("[]").Nombre);
            Assert.AreEqual("ab (2)", libro.AgregarHoja("a:b").Nombre);
            Assert.AreEqual(31, libro.AgregarHoja(new string('x', 50)).Nombre.Length);
        }

        [Test]
        public void Texto_SeEscapa_YQuitaCaracteresInvalidos()
        {
            Assert.AreEqual("a &amp; &lt;b&gt; &quot;c&quot;", LibroExcel.Xml("a & <b> \"c\""));
            Assert.AreEqual("ab", LibroExcel.Xml("a\u0001\u0008b"));
            Assert.AreEqual("😀", LibroExcel.Xml("😀"));
            Assert.AreEqual("x", LibroExcel.Xml("x\uD800"));      // sustituto suelto
        }

        [Test]
        public void Celdas_NumerosInvalidosQuedanVacias()
        {
            Assert.AreEqual(TipoCelda.Vacia, Composta.Core.Exportacion.Celda.N(double.NaN).Tipo);
            Assert.AreEqual(TipoCelda.Vacia, Composta.Core.Exportacion.Celda.N(double.PositiveInfinity).Tipo);
            Assert.AreEqual(TipoCelda.Vacia, Composta.Core.Exportacion.Celda.T("").Tipo);
        }

        [Test]
        public void LoteManual_TieneLasHojasYPartesObligatorias()
        {
            var partes = Abrir(ExportadorExcel.Generar(LoteManual(), null, Mexico, Ahora, "1.1.2"));
            CollectionAssert.AreEqual(new[] { "Resumen", "Mediciones", "Aportes", "Cambios" }, NombresDeHojas(partes));
            foreach (var p in new[] { "[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "docProps/core.xml", "docProps/app.xml" })
                Xml(partes, p);                                   // todas las partes son XML bien formado
            for (int i = 1; i <= 4; i++) Xml(partes, $"xl/worksheets/sheet{i}.xml");

            // Cada hoja declarada tiene su relación y su tipo de contenido
            var rels = Xml(partes, "xl/_rels/workbook.xml.rels").Root.Elements().Select(e => (string)e.Attribute("Target")).ToList();
            var tipos = Xml(partes, "[Content_Types].xml").ToString();
            for (int i = 1; i <= 4; i++)
            {
                CollectionAssert.Contains(rels, $"worksheets/sheet{i}.xml");
                StringAssert.Contains($"/xl/worksheets/sheet{i}.xml", tipos);
            }
        }

        [Test]
        public void LoteSimulado_IncluyeBitacoraEHistorial()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.PilaAbierta), 7, "ana", Ahora, ModoLote.Simular);
            l.Comandos.Add(new Comando { Tick = 30, Tipo = TipoComando.Regar, Valor = 12.5f, Usuario = "ana", FechaUtc = Ahora.Ticks });
            l.Comandos.Add(new Comando { Tick = 40, Tipo = TipoComando.Regar, Valor = -1f, Usuario = "ana", FechaUtc = Ahora.Ticks });
            var s = new SistemaComposta(ReproductorLote.CrearPerfil(l), l.Semilla);
            for (int i = 0; i < 50; i++) s.Paso();

            var partes = Abrir(ExportadorExcel.Generar(l, s.Historial, Mexico, Ahora, "1.1.2"));
            CollectionAssert.AreEqual(new[] { "Resumen", "Bitácora", "Historial", "Aportes", "Cambios" }, NombresDeHojas(partes));

            var bit = Xml(partes, "xl/worksheets/sheet2.xml");
            Assert.AreEqual("Riego", Texto(bit, "C2"));
            Assert.AreEqual(12.5, Numero(bit, "D2"), 1e-9);
            Assert.AreEqual("litros", Texto(bit, "E2"));
            Assert.IsNull(Celda(bit, "D3"), "riego estándar no tiene número");
            Assert.AreEqual("riego estándar", Texto(bit, "E3"));

            var hist = Xml(partes, "xl/worksheets/sheet3.xml");
            Assert.AreEqual(s.Historial.Lecturas.Count + 1, hist.Descendants(M + "row").Count());
            StringAssert.StartsWith("Fase", Texto(hist, "C2"));
        }

        [Test]
        public void Mediciones_FechasLocalesComoFechaReal_YNumerosRedondeados()
        {
            var l = LoteManual();
            var medida = new DateTime(2026, 6, 14, 15, 30, 0, DateTimeKind.Utc);       // 09:30 en UTC-6
            Assert.IsTrue(EditorLote.AgregarMedicion(l, 55.2f, 60f, 20.15f, "olor a tierra; ok", medida, "josé", Ahora).Exito);
            var partes = Abrir(ExportadorExcel.Generar(l, null, Mexico, Ahora, "1.1.2"));
            var hoja = Xml(partes, "xl/worksheets/sheet2.xml");

            Assert.AreEqual("Fecha de medición", Texto(hoja, "A1"));
            Assert.AreEqual("Temperatura (°C)", Texto(hoja, "B1"));
            Assert.AreEqual(LibroExcel.SerialExcel(new DateTime(2026, 6, 14, 9, 30, 0)), Numero(hoja, "A2"), 1e-9);
            Assert.AreEqual("2", (string)Celda(hoja, "A2").Attribute("s"), "estilo de fecha");
            Assert.AreEqual("55.2", Celda(hoja, "B2").Element(M + "v").Value, "sin ruido de float");
            Assert.AreEqual(20.15, Numero(hoja, "D2"), 1e-12);
            Assert.AreEqual("josé", Texto(hoja, "E2"));
            Assert.AreEqual("olor a tierra; ok", Texto(hoja, "F2"));
            Assert.IsNotNull(hoja.Descendants(M + "autoFilter").SingleOrDefault());
            Assert.AreEqual("frozen", (string)hoja.Descendants(M + "pane").Single().Attribute("state"));
        }

        [Test]
        public void HojasVacias_LlevanNota_YSinFiltro()
        {
            var partes = Abrir(ExportadorExcel.Generar(LoteManual(), null, Mexico, Ahora, "1.1.2"));
            var hoja = Xml(partes, "xl/worksheets/sheet2.xml");
            StringAssert.Contains("Todavía no hay mediciones", Texto(hoja, "A2"));
            Assert.IsNull(hoja.Descendants(M + "autoFilter").SingleOrDefault());
            var libro = Xml(partes, "xl/workbook.xml");
            Assert.IsFalse(libro.Descendants(M + "definedName").Any(d => (string)d.Attribute("localSheetId") == "1"));
        }

        [Test]
        public void Resumen_EnEspanol_ConNombreEscapado()
        {
            var l = LoteManual();
            EditorLote.AgregarAporte(l, 3.5f, MaterialEspecifico.Hojarasca, 0f, "", Ahora, 0, "ana", Ahora);
            var partes = Abrir(ExportadorExcel.Generar(l, null, Mexico, Ahora, "1.1.2"));
            var hoja = Xml(partes, "xl/worksheets/sheet1.xml");
            var textos = hoja.Descendants(M + "t").Select(t => t.Value).ToList();
            CollectionAssert.Contains(textos, "Huerto Ñandú & <amigos>");
            CollectionAssert.Contains(textos, "Control manual");
            CollectionAssert.Contains(textos, "Compostera doméstica");
            CollectionAssert.Contains(textos, "UTC−06:00 (Prueba-6)");
            CollectionAssert.Contains(textos, "Composta 1.1.2");
            Assert.IsNull(hoja.Descendants(M + "pane").SingleOrDefault(), "el resumen no es una tabla");

            var aportes = Xml(partes, "xl/worksheets/sheet3.xml");
            Assert.AreEqual("Hojarasca seca", Texto(aportes, "D2"));
            Assert.AreEqual("Café (carbono)", Texto(aportes, "E2"));
        }

        [Test]
        public void Filtros_TienenSuNombreDefinido()
        {
            var l = LoteManual();
            EditorLote.AgregarMedicion(l, 50f, 50f, 30f, "", Ahora.AddDays(-2), "ana", Ahora);
            EditorLote.AgregarMedicion(l, 52f, 50f, 29f, "", Ahora.AddDays(-1), "ana", Ahora);
            var partes = Abrir(ExportadorExcel.Generar(l, null, Mexico, Ahora, "1.1.2"));
            var hoja = Xml(partes, "xl/worksheets/sheet2.xml");
            Assert.AreEqual("A1:I3", (string)hoja.Descendants(M + "autoFilter").Single().Attribute("ref"));
            var nombre = Xml(partes, "xl/workbook.xml").Descendants(M + "definedName").Single(d => (string)d.Attribute("localSheetId") == "1");
            Assert.AreEqual("'Mediciones'!$A$1:$I$3", nombre.Value);
        }
    }

    public class ExportacionArchivosTests
    {
        [TestCase("Compostera doméstica · Huerto Ñandú", "compostera-domestica-huerto-nandu")]
        [TestCase("  ¡Pila #3!  ", "pila-3")]
        [TestCase("···", "lote")]
        [TestCase(null, "lote")]
        public void Slug(string entrada, string esperado) => Assert.AreEqual(esperado, NombreArchivo.Slug(entrada));

        [Test]
        public void Slug_RespetaElLargo() => Assert.LessOrEqual(NombreArchivo.Slug(new string('a', 100), 40).Length, 40);

        [Test]
        public void Csv_LlevaBomYAcentos()
        {
            var bytes = ExportadorCsv.ABytes("acción\n");
            Assert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.AreEqual("acción\n", new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
        }

        [Test]
        public void Csv_ValoresEnEspanol()
        {
            var ahora = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 1, "ana", ahora, ModoLote.Simular);
            l.Comandos.Add(new Comando { Tick = 3, Tipo = TipoComando.Automatico, Valor = 1f, Usuario = "ana", FechaUtc = ahora.Ticks });
            StringAssert.Contains(",Control automático,1,activado", ExportadorCsv.Bitacora(l));
            StringAssert.Contains("tipo,Compostera doméstica", ExportadorCsv.Resumen(l));
            StringAssert.Contains("modo,Simulación", ExportadorCsv.Resumen(l));
        }

        string dir;
        [SetUp] public void Preparar() => dir = Path.Combine(Path.GetTempPath(), "composta_tests_" + Guid.NewGuid().ToString("N"));
        [TearDown] public void Limpiar() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

        [Test]
        public void EscribirBytes_EsAtomico_YAceptaXlsx()
        {
            var a = new AlmacenArchivos(dir);
            a.EscribirBytes("exportaciones/20260615_120000_huerto/huerto_20260615_120000.xlsx", new byte[] { 1, 2, 3 });
            a.EscribirBytes("exportaciones/20260615_120000_huerto/huerto_20260615_120000.xlsx", new byte[] { 4 });
            string ruta = a.RutaAbsoluta("exportaciones/20260615_120000_huerto/huerto_20260615_120000.xlsx");
            CollectionAssert.AreEqual(new byte[] { 4 }, File.ReadAllBytes(ruta));
            Assert.IsFalse(File.Exists(ruta + ".tmp"));
            Assert.Throws<ArgumentException>(() => a.EscribirBytes("exportaciones/x.exe", new byte[0]));
        }
    }
}
