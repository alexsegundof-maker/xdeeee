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

        // ---------------- Íconos de ayuda ----------------
        public static readonly Color ColIconoAviso = new Color(0.55f, 0.33f, 0.05f);

        // Ícono circular con "?" (información) o "!" (advertencia) que muestra un tooltip al pasar el ratón.
        // ancla/pivote/posición en píxeles de referencia: así el círculo nunca se deforma.
        public static TooltipTrigger IconoInfo(Transform padre, string texto, Vector2 ancla, Vector2 pivote, Vector2 posicion,
                                               string simbolo = "?", float tam = 26f, Color? colorReposo = null)
        {
            var go = new GameObject("Info " + simbolo, typeof(RectTransform), typeof(Image), typeof(TooltipTrigger));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = ancla;
            rt.pivot = pivote;
            rt.sizeDelta = new Vector2(tam, tam);
            rt.anchoredPosition = posicion;

            var img = go.GetComponent<Image>();
            img.sprite = SpriteCirculo;
            img.raycastTarget = true;
            var borde = go.AddComponent<Outline>();
            borde.effectColor = new Color(1f, 1f, 1f, 0.18f);
            borde.effectDistance = new Vector2(1f, -1f);

            var t = Texto(rt, "Simbolo", Vector2.zero, Vector2.one, Mathf.RoundToInt(tam * 0.66f), TextAnchor.MiddleCenter, Color.white);
            t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
            t.fontStyle = FontStyle.Bold;
            t.text = simbolo;

            var trig = go.GetComponent<TooltipTrigger>();
            trig.Configurar(texto, img, t, colorReposo ?? (simbolo == "!" ? ColIconoAviso : TooltipTrigger.ColNormal));
            return trig;
        }

        // Coloca el ícono justo a la derecha del texto visible de una etiqueta alineada a la izquierda
        public static TooltipTrigger IconoJuntoATexto(Text etiqueta, string texto, string simbolo = "?")
        {
            var trig = IconoInfo(etiqueta.rectTransform, texto, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, simbolo);
            UbicarTrasTexto((RectTransform)trig.transform, etiqueta);
            return trig;
        }

        public static void UbicarTrasTexto(RectTransform icono, Text etiqueta, float separacion = 10f)
        {
            float ancho = etiqueta.rectTransform.rect.width;
            float x = etiqueta.preferredWidth;
            if (ancho > 0f) x = Mathf.Min(x, ancho);
            icono.anchoredPosition = new Vector2(x + separacion, 0f);
        }

        // Círculo suavizado generado en memoria (en una build no hay sprites "Knob" integrados)
        static Sprite circulo;
        public static Sprite SpriteCirculo
        {
            get
            {
                if (circulo != null) return circulo;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                var px = new Color32[n * n];
                float c = (n - 1) / 2f, radio = n / 2f - 1f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radio - d + 0.5f) * 255f));
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                circulo = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
                circulo.hideFlags = HideFlags.HideAndDontSave;
                return circulo;
            }
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
