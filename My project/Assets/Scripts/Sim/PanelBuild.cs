using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Composta.Core;

public class PanelBuild : MonoBehaviour
{
    public SimuladorComposta simulador;

    [Header("Iconos (opcional). Orden: Doméstica, Cerrada, Pila abierta, Industrial, Vermicomposta")]
    public Sprite[] iconosBuild = new Sprite[5];
    public Sprite iconoPesoExagerado;               // el caballo

    public bool abrirAlIniciar = true;

    // Estado del formulario (todo libre)
    TipoComposta tipoSel;
    float peso, cn, humIni, aisl, amb;

    Font fuente;
    Transform raiz;
    GameObject ventana, insigniaHud, insigniaVentana;
    Button btnCancelar;
    Text txtResumen;
    readonly Image[] fondoCards = new Image[5];
    readonly Slider[] sliders = new Slider[5];
    readonly RectTransform[] bandas = new RectTransform[5];
    readonly Text[] txtValores = new Text[5];
    bool pausaPrevia;

    const float LogMax = 4.30103f;       // log10(20000)
    static readonly string[] Etiquetas = { "Peso", "Relación C/N", "Humedad inicial", "Aislamiento", "Temp. ambiente" };
    static readonly float[] Min = { 0f, 10f, 20f, 0.5f, 0f };
    static readonly float[] Max = { LogMax, 60f, 90f, 2f, 40f };

    static readonly Color ColPanel = new Color(0.14f, 0.15f, 0.18f);
    static readonly Color ColCard = new Color(0.20f, 0.22f, 0.27f);
    static readonly Color ColSel = new Color(0.25f, 0.45f, 0.75f);
    static readonly Color ColBoton = new Color(0.22f, 0.28f, 0.40f);

    void Start()
    {
        if (simulador == null) simulador = FindFirstObjectByType<SimuladorComposta>();
        fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        ConstruirUI();
        if (abrirAlIniciar) Abrir(true); else ventana.SetActive(false);
    }

    void Update()
    {
        if (simulador == null || simulador.Sistema == null) return;
        insigniaHud.SetActive(!ventana.activeSelf && simulador.Sistema.Perfil.PesoExagerado);
    }

    // ---------------- Abrir / cerrar / aplicar ----------------
    void Abrir(bool inicio)
    {
        pausaPrevia = simulador.pausado;
        simulador.pausado = true;
        CargarDesdeSimulador();
        btnCancelar.gameObject.SetActive(!inicio);
        ventana.SetActive(true);
        Refrescar();
    }

    void Cerrar()
    {
        simulador.pausado = pausaPrevia;
        ventana.SetActive(false);
    }

    void Iniciar()
    {
        simulador.tipo = tipoSel;
        simulador.pesoKg = peso;
        simulador.relacionCN = cn;
        simulador.humedadInicial = humIni;
        simulador.aislamiento = aisl;
        simulador.tempAmbiente = amb;
        simulador.permitirPesoExagerado = true;
        simulador.Reiniciar();
        simulador.pausado = false;
        ventana.SetActive(false);
    }

    void CargarDesdeSimulador()
    {
        var d = PerfilComposta.Crear(simulador.tipo);
        tipoSel = simulador.tipo;
        peso = simulador.pesoKg;
        cn = simulador.relacionCN;
        humIni = simulador.humedadInicial >= 0f ? simulador.humedadInicial : d.HumedadInicial;
        aisl = simulador.aislamiento >= 0f ? simulador.aislamiento : d.Aislamiento;
        amb = simulador.tempAmbiente;
    }

    void ElegirBuild(TipoComposta t)
    {
        var d = PerfilComposta.Crear(t);
        tipoSel = t;
        peso = d.PesoDefecto; cn = d.RelacionCN; humIni = d.HumedadInicial; aisl = d.Aislamiento;
        Refrescar();                                  // la temperatura ambiente se conserva
    }

    PerfilComposta PerfilDelFormulario()
    {
        var p = PerfilComposta.Crear(tipoSel, peso, true);   // true = peso libre
        p.RelacionCN = cn; p.HumedadInicial = humIni; p.Aislamiento = aisl; p.TempAmbiente = amb;
        return p;
    }

    // ---------------- Sliders ----------------
    void AlCambiarSlider(int i, float v)
    {
        switch (i)
        {
            case 0: peso = Redondear(Mathf.Pow(10f, v)); break;
            case 1: cn = Mathf.Round(v); break;
            case 2: humIni = Mathf.Round(v); break;
            case 3: aisl = Mathf.Round(v * 20f) / 20f; break;
            default: amb = Mathf.Round(v); break;
        }
        Refrescar();
    }

    void Paso(int i, int dir)
    {
        switch (i)
        {
            case 0: peso = Redondear(peso + dir * PasoPeso(peso)); break;
            case 1: cn += dir; break;
            case 2: humIni += dir; break;
            case 3: aisl += dir * 0.05f; break;
            default: amb += dir; break;
        }
        Refrescar();
    }

    static float PasoPeso(float p) => p < 100f ? 1f : p < 1000f ? 5f : p < 10000f ? 50f : 500f;

    static float Redondear(float p)
    {
        if (p < 100f) return Mathf.Max(1f, Mathf.Round(p));
        if (p < 1000f) return Mathf.Round(p / 5f) * 5f;
        if (p < 10000f) return Mathf.Round(p / 50f) * 50f;
        return Mathf.Min(20000f, Mathf.Round(p / 500f) * 500f);
    }

    void Refrescar()
    {
        peso = Mathf.Clamp(peso, 1f, 20000f);
        cn = Mathf.Clamp(cn, Min[1], Max[1]);
        humIni = Mathf.Clamp(humIni, Min[2], Max[2]);
        aisl = Mathf.Clamp(aisl, Min[3], Max[3]);
        amb = Mathf.Clamp(amb, Min[4], Max[4]);

        var p = PerfilDelFormulario();
        var m = new ModeloComposta(p, 1);

        sliders[0].SetValueWithoutNotify(Mathf.Log10(peso));
        sliders[1].SetValueWithoutNotify(cn);
        sliders[2].SetValueWithoutNotify(humIni);
        sliders[3].SetValueWithoutNotify(aisl);
        sliders[4].SetValueWithoutNotify(amb);

        txtValores[0].text = (peso < 10f ? peso.ToString("F1") : peso.ToString("F0")) + " kg";
        txtValores[1].text = cn.ToString("F0") + " : 1";
        txtValores[2].text = humIni.ToString("F0") + " %";
        txtValores[3].text = "x" + aisl.ToString("F2");
        txtValores[4].text = amb.ToString("F0") + " °C";

        var z = p.ZonaIdeal(Magnitud.Humedad);
        Banda(0, Mathf.Log10(p.PesoMin), Mathf.Log10(p.PesoMax));
        Banda(1, 25f, 30f);
        Banda(2, z.min, z.max);
        Banda(3, Min[3], Min[3]);
        Banda(4, Min[4], Min[4]);

        var tipos = (TipoComposta[])System.Enum.GetValues(typeof(TipoComposta));
        for (int i = 0; i < fondoCards.Length; i++)
            fondoCards[i].color = tipos[i] == tipoSel ? ColSel : ColCard;

        string fase = m.FaseCalientePosible
            ? $"fase caliente ~{m.TempPlateau:F0} °C"
            : $"no alcanza fase caliente (máx. ~{m.TempPlateau:F0} °C)";
        txtResumen.text = $"{p.Nombre}  ·  peso recomendado {p.PesoMin:F0}-{p.PesoMax:F0} kg  ·  {fase}" +
                          (p.PesoExagerado ? "  ·  <color=#ffb454>PESO EXAGERADO</color>" : "");
        insigniaVentana.SetActive(p.PesoExagerado);
    }

    void Banda(int i, float a, float b)
    {
        float t0 = Mathf.InverseLerp(Min[i], Max[i], a);
        float t1 = Mathf.InverseLerp(Min[i], Max[i], b);
        bandas[i].anchorMin = new Vector2(t0, 0f);
        bandas[i].anchorMax = new Vector2(t1, 1f);
    }

    // ---------------- Construcción de la UI ----------------
    void ConstruirUI()
    {
        var cgo = new GameObject("Canvas Build", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;                       // encima de la UI principal
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        raiz = cgo.transform;

        // --- HUD permanente (esquina superior derecha) ---
        Boton(raiz, "Nueva composta", new Vector2(0.835f, 0.943f), new Vector2(0.985f, 0.982f), 20, ColBoton, () => Abrir(false), out _);
        insigniaHud = Insignia(raiz, new Vector2(0.66f, 0.943f), new Vector2(0.825f, 0.982f));
        insigniaHud.SetActive(false);

        // --- Ventana de selección ---
        ventana = Panel("Ventana", raiz, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.78f)).gameObject;
        var w = Panel("Marco", ventana.transform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), ColPanel);

        Texto(w, "Titulo", new Vector2(0.02f, 0.91f), new Vector2(0.58f, 0.99f), 34, TextAnchor.MiddleLeft, Color.white).text = "Nueva composta";
        insigniaVentana = Insignia(w, new Vector2(0.62f, 0.92f), new Vector2(0.98f, 0.985f));

        // 5 builds
        var tipos = (TipoComposta[])System.Enum.GetValues(typeof(TipoComposta));
        for (int i = 0; i < tipos.Length; i++)
        {
            var t = tipos[i];
            var d = PerfilComposta.Crear(t);
            float x0 = 0.02f + i * 0.196f;
            var card = Panel("Build " + d.Nombre, w, new Vector2(x0, 0.62f), new Vector2(x0 + 0.186f, 0.89f), ColCard);
            fondoCards[i] = card.GetComponent<Image>();
            var b = card.gameObject.AddComponent<Button>();
            b.targetGraphic = fondoCards[i];
            b.onClick.AddListener(() => ElegirBuild(t));

            var icono = Panel("Icono", card, new Vector2(0.15f, 0.36f), new Vector2(0.85f, 0.96f), Color.white).GetComponent<Image>();
            icono.sprite = i < iconosBuild.Length ? iconosBuild[i] : null;
            icono.preserveAspect = true;
            icono.raycastTarget = false;
            icono.enabled = icono.sprite != null;

            Texto(card, "Nombre", new Vector2(0f, 0f), new Vector2(1f, 0.36f), 17, TextAnchor.MiddleCenter, Color.white)
                .text = $"{d.Nombre}\n{d.PesoMin:F0}-{d.PesoMax:F0} kg";
        }

        txtResumen = Texto(w, "Resumen", new Vector2(0.02f, 0.545f), new Vector2(0.98f, 0.615f), 20, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.85f));

        // 5 sliders
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            float top = 0.52f - i * 0.085f, bot = top - 0.075f;
            Texto(w, "Etiqueta", new Vector2(0.02f, bot), new Vector2(0.25f, top), 22, TextAnchor.MiddleLeft, Color.white).text = Etiquetas[i];
            sliders[i] = CrearSlider(w, new Vector2(0.27f, bot), new Vector2(0.72f, top), Min[i], Max[i], v => AlCambiarSlider(idx, v), out bandas[i]);
            txtValores[i] = Texto(w, "Valor", new Vector2(0.73f, bot), new Vector2(0.86f, top), 22, TextAnchor.MiddleLeft, Color.white);
            Boton(w, "-", new Vector2(0.87f, bot + 0.008f), new Vector2(0.915f, top - 0.008f), 28, ColBoton, () => { Paso(idx, -1); }, out _);
            Boton(w, "+", new Vector2(0.925f, bot + 0.008f), new Vector2(0.97f, top - 0.008f), 28, ColBoton, () => { Paso(idx, +1); }, out _);
        }

        Boton(w, "Iniciar composta", new Vector2(0.62f, 0.015f), new Vector2(0.97f, 0.085f), 26, new Color(0.18f, 0.45f, 0.25f), Iniciar, out _);
        Boton(w, "Cancelar", new Vector2(0.02f, 0.015f), new Vector2(0.25f, 0.085f), 24, ColBoton, Cerrar, out btnCancelar);
    }

    GameObject Insignia(Transform padre, Vector2 aMin, Vector2 aMax)
    {
        var rt = Panel("Insignia peso exagerado", padre, aMin, aMax, new Color(0.55f, 0.33f, 0.05f, 0.95f));
        var icono = Panel("Icono", rt, new Vector2(0.02f, 0.05f), new Vector2(0.3f, 0.95f), Color.white).GetComponent<Image>();
        icono.sprite = iconoPesoExagerado;
        icono.preserveAspect = true;
        icono.raycastTarget = false;
        icono.enabled = iconoPesoExagerado != null;
        Texto(rt, "Texto", new Vector2(0.3f, 0f), Vector2.one, 18, TextAnchor.MiddleCenter, Color.white).text = "PESO EXAGERADO";
        return rt.gameObject;
    }

    Slider CrearSlider(Transform padre, Vector2 aMin, Vector2 aMax, float min, float max,
                       UnityAction<float> alCambiar, out RectTransform banda)
    {
        var raizS = Panel("Slider", padre, aMin, aMax, Color.clear);
        var s = raizS.gameObject.AddComponent<Slider>();

        var pista = Panel("Pista", raizS, new Vector2(0f, 0.38f), new Vector2(1f, 0.62f), new Color(0.27f, 0.29f, 0.34f));
        pista.offsetMin = new Vector2(10f, pista.offsetMin.y);
        pista.offsetMax = new Vector2(-10f, pista.offsetMax.y);
        banda = Panel("Recomendado", pista, Vector2.zero, new Vector2(0f, 1f), new Color(0.30f, 0.75f, 0.40f, 0.70f));

        var area = Panel("Area", raizS, Vector2.zero, Vector2.one, Color.clear);
        area.offsetMin = new Vector2(10f, 0f);
        area.offsetMax = new Vector2(-10f, 0f);
        var mango = Panel("Mango", area, new Vector2(0f, 0.1f), new Vector2(0f, 0.9f), Color.white);
        mango.sizeDelta = new Vector2(18f, 0f);

        s.handleRect = mango;
        s.targetGraphic = mango.GetComponent<Image>();
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min; s.maxValue = max;
        s.onValueChanged.AddListener(alCambiar);
        return s;
    }

    RectTransform Panel(string nombre, Transform padre, Vector2 aMin, Vector2 aMax, Color color)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(padre, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return rt;
    }

    Text Texto(Transform padre, string nombre, Vector2 aMin, Vector2 aMax, int tam, TextAnchor ancla, Color color)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(padre, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = new Vector2(12, 4); rt.offsetMax = new Vector2(-12, -4);
        var t = go.GetComponent<Text>();
        t.font = fuente; t.fontSize = tam; t.alignment = ancla; t.color = color;
        t.raycastTarget = false;
        return t;
    }

    Text Boton(Transform padre, string texto, Vector2 aMin, Vector2 aMax, int tam, Color color,
               UnityAction accion, out Button boton)
    {
        var rt = Panel("Btn " + texto, padre, aMin, aMax, color);
        boton = rt.gameObject.AddComponent<Button>();
        boton.targetGraphic = rt.GetComponent<Image>();
        boton.onClick.AddListener(accion);
        var t = Texto(rt, "Etiqueta", Vector2.zero, Vector2.one, tam, TextAnchor.MiddleCenter, Color.white);
        t.text = texto;
        return t;
    }
}