using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Composta.App;
using Composta.Core;
using Composta.Core.Seguridad;

public class PanelBuild : MonoBehaviour
{
    public SimuladorComposta simulador;

    [Header("Iconos (opcional). Orden: Doméstica, Cerrada, Pila abierta, Industrial, Vermicomposta")]
    public Sprite[] iconosBuild = new Sprite[5];
    public Sprite iconoPesoExagerado;               // el caballo

    public bool abrirAlIniciar = true;

    // Estado del formulario
    TipoComposta tipoSel;
    ModoLote modoSel = ModoLote.Simular;
    float peso, cn, humIni, aisl, amb;

    Font fuente;
    Transform raiz;
    GameObject ventana, insigniaHud, insigniaVentana;
    Button btnCancelar, btnNueva, btnIniciar;
    Canvas canvasBuild;
    bool hudVisible = true;
    readonly Image[] fondoModo = new Image[2];
    readonly InputField[] campos = new InputField[5];     // edición exacta (solo modo Manual)
    readonly Text[] txtUnidades = new Text[5];
    Text txtResumen;
    RectTransform iconoResumenExagerado;      // "!" que sigue al final de la línea de resumen
    readonly Image[] fondoCards = new Image[5];
    readonly Slider[] sliders = new Slider[5];
    readonly RectTransform[] bandas = new RectTransform[5];
    readonly Text[] txtValores = new Text[5];
    bool pausaPrevia;

    static readonly string[] Etiquetas = { "Peso", "Relación C/N", "Humedad inicial", "Aislamiento", "Temp. ambiente" };
    static readonly string[] Unidades = { "kg", ": 1", "%", "x", "°C" };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Los límites ya no son constantes: dependen del modo (ver RangosParametros en Core)
    Rango RangoDe(int i) => RangosParametros.De(modoSel, (Parametro)i);
    float SliderMin(int i) => i == 0 ? Mathf.Log10(RangoDe(0).Min) : RangoDe(i).Min;   // el peso usa escala logarítmica
    float SliderMax(int i) => i == 0 ? Mathf.Log10(RangoDe(0).Max) : RangoDe(i).Max;

    static readonly Color ColPanel = new Color(0.14f, 0.15f, 0.18f);
    static readonly Color ColCard = new Color(0.20f, 0.22f, 0.27f);
    static readonly Color ColSel = new Color(0.25f, 0.45f, 0.75f);
    static readonly Color ColBoton = new Color(0.22f, 0.28f, 0.40f);

    void Start()
    {
        if (simulador == null) simulador = FindAnyObjectByType<SimuladorComposta>();
        if (simulador == null)
        {
            Debug.LogError("PanelBuild: no hay un SimuladorComposta en la escena.");
            enabled = false;
            return;
        }
        fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        ConstruirUI();
        // Con control de acceso, la ventana la abre ControlAcceso después del login
        bool hayLogin = FindAnyObjectByType<ControlAcceso>() != null;
        ventana.SetActive(false);
        if (abrirAlIniciar && !hayLogin) Abrir(true);
        btnNueva.interactable = permitirCrear;
        canvasBuild.enabled = hudVisible;
    }

    // Sin sesión no debe verse ni el HUD ni la ventana (lo controla ControlAcceso)
    public void MostrarHud(bool visible)
    {
        hudVisible = visible;
        if (canvasBuild != null) canvasBuild.enabled = visible;
    }

    void OnDestroy() => GuardiaEntrada.Liberar(this);

    // ---------------- API para la capa de aplicación ----------------
    bool permitirCrear = true;
    public bool Abierta => ventana != null && ventana.activeSelf;

    // obligatorio = sin botón Cancelar (no hay lote al que volver)
    public void AbrirFormulario(bool obligatorio)
    {
        if (ventana == null || !permitirCrear) return;
        Abrir(obligatorio);
    }

    public void PermitirCrear(bool valor)
    {
        permitirCrear = valor;
        if (btnNueva != null) btnNueva.interactable = valor;
        if (!valor && Abierta) Ocultar();
    }

    // Cierra sin tocar la pausa (la gestiona quien llama, p. ej. al cerrar sesión)
    public void Ocultar()
    {
        if (ventana == null) return;
        ventana.SetActive(false);
        GuardiaEntrada.Liberar(this);
    }

    void Update()
    {
        if (simulador == null || simulador.Sistema == null) return;
        insigniaHud.SetActive(!ventana.activeSelf && simulador.Sistema.Perfil.PesoExagerado);
        // El ancho del texto depende del tamaño de pantalla: se recoloca mientras la ventana esté abierta
        if (ventana.activeSelf && iconoResumenExagerado.gameObject.activeSelf)
            UIFabrica.UbicarTrasTexto(iconoResumenExagerado, txtResumen);
    }

    // ---------------- Abrir / cerrar / aplicar ----------------
    void Abrir(bool inicio)
    {
        if (Abierta) return;                            // evita perder la pausa previa al abrir dos veces
        GuardiaEntrada.Bloquear(this);                  // los atajos (Espacio, V...) no actúan detrás del modal
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
        Ocultar();
    }

    void Iniciar()
    {
        if (!simulador.PuedeHacer(Permiso.CrearLote))
        {
            Notificador.Mostrar("Tu rol no permite crear compostas nuevas.", TipoMensaje.Aviso);
            return;
        }
        // En modo Simular el aviso de peso es estricto: bloquea el inicio
        if (BloqueadoPorSimular(PerfilDelFormulario()))
        {
            Notificador.Mostrar("Peso fuera del rango recomendado. Ajústalo a la barra verde o cambia a modo Manual.", TipoMensaje.Aviso);
            return;
        }
        simulador.modo = modoSel;
        // Simular = progresión automatizada (control automático); Manual = la persona decide cuándo voltear/regar
        simulador.modoAutomatico = modoSel == ModoLote.Simular;
        simulador.tipo = tipoSel;
        simulador.pesoKg = peso;
        simulador.relacionCN = cn;
        simulador.humedadInicial = humIni;
        simulador.aislamiento = aisl;
        simulador.tempAmbiente = amb;
        simulador.permitirPesoExagerado = true;
        simulador.Reiniciar();
        simulador.pausado = false;
        Ocultar();
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
        modoSel = simulador.modo;                   // se propone el modo del último lote
        AplicarModo(false);
    }

    // ---------------- Modo Simular / Manual ----------------
    void ElegirModo(ModoLote m)
    {
        if (m == modoSel) return;
        modoSel = m;
        AplicarModo(true);
    }

    void AplicarModo(bool avisarAjustes)
    {
        for (int i = 0; i < fondoModo.Length; i++)
            fondoModo[i].color = i == (int)modoSel ? ColSel : ColCard;

        // Se guarda antes de tocar los sliders: cambiar min/max dispara onValueChanged si el valor queda fuera
        float[] antes = { peso, cn, humIni, aisl, amb };
        bool manual = modoSel == ModoLote.Manual;
        for (int i = 0; i < 5; i++)
        {
            sliders[i].minValue = SliderMin(i);
            sliders[i].maxValue = SliderMax(i);
            campos[i].gameObject.SetActive(manual);
            txtUnidades[i].gameObject.SetActive(manual);
            txtValores[i].gameObject.SetActive(!manual);
        }

        // Al volver a Simular, los valores experimentales se recortan a los rangos guiados
        Refrescar();
        float[] despues = { peso, cn, humIni, aisl, amb };
        if (avisarAjustes)
            for (int i = 0; i < 5; i++)
                if (!Mathf.Approximately(antes[i], despues[i]))
                {
                    Notificador.Mostrar("Algunos valores se ajustaron a los rangos del modo Simular.", TipoMensaje.Info);
                    break;
                }
    }

    bool BloqueadoPorSimular(PerfilComposta p) => modoSel == ModoLote.Simular && p.PesoExagerado;

    // Valor escrito a mano (modo Manual): acepta coma o punto; se limita a los márgenes físicos del modelo
    void AlEditarCampo(int i, string texto)
    {
        if (!RangosParametros.IntentarLeer(texto, out float v))
        {
            Notificador.Mostrar($"«{texto}» no es un número válido para {Etiquetas[i]}.", TipoMensaje.Aviso);
            Refrescar();
            return;
        }
        var r = RangoDe(i);
        if (!r.Contiene(v))
        {
            Notificador.Mostrar($"{Etiquetas[i]} se limitó a {r.Min.ToString("0.##", Inv)} – {r.Max.ToString("0.##", Inv)} (límite del modelo).", TipoMensaje.Info);
            v = r.Limitar(v);
        }
        switch (i)
        {
            case 0: peso = v; break;
            case 1: cn = v; break;
            case 2: humIni = v; break;
            case 3: aisl = v; break;
            default: amb = v; break;
        }
        Refrescar();
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

    static float PasoPeso(float p) => p < 10f ? 0.5f : p < 100f ? 1f : p < 1000f ? 5f : p < 10000f ? 50f : 500f;

    // Los límites los aplica Refrescar según el modo
    static float Redondear(float p)
    {
        if (p < 10f) return Mathf.Round(p * 10f) / 10f;
        if (p < 100f) return Mathf.Round(p);
        if (p < 1000f) return Mathf.Round(p / 5f) * 5f;
        if (p < 10000f) return Mathf.Round(p / 50f) * 50f;
        return Mathf.Round(p / 500f) * 500f;
    }

    void Refrescar()
    {
        peso = RangoDe(0).Limitar(peso);
        cn = RangoDe(1).Limitar(cn);
        humIni = RangoDe(2).Limitar(humIni);
        aisl = RangoDe(3).Limitar(aisl);
        amb = RangoDe(4).Limitar(amb);

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

        float[] valores = { peso, cn, humIni, aisl, amb };
        for (int i = 0; i < 5; i++)
        {
            if (!campos[i].isFocused) campos[i].SetTextWithoutNotify(valores[i].ToString("0.##", Inv));

            // Barra verde = rango recomendado; en Manual, un valor fuera de ella se marca en ámbar (no bloquea)
            var rec = RangosParametros.Recomendado(p, (Parametro)i);
            if (rec.HasValue)
            {
                var r = rec.Value;
                if (i == 0) Banda(0, Mathf.Log10(r.Min), Mathf.Log10(r.Max)); else Banda(i, r.Min, r.Max);
            }
            else Banda(i, SliderMin(i), SliderMin(i));
            bool fuera = rec.HasValue && !rec.Value.Contiene(valores[i]);
            campos[i].textComponent.color = fuera ? new Color(1f, 0.75f, 0.35f) : Color.white;
        }

        var tipos = (TipoComposta[])System.Enum.GetValues(typeof(TipoComposta));
        for (int i = 0; i < fondoCards.Length; i++)
            fondoCards[i].color = tipos[i] == tipoSel ? ColSel : ColCard;

        string fase = m.FaseCalientePosible
            ? $"fase caliente ~{m.TempPlateau:F0} °C"
            : $"no alcanza fase caliente (máx. ~{m.TempPlateau:F0} °C)";
        bool bloqueado = BloqueadoPorSimular(p);
        string aviso = !p.PesoExagerado ? ""
                     : bloqueado ? "  ·  <color=#ff8a6b>PESO EXAGERADO (ajústalo o usa Manual)</color>"
                     : "  ·  <color=#ffb454>PESO EXAGERADO (informativo)</color>";
        txtResumen.text = $"<b>{RangosParametros.Nombre(modoSel)}</b>  ·  {p.Nombre}  ·  peso recomendado {p.PesoMin:F0}-{p.PesoMax:F0} kg  ·  {fase}{aviso}";
        btnIniciar.interactable = !bloqueado;
        insigniaVentana.SetActive(p.PesoExagerado);
        iconoResumenExagerado.gameObject.SetActive(p.PesoExagerado);
        if (p.PesoExagerado) UIFabrica.UbicarTrasTexto(iconoResumenExagerado, txtResumen);
    }

    void Banda(int i, float a, float b)
    {
        float t0 = Mathf.InverseLerp(SliderMin(i), SliderMax(i), a);
        float t1 = Mathf.InverseLerp(SliderMin(i), SliderMax(i), b);
        bandas[i].anchorMin = new Vector2(t0, 0f);
        bandas[i].anchorMax = new Vector2(t1, 1f);
    }

    // ---------------- Construcción de la UI ----------------
    void ConstruirUI()
    {
        var cgo = new GameObject("Canvas Build", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasBuild = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;                       // encima de la UI principal
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        raiz = cgo.transform;

        // --- HUD permanente (esquina superior derecha) ---
        Boton(raiz, "Nueva composta", new Vector2(0.835f, 0.943f), new Vector2(0.985f, 0.982f), 20, ColBoton, () => Abrir(false), out btnNueva);
        insigniaHud = Insignia(raiz, new Vector2(0.66f, 0.943f), new Vector2(0.825f, 0.982f));
        insigniaHud.SetActive(false);

        // --- Ventana de selección ---
        ventana = Panel("Ventana", raiz, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.78f)).gameObject;
        var w = Panel("Marco", ventana.transform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), ColPanel);

        Texto(w, "Titulo", new Vector2(0.02f, 0.91f), new Vector2(0.58f, 0.99f), 34, TextAnchor.MiddleLeft, Color.white).text = "Nueva composta";
        insigniaVentana = Insignia(w, new Vector2(0.62f, 0.92f), new Vector2(0.98f, 0.985f));

        // Selector de modo (Simular / Manual)
        var modos = new[] { ModoLote.Simular, ModoLote.Manual };
        var nombresModo = new[] { "Simular", "Manual" };
        for (int i = 0; i < modos.Length; i++)
        {
            var m = modos[i];
            float x0 = 0.24f + i * 0.15f;
            Boton(w, nombresModo[i], new Vector2(x0, 0.925f), new Vector2(x0 + 0.145f, 0.982f), 22, ColCard, () => ElegirModo(m), out var b);
            fondoModo[i] = b.GetComponent<Image>();
        }
        UIFabrica.IconoInfo(w, AyudaBuild.Modo, new Vector2(0.54f, 0.9535f), new Vector2(0f, 0.5f), new Vector2(8f, 0f));

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

            // Ayuda del tipo (esquina superior derecha de la tarjeta)
            UIFabrica.IconoInfo(card, AyudaBuild.Tipo(d), Vector2.one, Vector2.one, new Vector2(-8f, -8f));
        }

        txtResumen = Texto(w, "Resumen", new Vector2(0.02f, 0.545f), new Vector2(0.98f, 0.615f), 20, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.85f));
        txtResumen.rectTransform.offsetMin = new Vector2(46f, txtResumen.rectTransform.offsetMin.y);   // hueco para el ícono
        UIFabrica.IconoInfo(txtResumen.rectTransform, AyudaBuild.Resumen, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-34f, 0f));
        iconoResumenExagerado = (RectTransform)UIFabrica.IconoJuntoATexto(txtResumen, AyudaBuild.PesoExagerado, "!").transform;

        // 5 sliders
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            float top = 0.52f - i * 0.085f, bot = top - 0.075f;
            var etiqueta = Texto(w, "Etiqueta", new Vector2(0.02f, bot), new Vector2(0.25f, top), 22, TextAnchor.MiddleLeft, Color.white);
            etiqueta.text = Etiquetas[i];
            UIFabrica.IconoJuntoATexto(etiqueta, AyudaBuild.Parametros[i]);
            sliders[i] = CrearSlider(w, new Vector2(0.27f, bot), new Vector2(0.72f, top), SliderMin(i), SliderMax(i), v => AlCambiarSlider(idx, v), out bandas[i]);
            txtValores[i] = Texto(w, "Valor", new Vector2(0.73f, bot), new Vector2(0.86f, top), 22, TextAnchor.MiddleLeft, Color.white);

            // Modo Manual: campo para escribir el valor exacto (reemplaza al texto)
            campos[i] = UIFabrica.Campo(w, "Campo " + Etiquetas[i], new Vector2(0.735f, bot + 0.012f), new Vector2(0.815f, top - 0.012f), "valor", false, 10, 22);
            campos[i].onEndEdit.AddListener(t => AlEditarCampo(idx, t));
            txtUnidades[i] = Texto(w, "Unidad", new Vector2(0.81f, bot), new Vector2(0.87f, top), 20, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.7f));
            txtUnidades[i].text = Unidades[i];
            campos[i].gameObject.SetActive(false);
            txtUnidades[i].gameObject.SetActive(false);
            Boton(w, "-", new Vector2(0.87f, bot + 0.008f), new Vector2(0.915f, top - 0.008f), 28, ColBoton, () => { Paso(idx, -1); }, out _);
            Boton(w, "+", new Vector2(0.925f, bot + 0.008f), new Vector2(0.97f, top - 0.008f), 28, ColBoton, () => { Paso(idx, +1); }, out _);
        }

        Boton(w, "Iniciar composta", new Vector2(0.62f, 0.015f), new Vector2(0.97f, 0.085f), 26, new Color(0.18f, 0.45f, 0.25f), Iniciar, out btnIniciar);
        Boton(w, "Cancelar", new Vector2(0.02f, 0.015f), new Vector2(0.25f, 0.085f), 24, ColBoton, Cerrar, out btnCancelar);

        // Íconos fuera del botón: a la izquierda de "Iniciar" y a la derecha de "Cancelar".
        // Al ser hijos del botón, se ocultan con él (Cancelar solo existe si hay una composta en curso).
        UIFabrica.IconoInfo(btnIniciar.transform, AyudaBuild.Iniciar, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-10f, 0f));
        UIFabrica.IconoInfo(btnCancelar.transform, AyudaBuild.Cancelar, new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f));
    }

    GameObject Insignia(Transform padre, Vector2 aMin, Vector2 aMax)
    {
        var rt = Panel("Insignia peso exagerado", padre, aMin, aMax, new Color(0.55f, 0.33f, 0.05f, 0.95f));
        var icono = Panel("Icono", rt, new Vector2(0.02f, 0.05f), new Vector2(0.3f, 0.95f), Color.white).GetComponent<Image>();
        icono.sprite = iconoPesoExagerado;
        icono.preserveAspect = true;
        icono.raycastTarget = false;
        icono.enabled = iconoPesoExagerado != null;
        Texto(rt, "Texto", new Vector2(0.3f, 0f), new Vector2(0.88f, 1f), 18, TextAnchor.MiddleCenter, Color.white).text = "PESO EXAGERADO";
        // "!" en gris pizarra para que contraste sobre el naranja de la insignia
        UIFabrica.IconoInfo(rt, AyudaBuild.PesoExagerado, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f),
                            "!", 24f, TooltipTrigger.ColNormal);
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