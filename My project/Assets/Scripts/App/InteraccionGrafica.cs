using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Composta.App
{
    // Convierte el cursor (o el dedo) sobre una gráfica en una posición horizontal 0..1.
    // No implementa IDragHandler a propósito: en móvil, arrastrar sigue desplazando la ventana.
    [RequireComponent(typeof(RectTransform))]
    public sealed class InteraccionGrafica : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler, IPointerDownHandler
    {
        public event Action<float> AlApuntar;      // fracción horizontal dentro de la gráfica
        public event Action AlSalir;

        public void OnPointerEnter(PointerEventData e) => Notificar(e);
        public void OnPointerMove(PointerEventData e) => Notificar(e);
        public void OnPointerDown(PointerEventData e) => Notificar(e);
        public void OnPointerExit(PointerEventData e) => AlSalir?.Invoke();

        void Notificar(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            var camara = e.enterEventCamera != null ? e.enterEventCamera : e.pressEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, camara, out var local)) return;
            float ancho = rt.rect.width;
            if (ancho <= 0f) return;
            AlApuntar?.Invoke(Mathf.Clamp01((local.x - rt.rect.xMin) / ancho));
        }
    }
}
