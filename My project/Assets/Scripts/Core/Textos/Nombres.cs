using System.Globalization;
using Composta.Core.Lotes;

namespace Composta.Core.Textos
{
    // Nombres en español de los valores internos (enumeraciones) tal como los ve la persona usuaria:
    // en pantalla, en el libro de Excel y en los CSV. Una sola fuente para que todo diga lo mismo.
    public static class Nombres
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        public static string Etapa(Etapa e)
        {
            switch (e)
            {
                case Core.Etapa.Mesofilica: return "Fase mesofílica";
                case Core.Etapa.Termofilica: return "Fase termofílica (caliente)";
                case Core.Etapa.Enfriamiento: return "Enfriamiento";
                default: return "Maduración";
            }
        }

        public static string Modo(ModoLote m) => m == ModoLote.Manual ? "Control manual" : "Simulación";

        public static string Estado(EstadoLote e)
            => e == EstadoLote.Cosechado ? "Cosechado" : e == EstadoLote.Cerrado ? "Cerrado" : "Activo";

        public static string Tipo(TipoComposta t) => PerfilComposta.Crear(t).Nombre;

        public static string Material(TipoMaterial m) => ValidadorAporte.Nombre(m);

        public static string Accion(AccionCambio a)
            => a == AccionCambio.Creacion ? "creación" : a == AccionCambio.Edicion ? "edición" : "eliminación";

        public static string Entidad(EntidadCambio e)
            => e == EntidadCambio.Medicion ? "medición" : e == EntidadCambio.Aporte ? "aporte" : "lote";

        public static string Comando(TipoComando t)
        {
            switch (t)
            {
                case TipoComando.Voltear: return "Volteo";
                case TipoComando.Regar: return "Riego";
                case TipoComando.Automatico: return "Control automático";
                case TipoComando.ProbFalla: return "Probabilidad de falla de sensores";
                case TipoComando.InyectarFalla: return "Falla de sensor (prueba)";
                case TipoComando.RepararSensores: return "Recalibración de sensores";
                case TipoComando.TempAmbiente: return "Temperatura ambiente";
                case TipoComando.Informe: return "Informe de campo";
                default: return t.ToString();
            }
        }

        // Valor numérico de una acción, si tiene sentido como número (para que la hoja de cálculo lo sume o grafique)
        public static bool ValorNumerico(Comando c, out double valor, out string unidad)
        {
            valor = 0; unidad = "";
            switch (c.Tipo)
            {
                case TipoComando.Regar:
                    if (c.Valor < 0f) { unidad = "riego estándar"; return false; }
                    valor = c.Valor; unidad = "litros"; return true;
                case TipoComando.ProbFalla: valor = c.Valor * 100.0; unidad = "% por hora"; return true;
                case TipoComando.TempAmbiente: valor = c.Valor; unidad = "°C"; return true;
                case TipoComando.Automatico: unidad = c.Valor > 0.5f ? "activado" : "desactivado"; return false;
                default: return false;
            }
        }

        // Texto corto para CSV o pantalla: "12.5 litros", "riego estándar", "activado"...
        public static string DetalleComando(Comando c)
            => ValorNumerico(c, out double v, out string u) ? v.ToString("0.####", C) + " " + u : u;

        // Fechas en español sin depender de la cultura del sistema (en Android/IL2CPP puede no estar "es-ES")
        static readonly string[] Meses = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };
        static readonly string[] DiasSemana = { "dom", "lun", "mar", "mié", "jue", "vie", "sáb" };

        public static string FechaCorta(System.DateTime local) => local.Day.ToString(C) + " " + Meses[local.Month - 1];          // "28 nov"
        public static string FechaConDia(System.DateTime local) => DiasSemana[(int)local.DayOfWeek] + " " + FechaCorta(local);   // "sáb 28 nov"

        public static string Cobertura(float aislamiento) => aislamiento > 1.15f ? "Tapada o cubierta" : "Al aire libre";

        public static string MaterialPredominante(float relacionCN)
            => relacionCN <= 23f ? "Restos de cocina (verde)" : relacionCN >= 34f ? "Hojas secas / cartón (café)" : "Mezcla";
    }
}
