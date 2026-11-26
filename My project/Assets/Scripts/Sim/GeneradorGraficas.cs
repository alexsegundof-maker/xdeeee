using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Composta.Core;

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

    void Start()
    {
        if (simulador == null) simulador = FindFirstObjectByType<SimuladorComposta>();
        simulador.AlTick += AlTick;
    }

    void OnDestroy()
    {
        if (simulador != null) simulador.AlTick -= AlTick;
    }

    void AlTick(Lectura l)
    {
        if (l.Tick % refrescarCadaTicks == 0) Regenerar();
    }

    public void Regenerar()
    {
        for (int i = 0; i < Todas.Length; i++)
        {
            if (texturas[i] != null) Destroy(texturas[i]);
            var serie = Agregador.Construir(simulador.Historial.Lecturas, Todas[i], granularidad);
            texturas[i] = DibujanteGrafica.Dibujar(serie, ancho, alto, simulador.Sistema.Perfil.ZonaIdeal(Todas[i]));
        }
    }

    // La UI usará esto para pintar cada gráfica en un RawImage
    public Texture2D ObtenerTextura(Magnitud m) => texturas[(int)m];

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
        Regenerar();
        for (int i = 0; i < Todas.Length; i++)
        {
            string ruta = Path.Combine(Application.persistentDataPath,
                $"grafica_{Todas[i]}_{granularidad}.png");
            File.WriteAllBytes(ruta, texturas[i].EncodeToPNG());
        }
        Debug.Log($"Gráficas guardadas en: {Application.persistentDataPath}");
    }

    void Update()
    {
        var k = Keyboard.current;
        if (k == null || simulador == null) return;
        if (k.gKey.wasPressedThisFrame) GuardarPng();
        if (k.tKey.wasPressedThisFrame) CambiarGranularidad();
    }

    // Vista de prueba para ver las 4 gráficas en la ventana Game
    void OnGUI()
    {
        if (!mostrarEnPantalla || texturas[0] == null) return;

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