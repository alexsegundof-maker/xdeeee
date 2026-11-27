using System;
using System.Globalization;
using Composta.Core.Campo;
using UnityEngine;
using UnityEngine.UI;

namespace Composta.App
{
    // Franja visible en lotes Manual: sensores no conectados + estado del informe semanal + calibración
    public sealed class PanelCampo : MonoBehaviour
    {
        public event Action PedirInforme;

        RectTransform raiz;
        Image fondo;
        Text txtEstado, txtCalibracion;
        Button btnInforme;

        static readonly Color ColAlDia = new Color(0.16f, 0.30f, 0.22f, 0.97f);
        static readonly Color ColPendiente = new Color(0.45f, 0.36f, 0.08f, 0.97f);
        static readonly Color ColAtrasado = new Color(0.50f, 0.16f, 0.14f, 0.97f);

        public bool Visible => raiz != null && raiz.gameObject.activeSelf;

        public void Mostrar(bool visible)
        {
            if (visible) Asegurar();
            if (raiz != null) raiz.gameObject.SetActive(visible);
        }

        public void Actualizar(GestorLotes gestor, bool puedeInformar)
        {
            if (!Visible || gestor == null || !gestor.EsLoteDeCampo) return;
            var s = gestor.Situacion();
            var cal = gestor.Calibracion;
            bool atrasado = s.Estado == EstadoInforme.Atrasado;

            fondo.color = atrasado ? ColAtrasado : s.Estado == EstadoInforme.Pendiente ? ColPendiente : ColAlDia;
            txtEstado.text = s.Texto();

            var c = CultureInfo.InvariantCulture;
            float? dias = cal.DiasHastaMadurez();
            string madurez = dias == null ? "madurez: sin estimación aún"
                           : dias.Value <= 0.5f ? "en maduración"
                           : string.Format(c, "madurez estimada en ~{0:F0} días{1}", dias.Value, gestor.LoteActual.RitmoReal ? "" : " simulados");
            txtCalibracion.text = string.Format(c, "Calibración: confianza {0} · pila {1} · peso ≈ {2:0.#} kg · {3}",
                cal.Confianza(atrasado), cal.DescribirPila(), gestor.Modelo.MasaKg, madurez);

            btnInforme.interactable = puedeInformar;
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Campo", transform, 15);
            var f = UIFabrica.Panel("Franja", raiz, new Vector2(0.01f, 0.236f), new Vector2(0.99f, 0.29f), ColAlDia);
            fondo = f.GetComponent<Image>();

            UIFabrica.IconoInfo(f, "No hay sensores físicos conectados a este lote. Los valores del tablero son estimaciones del " +
                                   "simulador, que se recalibra con cada informe semanal (temperatura, humedad y peso medidos a mano). " +
                                   "Cuanto más tiempo pasa sin informe, menor es la confianza de las estimaciones.",
                                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), "!", 26f, UIFabrica.ColIconoAviso);
            var aviso = UIFabrica.Texto(f, "Aviso", new Vector2(0.025f, 0.5f), new Vector2(0.36f, 1f), 18, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));
            aviso.text = "<b>Sensores físicos no conectados</b> · Modo de registro manual activo";
            aviso.resizeTextForBestFit = true; aviso.resizeTextMinSize = 11; aviso.resizeTextMaxSize = 18;

            txtEstado = UIFabrica.Texto(f, "Estado", new Vector2(0.36f, 0.5f), new Vector2(0.80f, 1f), 18, TextAnchor.MiddleLeft, Color.white);
            txtEstado.resizeTextForBestFit = true; txtEstado.resizeTextMinSize = 11; txtEstado.resizeTextMaxSize = 18;
            txtCalibracion = UIFabrica.Texto(f, "Calibracion", new Vector2(0.025f, 0f), new Vector2(0.80f, 0.5f), 15, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.8f));
            txtCalibracion.resizeTextForBestFit = true; txtCalibracion.resizeTextMinSize = 10; txtCalibracion.resizeTextMaxSize = 15;

            btnInforme = UIFabrica.Boton(f, "Informe semanal", new Vector2(0.81f, 0.12f), new Vector2(0.995f, 0.88f), 20,
                                         UIFabrica.ColPrimario, () => PedirInforme?.Invoke());
            raiz.gameObject.SetActive(false);
        }
    }
}
