using Composta.Core;

// Textos de ayuda de la ventana "Nueva composta". Centralizados para editarlos sin tocar la UI.
// Los rangos de peso salen del perfil real: si cambias PerfilComposta, la ayuda se actualiza sola.
public static class AyudaBuild
{
    public static string Tipo(PerfilComposta perfil)
        => $"{Descripcion(perfil.Tipo)} Rango recomendado: {perfil.PesoMin:F0}-{perfil.PesoMax:F0} kg.";

    static string Descripcion(TipoComposta t)
    {
        switch (t)
        {
            case TipoComposta.Domestica: return "Un bote en casa.";
            case TipoComposta.Cerrada: return "Recipiente con tapa, retiene más calor.";
            case TipoComposta.PilaAbierta: return "Montón al aire libre.";
            case TipoComposta.Industrial: return "Planta grande.";
            default: return "Con lombrices. Se mantiene fría (máx. ~28 °C) y más húmeda.";
        }
    }

    public const string Modo =
        "Simular: modo guiado. Rangos recomendados, el aviso PESO EXAGERADO bloquea el inicio y el control automático " +
        "voltea y riega por ti.\n\nManual: variables libres para experimentar (valores fuera de rango, escritos a mano). " +
        "Los avisos solo informan, el control automático empieza apagado y no hay sensores físicos: cada semana registras " +
        "temperatura, humedad y peso medidos a mano, y el simulador se recalibra con esos datos. " +
        "En ambos modos el simulador calcula temperatura, humedad y fases en tiempo real.";

    public const string Resumen =
        "Indica qué pasará con la configuración actual. Si dice 'No alcanza fase caliente (máx. ~28 °C)', " +
        "significa que con ese peso no se calentará lo suficiente (necesita llegar a ~45 °C para matar patógenos y semillas).";

    public const string PesoExagerado =
        "Aparece cuando el peso está fuera del rango recomendado del tipo seleccionado (por arriba o por abajo). " +
        "Muévelo dentro de la barra verde del control de Peso para que desaparezca y alcance la fase caliente.";

    // Mismo orden que PanelBuild.Etiquetas: Peso, Relación C/N, Humedad inicial, Aislamiento, Temp. ambiente
    public static readonly string[] Parametros =
    {
        "Kilos de residuos en el lote. La barra verde marca el rango recomendado. " +
        "Efecto: Más masa = se calienta más y se seca menos; una pila chica pierde calor.",

        "Carbono (hojas secas, cartón) vs. nitrógeno (restos de comida, pasto). Ideal: 25-30:1 (barra verde). " +
        "Con poco carbono es más activa y huele a amoníaco; con mucho carbono es lenta.",

        "Qué tan mojado empieza el lote (barra verde = rango óptimo). " +
        "Muy seca: los microbios no trabajan; muy mojada: falta oxígeno y huele mal.",

        "Qué tanto retiene el calor el recipiente. x1.00 = normal. Más de 1 = guarda más calor y evapora menos.",

        "Temperatura del lugar. La composta parte de esta temperatura y nunca baja de ella."
    };

    // ---- Modo Manual: preguntas fáciles en lugar de parámetros difíciles de medir ----
    public static readonly string[] EtiquetasSimples = { "Peso", "Material predominante", "Humedad inicial", "¿Tapada o cubierta?", "Temp. ambiente" };

    public static (string texto, float valor)[] OpcionesSimples(int parametro)
    {
        if (parametro == 1)   // relación C/N equivalente
            return new[] { ("Restos de cocina", 18f), ("Mezcla", 28f), ("Hojas secas / cartón", 40f) };
        return new[] { ("Sí, tapada o cubierta", 1.3f), ("No, al aire libre", 1f) };   // aislamiento equivalente
    }

    public static readonly string[] AyudaSimple =
    {
        "", 
        "Elige lo que más abunda. Restos de cocina = mucho nitrógeno (C/N ~18, más activa); hojas secas o cartón = mucho carbono " +
        "(C/N ~40, más lenta); mezcla = equilibrio (C/N ~28). Es solo un punto de partida: los informes semanales lo corrigen.",
        "",
        "Si la pila está en un contenedor con tapa o cubierta con lona, retiene más calor y se seca menos. " +
        "Es solo un punto de partida: los informes semanales lo corrigen.",
        ""
    };

    public const string Ritmo =
        "Calendario real: la simulación avanza al ritmo del reloj (1 hora simulada = 1 hora real) y el informe semanal " +
        "se pide cada 7 días de calendario. Úsalo con una pila real.\n\nDemostración: el tiempo va acelerado y cada semana " +
        "simulada la simulación se pausa para pedir el informe. Útil para probar o enseñar.";

    public const string Cancelar =
        "Cierra esta ventana sin cambiar nada. Solo aparece si ya tienes una composta en curso.";

    public const string Iniciar =
        "Crea un lote nuevo que queda guardado y se reanuda al volver a entrar. Si había otro lote activo, se archiva automáticamente.";
}
