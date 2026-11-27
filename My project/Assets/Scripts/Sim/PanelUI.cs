using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Composta.Core;
using Composta.Core.Seguridad;

public class PanelUI : MonoBehaviour
{
    public SimuladorComposta simulador;
    public GeneradorGraficas graficas;      // opcional

    Font fuente;
    Transform raiz;
    Text txtCabecera, txtAlertas, txtPausa, txtVel, txtModo, txtGraf;
    readonly Text[] txtValor = new Text[4], txtReal = new Text[4], txtSensor = new Text[4];
    readonly Image[] fondoCard = new Image[4];
    readonly RawImage[] imgGrafica = new RawImage[4];
    int tickMostrado = -1;
    bool pausaMostrada, autoMostrado;

    // Botones protegidos por permiso (se habilitan según el rol de la sesión)
    readonly List<(Button boton, Permiso permiso)> botonesProtegidos = new List<(Button, Permiso)>();
    Func<Permiso, bool> permisos;

    // Acciones que resuelve la capa de aplicación (ControlAcceso)
    public event Action PedirAportes;
    public event Action PedirExportar;

    static readonly Color ColFondo = new Color(0.08f, 0.09f, 0.11f);
    static readonly Color ColPanel = new Color(0.14f, 0.15f, 0.18f);
    static readonly Color ColOk = new Color(0.16f, 0.30f, 0.20f);
    static readonly Color ColAviso = new Color(0.45f, 0.36f, 0.08f);
    static readonly Color ColCritico = new Color(0.50f, 0.14f, 0.14f);

    void Start()
    {
        if (simulador == null) simulador = FindAnyObjectByType<SimuladorComposta>();
        if (graficas == null) graficas = FindAnyObjectByType<GeneradorGraficas>();
        if (simulador == null)
        {
            Debug.LogError("PanelUI: no hay un SimuladorComposta en la escena.");
            enabled = false;
            return;
        }
        if (graficas != null) graficas.mostrarEnPantalla = false;   // la UI las dibuja

        fuente = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        CrearEventSystem();
        Construir();
        simulador.AlCargar += AlCambiarSistema;
        simulador.AlReiniciar += AlCambiarSistema;
    }

    void OnDestroy()
    {
        if (simulador == null) return;
        simulador.AlCargar -= AlCambiarSistema;
        simulador.AlReiniciar -= AlCambiarSistema;
    }

    void AlCambiarSistema(SistemaComposta _) => Invalidar();

    // ---------------- Construcción ----------------
    void CrearEventSystem()
    {
        // Con el Input System nuevo se necesita este módulo (el estándar da error)
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    void Construir()
    {
        var cgo = new GameObject("Canvas UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        raiz = cgo.transform;

        Panel("Fondo", raiz, Vector2.zero, Vector2.one, ColFondo);

        // Barra superior
        var barra = Panel("Barra", raiz, new Vector2(0.01f, 0.935f), new Vector2(0.99f, 0.99f), ColPanel);
        // La cabecera ocupa la parte izquierda: el centro es de la sesión y la derecha de "Nueva composta"
        txtCabecera = Texto(barra, "Cabecera", Vector2.zero, new Vector2(0.40f, 1f), 26, TextAnchor.MiddleLeft, Color.white);
        txtCabecera.resizeTextForBestFit = true; txtCabecera.resizeTextMinSize = 14; txtCabecera.resizeTextMaxSize = 26;

        // 4 tarjetas (izquierda)
        for (int m = 0; m < 4; m++)
        {
            float top = 0.92f - m * 0.155f;
            var card = Panel("Card " + m, raiz, new Vector2(0.01f, top - 0.145f), new Vector2(0.33f, top), ColOk);
            fondoCard[m] = card.GetComponent<Image>();

            var mag = (Magnitud)m;
            var nombre = Texto(card, "Nombre", new Vector2(0f, 0.68f), new Vector2(0.55f, 1f), 22, TextAnchor.MiddleLeft, Color.white);
            nombre.text = Reglas.Nombre(mag);
            txtReal[m] = Texto(card, "Real", new Vector2(0.5f, 0.68f), new Vector2(1f, 1f), 18, TextAnchor.MiddleRight, new Color(1, 1, 1, 0.55f));
            txtValor[m] = Texto(card, "Valor", new Vector2(0f, 0.25f), new Vector2(1f, 0.75f), 46, TextAnchor.MiddleLeft, Color.white);
            txtSensor[m] = Texto(card, "Sensor", new Vector2(0f, 0f), new Vector2(1f, 0.3f), 17, TextAnchor.MiddleLeft, Color.white);
        }

        // 4 gráficas (derecha, 2x2)
        for (int m = 0; m < 4; m++)
        {
            int fila = m / 2, col = m % 2;
            float x0 = col == 0 ? 0.35f : 0.68f, x1 = col == 0 ? 0.67f : 0.99f;
            float y1 = fila == 0 ? 0.92f : 0.60f, y0 = y1 - 0.30f;
            var cuadro = Panel("Grafica " + m, raiz, new Vector2(x0, y0), new Vector2(x1, y1), ColPanel);

            var mag = (Magnitud)m;
            var titulo = Texto(cuadro, "Titulo", new Vector2(0f, 0.88f), new Vector2(1f, 1f), 18, TextAnchor.MiddleLeft, Color.white);
            titulo.text = Reglas.Nombre(mag) + (Reglas.De(mag).Unidad != "" ? " (" + Reglas.De(mag).Unidad + ")" : "");

            var go = new GameObject("Img", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(cuadro, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4, 4); rt.offsetMax = new Vector2(-4, -30);
            imgGrafica[m] = go.GetComponent<RawImage>();
            imgGrafica[m].color = Color.clear;       // oculta hasta que haya textura
        }

        // Alertas
        var panelAl = Panel("Alertas", raiz, new Vector2(0.01f, 0.10f), new Vector2(0.99f, 0.29f), ColPanel);
        txtAlertas = Texto(panelAl, "Texto", Vector2.zero, Vector2.one, 22, TextAnchor.UpperLeft, Color.white);
        txtAlertas.verticalOverflow = VerticalWrapMode.Truncate;

        // Botones (10 en una fila)
        const int total = 10;
        float paso = 0.98f / total, ancho = paso - 0.0075f;
        int i = 0;
        Boton("Voltear", i++, ancho, paso, simulador.Voltear, Permiso.OperarComposta);
        Boton("Regar", i++, ancho, paso, simulador.Regar, Permiso.OperarComposta);
        txtPausa = Boton("Pausa", i++, ancho, paso, simulador.AlternarPausa);
        txtVel = Boton("Velocidad", i++, ancho, paso, simulador.CambiarVelocidad);
        txtModo = Boton("Modo", i++, ancho, paso, simulador.AlternarAutomatico, Permiso.OperarComposta);
        Boton("Falla sensor", i++, ancho, paso, simulador.InyectarFalla, Permiso.InyectarFallas);
        Boton("Reparar", i++, ancho, paso, simulador.RepararSensores, Permiso.RepararSensores);
        txtGraf = Boton("Gráficas", i++, ancho, paso, () => { if (graficas != null) graficas.CambiarGranularidad(); });
        Boton("Aportes", i++, ancho, paso, () => PedirAportes?.Invoke());
        Boton("Exportar", i++, ancho, paso, () => PedirExportar?.Invoke(), Permiso.Exportar);
        RefrescarPermisos();
    }

    // La llama ControlAcceso al iniciar sesión. Puede llegar antes o después de Start.
    public void AplicarPermisos(Func<Permiso, bool> puede)
    {
        permisos = puede;
        RefrescarPermisos();
    }

    void RefrescarPermisos()
    {
        foreach (var (boton, permiso) in botonesProtegidos)
            if (boton != null) boton.interactable = permisos == null || permisos(permiso);
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

    Text Boton(string texto, int indice, float ancho, float paso, UnityAction accion, Permiso? permiso = null)
    {
        float x0 = 0.01f + indice * paso;
        var rt = Panel("Btn " + texto, raiz, new Vector2(x0, 0.015f), new Vector2(x0 + ancho, 0.085f), new Color(0.22f, 0.28f, 0.40f));
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = rt.GetComponent<Image>();
        b.onClick.AddListener(accion);
        if (permiso.HasValue) botonesProtegidos.Add((b, permiso.Value));
        var t = Texto(rt, "Etiqueta", Vector2.zero, Vector2.one, 20, TextAnchor.MiddleCenter, Color.white);
        t.text = texto;
        return t;
    }

    // ---------------- Actualización ----------------
    void Update()
    {
        if (simulador == null) return;

        txtPausa.text = simulador.pausado ? "Reanudar" : "Pausa";
        txtVel.text = "Velocidad x" + simulador.VelocidadActual;
        txtModo.text = simulador.modoAutomatico ? "Modo: AUTO" : "Modo: MANUAL";
        if (graficas != null) txtGraf.text = "Gráficas: " + graficas.granularidad;

        var r = simulador.Ultimo;
        if (r == null) return;

        // Antes la cabecera solo se actualizaba con cada hora simulada: al pausar nunca mostraba "PAUSADO"
        bool cambioEstado = simulador.pausado != pausaMostrada || simulador.modoAutomatico != autoMostrado;
        if (r.Medida.Tick == tickMostrado && !cambioEstado) return;
        bool tickNuevo = r.Medida.Tick != tickMostrado;
        tickMostrado = r.Medida.Tick;
        pausaMostrada = simulador.pausado;
        autoMostrado = simulador.modoAutomatico;

        ActualizarCabecera(r);
        if (!tickNuevo) return;
        ActualizarTelemetria(r);
        ActualizarAlertas();
        ActualizarGraficas();
    }

    void ActualizarCabecera(ResultadoPaso r)
    {
        txtCabecera.text = $"Día {r.Medida.Dias:F1}  ·  {r.Medida.Etapa}  ·  " +
                           (simulador.modoAutomatico ? "Automático" : "Manual") +
                           (simulador.pausado ? "  ·  <color=#ffd166>PAUSADO</color>" : "");
    }

    // Tras reanudar un lote o crear uno nuevo, forzar el redibujado completo
    public void Invalidar() => tickMostrado = -1;

    void ActualizarTelemetria(ResultadoPaso r)
    {

        for (int m = 0; m < 4; m++)
        {
            var mag = (Magnitud)m;
            var med = r.Mediciones[m];
            string fmt = mag == Magnitud.PH ? "F2" : "F1";
            string u = Reglas.De(mag).Unidad;

            txtValor[m].text = Agregador.Valor(r.Medida, mag).ToString(fmt) + " " + u + (med.Confiable ? "" : "  ?");
            txtReal[m].text = "real: " + Agregador.Valor(r.Real, mag).ToString(fmt);
            fondoCard[m].color = ColorNivel(NivelDe(Reglas.Nombre(mag)));

            if (med.Nivel == Nivel.Ok)
            {
                txtSensor[m].text = "Sensores A y B: OK";
                txtSensor[m].color = new Color(1, 1, 1, 0.6f);
            }
            else
            {
                txtSensor[m].text = med.Diagnostico;
                txtSensor[m].color = med.Nivel == Nivel.Critico ? new Color(1f, 0.55f, 0.55f) : new Color(1f, 0.85f, 0.4f);
            }
        }
    }

    void ActualizarAlertas()
    {
        var lista = new List<Alerta>(simulador.Monitor.Activas);
        lista.Sort((a, b) => b.Nivel.CompareTo(a.Nivel));

        var sb = new StringBuilder();
        if (lista.Count == 0) sb.Append("Sin alertas activas");
        foreach (var a in lista)
        {
            string col = a.Nivel == Nivel.Critico ? "#ff6b6b" : "#ffd166";
            string tipo = a.Variable.StartsWith("Sensor", StringComparison.Ordinal) ? "SENSOR"
                        : a.Variable.StartsWith("Predicción", StringComparison.Ordinal) ? "PREDICCIÓN" : "ALERTA";
            sb.Append($"<color={col}>[{tipo}]</color> {a.Mensaje}");
            if (!string.IsNullOrEmpty(a.Accion)) sb.Append($"   →   <b>{a.Accion}</b>");
            sb.AppendLine();
        }
        txtAlertas.text = sb.ToString();
    }

    void ActualizarGraficas()
    {
        if (graficas == null) return;
        for (int m = 0; m < 4; m++)
        {
            var tex = graficas.ObtenerTextura((Magnitud)m);
            imgGrafica[m].texture = tex;
            imgGrafica[m].color = tex == null ? Color.clear : Color.white;
        }
    }

    Nivel NivelDe(string variable)
    {
        var nivel = Nivel.Ok;
        foreach (var a in simulador.Monitor.Activas)
            if (a.Variable == variable && a.Nivel > nivel) nivel = a.Nivel;
        return nivel;
    }

    static Color ColorNivel(Nivel n) => n == Nivel.Critico ? ColCritico : n == Nivel.Aviso ? ColAviso : ColOk;
}