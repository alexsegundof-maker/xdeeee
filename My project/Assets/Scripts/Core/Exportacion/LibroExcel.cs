using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Composta.Core.Exportacion
{
    // Formato de cada celda. Los números y fechas se guardan como valores reales: Excel, Google Sheets,
    // LibreOffice o WPS los muestran con la coma o el punto decimal de la región de quien abre el archivo.
    // Los números de las tablas van centrados; las variantes "Izq" son para fichas (clave: valor) como el Resumen
    public enum EstiloCelda
    {
        Normal = 0, Encabezado = 1, Fecha = 2, Decimal1 = 3, Decimal2 = 4, Entero = 5, Nota = 6, Titulo = 7, Clave = 8,
        Decimal1Izq = 9, Decimal2Izq = 10, EnteroIzq = 11, FechaIzq = 12, Dia = 13, DiaIzq = 14     // Dia = solo fecha (estimaciones)
    }

    public enum TipoCelda { Vacia, Texto, Numero, Fecha }

    public readonly struct Celda
    {
        public readonly TipoCelda Tipo;
        public readonly string Texto;
        public readonly double Numero;
        public readonly EstiloCelda Estilo;

        Celda(TipoCelda tipo, string texto, double numero, EstiloCelda estilo) { Tipo = tipo; Texto = texto; Numero = numero; Estilo = estilo; }

        public static readonly Celda Vacia = new Celda(TipoCelda.Vacia, null, 0, EstiloCelda.Normal);

        public Celda ConEstilo(EstiloCelda estilo) => new Celda(Tipo, Texto, Numero, estilo);

        public static Celda T(string texto, EstiloCelda estilo = EstiloCelda.Normal)
            => string.IsNullOrEmpty(texto) ? new Celda(TipoCelda.Vacia, null, 0, estilo) : new Celda(TipoCelda.Texto, texto, 0, estilo);

        // NaN o infinito nunca llegan al archivo (Excel lo declararía dañado): quedan vacías
        public static Celda N(double valor, EstiloCelda estilo = EstiloCelda.Decimal2)
            => double.IsNaN(valor) || double.IsInfinity(valor) ? Vacia : new Celda(TipoCelda.Numero, null, valor, estilo);

        public static Celda F(DateTime fechaLocal, EstiloCelda estilo = EstiloCelda.Fecha)
        {
            double serial = LibroExcel.SerialExcel(fechaLocal);
            return serial < 1 ? Vacia : new Celda(TipoCelda.Fecha, null, serial, estilo);
        }
    }

    public sealed class HojaExcel
    {
        public const int NombreMax = 31;                // límite de Excel

        public string Nombre { get; }
        public bool TablaConEncabezado { get; set; } = true;   // fila 1 fija y con filtros
        internal readonly List<(string titulo, double ancho)> columnas = new List<(string, double)>();
        internal readonly List<Celda[]> filas = new List<Celda[]>();
        internal int filasDeDatos;

        internal HojaExcel(string nombre) { Nombre = nombre; }

        public HojaExcel Columna(string titulo, double ancho)
        {
            columnas.Add((titulo, ancho));
            return this;
        }

        public void Fila(params Celda[] celdas)
        {
            filas.Add(celdas ?? new Celda[0]);
            filasDeDatos++;
        }

        // Fila informativa (p. ej. "Todavía no hay mediciones"): no cuenta como dato para los filtros
        public void Nota(string texto) => filas.Add(new[] { Celda.T(texto, EstiloCelda.Nota) });

        public int FilasDeDatos => filasDeDatos;
    }

    // Libro .xlsx (Office Open XML / ISO 29500) con texto en línea, sin cadenas compartidas ni fórmulas.
    public sealed class LibroExcel
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        static readonly DateTime Origen = new DateTime(1899, 12, 30);
        const int MaxFilas = 1048576, MaxColumnas = 16384;

        readonly List<HojaExcel> hojas = new List<HojaExcel>();
        public string Titulo { get; set; } = "";
        public string Autor { get; set; } = "";
        public IReadOnlyList<HojaExcel> Hojas => hojas;

        public HojaExcel AgregarHoja(string nombre)
        {
            var h = new HojaExcel(NombreUnico(LimpiarNombreHoja(nombre)));
            hojas.Add(h);
            return h;
        }

        public static double SerialExcel(DateTime fechaLocal) => (fechaLocal - Origen).TotalDays;

        // Excel no admite : \ / ? * [ ] en el nombre de una hoja, ni más de 31 caracteres
        public static string LimpiarNombreHoja(string nombre)
        {
            var sb = new StringBuilder();
            foreach (char ch in nombre ?? "")
                if (ch != ':' && ch != '\\' && ch != '/' && ch != '?' && ch != '*' && ch != '[' && ch != ']' && !char.IsControl(ch)) sb.Append(ch);
            string r = sb.ToString().Trim().Trim('\'');
            if (r.Length == 0) r = "Hoja";
            return r.Length > HojaExcel.NombreMax ? r.Substring(0, HojaExcel.NombreMax) : r;
        }

        string NombreUnico(string nombre)
        {
            string candidato = nombre;
            for (int i = 2; hojas.Exists(h => string.Equals(h.Nombre, candidato, StringComparison.OrdinalIgnoreCase)); i++)
            {
                string sufijo = " (" + i.ToString(C) + ")";
                candidato = (nombre.Length + sufijo.Length > HojaExcel.NombreMax ? nombre.Substring(0, HojaExcel.NombreMax - sufijo.Length) : nombre) + sufijo;
            }
            return candidato;
        }

        public byte[] Generar(DateTime creadoUtc)
        {
            if (hojas.Count == 0) throw new InvalidOperationException("Un libro de Excel necesita al menos una hoja");
            var zip = new ZipSinCompresion(creadoUtc.ToLocalTime());
            zip.Agregar("[Content_Types].xml", TiposDeContenido());
            zip.Agregar("_rels/.rels", RelacionesRaiz());
            zip.Agregar("docProps/core.xml", PropiedadesCore(creadoUtc));
            zip.Agregar("docProps/app.xml", PropiedadesApp());
            zip.Agregar("xl/workbook.xml", Libro());
            zip.Agregar("xl/_rels/workbook.xml.rels", RelacionesLibro());
            zip.Agregar("xl/styles.xml", Estilos());
            for (int i = 0; i < hojas.Count; i++) zip.Agregar($"xl/worksheets/sheet{i + 1}.xml", Hoja(hojas[i], i == 0));
            return zip.Terminar();
        }

        // ---------------- Partes del paquete ----------------
        const string Cabecera = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
        const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        string TiposDeContenido()
        {
            var sb = new StringBuilder(Cabecera);
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            for (int i = 0; i < hojas.Count; i++)
                sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            sb.Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            sb.Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        static string RelacionesRaiz()
            => Cabecera + $"<Relationships xmlns=\"{NsPkgRel}\">" +
               $"<Relationship Id=\"rId1\" Type=\"{NsRel}/officeDocument\" Target=\"xl/workbook.xml\"/>" +
               "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
               $"<Relationship Id=\"rId3\" Type=\"{NsRel}/extended-properties\" Target=\"docProps/app.xml\"/>" +
               "</Relationships>";

        string PropiedadesCore(DateTime creadoUtc)
        {
            string w3c = creadoUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", C);
            return Cabecera +
                   "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                   "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                   "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                   $"<dc:title>{Xml(Titulo)}</dc:title><dc:creator>{Xml(Autor)}</dc:creator><dc:language>es</dc:language>" +
                   $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{w3c}</dcterms:created>" +
                   $"<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{w3c}</dcterms:modified>" +
                   "</cp:coreProperties>";
        }

        static string PropiedadesApp()
            => Cabecera + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\">" +
               "<Application>Composta</Application></Properties>";

        string Libro()
        {
            var sb = new StringBuilder(Cabecera);
            sb.Append($"<workbook xmlns=\"{NsMain}\" xmlns:r=\"{NsRel}\"><bookViews><workbookView activeTab=\"0\"/></bookViews><sheets>");
            for (int i = 0; i < hojas.Count; i++)
                sb.Append($"<sheet name=\"{Xml(hojas[i].Nombre)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            sb.Append("</sheets>");
            // Rango de los filtros automáticos (Excel lo espera como nombre definido oculto)
            var nombres = new StringBuilder();
            for (int i = 0; i < hojas.Count; i++)
            {
                string rango = RangoFiltro(hojas[i]);
                if (rango == null) continue;
                string hoja = "'" + hojas[i].Nombre.Replace("'", "''") + "'";
                nombres.Append($"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">{Xml(hoja + "!" + Absoluto(rango))}</definedName>");
            }
            if (nombres.Length > 0) sb.Append("<definedNames>").Append(nombres).Append("</definedNames>");
            sb.Append("</workbook>");
            return sb.ToString();
        }

        string RelacionesLibro()
        {
            var sb = new StringBuilder(Cabecera);
            sb.Append($"<Relationships xmlns=\"{NsPkgRel}\">");
            for (int i = 0; i < hojas.Count; i++)
                sb.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"{NsRel}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
            sb.Append($"<Relationship Id=\"rId{hojas.Count + 1}\" Type=\"{NsRel}/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        // El orden de cellXfs debe coincidir con EstiloCelda
        static string Estilos()
            => Cabecera + $"<styleSheet xmlns=\"{NsMain}\">" +
               "<numFmts count=\"3\"><numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy hh:mm\"/><numFmt numFmtId=\"165\" formatCode=\"0.0\"/><numFmt numFmtId=\"166\" formatCode=\"dd/mm/yyyy\"/></numFmts>" +
               "<fonts count=\"4\">" +
               "<font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
               "<font><b/><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
               "<font><i/><sz val=\"11\"/><color rgb=\"FF6E7987\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
               "<font><b/><sz val=\"14\"/><color rgb=\"FF2E7D4F\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
               "</fonts>" +
               "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
               "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE3F1E8\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
               "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
               "<border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FF9DBFA8\"/></bottom><diagonal/></border></borders>" +
               "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
               "<cellXfs count=\"15\">" +
               "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +                                                          // Normal
               "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\">" +
               "<alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +                                                                            // Encabezado
               Numero(164, "center") +                                                                                                            // Fecha
               Numero(165, "center") + Numero(2, "center") + Numero(1, "center") +                                                                  // Decimal1, Decimal2, Entero
               "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                          // Nota
               "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                          // Titulo
               "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                          // Clave
               Numero(165, "left") + Numero(2, "left") + Numero(1, "left") + Numero(164, "left") +                                                  // ...Izq
               Numero(166, "center") + Numero(166, "left") +                                                                                       // Dia, DiaIzq
               "</cellXfs>" +
               "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
               "</styleSheet>";

        static string Numero(int formato, string alineacion)
            => $"<xf numFmtId=\"{formato}\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyAlignment=\"1\">" +
               $"<alignment horizontal=\"{alineacion}\"/></xf>";

        string Hoja(HojaExcel h, bool activa)
        {
            if (h.filas.Count + 1 > MaxFilas) throw new InvalidOperationException($"La hoja «{h.Nombre}» supera el máximo de filas de Excel");
            if (h.columnas.Count > MaxColumnas) throw new InvalidOperationException($"La hoja «{h.Nombre}» supera el máximo de columnas de Excel");
            var sb = new StringBuilder(Cabecera, 256 + h.filas.Count * 64);
            sb.Append($"<worksheet xmlns=\"{NsMain}\" xmlns:r=\"{NsRel}\">");
            sb.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>");          // al imprimir: una página de ancho
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"").Append(activa ? " tabSelected=\"1\"" : "").Append('>');
            if (h.TablaConEncabezado && h.columnas.Count > 0)
                sb.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/><selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/>");
            sb.Append("</sheetView></sheetViews>");
            sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
            if (h.columnas.Count > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < h.columnas.Count; c++)
                {
                    double ancho = Math.Max(4, Math.Min(120, h.columnas[c].ancho));
                    sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{ancho.ToString("0.##", C)}\" customWidth=\"1\"/>");
                }
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            int r = 1;
            if (h.TablaConEncabezado && h.columnas.Count > 0)
            {
                var enc = new Celda[h.columnas.Count];
                for (int c = 0; c < enc.Length; c++) enc[c] = Celda.T(h.columnas[c].titulo, EstiloCelda.Encabezado);
                FilaXml(sb, r++, enc, 30);
            }
            foreach (var fila in h.filas) FilaXml(sb, r++, fila, 0);
            sb.Append("</sheetData>");
            string rango = RangoFiltro(h);
            if (rango != null) sb.Append($"<autoFilter ref=\"{rango}\"/>");
            sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
            sb.Append($"<pageSetup orientation=\"{(h.columnas.Count > 5 ? "landscape" : "portrait")}\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        static string RangoFiltro(HojaExcel h)
        {
            if (!h.TablaConEncabezado || h.columnas.Count == 0 || h.filasDeDatos == 0) return null;
            return "A1:" + Columna(h.columnas.Count - 1) + (h.filasDeDatos + 1).ToString(C);
        }

        static string Absoluto(string rango)
        {
            var partes = rango.Split(':');
            return "$" + SepararFila(partes[0]) + ":$" + SepararFila(partes[1]);
        }

        static string SepararFila(string celda)
        {
            int i = 0;
            while (i < celda.Length && char.IsLetter(celda[i])) i++;
            return celda.Substring(0, i) + "$" + celda.Substring(i);
        }

        static void FilaXml(StringBuilder sb, int r, Celda[] celdas, double alto)
        {
            sb.Append("<row r=\"").Append(r.ToString(C)).Append('"');
            if (alto > 0) sb.Append(" ht=\"").Append(alto.ToString("0.##", C)).Append("\" customHeight=\"1\"");
            sb.Append('>');
            for (int c = 0; c < celdas.Length; c++)
            {
                var x = celdas[c];
                if (x.Tipo == TipoCelda.Vacia && x.Estilo == EstiloCelda.Normal) continue;
                string refCelda = Columna(c) + r.ToString(C);
                int s = (int)x.Estilo;
                sb.Append("<c r=\"").Append(refCelda).Append('"');
                if (s != 0) sb.Append(" s=\"").Append(s.ToString(C)).Append('"');
                switch (x.Tipo)
                {
                    case TipoCelda.Texto:
                        string t = Xml(x.Texto);
                        bool preservar = t.Length > 0 && (char.IsWhiteSpace(t[0]) || char.IsWhiteSpace(t[t.Length - 1]) || t.IndexOf('\n') >= 0);
                        sb.Append(" t=\"inlineStr\"><is><t").Append(preservar ? " xml:space=\"preserve\"" : "").Append('>').Append(t).Append("</t></is></c>");
                        break;
                    case TipoCelda.Numero:
                    case TipoCelda.Fecha:
                        sb.Append("><v>").Append(x.Numero.ToString("R", C)).Append("</v></c>");
                        break;
                    default:
                        sb.Append("/>");
                        break;
                }
            }
            sb.Append("</row>");
        }

        // 0 -> A, 25 -> Z, 26 -> AA ...
        public static string Columna(int indice)
        {
            var sb = new StringBuilder();
            for (int n = indice + 1; n > 0; n = (n - 1) / 26) sb.Insert(0, (char)('A' + (n - 1) % 26));
            return sb.ToString();
        }

        // Escapa XML y quita caracteres que XML 1.0 no permite (un texto pegado con controles no daña el archivo)
        public static string Xml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(ch).Append(s[++i]); continue; }
                if (char.IsSurrogate(ch)) continue;
                if (ch < 0x20 && ch != '\t' && ch != '\n' && ch != '\r') continue;
                if (ch == 0xFFFE || ch == 0xFFFF) continue;
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\r': break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
