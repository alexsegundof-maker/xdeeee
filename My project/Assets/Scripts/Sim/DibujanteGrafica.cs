using UnityEngine;
using Composta.Core;

public static class DibujanteGrafica
{
    // Dibuja una serie agregada (promedio + barras mínimo–máximo) en una textura.
    // reutilizar: textura previa del mismo tamaño para no crear/destruir una por refresco.
    // marcas: puntos medidos en campo (x en días), se dibujan como círculos blancos.
    public static Texture2D Dibujar(Serie s, int w, int h, (float min, float max)? zona = null, Texture2D reutilizar = null,
                                    System.Collections.Generic.IList<(float x, float v)> marcas = null)
    {
        w = Mathf.Max(w, 32); h = Mathf.Max(h, 32);       // con tamaños menores los márgenes quedan invertidos
        var buf = new Color32[w * h];
        Rellenar(buf, w, h, 0, 0, w - 1, h - 1, ColFondo);

        var magnitud = s != null ? s.Magnitud : Magnitud.Temperatura;
        const int m = 12;
        int x0 = m, x1 = w - m - 1, y0 = m, y1 = h - m - 1;
        var (rMin, rMax) = Agregador.Rango(magnitud);
        if (rMax - rMin < 1e-3f) rMax = rMin + 1f;
        var z = zona ?? Agregador.ZonaIdeal(magnitud);
        float zMin = z.Item1, zMax = z.Item2;

        // Solo puntos válidos: un NaN/infinito daba coordenadas enormes y la línea de Bresenham no terminaba
        var pts = new System.Collections.Generic.List<Punto>();
        if (s != null && s.Puntos != null)
            foreach (var p in s.Puntos)
                if (p != null && Finito(p.X) && Finito(p.Prom) && Finito(p.Min) && Finito(p.Max)) pts.Add(p);
        pts.Sort((a, b) => a.X.CompareTo(b.X));

        float xMin = 0f, xMax = 1f;
        if (pts.Count > 0)
        {
            xMin = pts[0].X;
            xMax = pts[pts.Count - 1].X;
            if (xMax - xMin < 0.01f) xMax = xMin + 1f;
        }

        int Y(float v) => y0 + Mathf.RoundToInt((Mathf.Clamp(v, rMin, rMax) - rMin) / (rMax - rMin) * (y1 - y0));
        int X(float x) => x0 + Mathf.RoundToInt((Mathf.Clamp(x, xMin, xMax) - xMin) / (xMax - xMin) * (x1 - x0));

        // Zona ideal
        if (Finito(zMin) && Finito(zMax)) Rellenar(buf, w, h, x0, Y(zMin), x1, Y(zMax), ColZona);

        // Cuadrícula (4 divisiones) y ejes
        for (int i = 1; i < 4; i++)
        {
            int y = y0 + (y1 - y0) * i / 4;
            Rellenar(buf, w, h, x0, y, x1, y, ColRejilla);
        }
        Rellenar(buf, w, h, x0, y0, x1, y0, ColEje);
        Rellenar(buf, w, h, x0, y0, x0, y1, ColEje);

        Color32 c = ColorDe(magnitud);
        Color32 tenue = new Color32((byte)(c.r * 0.5f), (byte)(c.g * 0.5f), (byte)(c.b * 0.5f), 255);

        // Barras mínimo–máximo de cada bloque (2 px: con 1 px desaparecían al reducir la imagen en pantalla)
        for (int i = 0; i < pts.Count; i++)
        {
            int cx = X(pts[i].X);
            Linea(buf, w, h, cx, Y(pts[i].Min), cx, Y(pts[i].Max), tenue, 2);
        }

        // Línea del promedio
        for (int i = 0; i < pts.Count; i++)
        {
            int cx = X(pts[i].X), cy = Y(pts[i].Prom);
            if (i > 0) Linea(buf, w, h, X(pts[i - 1].X), Y(pts[i - 1].Prom), cx, cy, c, 2);
            else Rellenar(buf, w, h, cx - 2, cy - 2, cx + 2, cy + 2, c);
        }

        if (marcas != null)
            foreach (var (mx, mv) in marcas)
            {
                if (!Finito(mx) || !Finito(mv)) continue;
                // Con agrupación diaria/semanal el último punto es el inicio del bloque: se ajusta al borde
                int cx = X(mx), cy = Y(mv);
                Circulo(buf, w, h, cx, cy, 5, new Color32(20, 20, 24, 255));
                Circulo(buf, w, h, cx, cy, 3, new Color32(255, 255, 255, 255));
            }

        return VolcarEnTextura(buf, w, h, reutilizar);
    }

    static bool Finito(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    static readonly Color32 ColVolteo = new Color32(232, 177, 58, 255);
    static readonly Color32 ColSemana = new Color32(31, 36, 45, 255);

    // Curva de un pronóstico, hora a hora, ocupando TODA la textura (sin márgenes): así los números de los ejes
    // y la línea guía que pone la UI alrededor coinciden con lo dibujado. x = días desde ahora (0..diasTotales).
    //  · franja de la zona ideal · rejilla en 0/25/50/75/100 % del eje Y · una línea tenue por semana
    //  · volteos programados como líneas verticales punteadas (ámbar) en la misma posición en las 3 gráficas
    public static Texture2D DibujarPronostico(System.Collections.Generic.IList<Lectura> lecturas, Magnitud magnitud, float yMin, float yMax,
                                              (float min, float max)? zona, System.Collections.Generic.IList<float> diasVolteo, float diasTotales,
                                              int w, int h, Texture2D reutilizar = null)
    {
        w = Mathf.Max(w, 32); h = Mathf.Max(h, 32);
        if (!(yMax - yMin > 1e-3f)) yMax = yMin + 1f;
        if (!(diasTotales > 0.01f)) diasTotales = 1f;
        var buf = new Color32[w * h];
        Rellenar(buf, w, h, 0, 0, w - 1, h - 1, ColFondo);
        int Y(float v) => Mathf.RoundToInt((Mathf.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin) * (h - 3)) + 1;
        int X(float d) => Mathf.RoundToInt(Mathf.Clamp(d, 0f, diasTotales) / diasTotales * (w - 3)) + 1;

        if (zona.HasValue && Finito(zona.Value.min) && Finito(zona.Value.max)) Rellenar(buf, w, h, 0, Y(zona.Value.min), w - 1, Y(zona.Value.max), ColZona);
        for (float d = 7f; d < diasTotales; d += 7f) { int x = X(d); Rellenar(buf, w, h, x, 0, x, h - 1, ColSemana); }
        for (int i = 1; i < 4; i++) { int y = (h - 1) * i / 4; Rellenar(buf, w, h, 0, y, w - 1, y, ColRejilla); }
        Rellenar(buf, w, h, 0, 0, w - 1, 1, ColEje);
        Rellenar(buf, w, h, 0, 0, 1, h - 1, ColEje);

        if (diasVolteo != null)
            foreach (var d in diasVolteo)
                if (Finito(d)) { int x = X(d); LineaPunteada(buf, w, h, x, 0, x, h - 1, ColVolteo); }

        Color32 c = ColorDe(magnitud);
        int px = -1, py = -1;
        if (lecturas != null)
            foreach (var l in lecturas)
            {
                if (l == null) continue;
                float v = Agregador.Valor(l, magnitud);
                if (!Finito(v) || !Finito(l.Dias)) continue;
                int x = X(l.Dias), y = Y(v);
                if (px >= 0) Linea(buf, w, h, px, py, x, y, c, 2);
                px = x; py = y;
            }
        return VolcarEnTextura(buf, w, h, reutilizar);
    }

    // Copia el búfer a la textura (reutilizada si es del mismo tamaño). Filtro bilineal con mipmaps:
    // con FilterMode.Point, al mostrarla más chica que 640×320 (móvil, tarjetas angostas) se perdían
    // líneas enteras de 1–2 px y la gráfica se veía cortada o vacía.
    static Texture2D VolcarEnTextura(Color32[] buf, int w, int h, Texture2D reutilizar)
    {
        var tex = reutilizar;
        if (tex == null || tex.width != w || tex.height != h || tex.mipmapCount <= 1)
        {
            if (tex != null) Object.Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,       // sin "sangrado" del borde opuesto al filtrar
                name = "Grafica"
            };
        }
        tex.SetPixels32(buf);
        tex.Apply(true);                             // regenera los mipmaps con el contenido nuevo
        return tex;
    }

    // Colores del tema (misma superficie que las tarjetas, rejilla discreta, zona ideal verde translúcida)
    static readonly Color32 ColFondo = new Color32(22, 26, 33, 255);
    static readonly Color32 ColRejilla = new Color32(38, 44, 54, 255);
    static readonly Color32 ColEje = new Color32(70, 80, 94, 255);
    static readonly Color32 ColZona = new Color32(63, 179, 127, 46);

    // Gráfica SOLO con puntos medidos (Control Manual): sin curvas inventadas entre mediciones.
    // Los puntos se unen con una línea fina punteada para leer la tendencia; cada medición es un círculo.
    public static Texture2D DibujarPuntos(System.Collections.Generic.IList<(float x, float v)> puntos, float yMin, float yMax,
                                          (float min, float max)? zona, Color32 color, int w, int h, Texture2D reutilizar = null)
    {
        w = Mathf.Max(w, 32); h = Mathf.Max(h, 32);
        var buf = new Color32[w * h];
        Rellenar(buf, w, h, 0, 0, w - 1, h - 1, ColFondo);
        const int m = 14;
        int x0 = m, x1 = w - m - 1, y0 = m, y1 = h - m - 1;

        float xMin = 0f, xMax = 7f;
        if (puntos != null && puntos.Count > 0)
        {
            xMin = puntos[0].x; xMax = puntos[puntos.Count - 1].x;
            if (xMax - xMin < 7f) xMax = xMin + 7f;            // al menos una semana de ancho
        }
        int Y(float v) => y0 + Mathf.RoundToInt((Mathf.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin) * (y1 - y0));
        int X(float x) => x0 + Mathf.RoundToInt((x - xMin) / (xMax - xMin) * (x1 - x0));

        if (zona.HasValue) Rellenar(buf, w, h, x0, Y(zona.Value.min), x1, Y(zona.Value.max), ColZona);
        for (int i = 1; i < 4; i++) { int y = y0 + (y1 - y0) * i / 4; Rellenar(buf, w, h, x0, y, x1, y, ColRejilla); }
        Rellenar(buf, w, h, x0, y0, x1, y0, ColEje);

        if (puntos != null)
        {
            var tenue = new Color32((byte)(color.r * 0.6f), (byte)(color.g * 0.6f), (byte)(color.b * 0.6f), 255);
            for (int i = 1; i < puntos.Count; i++)
                LineaPunteada(buf, w, h, X(puntos[i - 1].x), Y(puntos[i - 1].v), X(puntos[i].x), Y(puntos[i].v), tenue);
            for (int i = 0; i < puntos.Count; i++)
            {
                int cx = X(puntos[i].x), cy = Y(puntos[i].v);
                Circulo(buf, w, h, cx, cy, 7, ColFondo);
                Circulo(buf, w, h, cx, cy, 5, color);
            }
        }

        var tex = reutilizar;
        if (tex == null || tex.width != w || tex.height != h)
        {
            if (tex != null) Object.Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        }
        tex.SetPixels32(buf);
        tex.Apply();
        return tex;
    }

    static void LineaPunteada(Color32[] buf, int w, int h, int xa, int ya, int xb, int yb, Color32 c)
    {
        int pasos = Mathf.Max(Mathf.Abs(xb - xa), Mathf.Abs(yb - ya));
        for (int i = 0; i <= pasos; i++)
        {
            if ((i / 6) % 2 == 1) continue;                     // 6 px trazo, 6 px hueco
            float t = pasos == 0 ? 0f : (float)i / pasos;
            Pixel(buf, w, h, Mathf.RoundToInt(Mathf.Lerp(xa, xb, t)), Mathf.RoundToInt(Mathf.Lerp(ya, yb, t)), c, 2);
        }
    }

    // Varias series en los mismos ejes (comparar lotes, o temperatura y humedad de un lote). x en días desde el inicio.
    public static Texture2D DibujarSeries(System.Collections.Generic.IList<(System.Collections.Generic.IList<(float x, float v)> puntos, Color32 color)> series,
                                          float yMin, float yMax, (float min, float max)? zona, int w, int h, Texture2D reutilizar = null, bool puntosGrandes = true)
    {
        w = Mathf.Max(w, 32); h = Mathf.Max(h, 32);
        var buf = new Color32[w * h];
        Rellenar(buf, w, h, 0, 0, w - 1, h - 1, ColFondo);
        int m = Mathf.Clamp(h / 12, 4, 14);
        int x0 = m, x1 = w - m - 1, y0 = m, y1 = h - m - 1;

        float xMin = float.MaxValue, xMax = float.MinValue;
        if (series != null)
            foreach (var s in series)
                if (s.puntos != null) foreach (var p in s.puntos) { xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x); }
        if (xMin > xMax) { xMin = 0f; xMax = 7f; }
        if (xMax - xMin < 7f) xMax = xMin + 7f;
        if (yMax - yMin < 1e-3f) yMax = yMin + 1f;
        int Y(float v) => y0 + Mathf.RoundToInt((Mathf.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin) * (y1 - y0));
        int X(float x) => x0 + Mathf.RoundToInt((x - xMin) / (xMax - xMin) * (x1 - x0));

        if (zona.HasValue) Rellenar(buf, w, h, x0, Y(zona.Value.min), x1, Y(zona.Value.max), ColZona);
        for (int i = 1; i < 4; i++) { int y = y0 + (y1 - y0) * i / 4; Rellenar(buf, w, h, x0, y, x1, y, ColRejilla); }
        Rellenar(buf, w, h, x0, y0, x1, y0, ColEje);

        if (series != null)
            foreach (var s in series)
            {
                if (s.puntos == null) continue;
                for (int i = 1; i < s.puntos.Count; i++)
                    Linea(buf, w, h, X(s.puntos[i - 1].x), Y(s.puntos[i - 1].v), X(s.puntos[i].x), Y(s.puntos[i].v), s.color, puntosGrandes ? 2 : 1);
                if (!puntosGrandes) continue;
                foreach (var p in s.puntos)
                {
                    Circulo(buf, w, h, X(p.x), Y(p.v), 5, ColFondo);
                    Circulo(buf, w, h, X(p.x), Y(p.v), 3, s.color);
                }
            }

        var tex = reutilizar;
        if (tex == null || tex.width != w || tex.height != h)
        {
            if (tex != null) Object.Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        }
        tex.SetPixels32(buf);
        tex.Apply();
        return tex;
    }

    public static Color32 ColorDe(Magnitud m)
    {
        switch (m)
        {
            case Magnitud.Temperatura: return new Color32(255, 110, 70, 255);
            case Magnitud.PH: return new Color32(190, 120, 255, 255);
            case Magnitud.Humedad: return new Color32(80, 160, 255, 255);
            default: return new Color32(90, 230, 230, 255);
        }
    }

    static void Rellenar(Color32[] buf, int w, int h, int xa, int ya, int xb, int yb, Color32 c)
    {
        if (ya > yb) { int t = ya; ya = yb; yb = t; }
        xa = Mathf.Max(xa, 0); xb = Mathf.Min(xb, w - 1);
        ya = Mathf.Max(ya, 0); yb = Mathf.Min(yb, h - 1);
        float a = c.a / 255f;
        for (int y = ya; y <= yb; y++)
            for (int x = xa; x <= xb; x++)
            {
                var o = buf[y * w + x];
                buf[y * w + x] = new Color32(
                    (byte)(o.r + (c.r - o.r) * a),
                    (byte)(o.g + (c.g - o.g) * a),
                    (byte)(o.b + (c.b - o.b) * a), 255);
            }
    }

    static void Circulo(Color32[] buf, int w, int h, int cx, int cy, int r, Color32 c)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r)
                {
                    int px = cx + x, py = cy + y;
                    if (px >= 0 && px < w && py >= 0 && py < h) buf[py * w + px] = c;
                }
    }

    static void Pixel(Color32[] buf, int w, int h, int x, int y, Color32 c, int grosor)
    {
        for (int i = 0; i < grosor; i++)
            for (int j = 0; j < grosor; j++)
            {
                int px = x + i, py = y + j;
                if (px >= 0 && px < w && py >= 0 && py < h) buf[py * w + px] = c;
            }
    }

    // Línea de Bresenham
    static void Linea(Color32[] buf, int w, int h, int x0, int y0, int x1, int y1, Color32 c, int grosor)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Pixel(buf, w, h, x0, y0, c, grosor);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}