using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Textos;

namespace Composta.Core.Exportacion
{
    // Libro de Excel de un lote, pensado para personas: encabezados en español, fechas en hora local
    // como fechas reales, números como números (cada región ve su separador decimal) y una nota
    // visible en las hojas sin datos. Los CSV siguen existiendo para otros programas.
    public static class ExportadorExcel
    {
        public const string TipoMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static LibroExcel Construir(Lote l, Historial historial, TimeZoneInfo zona, DateTime ahoraUtc, string versionApp)
        {
            if (l == null) throw new ArgumentNullException(nameof(l));
            zona = zona ?? TimeZoneInfo.Utc;
            var libro = new LibroExcel { Titulo = "Composta · " + (l.Nombre ?? ""), Autor = "Composta " + (versionApp ?? "") };
            Resumen(libro.AgregarHoja("Resumen"), l, zona, ahoraUtc, versionApp);
            if (l.Modo == ModoLote.Manual) Mediciones(libro.AgregarHoja("Mediciones"), l, zona);
            else
            {
                Bitacora(libro.AgregarHoja("Bitácora"), l, zona);
                Historial(libro.AgregarHoja("Historial"), historial);
            }
            Aportes(libro.AgregarHoja("Aportes"), l, zona);
            Cambios(libro.AgregarHoja("Cambios"), l, zona);
            return libro;
        }

        public static byte[] Generar(Lote l, Historial historial, TimeZoneInfo zona, DateTime ahoraUtc, string versionApp)
            => Construir(l, historial, zona, ahoraUtc, versionApp).Generar(ahoraUtc);

        // ---------------- Hojas ----------------
        static void Resumen(HojaExcel h, Lote l, TimeZoneInfo zona, DateTime ahoraUtc, string versionApp)
        {
            h.TablaConEncabezado = false;
            h.Columna("", 34).Columna("", 62);
            h.Fila(Celda.T("Resumen del lote", EstiloCelda.Titulo));
            h.Fila();
            void Dato(string clave, Celda valor) => h.Fila(Celda.T(clave, EstiloCelda.Clave), AlineadoIzquierda(valor));
            void Texto(string clave, string valor) => Dato(clave, Celda.T(valor));

            Texto("Lote", l.Nombre);
            Texto("Modo", Nombres.Modo(l.Modo));
            Texto("Estado", Nombres.Estado(l.Estado));
            Texto("Tipo de compostera", Nombres.Tipo(l.Tipo));
            Texto("Creado por", l.CreadoPor);
            Dato("Creado el", Fecha(l.CreadoUtc, zona));
            if (l.Modo == ModoLote.Manual)
            {
                Texto("Material predominante", Nombres.MaterialPredominante(l.RelacionCN));
                Texto("Cobertura", Nombres.Cobertura(l.Aislamiento));
            }
            else
            {
                Dato("Peso inicial (kg)", Celda.N(R(l.PesoKg, 1), EstiloCelda.Decimal1));
                Dato("Relación C/N", Celda.N(R(l.RelacionCN, 1), EstiloCelda.Decimal1));
                Dato("Humedad inicial (%)", Celda.N(R(l.HumedadInicial, 1), EstiloCelda.Decimal1));
                Dato("Aislamiento", Celda.N(R(l.Aislamiento, 2), EstiloCelda.Decimal2));
                Dato("Temperatura ambiente (°C)", Celda.N(R(l.TempAmbiente, 1), EstiloCelda.Decimal1));
                Dato("Días simulados", Celda.N(R(l.DiasSimulados, 2), EstiloCelda.Decimal2));
                Dato("Semilla de la simulación", Celda.N(l.Semilla, EstiloCelda.Entero));
            }

            h.Fila();
            h.Fila(Celda.T("Totales", EstiloCelda.Titulo));
            if (l.Modo == ModoLote.Manual) Dato("Mediciones registradas", Celda.N(Contar(l.Informes), EstiloCelda.Entero));
            else Dato("Acciones en la bitácora", Celda.N(Contar(l.Comandos), EstiloCelda.Entero));
            Dato("Aportes registrados", Celda.N(Contar(l.Aportes), EstiloCelda.Entero));
            Dato("Total aportado (kg)", Celda.N(R(l.TotalAportadoKg, 2), EstiloCelda.Decimal2));
            Dato("Cambios auditados", Celda.N(Contar(l.Cambios), EstiloCelda.Entero));

            if (l.Modo == ModoLote.Manual)
            {
                var e = AnalisisCampo.Analizar(l);
                h.Fila();
                h.Fila(Celda.T("Situación según las mediciones", EstiloCelda.Titulo));
                if (e.Registros == 0)
                    h.Nota("Todavía no hay mediciones: la fase estimada y las recomendaciones aparecerán con el primer registro.");
                else
                {
                    var u = e.Ultimo;
                    Dato("Última medición", Fecha(u.FechaUtc, zona));
                    Dato("Temperatura (°C)", Celda.N(R(u.Temperatura, 1), EstiloCelda.Decimal1));
                    Dato("Humedad (%)", Celda.N(R(u.Humedad, 1), EstiloCelda.Decimal1));
                    Dato("Peso (kg)", Celda.N(R(u.PesoKg, 2), EstiloCelda.Decimal2));
                    Texto("Fase estimada", e.Fase.HasValue ? Nombres.Etapa(e.Fase.Value) : "Sin datos suficientes");
                    if (e.PerdidaPesoTotalPct.HasValue && e.Registros > 1)
                        Dato("Peso perdido desde la primera medición (%)", Celda.N(R(e.PerdidaPesoTotalPct.Value, 1), EstiloCelda.Decimal1));
                    foreach (var rec in e.Recomendaciones)
                        Texto("Recomendación", SinEtiquetas(rec.Titulo).TrimEnd('.') + ". " + SinEtiquetas(rec.Detalle));
                }
            }

            h.Fila();
            h.Fila(Celda.T("Sobre este archivo", EstiloCelda.Titulo));
            Dato("Exportado el", Celda.F(Local(ahoraUtc, zona)));
            Texto("Zona horaria de las fechas", Zona(zona, ahoraUtc));
            Texto("Generado con", "Composta " + (versionApp ?? ""));
            Texto("Identificador del lote", l.Id);
        }

        static void Mediciones(HojaExcel h, Lote l, TimeZoneInfo zona)
        {
            h.Columna("Fecha de medición", 18).Columna("Temperatura (°C)", 15).Columna("Humedad (%)", 13).Columna("Peso (kg)", 12)
             .Columna("Registró", 16).Columna("Nota", 40).Columna("Registrada el", 18).Columna("Corregida por", 16).Columna("Corregida el", 18);
            var regs = AnalisisCampo.Ordenados(l);
            foreach (var r in regs)
                h.Fila(Fecha(r.FechaUtc, zona), Celda.N(R(r.Temperatura, 1), EstiloCelda.Decimal1), Celda.N(R(r.Humedad, 1), EstiloCelda.Decimal1),
                       Celda.N(R(r.PesoKg, 2), EstiloCelda.Decimal2), Celda.T(r.Usuario), Celda.T(r.Nota), Fecha(r.RegistradoUtc, zona),
                       Celda.T(r.EditadoPor), string.IsNullOrEmpty(r.EditadoPor) ? Celda.Vacia : Fecha(r.EditadoUtc, zona));
            if (regs.Count == 0) h.Nota("Todavía no hay mediciones registradas en este lote.");
        }

        static void Aportes(HojaExcel h, Lote l, TimeZoneInfo zona)
        {
            bool sim = l.Modo != ModoLote.Manual;
            h.Columna("Fecha", 18);
            if (sim) h.Columna("Día simulado", 13);
            h.Columna("Peso (kg)", 12).Columna("Material", 20).Columna("Registró", 16).Columna("Nota", 40).Columna("Corregido por", 16).Columna("Corregido el", 18);
            int n = 0;
            foreach (var a in l.Aportes ?? new List<Aporte>())
            {
                if (a == null) continue;
                var fila = new List<Celda> { Fecha(a.FechaUtc, zona) };
                if (sim) fila.Add(Celda.N(R(a.Tick / 24f, 2), EstiloCelda.Decimal2));
                fila.Add(Celda.N(R(a.Kg, 2), EstiloCelda.Decimal2));
                fila.Add(Celda.T(Nombres.Material(a.Material)));
                fila.Add(Celda.T(a.Usuario));
                fila.Add(Celda.T(a.Nota));
                fila.Add(Celda.T(a.EditadoPor));
                fila.Add(string.IsNullOrEmpty(a.EditadoPor) ? Celda.Vacia : Fecha(a.EditadoUtc, zona));
                h.Fila(fila.ToArray());
                n++;
            }
            if (n == 0) h.Nota("Todavía no hay aportes de residuos registrados en este lote.");
        }

        static void Bitacora(HojaExcel h, Lote l, TimeZoneInfo zona)
        {
            h.Columna("Fecha", 18).Columna("Día simulado", 13).Columna("Acción", 32).Columna("Valor", 10).Columna("Unidad o detalle", 18).Columna("Usuario", 16);
            int n = 0;
            foreach (var c in l.Comandos ?? new List<Comando>())
            {
                if (c == null) continue;
                bool num = Nombres.ValorNumerico(c, out double v, out string unidad);
                h.Fila(Fecha(c.FechaUtc, zona), Celda.N(R(c.Tick / 24f, 2), EstiloCelda.Decimal2), Celda.T(Nombres.Comando(c.Tipo)),
                       num ? Celda.N(Math.Round(v, 4), EstiloCelda.Decimal2) : Celda.Vacia, Celda.T(unidad), Celda.T(c.Usuario));
                n++;
            }
            if (n == 0) h.Nota("Sin acciones registradas.");
        }

        static void Historial(HojaExcel h, Historial historial)
        {
            h.Columna("Hora simulada", 13).Columna("Día", 9).Columna("Etapa", 26)
             .Columna("Temperatura medida (°C)", 15).Columna("pH medido", 10).Columna("Humedad medida (%)", 14).Columna("Oxígeno medido (%)", 14)
             .Columna("Temperatura real (°C)", 15).Columna("pH real", 9).Columna("Humedad real (%)", 13).Columna("Oxígeno real (%)", 13).Columna("Masa (kg)", 11);
            int n = historial == null ? 0 : Math.Min(historial.Lecturas.Count, historial.Reales.Count);
            for (int i = 0; i < n; i++)
            {
                var m = historial.Lecturas[i]; var r = historial.Reales[i];
                if (m == null || r == null) continue;
                h.Fila(Celda.N(m.Tick, EstiloCelda.Entero), Celda.N(R(m.Dias, 2), EstiloCelda.Decimal2), Celda.T(Nombres.Etapa(m.Etapa)),
                       Celda.N(R(m.Temperatura, 2), EstiloCelda.Decimal2), Celda.N(R(m.PH, 2), EstiloCelda.Decimal2),
                       Celda.N(R(m.Humedad, 2), EstiloCelda.Decimal2), Celda.N(R(m.Oxigeno, 2), EstiloCelda.Decimal2),
                       Celda.N(R(r.Temperatura, 2), EstiloCelda.Decimal2), Celda.N(R(r.PH, 2), EstiloCelda.Decimal2),
                       Celda.N(R(r.Humedad, 2), EstiloCelda.Decimal2), Celda.N(R(r.Oxigeno, 2), EstiloCelda.Decimal2),
                       Celda.N(R(r.MasaKg, 2), EstiloCelda.Decimal2));
            }
            if (n == 0) h.Nota("Sin lecturas: la simulación todavía no ha avanzado.");
        }

        static void Cambios(HojaExcel h, Lote l, TimeZoneInfo zona)
        {
            h.Columna("Fecha", 18).Columna("Usuario", 16).Columna("Acción", 13).Columna("Elemento", 12).Columna("Detalle", 90);
            int n = 0;
            foreach (var c in l.Cambios ?? new List<CambioLote>())
            {
                if (c == null) continue;
                h.Fila(Fecha(c.FechaUtc, zona), Celda.T(c.Usuario), Celda.T(Capital(Nombres.Accion(c.Accion))), Celda.T(Capital(Nombres.Entidad(c.Entidad))),
                       Celda.T(c.Detalle));
                n++;
            }
            if (n == 0) h.Nota("Sin cambios registrados: aquí aparece quién crea, corrige o elimina mediciones y aportes, y cuándo.");
        }

        // ---------------- Utilidades ----------------
        // En una ficha "clave: valor" los números se alinean a la izquierda, igual que los textos
        static Celda AlineadoIzquierda(Celda c)
        {
            switch (c.Estilo)
            {
                case EstiloCelda.Fecha: return c.ConEstilo(EstiloCelda.FechaIzq);
                case EstiloCelda.Decimal1: return c.ConEstilo(EstiloCelda.Decimal1Izq);
                case EstiloCelda.Decimal2: return c.ConEstilo(EstiloCelda.Decimal2Izq);
                case EstiloCelda.Entero: return c.ConEstilo(EstiloCelda.EnteroIzq);
                default: return c;
            }
        }

        static Celda Fecha(long ticksUtc, TimeZoneInfo zona)
            => ticksUtc <= TimeSpan.TicksPerDay || ticksUtc >= DateTime.MaxValue.Ticks - TimeSpan.TicksPerDay ? Celda.Vacia
               : Celda.F(Local(new DateTime(ticksUtc, DateTimeKind.Utc), zona));

        static DateTime Local(DateTime utc, TimeZoneInfo zona)
        {
            try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zona); }
            catch (ArgumentException) { return utc; }
        }

        public static string Zona(TimeZoneInfo zona, DateTime ahoraUtc)
        {
            var d = zona.GetUtcOffset(ahoraUtc);
            string desfase = (d < TimeSpan.Zero ? "−" : "+") + d.Duration().ToString(@"hh\:mm", C);
            string id = zona.Id ?? "";
            return id.Length == 0 || id == "UTC" ? "UTC" + desfase : $"UTC{desfase} ({id})";
        }

        // Los float se redondean antes de pasar a double: así la celda guarda 55,2 y no 55,200000762939453
        static double R(float v, int decimales) => Math.Round((double)v, decimales);

        static int Contar<T>(List<T> lista) where T : class
        {
            if (lista == null) return 0;
            int n = 0;
            foreach (var x in lista) if (x != null) n++;
            return n;
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string SinEtiquetas(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool dentro = false;
            foreach (char ch in s)
            {
                if (ch == '<') { dentro = true; continue; }
                if (ch == '>' && dentro) { dentro = false; continue; }
                if (!dentro) sb.Append(ch);
            }
            return sb.ToString().Trim();
        }
    }
}
