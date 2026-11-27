using System;
using System.Collections.Generic;

namespace Composta.Core.Lotes
{
    // Reanuda un lote guardado reproduciendo, con la misma semilla, las mismas acciones
    // en las mismas horas simuladas ("event sourcing"). No hace falta serializar el estado
    // interno del modelo ni de los generadores aleatorios.
    public static class ReproductorLote
    {
        public const int MaxTicks = 24 * 365 * 3;   // 3 años simulados: límite de seguridad

        public static PerfilComposta CrearPerfil(Lote l)
        {
            var p = PerfilComposta.Crear(l.Tipo, l.PesoKg, true);
            p.RelacionCN = l.RelacionCN;
            p.TempAmbiente = l.TempAmbiente;
            p.HumedadInicial = l.HumedadInicial;
            p.Aislamiento = l.Aislamiento;
            return p;
        }

        // Corrige un lote leído de disco (archivo editado a mano o de una versión vieja).
        // Devuelve la lista de problemas encontrados; el lote queda utilizable.
        public static List<string> Sanear(Lote l)
        {
            var avisos = new List<string>();
            if (l == null) throw new ArgumentNullException(nameof(l));
            if (l.Comandos == null) l.Comandos = new List<Comando>();
            if (l.Aportes == null) l.Aportes = new List<Aporte>();
            if (!Enum.IsDefined(typeof(TipoComposta), l.Tipo)) { l.Tipo = TipoComposta.PilaAbierta; avisos.Add("tipo de composta desconocido"); }

            if (!Enum.IsDefined(typeof(ModoLote), l.Modo)) { l.Modo = ModoLote.Simular; avisos.Add("modo desconocido"); }

            // Se valida contra los rangos del modo Manual (los más amplios): un lote Simular siempre cabe
            var defecto = PerfilComposta.Crear(l.Tipo);
            l.PesoKg = Valido(l.PesoKg, Parametro.Peso, defecto.PesoDefecto, "peso", avisos);
            l.RelacionCN = Valido(l.RelacionCN, Parametro.RelacionCN, defecto.RelacionCN, "relación C/N", avisos);
            l.HumedadInicial = Valido(l.HumedadInicial, Parametro.HumedadInicial, defecto.HumedadInicial, "humedad inicial", avisos);
            l.Aislamiento = Valido(l.Aislamiento, Parametro.Aislamiento, defecto.Aislamiento, "aislamiento", avisos);
            l.TempAmbiente = Valido(l.TempAmbiente, Parametro.TempAmbiente, defecto.TempAmbiente, "temperatura ambiente", avisos);

            if (l.TicksSimulados < 0 || l.TicksSimulados > MaxTicks)
            {
                avisos.Add($"horas simuladas fuera de rango ({l.TicksSimulados})");
                l.TicksSimulados = Math.Max(0, Math.Min(l.TicksSimulados, MaxTicks));
            }

            int antes = l.Comandos.Count;
            l.Comandos.RemoveAll(c => c == null || c.Tick < 0 || c.Tick > l.TicksSimulados
                                      || !Enum.IsDefined(typeof(TipoComando), c.Tipo)
                                      || float.IsNaN(c.Valor) || float.IsInfinity(c.Valor));
            if (l.Comandos.Count != antes) avisos.Add($"{antes - l.Comandos.Count} acciones inválidas descartadas");

            // Orden estable por hora (List.Sort no es estable)
            var ordenados = new List<Comando>(l.Comandos);
            var idx = new Dictionary<Comando, int>();
            for (int i = 0; i < l.Comandos.Count; i++) idx[l.Comandos[i]] = i;
            ordenados.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : idx[a].CompareTo(idx[b]));
            l.Comandos = ordenados;

            l.Aportes.RemoveAll(a => a == null || float.IsNaN(a.Kg) || a.Kg <= 0f || a.Kg > ValidadorAporte.KgMax);
            return avisos;
        }

        public static SistemaComposta Reconstruir(Lote l, out ResultadoPaso ultimo)
        {
            var s = new SistemaComposta(CrearPerfil(l), l.Semilla);
            ultimo = null;
            int k = 0, n = l.Comandos.Count;
            for (int t = 0; ; t++)
            {
                while (k < n && l.Comandos[k].Tick == t) Aplicar(s, l.Comandos[k++]);
                if (t >= l.TicksSimulados) break;
                ultimo = s.Paso();
            }
            return s;
        }

        public static void Aplicar(SistemaComposta s, Comando c)
        {
            switch (c.Tipo)
            {
                case TipoComando.Voltear: s.Voltear(); break;
                case TipoComando.Regar: s.Regar(c.Valor); break;
                case TipoComando.Automatico: s.Controlador.Activo = c.Valor > 0.5f; break;
                case TipoComando.ProbFalla: s.Sensores.ProbFalla = c.Valor; break;
                case TipoComando.InyectarFalla: s.Sensores.InyectarAleatoria(); break;
                case TipoComando.RepararSensores: s.Sensores.RepararTodo(); break;
                case TipoComando.TempAmbiente: s.Modelo.EstablecerTempAmbiente(c.Valor); break;
            }
        }

        static float Valido(float v, Parametro p, float defecto, string nombre, List<string> avisos)
        {
            if (!RangosParametros.De(ModoLote.Manual, p).Contiene(v) || float.IsInfinity(v))
            {
                avisos.Add($"{nombre} inválido ({v})");
                return defecto;
            }
            return v;
        }
    }
}
