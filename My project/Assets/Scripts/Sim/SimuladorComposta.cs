using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Composta.Core;

public class SimuladorComposta : MonoBehaviour
{
    [Header("Tiempo")]
    public float horasPorSegundo = 6f;
    public int[] velocidades = { 1, 2, 5 };
    public bool pausado;
    public int semilla = 1234;
    public bool logPorDia = true;

    [Header("Perfil de composta (build)")]
    public TipoComposta tipo = TipoComposta.PilaAbierta;
    public float pesoKg = 500f;
    public float relacionCN = 28f;              // ideal ~25-30
    public bool permitirPesoExagerado = true;     // true = peso libre; fuera del rango marca PESO EXAGERADO
    public float tempAmbiente = 22f;
    [Tooltip("-1 = usar el valor del perfil")] public float humedadInicial = -1f;
    [Tooltip("-1 = usar el valor del perfil")] public float aislamiento = -1f;


    [Header("Control y sensores")]
    public bool modoAutomatico = false;
    [Range(0f, 0.02f)] public float probFallaPorHora = 0f;

    public SistemaComposta Sistema { get; private set; }
    public ResultadoPaso Ultimo { get; private set; }
    public ModeloComposta Modelo => Sistema.Modelo;
    public Historial Historial => Sistema.Historial;
    public MonitorAlertas Monitor => Sistema.Monitor;
    public int VelocidadActual => velocidades[idxVel];

    public event Action<Lectura> AlTick;
    public event Action<EventoAlerta> AlAlerta;

    int idxVel;
    float acumulado;
    const int MaxTicksPorFrame = 50;

    PerfilComposta ConstruirPerfil()
    {
        var p = PerfilComposta.Crear(tipo, pesoKg, permitirPesoExagerado);
        p.RelacionCN = relacionCN;
        p.TempAmbiente = tempAmbiente;
        if (humedadInicial >= 0f) p.HumedadInicial = humedadInicial;
        if (aislamiento >= 0f) p.Aislamiento = aislamiento;
        return p;
    }

    // En el Inspector el peso se limita solo al rango del perfil
    void OnValidate()
    {
        pesoKg = PerfilComposta.Crear(tipo, pesoKg, permitirPesoExagerado).PesoKg;
    }

    void Awake()
    {
        Sistema = new SistemaComposta(ConstruirPerfil(), semilla);
    }

    // Empieza una composta nueva con los valores actuales del Inspector
    [ContextMenu("Reiniciar simulación")]
    public void Reiniciar()
    {
        Sistema = new SistemaComposta(ConstruirPerfil(), semilla);
        Ultimo = null;
        acumulado = 0f;
        var p = Sistema.Perfil;
        Debug.Log($"Nueva composta: {p.Nombre}, {p.PesoKg:F0} kg (rango {p.PesoMin:F0}-{p.PesoMax:F0}), " +
                  $"fase caliente ~{Modelo.TempPlateau:F0}°C");
    }

    public void SiguientePerfil()
    {
        int n = Enum.GetValues(typeof(TipoComposta)).Length;
        tipo = (TipoComposta)(((int)tipo + 1) % n);
        pesoKg = PerfilComposta.Crear(tipo).PesoDefecto;
        humedadInicial = -1f;
        aislamiento = -1f;
        Reiniciar();
    }

    void Update()
    {
        LeerTeclas();
        Sistema.Controlador.Activo = modoAutomatico;
        Sistema.Sensores.ProbFalla = probFallaPorHora;
        if (pausado) return;

        float segPorTick = 1f / (horasPorSegundo * VelocidadActual);
        acumulado += Time.deltaTime;

        int n = 0;
        while (acumulado >= segPorTick && n < MaxTicksPorFrame)
        {
            acumulado -= segPorTick;
            n++;
            Paso();
        }
        if (n == MaxTicksPorFrame) acumulado = 0f;
    }

    void Paso()
    {
        var r = Sistema.Paso();
        Ultimo = r;

        foreach (var o in r.Ordenes)
            Debug.Log($"[AUTO] {o.Accion} ({o.Motivo})");

        foreach (var e in r.Eventos)
        {
            LogEvento(e);
            AlAlerta?.Invoke(e);
        }
        AlTick?.Invoke(r.Medida);

        if (logPorDia && Modelo.Ticks % 24 == 0)
        {
            var m = r.Medida; var re = r.Real; var p = Sistema.Perfil;
            Debug.Log($"[{p.Nombre} {p.PesoKg:F0} kg] Día {m.Dias:F0} [{m.Etapa}] medido T:{m.Temperatura:F1}°C pH:{m.PH:F2} H:{m.Humedad:F0}% O2:{m.Oxigeno:F0}%" +
                      $"  |  real T:{re.Temperatura:F1}°C pH:{re.PH:F2} H:{re.Humedad:F0}% O2:{re.Oxigeno:F0}%");
        }
    }

    void LogEvento(EventoAlerta e)
    {
        var a = e.Alerta;
        string tipoAlerta = a.Variable.StartsWith("Sensor") ? "SENSOR"
                          : a.Variable.StartsWith("Predicción") ? "PREDICCIÓN"
                          : a.Variable == "Perfil" ? "PERFIL" : "ALERTA";
        switch (e.Tipo)
        {
            case TipoEvento.Nueva:
                Debug.LogWarning($"[{tipoAlerta}/{a.Nivel}] {a.Mensaje} → {a.Accion}"); break;
            case TipoEvento.CambioNivel:
                Debug.LogWarning($"[{tipoAlerta}/{e.Anterior}→{a.Nivel}] {a.Mensaje} → {a.Accion}"); break;
            case TipoEvento.Resuelta:
                Debug.Log($"✔ {a.Variable} volvió a la normalidad"); break;
        }
    }

    // ---- Métodos públicos: los botones de la UI los llaman ----
    public void Voltear() => Sistema.Voltear();
    public void Regar() => Sistema.Regar();
    public void AlternarPausa() => pausado = !pausado;
    public void CambiarVelocidad() => idxVel = (idxVel + 1) % velocidades.Length;
    public void AlternarAutomatico() => modoAutomatico = !modoAutomatico;
    public void InyectarFalla() => Debug.LogWarning(Sistema.Sensores.InyectarAleatoria());
    public void RepararSensores() { Sistema.Sensores.RepararTodo(); Debug.Log("Sensores recalibrados"); }

    public void GuardarCsv()
    {
        string ruta = System.IO.Path.Combine(Application.persistentDataPath, "historial_composta.csv");
        System.IO.File.WriteAllText(ruta, Historial.ToCsv());
        Debug.Log($"CSV guardado en: {ruta}");
    }

    // 3 escenarios con el perfil actual
    public void CorrerComparativa(int dias = 45)
    {
        var p = Sistema.Perfil;
        var nombres = new[] { "Sin intervención", "Automático", "Automático + fallas (2%/h)" };
        var datos = new[]
        {
            SistemaComposta.Experimento(p, dias, false, semilla, 0f),
            SistemaComposta.Experimento(p, dias, true,  semilla, 0f),
            SistemaComposta.Experimento(p, dias, true,  semilla, 0.02f)
        };
        Debug.Log($"=== {p.Nombre} ({p.PesoKg:F0} kg), {dias} días ===\n" + Metricas.Tabla(nombres, datos));
    }

    // Los 5 perfiles (peso por defecto) en modo automático
    public void CompararPerfiles(int dias = 45)
    {
        var tipos = (TipoComposta[])Enum.GetValues(typeof(TipoComposta));
        var nombres = new string[tipos.Length];
        var pesos = new float[tipos.Length];
        var datos = new Metricas[tipos.Length];
        for (int i = 0; i < tipos.Length; i++)
        {
            var p = PerfilComposta.Crear(tipos[i]);
            nombres[i] = p.Nombre; pesos[i] = p.PesoKg;
            datos[i] = SistemaComposta.Experimento(p, dias, true, semilla, 0f);
        }
        Debug.Log($"=== Comparativa de perfiles a {dias} días (automático) ===\n" + Metricas.TablaPerfiles(nombres, pesos, datos));
    }

    void LeerTeclas()
    {
        var k = Keyboard.current;
        if (k == null) return;
        if (k.vKey.wasPressedThisFrame) Voltear();
        if (k.rKey.wasPressedThisFrame) Regar();
        if (k.spaceKey.wasPressedThisFrame) AlternarPausa();
        if (k.fKey.wasPressedThisFrame) { CambiarVelocidad(); Debug.Log($"Velocidad x{VelocidadActual}"); }
        if (k.cKey.wasPressedThisFrame) GuardarCsv();
        if (k.mKey.wasPressedThisFrame) { AlternarAutomatico(); Debug.Log(modoAutomatico ? "Modo AUTOMÁTICO" : "Modo MANUAL"); }
        if (k.kKey.wasPressedThisFrame) InyectarFalla();
        if (k.lKey.wasPressedThisFrame) RepararSensores();
        if (k.bKey.wasPressedThisFrame) CorrerComparativa();
        if (k.nKey.wasPressedThisFrame) SiguientePerfil();
        if (k.pKey.wasPressedThisFrame) CompararPerfiles();
    }
}