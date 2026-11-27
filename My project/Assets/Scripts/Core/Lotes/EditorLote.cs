using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Composta.Core.Campo;
using Composta.Core.Seguridad;

namespace Composta.Core.Lotes
{
    public sealed class ResultadoEdicion
    {
        public bool Exito { get; private set; }
        public string Mensaje { get; private set; }
        public string IdEntidad { get; private set; }
        public bool SinCambios { get; private set; }

        public static ResultadoEdicion Ok(string mensaje, string id = null, bool sinCambios = false)
            => new ResultadoEdicion { Exito = true, Mensaje = mensaje, IdEntidad = id, SinCambios = sinCambios };
        public static ResultadoEdicion Error(string mensaje) => new ResultadoEdicion { Exito = false, Mensaje = mensaje };
    }

    // Fecha de una medición escrita a mano. Vacía = ahora. Se interpreta en la zona horaria del dispositivo
    // (con su horario de verano en esa fecha, no el de hoy).
    public static class ValidadorFechaMedicion
    {
        public static readonly TimeSpan ToleranciaFuturo = TimeSpan.FromMinutes(5);   // relojes ligeramente desfasados
        public static readonly TimeSpan VentanaDuplicado = TimeSpan.FromHours(1);
        public const int DiasMaximosAtras = 365;
        public const string FormatoVisible = "dd/MM/yyyy HH:mm";
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        static readonly string[] FormatosConHora = { "d/M/yyyy H:mm", "d-M-yyyy H:mm", "yyyy-M-d H:mm", "d/M/yy H:mm" };
        static readonly string[] FormatosSinHora = { "d/M/yyyy", "d-M-yyyy", "yyyy-M-d", "d/M/yy" };

        public static bool Validar(string texto, TimeZoneInfo zona, DateTime ahoraUtc, out DateTime fechaUtc, out string error)
        {
            error = null;
            fechaUtc = ahoraUtc;
            zona = zona ?? TimeZoneInfo.Utc;
            string t = (texto ?? "").Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            if (t.Length == 0) return true;

            DateTime local;
            if (DateTime.TryParseExact(t, FormatosConHora, C, DateTimeStyles.None, out local)) { }
            else if (DateTime.TryParseExact(t, FormatosSinHora, C, DateTimeStyles.None, out local))
            {
                // Solo fecha: si es hoy se usa la hora actual; si es otro día, las 12:00 (evita saltos de día por zona horaria)
                var ahoraLocal = TimeZoneInfo.ConvertTimeFromUtc(ahoraUtc, zona);
                if (local.Date == ahoraLocal.Date) { fechaUtc = ahoraUtc; return ValidarUtc(fechaUtc, ahoraUtc, out error); }
                local = local.Date.AddHours(12);
            }
            else
            {
                error = "Fecha no válida. Usa el formato dd/mm/aaaa o dd/mm/aaaa hh:mm (por ejemplo 25/11/2026 09:30).";
                return false;
            }

            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zona.IsInvalidTime(local))
            {
                error = "Esa hora no existe en tu zona horaria (cambio al horario de verano). Escribe otra hora.";
                return false;
            }
            fechaUtc = TimeZoneInfo.ConvertTimeToUtc(local, zona);
            return ValidarUtc(fechaUtc, ahoraUtc, out error);
        }

        public static bool ValidarUtc(DateTime fechaUtc, DateTime ahoraUtc, out string error)
        {
            error = null;
            if (fechaUtc > ahoraUtc + ToleranciaFuturo) { error = "La fecha de la medición no puede ser futura."; return false; }
            if (fechaUtc < ahoraUtc.AddDays(-DiasMaximosAtras)) { error = "La fecha no puede ser de hace más de un año."; return false; }
            return true;
        }

        // Datos posibles pero dudosos: se avisan y la persona decide si guardar igual
        public static void Advertencias(Lote l, DateTime fechaUtc, string excluirId, TimeZoneInfo zona, List<string> advertencias)
        {
            if (l == null || advertencias == null) return;
            if (fechaUtc.Ticks < l.CreadoUtc - TimeSpan.TicksPerMinute)
                advertencias.Add($"La fecha es anterior a la creación del lote ({Formatear(l.CreadoUtc, zona)}). " +
                                 "Es correcto si la pila ya existía antes de registrarla en la app.");
            if (l.Informes == null) return;
            foreach (var r in l.Informes)
                if (r != null && r.Id != excluirId && Math.Abs(r.FechaUtc - fechaUtc.Ticks) < VentanaDuplicado.Ticks)
                {
                    advertencias.Add($"Ya hay una medición del {Formatear(r.FechaUtc, zona)}. Revisa que no la estés registrando dos veces.");
                    break;
                }
        }

        // Fechas vacías o fuera de rango (archivo editado a mano) se muestran como "—" en vez de romper la pantalla
        public static string Formatear(long fechaUtcTicks, TimeZoneInfo zona)
        {
            if (fechaUtcTicks <= TimeSpan.TicksPerDay || fechaUtcTicks >= DateTime.MaxValue.Ticks - TimeSpan.TicksPerDay) return "—";
            try { return TimeZoneInfo.ConvertTimeFromUtc(new DateTime(fechaUtcTicks, DateTimeKind.Utc), zona ?? TimeZoneInfo.Utc).ToString(FormatoVisible, C); }
            catch (ArgumentException) { return "—"; }
        }
    }

    // Reglas para modificar un lote. Sin Unity ni disco: valida, aplica el cambio y lo anota en la auditoría.
    // Quien llama (GestorLotes) se encarga de permisos, de guardar y de deshacer si el guardado falla.
    public static class EditorLote
    {
        public const int NombreMax = 80;
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // ---------------- Mediciones (Control Manual) ----------------
        public static ResultadoEdicion AgregarMedicion(Lote l, float t, float h, float p, string nota, DateTime fechaUtc,
                                                       string usuario, DateTime ahoraUtc)
        {
            var err = Comprobar(l, true) ?? ErrorMedicion(t, h, p, fechaUtc, ahoraUtc);
            if (err != null) return ResultadoEdicion.Error(err);
            var r = new InformeCampo
            {
                Id = Guid.NewGuid().ToString("N"),
                FechaUtc = fechaUtc.Ticks,
                RegistradoUtc = ahoraUtc.Ticks,
                Temperatura = Redondear(t, 1), Humedad = Redondear(h, 1), PesoKg = Redondear(p, 2),
                Nota = ValidadorAporte.LimpiarNota(nota),
                Usuario = usuario
            };
            l.Informes.Add(r);
            Anotar(l, AccionCambio.Creacion, EntidadCambio.Medicion, r.Id,
                   $"Medición del {Fecha(r.FechaUtc)}: {Num(r.Temperatura)} °C, {Num(r.Humedad)} %, {Num(r.PesoKg)} kg", usuario, ahoraUtc);
            return ResultadoEdicion.Ok("Medición guardada.", r.Id);
        }

        public static ResultadoEdicion EditarMedicion(Lote l, string id, float t, float h, float p, string nota, DateTime fechaUtc,
                                                      string usuario, DateTime ahoraUtc)
        {
            var err = Comprobar(l, true) ?? ErrorMedicion(t, h, p, fechaUtc, ahoraUtc);
            if (err != null) return ResultadoEdicion.Error(err);
            var r = l.Informes.Find(x => x != null && x.Id == id);
            if (r == null) return ResultadoEdicion.Error("La medición ya no existe.");

            t = Redondear(t, 1); h = Redondear(h, 1); p = Redondear(p, 2);
            nota = ValidadorAporte.LimpiarNota(nota);
            var cambios = new List<string>();
            if (r.FechaUtc / TimeSpan.TicksPerMinute != fechaUtc.Ticks / TimeSpan.TicksPerMinute)
                cambios.Add($"fecha {Fecha(r.FechaUtc)} → {Fecha(fechaUtc.Ticks)}");
            if (r.Temperatura != t) cambios.Add($"temperatura {Num(r.Temperatura)} → {Num(t)} °C");
            if (r.Humedad != h) cambios.Add($"humedad {Num(r.Humedad)} → {Num(h)} %");
            if (r.PesoKg != p) cambios.Add($"peso {Num(r.PesoKg)} → {Num(p)} kg");
            if ((r.Nota ?? "") != nota) cambios.Add("nota");
            if (cambios.Count == 0) return ResultadoEdicion.Ok("No había cambios que guardar.", r.Id, true);

            r.FechaUtc = fechaUtc.Ticks; r.Temperatura = t; r.Humedad = h; r.PesoKg = p; r.Nota = nota;
            r.EditadoPor = usuario; r.EditadoUtc = ahoraUtc.Ticks;
            Anotar(l, AccionCambio.Edicion, EntidadCambio.Medicion, r.Id, "Cambió " + string.Join(", ", cambios), usuario, ahoraUtc);
            return ResultadoEdicion.Ok("Medición actualizada.", r.Id);
        }

        public static ResultadoEdicion EliminarMedicion(Lote l, string id, string usuario, DateTime ahoraUtc)
        {
            var err = Comprobar(l, true);
            if (err != null) return ResultadoEdicion.Error(err);
            int i = l.Informes.FindIndex(x => x != null && x.Id == id);
            if (i < 0) return ResultadoEdicion.Error("La medición ya no existe.");
            var r = l.Informes[i];
            l.Informes.RemoveAt(i);
            Anotar(l, AccionCambio.Eliminacion, EntidadCambio.Medicion, r.Id,
                   $"Eliminó la medición del {Fecha(r.FechaUtc)} ({Num(r.Temperatura)} °C, {Num(r.Humedad)} %, {Num(r.PesoKg)} kg; registrada por {r.Usuario})",
                   usuario, ahoraUtc);
            return ResultadoEdicion.Ok("Medición eliminada.", r.Id);
        }

        // Medición inmediatamente anterior a una fecha (para detectar saltos raros al editar o registrar con fecha pasada)
        public static InformeCampo MedicionAnterior(Lote l, long fechaUtc, string excluirId)
        {
            InformeCampo mejor = null;
            if (l?.Informes == null) return null;
            foreach (var r in l.Informes)
                if (r != null && r.Id != excluirId && r.FechaUtc <= fechaUtc && (mejor == null || r.FechaUtc > mejor.FechaUtc)) mejor = r;
            return mejor;
        }

        // ---------------- Aportes (ambos modos) ----------------
        public static ResultadoEdicion AgregarAporte(Lote l, float kg, TipoMaterial material, string nota, int tick,
                                                     string usuario, DateTime ahoraUtc)
        {
            var err = Comprobar(l, false) ?? ErrorAporte(kg, material);
            if (err != null) return ResultadoEdicion.Error(err);
            var a = new Aporte
            {
                Id = Guid.NewGuid().ToString("N"), FechaUtc = ahoraUtc.Ticks, Tick = tick,
                Kg = Redondear(kg, 2), Material = material, Nota = ValidadorAporte.LimpiarNota(nota), Usuario = usuario
            };
            l.Aportes.Add(a);
            Anotar(l, AccionCambio.Creacion, EntidadCambio.Aporte, a.Id, $"Aporte de {Num(a.Kg)} kg ({ValidadorAporte.Nombre(material)})", usuario, ahoraUtc);
            return ResultadoEdicion.Ok(string.Format(C, "Aporte registrado: {0:0.##} kg ({1}).", a.Kg, ValidadorAporte.Nombre(material)), a.Id);
        }

        public static ResultadoEdicion EditarAporte(Lote l, string id, float kg, TipoMaterial material, string nota,
                                                    string usuario, DateTime ahoraUtc)
        {
            var err = Comprobar(l, false) ?? ErrorAporte(kg, material);
            if (err != null) return ResultadoEdicion.Error(err);
            var a = l.Aportes.Find(x => x != null && x.Id == id);
            if (a == null) return ResultadoEdicion.Error("El aporte ya no existe.");

            kg = Redondear(kg, 2);
            nota = ValidadorAporte.LimpiarNota(nota);
            var cambios = new List<string>();
            if (a.Kg != kg) cambios.Add($"peso {Num(a.Kg)} → {Num(kg)} kg");
            if (a.Material != material) cambios.Add($"material {ValidadorAporte.Nombre(a.Material)} → {ValidadorAporte.Nombre(material)}");
            if ((a.Nota ?? "") != nota) cambios.Add("nota");
            if (cambios.Count == 0) return ResultadoEdicion.Ok("No había cambios que guardar.", a.Id, true);

            a.Kg = kg; a.Material = material; a.Nota = nota;
            a.EditadoPor = usuario; a.EditadoUtc = ahoraUtc.Ticks;
            Anotar(l, AccionCambio.Edicion, EntidadCambio.Aporte, a.Id, "Cambió " + string.Join(", ", cambios), usuario, ahoraUtc);
            return ResultadoEdicion.Ok("Aporte actualizado.", a.Id);
        }

        public static ResultadoEdicion EliminarAporte(Lote l, string id, string usuario, DateTime ahoraUtc)
        {
            var err = Comprobar(l, false);
            if (err != null) return ResultadoEdicion.Error(err);
            int i = l.Aportes.FindIndex(x => x != null && x.Id == id);
            if (i < 0) return ResultadoEdicion.Error("El aporte ya no existe.");
            var a = l.Aportes[i];
            l.Aportes.RemoveAt(i);
            Anotar(l, AccionCambio.Eliminacion, EntidadCambio.Aporte, a.Id,
                   $"Eliminó el aporte de {Num(a.Kg)} kg ({ValidadorAporte.Nombre(a.Material)}) del {Fecha(a.FechaUtc)}", usuario, ahoraUtc);
            return ResultadoEdicion.Ok("Aporte eliminado.", a.Id);
        }

        // ---------------- Datos del lote ----------------
        // En un lote de simulación solo se puede cambiar el nombre: sus parámetros definen la simulación ya calculada
        // (cambiarlos haría que al reanudarlo saliera otra historia distinta).
        public static ResultadoEdicion EditarLote(Lote l, string nombre, TipoComposta tipo, float relacionCN, float aislamiento,
                                                  string usuario, DateTime ahoraUtc)
        {
            if (l == null) return ResultadoEdicion.Error("No hay un lote abierto.");
            if (l.Estado != EstadoLote.Activo) return ResultadoEdicion.Error("El lote está cerrado y ya no se puede modificar.");
            string n = ColapsarEspacios(ValidadorCredenciales.LimpiarTexto(nombre, NombreMax * 2));
            if (n.Length == 0) return ResultadoEdicion.Error("Escribe un nombre para el lote.");
            if (n.Length > NombreMax) return ResultadoEdicion.Error($"El nombre no puede superar {NombreMax} caracteres.");
            if (!Enum.IsDefined(typeof(TipoComposta), tipo)) return ResultadoEdicion.Error("Tipo de compostera no válido.");

            var cambios = new List<string>();
            if (l.Nombre != n) cambios.Add($"nombre «{l.Nombre}» → «{n}»");
            bool parametros = l.Tipo != tipo || l.RelacionCN != relacionCN || l.Aislamiento != aislamiento;
            if (parametros)
            {
                if (l.Modo != ModoLote.Manual)
                    return ResultadoEdicion.Error("En un lote de simulación solo se puede cambiar el nombre: sus parámetros definen la simulación ya calculada.");
                if (!RangosParametros.De(ModoLote.Manual, Parametro.RelacionCN).Contiene(relacionCN) ||
                    !RangosParametros.De(ModoLote.Manual, Parametro.Aislamiento).Contiene(aislamiento))
                    return ResultadoEdicion.Error("Material o cobertura fuera de rango.");
                if (l.Tipo != tipo) cambios.Add($"tipo {PerfilComposta.Crear(l.Tipo).Nombre} → {PerfilComposta.Crear(tipo).Nombre}");
                if (l.RelacionCN != relacionCN) cambios.Add($"material (C/N {Num(l.RelacionCN)} → {Num(relacionCN)})");
                if (l.Aislamiento != aislamiento) cambios.Add($"cobertura (aislamiento {Num(l.Aislamiento)} → {Num(aislamiento)})");
            }
            if (cambios.Count == 0) return ResultadoEdicion.Ok("No había cambios que guardar.", l.Id, true);

            l.Nombre = n; l.Tipo = tipo; l.RelacionCN = relacionCN; l.Aislamiento = aislamiento;
            Anotar(l, AccionCambio.Edicion, EntidadCambio.Lote, l.Id, "Cambió " + string.Join(", ", cambios), usuario, ahoraUtc);
            return ResultadoEdicion.Ok("Datos del lote actualizados.", l.Id);
        }

        public static void AnotarEliminacionLote(Lote l, string usuario, DateTime ahoraUtc)
            => Anotar(l, AccionCambio.Eliminacion, EntidadCambio.Lote, l.Id, $"Eliminó el lote «{l.Nombre}»", usuario, ahoraUtc);

        // ---------------- Copia profunda (para deshacer si el guardado falla) ----------------
        public static Lote Clonar(Lote l)
        {
            if (l == null) return null;
            var c = new Lote
            {
                Version = l.Version, Id = l.Id, Nombre = l.Nombre, Estado = l.Estado, Modo = l.Modo, RitmoReal = l.RitmoReal,
                Tipo = l.Tipo, PesoKg = l.PesoKg, RelacionCN = l.RelacionCN, HumedadInicial = l.HumedadInicial,
                Aislamiento = l.Aislamiento, TempAmbiente = l.TempAmbiente, Semilla = l.Semilla,
                CreadoPor = l.CreadoPor, CreadoUtc = l.CreadoUtc, ActualizadoUtc = l.ActualizadoUtc, TicksSimulados = l.TicksSimulados,
                Comandos = new List<Comando>(), Aportes = new List<Aporte>(), Informes = new List<InformeCampo>(), Cambios = new List<CambioLote>()
            };
            if (l.Comandos != null) foreach (var x in l.Comandos) if (x != null)
                c.Comandos.Add(new Comando { Tick = x.Tick, Tipo = x.Tipo, Valor = x.Valor, Usuario = x.Usuario, FechaUtc = x.FechaUtc });
            if (l.Aportes != null) foreach (var x in l.Aportes) if (x != null)
                c.Aportes.Add(new Aporte { Id = x.Id, FechaUtc = x.FechaUtc, Tick = x.Tick, Kg = x.Kg, Material = x.Material, Nota = x.Nota,
                                           Usuario = x.Usuario, EditadoPor = x.EditadoPor, EditadoUtc = x.EditadoUtc });
            if (l.Informes != null) foreach (var x in l.Informes) if (x != null)
                c.Informes.Add(new InformeCampo { Id = x.Id, FechaUtc = x.FechaUtc, Tick = x.Tick, Temperatura = x.Temperatura, Humedad = x.Humedad,
                                                  PesoKg = x.PesoKg, Nota = x.Nota, Usuario = x.Usuario, RegistradoUtc = x.RegistradoUtc,
                                                  EditadoPor = x.EditadoPor, EditadoUtc = x.EditadoUtc });
            if (l.Cambios != null) foreach (var x in l.Cambios) if (x != null)
                c.Cambios.Add(new CambioLote { FechaUtc = x.FechaUtc, Usuario = x.Usuario, Accion = x.Accion, Entidad = x.Entidad,
                                               IdEntidad = x.IdEntidad, Detalle = x.Detalle });
            return c;
        }

        // ---------------- Internos ----------------
        static string Comprobar(Lote l, bool requiereManual)
        {
            if (l == null) return "No hay un lote abierto.";
            if (l.Estado != EstadoLote.Activo) return "El lote está cerrado y ya no se puede modificar.";
            if (requiereManual && l.Modo != ModoLote.Manual) return "Las mediciones de campo son solo para lotes de Control Manual.";
            if (l.Informes == null) l.Informes = new List<InformeCampo>();
            if (l.Aportes == null) l.Aportes = new List<Aporte>();
            if (l.Cambios == null) l.Cambios = new List<CambioLote>();
            return null;
        }

        static string ErrorMedicion(float t, float h, float p, DateTime fechaUtc, DateTime ahoraUtc)
        {
            if (float.IsNaN(t) || t < ValidadorInforme.TempMin || t > ValidadorInforme.TempMax)
                return $"La temperatura debe estar entre {ValidadorInforme.TempMin:F0} y {ValidadorInforme.TempMax:F0} °C.";
            if (float.IsNaN(h) || h < 0f || h > 100f) return "La humedad debe estar entre 0 y 100 %.";
            var rp = RangosParametros.De(ModoLote.Manual, Parametro.Peso);
            if (!rp.Contiene(p)) return $"El peso debe estar entre {Num(rp.Min)} y {rp.Max:F0} kg.";
            return ValidadorFechaMedicion.ValidarUtc(fechaUtc, ahoraUtc, out var e) ? null : e;
        }

        static string ErrorAporte(float kg, TipoMaterial material)
        {
            if (float.IsNaN(kg) || float.IsInfinity(kg) || kg < ValidadorAporte.KgMin || kg > ValidadorAporte.KgMax)
                return $"El peso del aporte debe estar entre {Num(ValidadorAporte.KgMin)} y {ValidadorAporte.KgMax:F0} kg.";
            if (!Enum.IsDefined(typeof(TipoMaterial), material)) return "Tipo de material no válido.";
            return null;
        }

        static void Anotar(Lote l, AccionCambio accion, EntidadCambio entidad, string id, string detalle, string usuario, DateTime ahoraUtc)
        {
            l.Cambios.Add(new CambioLote { FechaUtc = ahoraUtc.Ticks, Usuario = usuario, Accion = accion, Entidad = entidad, IdEntidad = id, Detalle = detalle });
            l.ActualizadoUtc = ahoraUtc.Ticks;
        }

        static string ColapsarEspacios(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool espacio = false;
            foreach (char ch in s)
            {
                if (char.IsWhiteSpace(ch)) { espacio = true; continue; }
                if (espacio && sb.Length > 0) sb.Append(' ');
                espacio = false;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        static float Redondear(float v, int decimales) => (float)Math.Round(v, decimales);
        static string Num(float v) => v.ToString("0.##", C);
        static string Fecha(long ticksUtc)
            => ticksUtc <= 0 || ticksUtc > DateTime.MaxValue.Ticks ? "—" : new DateTime(ticksUtc, DateTimeKind.Utc).ToString("dd/MM/yyyy HH:mm", C) + " UTC";
    }
}
