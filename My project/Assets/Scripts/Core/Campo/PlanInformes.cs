using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core.Lotes;

namespace Composta.Core.Campo
{
    public enum EstadoInforme { AlDia, Pendiente, Atrasado }

    public readonly struct SituacionInforme
    {
        public readonly EstadoInforme Estado;
        public readonly double HorasRestantes;     // negativo = atraso
        public readonly bool RitmoReal;
        public readonly int Informes;

        public SituacionInforme(EstadoInforme estado, double horasRestantes, bool ritmoReal, int informes)
        { Estado = estado; HorasRestantes = horasRestantes; RitmoReal = ritmoReal; Informes = informes; }

        public string Texto()
        {
            var c = CultureInfo.InvariantCulture;
            string unidad = RitmoReal ? "" : " simuladas";
            double h = Math.Abs(HorasRestantes);
            string tiempo = h >= 48 ? string.Format(c, "{0:F0} días{1}", h / 24, unidad) : string.Format(c, "{0:F0} h{1}", h, unidad);
            switch (Estado)
            {
                case EstadoInforme.Atrasado:
                    return $"Informe semanal atrasado {tiempo} · valores estimados con baja confianza";
                case EstadoInforme.Pendiente:
                    return $"Informe semanal pendiente · vence en {tiempo}";
                default:
                    return Informes == 0 ? $"Primer informe en {tiempo}" : $"Informe al día · próximo en {tiempo}";
            }
        }
    }

    // Cuándo toca el siguiente informe. Con ritmo real cuenta el calendario; en demostración, horas simuladas.
    public static class PlanInformes
    {
        public const int HorasPeriodo = 168;     // una semana
        public const int HorasAvisoPrevio = 24;

        public static SituacionInforme Evaluar(Lote l, DateTime ahoraUtc, int tickActual)
        {
            int n = l?.Informes?.Count ?? 0;
            if (l == null || l.Modo != ModoLote.Manual) return new SituacionInforme(EstadoInforme.AlDia, HorasPeriodo, false, n);

            double transcurridas;
            if (l.RitmoReal)
            {
                long ultimo = n > 0 ? l.Informes[n - 1].FechaUtc : l.CreadoUtc;
                transcurridas = (ahoraUtc - new DateTime(ultimo, DateTimeKind.Utc)).TotalHours;
            }
            else
            {
                int ultimoTick = n > 0 ? l.Informes[n - 1].Tick : 0;
                transcurridas = tickActual - ultimoTick;
            }

            double restantes = HorasPeriodo - transcurridas;
            var estado = restantes < 0 ? EstadoInforme.Atrasado
                       : restantes <= HorasAvisoPrevio ? EstadoInforme.Pendiente : EstadoInforme.AlDia;
            return new SituacionInforme(estado, restantes, l.RitmoReal, n);
        }
    }

    public static class ValidadorInforme
    {
        public const float TempMin = -10f, TempMax = 85f;

        // Errores bloquean; advertencias solo piden confirmación (un dato raro puede ser real)
        public static bool Validar(string temp, string hum, string peso, ModeloComposta estimado, float masaUltimoInforme,
                                   out float t, out float h, out float p, out string error, List<string> advertencias)
        {
            t = h = p = 0f; error = null;
            if (!RangosParametros.IntentarLeer(temp, out t) || t < TempMin || t > TempMax)
            { error = $"Temperatura: escribe un número entre {TempMin:F0} y {TempMax:F0} °C."; return false; }
            if (!RangosParametros.IntentarLeer(hum, out h) || h < 0f || h > 100f)
            { error = "Humedad: escribe un porcentaje entre 0 y 100."; return false; }
            var rp = RangosParametros.De(ModoLote.Manual, Parametro.Peso);
            if (!RangosParametros.IntentarLeer(peso, out p) || !rp.Contiene(p))
            { error = $"Peso: escribe un número entre {rp.Min} y {rp.Max:F0} kg."; return false; }

            if (advertencias != null && estimado != null)
            {
                var c = CultureInfo.InvariantCulture;
                if (Math.Abs(t - estimado.Temperatura) > 25f)
                    advertencias.Add(string.Format(c, "La temperatura difiere {0:F0} °C de lo estimado ({1:F0} °C).", Math.Abs(t - estimado.Temperatura), estimado.Temperatura));
                if (t > estimado.TempMax)
                    advertencias.Add(string.Format(c, "{0:F0} °C supera el máximo que maneja el modelo ({1:F0} °C); se usará ese máximo.", t, estimado.TempMax));
                if (Math.Abs(h - estimado.Humedad) > 30f)
                    advertencias.Add(string.Format(c, "La humedad difiere {0:F0} puntos de lo estimado ({1:F0} %).", Math.Abs(h - estimado.Humedad), estimado.Humedad));
                if (masaUltimoInforme > 0f && p > masaUltimoInforme * 1.15f)
                    advertencias.Add("El peso subió más de 15 % desde el último informe. Si agregaste material, regístralo también como aporte.");
                if (masaUltimoInforme > 0f && p < masaUltimoInforme * 0.6f)
                    advertencias.Add("El peso bajó más de 40 % desde el último informe. ¿Se retiró material de la pila?");
            }
            return true;
        }
    }
}
