using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Composta.App;
using Composta.Core;
using Composta.Core.Seguridad;

public class GeneradorGraficas : MonoBehaviour
{
    public SimuladorComposta simulador;
    public Granularidad granularidad = Granularidad.Dia;
    public int ancho = 640, alto = 320;
    public int refrescarCadaTicks = 12;
    public bool mostrarEnPantalla = true;   // vista de prueba (luego la reemplaza la UI)

    static readonly Magnitud[] Todas =
        { Magnitud.Temperatura, Magnitud.PH, Magnitud.Humedad, Magnitud.Oxigeno };
    readonly Texture2D[] texturas = new Texture2D[4];

    // Se dispara DESPUÉS de redibujar. La UI asigna aquí las texturas: antes las pedía en su Update, que corre
    // antes que el LateUpdate de este componente, y en ese cuadro recibía null (o una textura ya destruida);
    // si la simulación se pausaba justo entonces, las gráficas se quedaban vacías.
    public event Action AlActualizar;

    void Start()
    {
        if (simulador == null) simulador = FindAnyObjectByType<SimuladorComposta>();
        if (simulador == null)
        {
            Debug.LogError("GeneradorGraficas: no hay un SimuladorComposta en la escena.");
            enabled = false;
            return;
        }
        simulador.AlTick += AlTick;
        simulador.AlReiniciar += AlCambiarSistema;
        simulador.AlCargar += AlCambiarSistema;
        // El simulador pudo crear o cargar su sistema antes de este Start (orden de Start no garantizado):
        // sin esto no había gráficas hasta la hora simulada 12
        if (simulador.Sistema != null) Regenerar();
    }

    void OnDestroy()
    {
        if (simulador != null)
        {
            simulador.AlTick -= AlTick;
            simulador.AlReiniciar -= AlCambiarSistema;
            simulador.AlCargar -= AlCambiarSistema;
        }
        for (int i = 0; i < texturas.Length; i++)          // las Texture2D no las libera el GC
            if (texturas[i] != null) Destroy(texturas[i]);
    }

    void AlTick(Lectura l)
    {
        // Se marca y se redibuja una sola vez por fotograma (al ponerse al día pasan cientos de horas de golpe)
        if (l.Tick % Mathf.Max(1, refrescarCadaTicks) == 0) pendiente = true;   // 0 provocaba DivideByZeroException
    }

    bool pendiente;

    void LateUpdate()
    {
        if (!pendiente) return;
        pendiente = false;
        Regenerar();
    }

    // Nueva composta o lote reanudado: redibujar ya (antes quedaban las gráficas del lote anterior)
    void AlCambiarSistema(SistemaComposta _) => Regenerar();

    public void Regenerar()
    {
        if (simulador == null || simulador.Sistema == null) return;
        pendiente = false;                                   // ya quedó al día: no repetir en LateUpdate
        var lecturas = simulador.Historial.Lecturas;
        for (int i = 0; i < Todas.Length; i++)
        {
            var serie = Agregador.Construir(lecturas, Todas[i], granularidad);
            // Reutiliza la textura: antes se creaba y destruía una de 640x320 cuatro veces cada 12 horas simuladas
            texturas[i] = DibujanteGrafica.Dibujar(serie, ancho, alto, simulador.Sistema.Perfil.ZonaIdeal(Todas[i]), texturas[i]);
        }
        AlActualizar?.Invoke();
    }

    public bool HayGraficas => Array.TrueForAll(texturas, t => t != null);

    // Textura de una magnitud. Busca su posición en Todas: no depende del orden numérico del enum Magnitud.
    public Texture2D ObtenerTextura(Magnitud m)
    {
        int i = Array.IndexOf(Todas, m);
        return i >= 0 ? texturas[i] : null;
    }

    public void CambiarGranularidad()
    {
        granularidad = granularidad == Granularidad.Hora ? Granularidad.Dia
                     : granularidad == Granularidad.Dia ? Granularidad.Semana
                     : Granularidad.Hora;
        Regenerar();
        Debug.Log($"Gráficas por: {granularidad}");
    }

    public void GuardarPng()
    {
        if (simulador != null && !simulador.PuedeHacer(Permiso.Exportar)) return;
        try
        {
            Regenerar();
            if (!HayGraficas)
            {
                // Antes: NullReferenceException al codificar una textura que todavía no existía
                Notificador.Mostrar("Todavía no hay datos para graficar: inicia o reanuda una simulación.", TipoMensaje.Aviso);
                return;
            }
            for (int i = 0; i < Todas.Length; i++)
            {
                string ruta = Path.Combine(Application.persistentDataPath,
                    $"grafica_{Todas[i]}_{granularidad}.png");
                File.WriteAllBytes(ruta, texturas[i].EncodeToPNG());
            }
            Debug.Log($"Gráficas guardadas en: {Application.persistentDataPath}");
            Notificador.Mostrar($"Gráficas PNG guardadas en: {Application.persistentDataPath}", TipoMensaje.Exito);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Notificador.Mostrar("No se pudieron guardar las gráficas: " + e.Message, TipoMensaje.Error);
        }
    }

    void Update()
    {
        var k = Keyboard.current;
        if (k == null || simulador == null || !GuardiaEntrada.AtajosPermitidos) return;
        if (k.gKey.wasPressedThisFrame) GuardarPng();
        if (k.tKey.wasPressedThisFrame) CambiarGranularidad();
    }

    // Vista de prueba para ver las 4 gráficas en la ventana Game
    void OnGUI()
    {
        if (!mostrarEnPantalla || texturas[0] == null) return;
        if (GuardiaEntrada.SesionActiva != null && !GuardiaEntrada.SesionActiva()) return;   // sin sesión no se ve nada

        float cw = Mathf.Min(Screen.width / 2f - 15f, (Screen.height / 2f - 30f) * ancho / alto);
        float ch = cw * alto / ancho;

        for (int i = 0; i < Todas.Length; i++)
        {
            float x = 10f + (i % 2) * (cw + 10f);
            float y = 6f + (i / 2) * (ch + 24f);
            var m = Todas[i];
            var (rMin, rMax) = Agregador.Rango(m);
            var (zMin, zMax) = Agregador.ZonaIdeal(m);
            string u = Agregador.Unidad(m);

            GUI.Label(new Rect(x, y, cw, 20f),
                $"{m} ({granularidad})  eje {rMin}-{rMax}{u}  |  ideal {zMin}-{zMax}{u}");
            GUI.DrawTexture(new Rect(x, y + 18f, cw, ch), texturas[i]);
        }
    }
}