using UnityEngine;
using UnityEngine.UI;

namespace Composta.App
{
    // Cuadrícula de tarjetas con desplazamiento vertical. El número de columnas sale del ancho disponible:
    // 3–4 en escritorio, 1 en un teléfono vertical. Las tarjetas se agregan con Tarjeta().
    public sealed class CuadriculaDesplazable
    {
        public RectTransform Vista { get; }
        readonly RectTransform contenido;
        readonly ScrollRect scroll;
        readonly AjusteCuadricula ajuste;

        public CuadriculaDesplazable(string nombre, Transform padre, Vector2 aMin, Vector2 aMax, float anchoMinimo, float altoTarjeta, float altoTarjetaCompacta)
        {
            Vista = UIFabrica.Panel(nombre, padre, aMin, aMax, new Color(0, 0, 0, 0), 0);
            Vista.GetComponent<Image>().raycastTarget = true;
            Vista.gameObject.AddComponent<RectMask2D>();

            var go = new GameObject("Contenido", typeof(RectTransform));
            go.transform.SetParent(Vista, false);
            contenido = (RectTransform)go.transform;
            contenido.anchorMin = new Vector2(0f, 1f); contenido.anchorMax = new Vector2(1f, 1f);
            contenido.pivot = new Vector2(0.5f, 1f);
            contenido.anchoredPosition = Vector2.zero;
            contenido.sizeDelta = Vector2.zero;
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(14f, 14f);
            grid.padding = new RectOffset(4, 4, 4, 4);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            var fit = go.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ajuste = go.AddComponent<AjusteCuadricula>();
            ajuste.grid = grid; ajuste.anchoMinimo = anchoMinimo; ajuste.alto = altoTarjeta; ajuste.altoCompacto = altoTarjetaCompacta;

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
                hijo.SetActive(false);
                hijo.transform.SetParent(null, false);
                UIFabrica.Destruir(hijo);
            }
        }

        public void IrAlInicio()
        {
            ajuste.Recalcular();
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1f;
        }

        public RectTransform Tarjeta(Color fondo) => UIFabrica.Tarjeta("Tarjeta", contenido, Vector2.zero, Vector2.one, fondo);

        // Mensaje que ocupa una celda (lista vacía o sin coincidencias)
        public Text Aviso(string texto)
        {
            var celda = UIFabrica.Panel("Aviso", contenido, Vector2.zero, Vector2.one, new Color(0, 0, 0, 0), 0);
            var t = UIFabrica.Texto(celda, "Texto", Vector2.zero, Vector2.one, Tema.TxtCuerpo, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            t.text = texto;
            return t;
        }
    }

    public sealed class AjusteCuadricula : MonoBehaviour
    {
        public GridLayoutGroup grid;
        public float anchoMinimo = 340f, alto = 300f, altoCompacto = 320f;

        void OnEnable() { AdaptadorPantalla.AlCambiar += Recalcular; Recalcular(); }
        void OnDisable() => AdaptadorPantalla.AlCambiar -= Recalcular;
        void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Recalcular(); }

        public void Recalcular()
        {
            if (grid == null) return;
            float ancho = ((RectTransform)transform).rect.width - grid.padding.left - grid.padding.right;
            if (ancho <= 1f) return;
            int columnas = Mathf.Max(1, Mathf.FloorToInt((ancho + grid.spacing.x) / (anchoMinimo + grid.spacing.x)));
            float celda = (ancho - grid.spacing.x * (columnas - 1)) / columnas;
            var tam = new Vector2(Mathf.Floor(celda), AdaptadorPantalla.Compacto ? altoCompacto : alto);
            if ((grid.cellSize - tam).sqrMagnitude > 0.5f) grid.cellSize = tam;
        }
    }
}
