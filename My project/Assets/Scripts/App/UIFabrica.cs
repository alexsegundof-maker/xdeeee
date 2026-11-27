using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Composta.App
{
    // Construcción de UI por código, con el mismo estilo que PanelUI/PanelBuild
    // (anclas relativas + CanvasScaler 1920x1080). Centraliza lo que antes estaba duplicado.
    public static class UIFabrica
    {
        public static readonly Color ColFondo = new Color(0.08f, 0.09f, 0.11f);
        public static readonly Color ColPanel = new Color(0.14f, 0.15f, 0.18f);
        public static readonly Color ColCard = new Color(0.20f, 0.22f, 0.27f);
        public static readonly Color ColCampo = new Color(0.10f, 0.11f, 0.14f);
        public static readonly Color ColBoton = new Color(0.22f, 0.28f, 0.40f);
        public static readonly Color ColPrimario = new Color(0.18f, 0.45f, 0.25f);
        public static readonly Color ColPeligro = new Color(0.50f, 0.18f, 0.18f);
        public static readonly Color ColSel = new Color(0.25f, 0.45f, 0.75f);
        public static readonly Color ColTextoTenue = new Color(1f, 1f, 1f, 0.65f);
        public static readonly Color ColError = new Color(1f, 0.55f, 0.55f);
        public static readonly Color ColExito = new Color(0.55f, 0.95f, 0.6f);

        static Font fuente;
        public static Font Fuente => fuente != null ? fuente : (fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static void AsegurarEventSystem()
        {
            // Con el Input System nuevo hace falta InputSystemUIInputModule (el StandaloneInputModule da error)
            if (Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        public static RectTransform Canvas(string nombre, Transform padre, int orden)
        {
            var go = new GameObject(nombre, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            if (padre != null) go.transform.SetParent(padre, false);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = orden;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        public static RectTransform Panel(string nombre, Transform padre, Vector2 aMin, Vector2 aMax, Color color)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = color.a > 0f;
            return rt;
        }

        public static Text Texto(Transform padre, string nombre, Vector2 aMin, Vector2 aMax, int tam, TextAnchor ancla, Color color)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = new Vector2(12, 4); rt.offsetMax = new Vector2(-12, -4);
            var t = go.GetComponent<Text>();
            t.font = Fuente; t.fontSize = tam; t.alignment = ancla; t.color = color;
            t.raycastTarget = false;
            t.supportRichText = true;
            return t;
        }

        public static Button Boton(Transform padre, string texto, Vector2 aMin, Vector2 aMax, int tam, Color color, UnityAction accion)
        {
            var rt = Panel("Btn " + texto, padre, aMin, aMax, color);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = rt.GetComponent<Image>();
            var colores = b.colors;
            colores.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.45f);
            b.colors = colores;
            if (accion != null) b.onClick.AddListener(accion);
            var t = Texto(rt, "Etiqueta", Vector2.zero, Vector2.one, tam, TextAnchor.MiddleCenter, Color.white);
            t.text = texto;
            return b;
        }

        // Texto escrito por personas usuarias: evita que "<color=...>" altere el texto enriquecido
        public static string SinRich(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");

        public static Text Etiqueta(Button b) => b.GetComponentInChildren<Text>(true);

        // InputField de una línea, con placeholder y opción de contraseña
        public static InputField Campo(Transform padre, string nombre, Vector2 aMin, Vector2 aMax, string placeholder,
                                       bool contrasena = false, int limite = 64, int tam = 24)
        {
            var rt = Panel(nombre, padre, aMin, aMax, ColCampo);
            var campo = rt.gameObject.AddComponent<InputField>();
            campo.targetGraphic = rt.GetComponent<Image>();

            var texto = Texto(rt, "Texto", Vector2.zero, Vector2.one, tam, TextAnchor.MiddleLeft, Color.white);
            texto.supportRichText = false;           // obligatorio en InputField
            texto.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = Texto(rt, "Placeholder", Vector2.zero, Vector2.one, tam, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.35f));
            ph.fontStyle = FontStyle.Italic;
            ph.text = placeholder;

            campo.textComponent = texto;
            campo.placeholder = ph;
            campo.lineType = InputField.LineType.SingleLine;
            campo.characterLimit = limite;
            campo.contentType = contrasena ? InputField.ContentType.Password : InputField.ContentType.Standard;
            campo.asteriskChar = '•';
            campo.caretWidth = 2;
            campo.customCaretColor = true;
            campo.caretColor = Color.white;
            campo.selectionColor = new Color(0.25f, 0.45f, 0.75f, 0.6f);
            return campo;
        }

        public static void Enfocar(Selectable s)
        {
            if (s == null || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(s.gameObject);
            if (s is InputField f) f.ActivateInputField();
        }

        public static void Destruir(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
        }
    }
}
