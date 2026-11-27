using System;
using System.Globalization;
using System.Text;

namespace Composta.Core.Lotes
{
    public static class ExportadorCsv
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        // Escapa comas, comillas y saltos de línea, y neutraliza "inyección de fórmulas"
        // (una nota que empiece con = + - @ se ejecutaría como fórmula al abrirla en Excel)
        public static string Campo(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            char p = v[0];
            if (p == '=' || p == '+' || p == '-' || p == '@' || p == '\t' || p == '\r') v = "'" + v;
            if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) v = "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }

        static string Fecha(long ticksUtc)
            => ticksUtc <= 0 ? "" : new DateTime(ticksUtc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss'Z'", C);

        public static string Bitacora(Lote l)
        {
            var sb = new StringBuilder("fecha_utc,tick,dia,usuario,accion,valor\n");
            foreach (var c in l.Comandos)
                if (c != null) sb.Append(Fecha(c.FechaUtc)).Append(',')
                  .Append(c.Tick.ToString(C)).Append(',')
                  .Append((c.Tick / 24f).ToString("F2", C)).Append(',')
                  .Append(Campo(c.Usuario)).Append(',')
                  .Append(c.Tipo).Append(',')
                  .Append(c.Valor.ToString("0.####", C)).Append('\n');
            return sb.ToString();
        }

        public static string Aportes(Lote l)
        {
            var sb = new StringBuilder("fecha_utc,tick,dia,kg,material,usuario,nota,editado_por,editado_utc\n");
            foreach (var a in l.Aportes)
            {
                if (a == null) continue;
                sb.Append(Fecha(a.FechaUtc)).Append(',')
                  .Append(a.Tick.ToString(C)).Append(',')
                  .Append((a.Tick / 24f).ToString("F2", C)).Append(',')
                  .Append(a.Kg.ToString("F2", C)).Append(',')
                  .Append(a.Material).Append(',')
                  .Append(Campo(a.Usuario)).Append(',')
                  .Append(Campo(a.Nota)).Append(',')
                  .Append(Campo(a.EditadoPor)).Append(',')
                  .Append(Fecha(a.EditadoUtc)).Append('\n');
            }
            return sb.ToString();
        }

        // Control Manual: mediciones reales registradas a mano
        public static string Mediciones(Lote l)
        {
            var sb = new StringBuilder("fecha_utc,temperatura_c,humedad_pct,peso_kg,usuario,nota,registrado_utc,editado_por,editado_utc\n");
            foreach (var r in Composta.Core.Campo.AnalisisCampo.Ordenados(l))
                sb.Append(Fecha(r.FechaUtc)).Append(',')
                  .Append(r.Temperatura.ToString("F1", C)).Append(',')
                  .Append(r.Humedad.ToString("F1", C)).Append(',')
                  .Append(r.PesoKg.ToString("F2", C)).Append(',')
                  .Append(Campo(r.Usuario)).Append(',')
                  .Append(Campo(r.Nota)).Append(',')
                  .Append(Fecha(r.RegistradoUtc)).Append(',')
                  .Append(Campo(r.EditadoPor)).Append(',')
                  .Append(Fecha(r.EditadoUtc)).Append('\n');
            return sb.ToString();
        }

        // Auditoría: quién creó, corrigió o eliminó cada registro (se exporta aunque esté vacía)
        public static string Cambios(Lote l)
        {
            var sb = new StringBuilder("fecha_utc,usuario,accion,elemento,id_elemento,detalle\n");
            if (l.Cambios == null) return sb.ToString();
            foreach (var c in l.Cambios)
            {
                if (c == null) continue;
                sb.Append(Fecha(c.FechaUtc)).Append(',')
                  .Append(Campo(c.Usuario)).Append(',')
                  .Append(NombreAccion(c.Accion)).Append(',')
                  .Append(NombreEntidad(c.Entidad)).Append(',')
                  .Append(Campo(c.IdEntidad)).Append(',')
                  .Append(Campo(c.Detalle)).Append('\n');
            }
            return sb.ToString();
        }

        public static string NombreAccion(AccionCambio a)
            => a == AccionCambio.Creacion ? "creación" : a == AccionCambio.Edicion ? "edición" : "eliminación";

        public static string NombreEntidad(EntidadCambio e)
            => e == EntidadCambio.Medicion ? "medición" : e == EntidadCambio.Aporte ? "aporte" : "lote";

        public static string Resumen(Lote l)
        {
            var sb = new StringBuilder("campo,valor\n");
            void Fila(string k, string v) => sb.Append(k).Append(',').Append(Campo(v)).Append('\n');
            Fila("lote", l.Nombre);
            Fila("id", l.Id);
            Fila("estado", l.Estado.ToString());
            Fila("modo", RangosParametros.Nombre(l.Modo));
            Fila("tipo", l.Tipo.ToString());
            Fila("peso_inicial_kg", l.PesoKg.ToString("F1", C));
            Fila("relacion_cn", l.RelacionCN.ToString("F1", C));
            Fila("humedad_inicial", l.HumedadInicial.ToString("F1", C));
            Fila("aislamiento", l.Aislamiento.ToString("F2", C));
            Fila("temp_ambiente", l.TempAmbiente.ToString("F1", C));
            Fila("semilla", l.Semilla.ToString(C));
            Fila("creado_por", l.CreadoPor);
            Fila("creado_utc", Fecha(l.CreadoUtc));
            Fila("dias_simulados", l.DiasSimulados.ToString("F2", C));
            Fila("aportes_total_kg", l.TotalAportadoKg.ToString("F2", C));
            Fila("aportes_registrados", (l.Aportes?.Count ?? 0).ToString(C));
            if (l.Modo == ModoLote.Manual) Fila("mediciones_registradas", (l.Informes?.Count ?? 0).ToString(C));
            Fila("cambios_auditados", (l.Cambios?.Count ?? 0).ToString(C));
            return sb.ToString();
        }
    }
}
