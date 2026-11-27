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
            var lotes = new[] { l };
            Resumen(libro.AgregarHoja("Resumen"), l, zona, ahoraUtc, versionApp);
            if (l.Modo == ModoLote.Manual) Mediciones(libro.AgregarHoja("Mediciones"), lotes, zona, false);
            else
            {
                Bitacora(libro.AgregarHoja("Bitácora"), l, zona);
                Historial(libro.AgregarHoja("Historial"), historial);
            }
            Aportes(libro.AgregarHoja("Aportes"), lotes, zona, false);
            Cambios(libro.AgregarHoja("Cambios"), lotes, zona, false);
            return libro;
        }

        public static byte[] Generar(Lote l, Historial historial, TimeZoneInfo zona, DateTime ahoraUtc, string versionApp)
            => Construir(l, historial, zona, ahoraUtc, versionApp).Generar(ahoraUtc);

        // Bitácora consolidada: una fila por lote (con sus indicadores) y todas las mediciones, aportes y cambios
        // juntos, con una columna «Lote» para filtrar en la hoja de cálculo.
        public static LibroExcel ConstruirConsolidado(IReadOnlyList<Lote> lotes, TimeZoneInfo zona, DateTime ahoraUtc, string versionApp)
        {
            zona = zona ?? TimeZoneInfo.Utc;
            var lista = new List<Lote>();
            if (lotes != null) foreach (var l in lotes) if (l != null) lista.Add(l);
            lista.Sort((x, y) => x.Numero != y.Numero ? x.Numero.CompareTo(y.Numero) : x.CreadoUtc.CompareTo(y.CreadoUtc));
            var libro = new LibroExcel { Titulo = "Composta · bitácora consolidada", Autor = "Composta " + (versionApp ?? "") };
            HojaLotes(libro.AgregarHoja("Lotes"), lista, zona, ahoraUtc);
            Mediciones(libro.AgregarHoja("Mediciones"), lista, zona, true);
            Aportes(libro.AgregarHoja("Aportes"), lista, zona, true);
            Cambios(libro.AgregarHoja("Cambios"), lista, zona, true);
            var info = libro.AgregarHoja("Sobre este archivo");
            info.TablaConEncabezado = false;
            info.Columna("", 30).Columna("", 60);
            info.Fila(Celda.T("Bitácora consolidada", EstiloCelda.Titulo));
            info.Fila(Celda.T("Exportado el", EstiloCelda.Clave), Celda.F(Local(ahoraUtc, zona), EstiloCelda.FechaIzq));
            info.Fila(Celda.T("Zona horaria de las fechas", EstiloCelda.Clave), Celda.T(Zona(zona, ahoraUtc)));
            info.Fila(Celda.T("Lotes incluidos", EstiloCelda.Clave), Celda.N(lista.Count, EstiloCelda.EnteroIzq));
            info.Fila(Celda.T("Generado con", EstiloCelda.Clave), Celda.T("Composta " + (versionApp ?? "")));
            if (lista.Exists(x => x.Demo)) info.Nota("Incluye lotes de DEMOSTRACIÓN (marcados en la columna «Demo»).");
            return libro;
        }

        public static byte[] GenerarConsolidado(IReadOnlyList<Lote> lotes, TimeZoneInfo zona, DateTime ahoraUtc, string versionApp)
            => ConstruirConsolidado(lotes, zona, ahoraUtc, versionApp).Generar(ahoraUtc);

        // ---------------- Hojas ----------------
        static void Resumen(HojaExcel h, Lote l, TimeZoneInfo zona, DateTime ahoraUtc, string versionApp)
        {
            h.TablaConEncabezado = false;
            h.Columna("", 34).Columna("", 62);
            h.Fila(Celda.T(l.Demo ? "Resumen del lote (DEMOSTRACIÓN)" : "Resumen del lote", EstiloCelda.Titulo));
            h.Fila();
            void Dato(string clave, Celda valor) => h.Fila(Celda.T(clave, EstiloCelda.Clave), AlineadoIzquierda(valor));
            void Texto(string clave, string valor) => Dato(clave, Celda.T(valor));

            Texto("Lote", l.Nombre);
            if (l.Numero > 0) Dato("Número de lote", Celda.N(l.Numero, EstiloCelda.Entero));
            if (!string.IsNullOrEmpty(l.Compostera)) Texto("Compostera", l.Compostera);
            Texto("Modo", Nombres.Modo(l.Modo));
            Texto("Estado", Nombres.Estado(l.Estado) + (l.BloqueadoMaduracion && l.Estado == EstadoLote.Activo ? " (en maduración)" : ""));
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

            var b = BalanceLote.Calcular(l);
            h.Fila();
            h.Fila(Celda.T("Entradas y balance de la mezcla", EstiloCelda.Titulo));
            Dato("Peso de entrada total (kg)", Celda.N(R(b.PesoEntradaKg, 2), EstiloCelda.Decimal2));
            Dato("Carga inicial (kg)", Celda.N(R(b.CargaBaseKg, 2), EstiloCelda.Decimal2));
            Dato("Aportaciones posteriores (kg)", Celda.N(R(b.AportadoKg, 2), EstiloCelda.Decimal2));
            Dato("Número de aportaciones", Celda.N(l.NumeroAportaciones, EstiloCelda.Entero));
            Dato("Relación C:N de la mezcla", Celda.N(R(b.RelacionCN, 1), EstiloCelda.Decimal1));
            Texto("Estado de la relación C:N", BalanceLote.NombreNivel(b.Nivel));
            Dato("Humedad de la mezcla (%)", Celda.N(R(b.HumedadMezclaPct, 1), EstiloCelda.Decimal1));
            if (b.HumedadActualPct.HasValue) Dato("Humedad actual estimada (%)", Celda.N(R(b.HumedadActualPct.Value, 1), EstiloCelda.Decimal1));
            if (l.Modo == ModoLote.Manual)
            {
                var m = Maduracion.Estimar(l, ahoraUtc);
                Dato("Madurez estimada", Celda.F(Local(m.FechaMadurezUtc, zona).Date, EstiloCelda.Dia));
                if (l.Estado == EstadoLote.Activo) Dato("Días que faltan", Celda.N(Math.Ceiling(m.DiasRestantes), EstiloCelda.Entero));
                if (m.Reajustada) Texto("Reajuste de la fecha", m.Motivo);
                Texto("En maduración (sin aportaciones)", l.BloqueadoMaduracion ? "Sí" : "No");
            }
            Dato("Mediciones registradas", Celda.N(Contar(l.Informes), EstiloCelda.Entero));
            if (l.Modo != ModoLote.Manual) Dato("Acciones en la bitácora", Celda.N(Contar(l.Comandos), EstiloCelda.Entero));
            Dato("Cambios auditados", Celda.N(Contar(l.Cambios), EstiloCelda.Entero));

            if (l.TieneCosecha)
            {
                var ev = EvaluacionCosecha.Evaluar(l);
                h.Fila();
                h.Fila(Celda.T("Cosecha", EstiloCelda.Titulo));
                Dato("Fecha de cosecha", Fecha(l.Cosecha.FechaUtc, zona));
                Dato("Duración (días)", Celda.N(Math.Round(ev.DuracionDias), EstiloCelda.Entero));
                Dato("Peso cosechado (kg)", Celda.N(R(l.Cosecha.PesoKg, 2), EstiloCelda.Decimal2));
                Dato("Rendimiento (%)", Celda.N(R(ev.RendimientoPct, 1), EstiloCelda.Decimal1));
                Texto("Calidad", EvaluacionCosecha.DescribirCalidad(ev.Calidad) + $" ({ev.Puntos} de {ev.PuntosMaximos} criterios)");
                foreach (var cr in ev.Criterios) Texto((cr.Cumple ? "✔ " : "✘ ") + cr.Nombre, cr.Detalle);
                Texto("Registró la cosecha", l.Cosecha.Usuario);
                if (!string.IsNullOrEmpty(l.Cosecha.Nota)) Texto("Nota de cosecha", l.Cosecha.Nota);
            }

            if (l.Modo == ModoLote.Manual)
            {
                var e = AnalisisCampo.Analizar(l, ahoraUtc);
                h.Fila();
                h.Fila(Celda.T("Situación según las mediciones", EstiloCelda.Titulo));
                if (e.Registros == 0)
                    h.Nota("Todavía no hay mediciones: la fase estimada aparecerá con el primer registro.");
                else
                {
                    var u = e.Ultimo;
                    Dato("Última medición", Fecha(u.FechaUtc, zona));
                    Dato("Temperatura (°C)", Celda.N(R(u.Temperatura, 1), EstiloCelda.Decimal1));
                    Dato("Humedad (%)", Celda.N(R(u.Humedad, 1), EstiloCelda.Decimal1));
                    Dato("Peso (kg)", Celda.N(R(u.PesoKg, 2), EstiloCelda.Decimal2));
                    Texto("Fase estimada", e.Fase.HasValue ? Nombres.Etapa(e.Fase.Value) : "Sin datos suficientes");
                    if (e.PerdidaPesoTotalPct.HasValue && e.Registros > 1)
                        Dato("Pérdida de peso real (%), descontando aportaciones", Celda.N(R(e.PerdidaPesoTotalPct.Value, 1), EstiloCelda.Decimal1));
                }
                if (l.Estado == EstadoLote.Activo)
                    foreach (var rec in e.Recomendaciones)
                        Texto("Recomendación", SinEtiquetas(rec.Titulo).TrimEnd('.') + ". " + SinEtiquetas(rec.Detalle));
            }

            h.Fila();
            h.Fila(Celda.T("Sobre este archivo", EstiloCelda.Titulo));
            Dato("Exportado el", Celda.F(Local(ahoraUtc, zona)));
            Texto("Zona horaria de las fechas", Zona(zona, ahoraUtc));
            Texto("Generado con", "Composta " + (versionApp ?? ""));
            Texto("Identificador del lote", l.Id);
            if (l.Demo) h.Nota("Lote de DEMOSTRACIÓN: datos de ejemplo, no corresponden a una composta real.");
        }

        static void HojaLotes(HojaExcel h, List<Lote> lotes, TimeZoneInfo zona, DateTime ahoraUtc)
        {
            h.Columna("Lote", 9).Columna("Compostera", 16).Columna("Nombre", 34).Columna("Modo", 15).Columna("Estado", 14).Columna("Tipo", 22)
             .Columna("Inicio", 17).Columna("Cosecha", 17).Columna("Duración (días)", 11).Columna("Entrada total (kg)", 13)
             .Columna("Aportaciones", 12).Columna("C:N de la mezcla", 11).Columna("Peso cosechado (kg)", 13).Columna("Rendimiento (%)", 12)
             .Columna("Calidad", 9).Columna("Madurez estimada", 17).Columna("Demo", 7);
            foreach (var l in lotes)
            {
                var b = BalanceLote.Calcular(l);
                var m = Maduracion.Estimar(l, ahoraUtc);
                var ev = l.TieneCosecha ? EvaluacionCosecha.Evaluar(l) : null;
                string estado = Nombres.Estado(l.Estado) + (l.Estado == EstadoLote.Activo && l.BloqueadoMaduracion ? " (maduración)" : "");
                h.Fila(l.Numero > 0 ? Celda.N(l.Numero, EstiloCelda.Entero) : Celda.Vacia, Celda.T(l.Compostera), Celda.T(l.Nombre),
                       Celda.T(Nombres.Modo(l.Modo)), Celda.T(estado), Celda.T(Nombres.Tipo(l.Tipo)),
                       Celda.F(Local(m.InicioUtc, zona)), ev != null ? Fecha(l.Cosecha.FechaUtc, zona) : Celda.Vacia,
                       ev != null ? Celda.N(Math.Round(ev.DuracionDias), EstiloCelda.Entero) : Celda.Vacia,
                       Celda.N(R(b.PesoEntradaKg, 2), EstiloCelda.Decimal2), Celda.N(l.NumeroAportaciones, EstiloCelda.Entero),
                       Celda.N(R(b.RelacionCN, 1), EstiloCelda.Decimal1),
                       ev != null ? Celda.N(R(l.Cosecha.PesoKg, 2), EstiloCelda.Decimal2) : Celda.Vacia,
                       ev != null ? Celda.N(R(ev.RendimientoPct, 1), EstiloCelda.Decimal1) : Celda.Vacia,
                       ev != null ? Celda.T(ev.Calidad.ToString()) : Celda.Vacia,
                       l.Modo == ModoLote.Manual && ev == null && l.Estado == EstadoLote.Activo ? Celda.F(Local(m.FechaMadurezUtc, zona).Date, EstiloCelda.Dia) : Celda.Vacia,
                       Celda.T(l.Demo ? "Sí" : "No"));
            }
            if (lotes.Count == 0) h.Nota("No hay lotes guardados.");
        }

        static string EtiquetaCorta(Lote l) => l.Numero > 0 ? "#" + l.Numero.ToString("000", C) : l.Nombre;

        static void Mediciones(HojaExcel h, IReadOnlyList<Lote> lotes, TimeZoneInfo zona, bool conLote)
        {
            if (conLote) h.Columna("Lote", 9).Columna("Compostera", 16);
            h.Columna("Fecha de medición", 18).Columna("Temperatura (°C)", 15).Columna("Humedad (%)", 13).Columna("Peso (kg)", 12)
             .Columna("Registró", 16).Columna("Nota", 40).Columna("Registrada el", 18).Columna("Corregida por", 16).Columna("Corregida el", 18);
            int n = 0;
            foreach (var l in lotes)
                foreach (var r in AnalisisCampo.Ordenados(l))
                {
                    var fila = new List<Celda>();
                    if (conLote) { fila.Add(Celda.T(EtiquetaCorta(l))); fila.Add(Celda.T(l.Compostera)); }
                    fila.Add(Fecha(r.FechaUtc, zona)); fila.Add(Celda.N(R(r.Temperatura, 1), EstiloCelda.Decimal1));
                    fila.Add(Celda.N(R(r.Humedad, 1), EstiloCelda.Decimal1)); fila.Add(Celda.N(R(r.PesoKg, 2), EstiloCelda.Decimal2));
                    fila.Add(Celda.T(r.Usuario)); fila.Add(Celda.T(r.Nota)); fila.Add(Fecha(r.RegistradoUtc, zona));
                    fila.Add(Celda.T(r.EditadoPor)); fila.Add(string.IsNullOrEmpty(r.EditadoPor) ? Celda.Vacia : Fecha(r.EditadoUtc, zona));
                    h.Fila(fila.ToArray());
                    n++;
                }
            if (n == 0) h.Nota(conLote ? "Ningún lote tiene mediciones registradas." : "Todavía no hay mediciones registradas en este lote.");
        }

        static void Aportes(HojaExcel h, IReadOnlyList<Lote> lotes, TimeZoneInfo zona, bool conLote)
        {
            bool sim = !conLote && lotes.Count == 1 && lotes[0].Modo != ModoLote.Manual;
            if (conLote) h.Columna("Lote", 9).Columna("Compostera", 16);
            h.Columna("Fecha", 18);
            if (sim) h.Columna("Día simulado", 13);
            h.Columna("Tipo de registro", 15).Columna("Peso (kg)", 12).Columna("Material", 22).Columna("Grupo", 17).Columna("Humedad (%)", 12)
             .Columna("Registró", 16).Columna("Nota", 36).Columna("Corregido por", 16).Columna("Corregido el", 18);
            int n = 0;
            foreach (var l in lotes)
                foreach (var a in BalanceLote.AportesOrdenados(l))
                {
                    var d = Materiales.De(Materiales.Resolver(a));
                    var fila = new List<Celda>();
                    if (conLote) { fila.Add(Celda.T(EtiquetaCorta(l))); fila.Add(Celda.T(l.Compostera)); }
                    fila.Add(Fecha(a.FechaUtc, zona));
                    if (sim) fila.Add(Celda.N(R(a.Tick / 24f, 2), EstiloCelda.Decimal2));
                    fila.Add(Celda.T(a.EsCargaInicial ? "Carga inicial" : "Aportación"));
                    fila.Add(Celda.N(R(a.Kg, 2), EstiloCelda.Decimal2));
                    fila.Add(Celda.T(d.Nombre));
                    fila.Add(Celda.T(Nombres.Material(d.Grupo)));
                    fila.Add(Celda.N(R(Materiales.HumedadDe(a), 1), EstiloCelda.Decimal1));
                    fila.Add(Celda.T(a.Usuario));
                    fila.Add(Celda.T(a.Nota));
                    fila.Add(Celda.T(a.EditadoPor));
                    fila.Add(string.IsNullOrEmpty(a.EditadoPor) ? Celda.Vacia : Fecha(a.EditadoUtc, zona));
                    h.Fila(fila.ToArray());
                    n++;
                }
            if (n == 0) h.Nota(conLote ? "Ningún lote tiene aportaciones registradas." : "Todavía no hay aportes de residuos registrados en este lote.");
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

        static void Cambios(HojaExcel h, IReadOnlyList<Lote> lotes, TimeZoneInfo zona, bool conLote)
        {
            if (conLote) h.Columna("Lote", 9).Columna("Compostera", 16);
            h.Columna("Fecha", 18).Columna("Usuario", 16).Columna("Acción", 13).Columna("Elemento", 12).Columna("Detalle", 90);
            int n = 0;
            foreach (var l in lotes)
                foreach (var c in l.Cambios ?? new List<CambioLote>())
                {
                    if (c == null) continue;
                    var fila = new List<Celda>();
                    if (conLote) { fila.Add(Celda.T(EtiquetaCorta(l))); fila.Add(Celda.T(l.Compostera)); }
                    fila.Add(Fecha(c.FechaUtc, zona)); fila.Add(Celda.T(c.Usuario)); fila.Add(Celda.T(Capital(Nombres.Accion(c.Accion))));
                    fila.Add(Celda.T(Capital(Nombres.Entidad(c.Entidad)))); fila.Add(Celda.T(c.Detalle));
                    h.Fila(fila.ToArray());
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
                case EstiloCelda.Dia: return c.ConEstilo(EstiloCelda.DiaIzq);
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
