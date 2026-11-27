using UnityEngine;
using UnityEngine.UI;

namespace Composta.App
{
    // Lista vertical con desplazamiento y filas de alto propio (mediciones, aportes, auditoría).
    // El contenido crece con las filas (VerticalLayoutGroup + ContentSizeFitter), en escritorio y en móvil.
    public sealed class ListaDesplazable
    {
        public const float Separacion = 8f;

        public RectTransform Vista { get; }
        readonly RectTransform contenido;
        readonly ScrollRect scroll;

        public ListaDesplazable(string nombre, Transform padre, Vector2 aMin, Vector2 aMax)
        {
            Vista = UIFabrica.Panel(nombre, padre, aMin, aMax, new Color(0, 0, 0, 0), 0);
            Vista.GetComponent<Image>().raycastTarget = true;        // se puede arrastrar desde huecos entre filas
            Vista.gameObject.AddComponent<RectMask2D>();

            var go = new GameObject("Contenido", typeof(RectTransform));
            go.transform.SetParent(Vista, false);
            contenido = (RectTransform)go.transform;
            contenido.anchorMin = new Vector2(0f, 1f); contenido.anchorMax = new Vector2(1f, 1f);
            contenido.pivot = new Vector2(0.5f, 1f);
            contenido.anchoredPosition = Vector2.zero;
            contenido.sizeDelta = Vector2.zero;

            var capa = go.AddComponent<VerticalLayoutGroup>();
            capa.spacing = Separacion;
            capa.padding = new RectOffset(2, 2, 2, 2);
            capa.childControlWidth = true; capa.childControlHeight = true;
            capa.childForceExpandWidth = true; capa.childForceExpandHeight = false;
            var ajuste = go.AddComponent<ContentSizeFitter>();
            ajuste.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ajuste.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll = Vista.gameObject.AddComponent<ScrollRect>();
            scroll.content = contenido; scroll.viewport = Vista;
            scroll.horizontal = false; scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.inertia = true; scroll.decelerationRate = 0.12f; scroll.scrollSensitivity = 30f;
        }

        public void Limpiar()
        {
            for (int i = contenido.childCount - 1; i >= 0; i--)
            {
                var hijo = contenido.GetChild(i).gameObject;
                hijo.SetActive(false);                  // el layout deja de contarla en este mismo cuadro
                hijo.transform.SetParent(null, false);
                UIFabrica.Destruir(hijo);
            }
        }

        public void IrAlInicio()
        {
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1f;
        }

        public RectTransform Fila(float alto, Color fondo)
        {
            var fila = UIFabrica.Panel("Fila", contenido, Vector2.zero, Vector2.one, fondo, Tema.RadioCampo);
            fila.GetComponent<Image>().raycastTarget = true;
            var le = fila.gameObject.AddComponent<LayoutElement>();
            le.minHeight = alto; le.preferredHeight = alto; le.flexibleHeight = 0f;
            return fila;
        }

        // Mensaje cuando la lista está vacía (para que nunca se vea un hueco sin explicación)
        public Text Aviso(string texto, float alto)
        {
            var fila = Fila(alto, new Color(0, 0, 0, 0));
            var t = UIFabrica.Texto(fila, "Aviso", Vector2.zero, Vector2.one, Tema.TxtPequeno + 1, TextAnchor.MiddleCenter, Tema.TextoSecundario);
            t.text = texto;
            return t;
        }
    }
}
