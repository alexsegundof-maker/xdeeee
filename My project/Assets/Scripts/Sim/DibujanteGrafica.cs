using UnityEngine;
using Composta.Core;

public static class DibujanteGrafica
{
    // reutilizar: textura previa del mismo tamaño para evitar crear/destruir una por refresco
    public static Texture2D Dibujar(Serie s, int w, int h, (float min, float max)? zona = null, Texture2D reutilizar = null)
    {
        w = Mathf.Max(w, 32); h = Mathf.Max(h, 32);       // con tamaños menores los márgenes quedan invertidos
        var buf = new Color32[w * h];
        Rellenar(buf, w, h, 0, 0, w - 1, h - 1, new Color32(24, 24, 28, 255));

        const int m = 12;
        int x0 = m, x1 = w - m - 1, y0 = m, y1 = h - m - 1;
        var (rMin, rMax) = Agregador.Rango(s.Magnitud);
        var z = zona ?? Agregador.ZonaIdeal(s.Magnitud);
        float zMin = z.Item1, zMax = z.Item2;

        float xMin = 0f, xMax = 1f;
        if (s.Puntos.Count > 0)
        {
            xMin = s.Puntos[0].X;
            xMax = s.Puntos[s.Puntos.Count - 1].X;
            if (xMax - xMin < 0.01f) xMax = xMin + 1f;
        }

        int Y(float v) => y0 + Mathf.RoundToInt((Mathf.Clamp(v, rMin, rMax) - rMin) / (rMax - rMin) * (y1 - y0));
        int X(float x) => x0 + Mathf.RoundToInt((x - xMin) / (xMax - xMin) * (x1 - x0));

        // Zona ideal
        Rellenar(buf, w, h, x0, Y(zMin), x1, Y(zMax), new Color32(60, 180, 75, 60));

        // Cuadrícula (4 divisiones) y ejes
        var rejilla = new Color32(50, 50, 58, 255);
        for (int i = 1; i < 4; i++)
        {
            int y = y0 + (y1 - y0) * i / 4;
            Rellenar(buf, w, h, x0, y, x1, y, rejilla);
        }
        var eje = new Color32(110, 110, 125, 255);
        Rellenar(buf, w, h, x0, y0, x1, y0, eje);
        Rellenar(buf, w, h, x0, y0, x0, y1, eje);

        Color32 c = ColorDe(s.Magnitud);
        Color32 tenue = new Color32((byte)(c.r * 0.5f), (byte)(c.g * 0.5f), (byte)(c.b * 0.5f), 255);
        var pts = s.Puntos;

        // Barras min-max de cada bloque
        for (int i = 0; i < pts.Count; i++)
        {
            int cx = X(pts[i].X);
            Linea(buf, w, h, cx, Y(pts[i].Min), cx, Y(pts[i].Max), tenue, 1);
        }

        // Línea del promedio
        for (int i = 0; i < pts.Count; i++)
        {
            int cx = X(pts[i].X), cy = Y(pts[i].Prom);
            if (i > 0) Linea(buf, w, h, X(pts[i - 1].X), Y(pts[i - 1].Prom), cx, cy, c, 2);
            else Rellenar(buf, w, h, cx - 2, cy - 2, cx + 2, cy + 2, c);
        }

        var tex = reutilizar;
        if (tex == null || tex.width != w || tex.height != h)
        {
            if (tex != null) Object.Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
        }
        tex.SetPixels32(buf);
        tex.Apply();
        return tex;
    }

    static Color32 ColorDe(Magnitud m)
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