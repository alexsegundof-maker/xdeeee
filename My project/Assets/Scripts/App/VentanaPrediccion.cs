using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Composta.App.Google;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Pronostico;
using Composta.Core.Textos;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Ventana dedicada de PREDICCIÓN (acento violeta). Arriba, el diagnóstico con los datos reales del lote
    // (estado vs. ideal, 3 acciones prioritarias y alertas); debajo, las 3 curvas proyectadas con ejes, fechas,
    // leyenda y ficha al pasar el cursor; a la derecha, los hitos (clave o todos) y la exportación a Google Calendar.
    // Nunca toca el simulador ni los lotes: recibe un ResultadoPronostico ya calculado en una instancia aislada.
    public sealed class VentanaPrediccion : MonoBehaviour
    {
        const int AnchoGraf = 900, AltoGraf = 240;
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        static readonly TipoHito[] HitosClave =
            { TipoHito.InicioFaseCaliente, TipoHito.PicoTemperatura, TipoHito.FinFaseCaliente, TipoHito.InicioMaduracion, TipoHito.Cosecha };

        // Una gráfica con sus ejes, leyenda, línea guía y ficha
        sealed class Grafica
        {
            public Magnitud M;
            public string Unidad;
            public float YMin, YMax;
            public RawImage Img;
            public Texture2D Tex;
            public RectTransform Plot, Guia, Ficha;
            public Text Leyenda, TxtFicha;
            public readonly Text[] EtiquetasY = new Text[5];
            public readonly List<Text> EtiquetasX = new List<Text>();
        }

        ResultadoPronostico pronostico;
        Lote lote;
        string clave, nombreLote;
        InfoTiempo tiempo;
        DateTime ahoraLocal;
        List<EventoCalendario> eventos = new List<EventoCalendario>();
        readonly List<float> diasVolteo = new List<float>();
        bool exportando, soloClave = true;

        RectTransform raiz;
        Text txtTitulo, txtSupuestos, txtTiempo, txtGoogle;
        Text txtOrigen, txtEstado, txtAcciones, txtAlertas;
        Button btnExportar, btnDesconectar;
        Image fondoClave, fondoTodos;
        ListaDesplazable listaHitos;
        readonly Grafica[] graficas = new Grafica[3];

        public bool Abierta => raiz != null && raiz.gameObject.activeSelf;

        // lote: del que salen el diagnóstico y las recomendaciones (null = plan desde «Nueva composta»)
        public void Abrir(ResultadoPronostico p, string claveCalendario, string nombre, Lote loteReal = null)
        {
            pronostico = p; clave = claveCalendario; nombreLote = nombre; lote = loteReal;
            Asegurar();
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);

            ServicioTiempo.Refrescar();                 // por si cambió la zona del equipo
            tiempo = ServicioTiempo.Actual;             // hora y zona del teléfono o PC: sin cuentas ni claves
            ahoraLocal = DateTime.SpecifyKind(tiempo.AhoraUtc + tiempo.Zona.Desplazamiento, DateTimeKind.Unspecified);

            txtTitulo.text = string.Format(C, "<b>Pronóstico</b> · {0} · {1:0.#} kg", p.Perfil.Nombre, p.Perfil.PesoKg);
            txtSupuestos.text = (p.DiaSimuladoInicial > 0.01f ? string.Format(C, "Proyectado desde el día simulado {0:F1}. ", p.DiaSimuladoInicial) : "") +
                                string.Format(C, "Horizonte: {0:F0} días · temperatura máxima prevista {1:F0} °C{2}. ", p.DiasProyectados, p.TempMaxima,
                                              p.AlcanzaFaseCaliente ? "" : " (no alcanza la fase caliente)") + p.Supuestos;

            diasVolteo.Clear();
            foreach (var h in p.Hitos) if (h.Tipo == TipoHito.Volteo) diasVolteo.Add(h.DiasDesdeAhora);

            RefrescarHitos();
            DibujarCurvas();
            RefrescarDiagnostico();
            RefrescarGoogle();
            OcultarGuia();
        }

        public void Cerrar()
        {
            if (raiz == null) return;
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
        }

        void OnDestroy()
        {
            GuardiaEntrada.Liberar(this);
            foreach (var g in graficas) if (g?.Tex != null) Destroy(g.Tex);
        }

        void Update()
        {
            if (!Abierta || Notificador.DialogoAbierto || exportando) return;
            var k = Keyboard.current;                       // en Android, el botón "Atrás" llega como Escape
            if (k != null && k.escapeKey.wasPressedThisFrame) Cerrar();
        }

        // ---------------- Fechas (zona del dispositivo; mismo redondeo que los eventos del calendario) ----------------
        DateTime LocalDe(float dias) => ahoraLocal.AddDays(dias);

        string FechaDia(float dias)
        {
            var f = ahoraLocal.Date.AddDays(Math.Round(dias));
            if (dias > 0f && f.AddHours(9) <= ahoraLocal) f = f.AddDays(1);
            return Nombres.FechaConDia(f);
        }

        // ---------------- Diagnóstico ----------------
        void RefrescarDiagnostico()
        {
            DiagnosticoPronostico d;
            try { d = GeneradorDiagnostico.Generar(pronostico, lote, tiempo.AhoraUtc, FechaDia); }
            catch (Exception e)
            {
                Debug.LogException(e);
                txtOrigen.text = "No se pudo generar el diagnóstico: " + e.Message;
                txtEstado.text = txtAcciones.text = txtAlertas.text = "";
                return;
            }
            txtOrigen.text = d.Origen;
            string tenue = Tema.HexDe(Tema.TextoTenue), sec = Tema.HexDe(Tema.TextoSecundario);

            var sb = new StringBuilder();
            foreach (var l in d.Estado)
                sb.Append($"<color={Tema.HexDe(Tema.Estado(l.Nivel))}>●</color> <b>{l.Nombre}</b>  {l.Valor}  <color={tenue}>(ideal {l.Ideal})</color>\n" +
                          $"<color={sec}>{l.Explicacion}</color>\n");
            txtEstado.text = sb.ToString().TrimEnd();

            sb.Clear();
            for (int i = 0; i < d.Acciones.Count; i++)
                sb.Append($"<color={Tema.HexDe(Tema.Estado(d.Acciones[i].Nivel == NivelEstado.Ok ? NivelEstado.Ok : d.Acciones[i].Nivel))}><b>{i + 1}.</b></color> {d.Acciones[i].Texto}\n");
            txtAcciones.text = d.Acciones.Count == 0 ? "No hay acciones urgentes: sigue el plan de volteos y la revisión semanal." : sb.ToString().TrimEnd();

            sb.Clear();
            foreach (var a in d.Alertas)
                sb.Append($"<color={Tema.HexDe(a.Nivel == NivelEstado.Ok ? Tema.AcentoPrediccion : Tema.Estado(a.Nivel))}>▲</color> {a.Texto}\n");
            txtAlertas.text = d.Alertas.Count == 0 ? $"<color={Tema.HexDe(Tema.Exito)}>●</color> Sin alertas: tus datos no muestran riesgos." : sb.ToString().TrimEnd();
        }

        // ---------------- Gráficas ----------------
        void DibujarCurvas()
        {
            float dias = Math.Max(1f, pronostico.DiasProyectados);
            foreach (var g in graficas)
            {
                var zona = pronostico.Perfil.ZonaIdeal(g.M);
                g.Tex = DibujanteGrafica.DibujarPronostico(pronostico.Lecturas, g.M, g.YMin, g.YMax, zona, diasVolteo, dias, AnchoGraf, AltoGraf, g.Tex);
                g.Img.texture = g.Tex;
                g.Img.color = Color.white;

                string zonaTxt = g.M == Magnitud.Temperatura ? $"Zona óptima: {zona.min:F0}–{zona.max:F0} °C"
                               : g.M == Magnitud.Humedad ? $"Humedad ideal: {zona.min:F0}–{zona.max:F0} %"
                               : $"Oxígeno suficiente: más de {zona.min:F0} %";
                string verde = Tema.HexDe(Tema.Exito), ambar = Tema.HexDe(Tema.Aviso), tenue = Tema.HexDe(Tema.TextoTenue);
                g.Leyenda.text = $"<color={verde}>■</color> {zonaTxt}    <color={ambar}>¦</color> Volteo programado" +
                                 (g.M == Magnitud.Oxigeno ? $"  <color={tenue}>(el oxígeno sube justo después)</color>" : "") +
                                 $"    <color={tenue}>Pasa el cursor o toca para ver valores</color>";

                for (int i = 0; i < g.EtiquetasY.Length; i++)
                    g.EtiquetasY[i].text = (g.YMin + (g.YMax - g.YMin) * i / 4f).ToString("0", C) + (i == 4 ? " " + g.Unidad : "");

                foreach (var t in g.EtiquetasX) UIFabrica.Destruir(t.gameObject);
                g.EtiquetasX.Clear();
                float paso = dias > 70f ? 14f : 7f;
                for (float d = 0f; d <= dias + 0.01f; d += paso) g.EtiquetasX.Add(EtiquetaX(g.Plot, d / dias, d == 0f ? "hoy" : Nombres.FechaCorta(LocalDe(d))));
            }
        }

        static Text EtiquetaX(RectTransform plot, float f, string texto)
        {
            var t = UIFabrica.Texto(plot, "X", new Vector2(f, 0f), new Vector2(f, 0f), 12, f < 0.02f ? TextAnchor.UpperLeft : f > 0.98f ? TextAnchor.UpperRight : TextAnchor.UpperCenter, Tema.TextoTenue);
            var rt = t.rectTransform;
            rt.offsetMin = new Vector2(f < 0.02f ? 0f : f > 0.98f ? -60f : -30f, -24f);
            rt.offsetMax = new Vector2(f < 0.02f ? 60f : f > 0.98f ? 0f : 30f, -4f);
            t.text = texto;
            return t;
        }

        // Línea guía en las 3 gráficas y ficha con fecha, valores, fase y eventos del día
        void Apuntar(Grafica origen, float f)
        {
            var lecturas = pronostico?.Lecturas;
            if (lecturas == null || lecturas.Count == 0) return;
            float dias = Math.Max(1f, pronostico.DiasProyectados);
            float dia = f * dias;
            int i = Mathf.Clamp(Mathf.RoundToInt(dia * 24f) - 1, 0, lecturas.Count - 1);
            var l = lecturas[i];
            float fx = Mathf.Clamp01(l.Dias / dias);
            foreach (var g in graficas)
            {
                g.Guia.gameObject.SetActive(true);
                g.Guia.anchorMin = new Vector2(fx, 0f); g.Guia.anchorMax = new Vector2(fx, 1f);
                g.Ficha.gameObject.SetActive(g == origen);
            }

            var local = LocalDe(l.Dias);
            var sb = new StringBuilder();
            sb.Append($"<b>{Nombres.FechaConDia(local)} · {local:HH}:00</b>  <color={Tema.HexDe(Tema.TextoTenue)}>(día {l.Dias.ToString("F1", C)})</color>\n");
            sb.Append($"Temperatura <b>{l.Temperatura.ToString("F1", C)} °C</b> · Humedad <b>{l.Humedad.ToString("F0", C)} %</b> · Oxígeno <b>{l.Oxigeno.ToString("F1", C)} %</b>\n");
            sb.Append($"Fase: {Nombres.Etapa(l.Etapa)}");
            foreach (var e in eventos)
                if (e.InicioLocal.Date == local.Date)
                    sb.Append($"\n<color={Tema.HexDe(ColorHito(e.Tipo))}>●</color> {e.Titulo.Replace("Composta · ", "")}");
            origen.TxtFicha.text = sb.ToString();

            bool derecha = fx < 0.6f;
            origen.Ficha.anchorMin = origen.Ficha.anchorMax = new Vector2(fx, 1f);
            origen.Ficha.pivot = new Vector2(derecha ? 0f : 1f, 1f);
            origen.Ficha.anchoredPosition = new Vector2(derecha ? 10f : -10f, -4f);
        }

        void OcultarGuia()
        {
            foreach (var g in graficas)
            {
                if (g == null) continue;
                g.Guia.gameObject.SetActive(false);
                g.Ficha.gameObject.SetActive(false);
            }
        }

        // ---------------- Hitos ----------------
        void RefrescarHitos()
        {
            var cfg = ConfiguracionGoogle.Cargar();
            int hora = cfg != null ? cfg.horaRecordatorios : 9;
            eventos = PlanCalendario.Generar(pronostico, tiempo.AhoraUtc, tiempo.Zona, nombreLote, hora);
            txtTiempo.text = $"{tiempo.Detalle} · {tiempo.Zona.Etiqueta()}";
            PintarHitos();
        }

        void PintarHitos()
        {
            fondoClave.color = soloClave ? UIFabrica.ColSel : UIFabrica.ColCard;
            fondoTodos.color = soloClave ? UIFabrica.ColCard : UIFabrica.ColSel;
            listaHitos.Limpiar();
            string tenue = Tema.HexDe(Tema.TextoTenue);
            bool siguienteMarcado = false;
            int n = 0;
            foreach (var e in eventos)
            {
                bool esClave = Array.IndexOf(HitosClave, e.Tipo) >= 0;
                if (soloClave && !esClave) continue;
                bool siguiente = !siguienteMarcado && e.InicioLocal >= ahoraLocal;
                if (siguiente) siguienteMarcado = true;
                var fondo = siguiente ? Color.Lerp(Tema.SuperficieAlta, Tema.AcentoPrediccion, 0.22f) : esClave ? Tema.SuperficieAlta : new Color(0, 0, 0, 0);
                var fila = listaHitos.Fila(esClave ? 54f : 34f, fondo);
                var franja = UIFabrica.Panel("Color", fila, Vector2.zero, new Vector2(0f, 1f), ColorHito(e.Tipo), 2);
                franja.offsetMax = new Vector2(esClave ? 5f : 3f, 0f);
                var t = UIFabrica.Texto(fila, "Texto", Vector2.zero, Vector2.one, esClave ? 15 : 13, TextAnchor.MiddleLeft, esClave ? Tema.TextoPrimario : Tema.TextoSecundario);
                t.rectTransform.offsetMin = new Vector2(14f, 2f);
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = esClave ? 15 : 13;
                string titulo = e.Titulo.Replace("Composta · ", "");
                t.text = (siguiente ? $"<color={Tema.HexDe(Tema.AcentoPrediccion)}><b>SIGUIENTE</b></color>  " : "") +
                         $"<b>{Nombres.FechaConDia(e.InicioLocal)}</b> <color={tenue}>{e.InicioLocal:HH:mm}</color>   " +
                         (esClave ? $"<b>{titulo}</b>" : titulo);
                n++;
            }
            if (n == 0) listaHitos.Aviso(soloClave ? "No hay hitos clave en el horizonte proyectado." : "No hay eventos en el horizonte proyectado.", 60f);
            listaHitos.IrAlInicio();
        }

        static Color ColorHito(TipoHito t)
        {
            switch (t)
            {
                case TipoHito.InicioFaseCaliente: case TipoHito.PicoTemperatura: return Tema.Critico;
                case TipoHito.FinFaseCaliente: return Tema.AcentoManual;
                case TipoHito.Volteo: return Tema.Aviso;
                case TipoHito.Cosecha: case TipoHito.InicioMaduracion: return Tema.Exito;
                default: return Tema.TextoTenue;
            }
        }

        // ---------------- Google Calendar ----------------
        void RefrescarGoogle()
        {
            var cfg = ConfiguracionGoogle.Cargar();
            bool configurado = cfg != null && cfg.TieneOAuth;
            bool conectado = configurado && AutenticacionGoogle.Conectado;
            txtGoogle.text = !configurado ? "Google Calendar sin configurar (ver docs/GOOGLE_Y_ANDROID.md)"
                           : conectado ? "Google Calendar conectado"
                           : "Google Calendar: se pedirá permiso al exportar";
            btnExportar.interactable = configurado && !exportando && eventos.Count > 0;
            btnDesconectar.gameObject.SetActive(conectado && !exportando);
        }

        async void Exportar()
        {
            if (exportando || eventos.Count == 0) return;
            exportando = true;
            RefrescarGoogle();
            var res = await ServicioCalendarioGoogle.ExportarAsync(eventos, tiempo.Zona, clave, s => { if (txtGoogle != null) txtGoogle.text = s; });
            exportando = false;
            if (this == null) return;
            RefrescarGoogle();
            if (res.Exito)
                Notificador.Dialogo("Hitos en tu Google Calendar",
                    $"Se crearon {res.Creados} eventos con recordatorio" +
                    (res.Reemplazados > 0 ? $" (se reemplazaron {res.Reemplazados} de una exportación anterior de este lote)." : ".") +
                    $"\n\nZona horaria: {tiempo.Zona.Etiqueta()}");
            else
                Notificador.Mostrar((res.Creados > 0 ? $"Se crearon {res.Creados} eventos, pero hubo errores: " : "No se pudo exportar: ") + res.Error,
                                    TipoMensaje.Error);
        }

        async void Desconectar()
        {
            await AutenticacionGoogle.DesconectarAsync();
            if (this == null) return;
            RefrescarGoogle();
            Notificador.Mostrar("Cuenta de Google desconectada.", TipoMensaje.Info);
        }

        // ---------------- Construcción (amplio + compacto) ----------------
        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Prediccion", transform, 55);
            UIFabrica.Panel("Velo", raiz, Vector2.zero, Vector2.one, Tema.Velo);
            var w = UIFabrica.Tarjeta("Marco", raiz, new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.97f));
            Responsivo.En(w, Vector2.zero, Vector2.one, new Vector2(6, 6), new Vector2(-6, -6));
            var franja = UIFabrica.Panel("Acento", w, new Vector2(0f, 1f), Vector2.one, Tema.AcentoPrediccion, 3);
            franja.offsetMin = new Vector2(0f, -5f);

            // Pie fijo: estado de Google + botones
            txtGoogle = UIFabrica.Texto(w, "Google", new Vector2(0.015f, 0.015f), new Vector2(0.42f, 0.075f), 16, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            Responsivo.En(txtGoogle, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14, 152), new Vector2(-14, 192));
            btnDesconectar = UIFabrica.Boton(w, "Desconectar Google", new Vector2(0.43f, 0.015f), new Vector2(0.58f, 0.075f), 16, Tema.BotonNeutro, Desconectar);
            Responsivo.En(btnDesconectar, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12, 84), new Vector2(-12, 144));
            var cerrar = UIFabrica.Boton(w, "Cerrar", new Vector2(0.60f, 0.015f), new Vector2(0.72f, 0.075f), 20, Tema.BotonNeutro, Cerrar);
            Responsivo.En(cerrar, new Vector2(0f, 0f), new Vector2(0.35f, 0f), new Vector2(12, 12), new Vector2(-6, 76));
            btnExportar = UIFabrica.Boton(w, "Exportar hitos a Google Calendar", new Vector2(0.735f, 0.015f), new Vector2(0.985f, 0.075f), 19,
                                          Tema.AcentoPrediccion, Exportar);
            var et = UIFabrica.Etiqueta(btnExportar);
            et.color = Tema.Fondo; et.fontStyle = FontStyle.Bold;
            et.resizeTextForBestFit = true; et.resizeTextMinSize = 12; et.resizeTextMaxSize = 19;
            Responsivo.En(btnExportar, new Vector2(0.35f, 0f), new Vector2(1f, 0f), new Vector2(6, 12), new Vector2(-12, 76));

            // Cuerpo desplazable: en escritorio mide 1600 u (se desplaza un poco); en móvil todo se apila
            var vista = UIFabrica.Desplazable("Cuerpo", w, new Vector2(0f, 0.085f), new Vector2(1f, 0.99f), 3700f, out var c);
            Responsivo.En(vista, Vector2.zero, Vector2.one, new Vector2(0, 200), new Vector2(0, -8));
            var cd = vista.GetComponent<ContenidoDesplazable>();
            cd.altoAmplio = 1600f;
            cd.Recalcular();

            // Cabecera
            var pild = UIFabrica.Pildora(c, "Modo", new Vector2(0.012f, 0.962f), new Vector2(0.12f, 0.992f), "PREDICCIÓN",
                                         Tema.ConAlfa(Tema.AcentoPrediccion, 0.18f), Tema.AcentoPrediccion, 15);
            Responsivo.En(pild.transform.parent, new Vector2(0.03f, 0.987f), new Vector2(0.45f, 0.998f));
            txtTitulo = UIFabrica.Texto(c, "Titulo", new Vector2(0.13f, 0.958f), new Vector2(0.988f, 0.996f), 26, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtTitulo.resizeTextForBestFit = true; txtTitulo.resizeTextMinSize = 14; txtTitulo.resizeTextMaxSize = 26;
            Responsivo.En(txtTitulo, new Vector2(0.02f, 0.975f), new Vector2(0.98f, 0.987f));
            txtSupuestos = UIFabrica.Texto(c, "Supuestos", new Vector2(0.012f, 0.928f), new Vector2(0.988f, 0.958f), 14, TextAnchor.UpperLeft, Tema.TextoSecundario);
            txtSupuestos.resizeTextForBestFit = true; txtSupuestos.resizeTextMinSize = 10; txtSupuestos.resizeTextMaxSize = 14;
            Responsivo.En(txtSupuestos, new Vector2(0.02f, 0.957f), new Vector2(0.98f, 0.975f));

            ConstruirDiagnostico(c);

            // Gráficas (izquierda en amplio, apiladas en compacto)
            var defs = new[]
            {
                (Magnitud.Temperatura, "Temperatura proyectada (°C)", "°C",
                 "Calor que generan los microbios. La fase caliente (zona verde) elimina patógenos y semillas de malezas."),
                (Magnitud.Humedad, "Humedad proyectada (%)", "%",
                 "El calor seca la pila; los riegos del manejo ideal la mantienen dentro del rango."),
                (Magnitud.Oxigeno, "Oxígeno proyectado (%)", "%",
                 "Baja por la actividad de los microbios y sube cada vez que volteas la pila."),
            };
            for (int i = 0; i < 3; i++)
            {
                float top = 0.672f - i * 0.224f;
                float topC = 0.685f - i * 0.103f;
                graficas[i] = ConstruirGrafica(c, defs[i].Item1, defs[i].Item2, defs[i].Item3, defs[i].Item4,
                                               new Vector2(0.012f, top - 0.219f), new Vector2(0.655f, top),
                                               new Vector2(0.02f, topC - 0.099f), new Vector2(0.98f, topC));
            }

            // Hitos (derecha en amplio, debajo en compacto)
            var h = UIFabrica.Panel("Hitos", c, new Vector2(0.667f, 0.003f), new Vector2(0.988f, 0.672f), Tema.SuperficieAlta);
            Responsivo.En(h, new Vector2(0.02f, 0.003f), new Vector2(0.98f, 0.37f));
            var tit = UIFabrica.Texto(h, "Titulo", new Vector2(0f, 1f), new Vector2(1f, 1f), 16, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            tit.rectTransform.offsetMin = new Vector2(14f, -36f); tit.rectTransform.offsetMax = new Vector2(-14f, -6f);
            tit.text = "<b>HITOS PREVISTOS</b>";
            txtTiempo = UIFabrica.Texto(h, "Tiempo", new Vector2(0f, 1f), new Vector2(1f, 1f), 12, TextAnchor.UpperLeft, Tema.TextoTenue);
            txtTiempo.rectTransform.offsetMin = new Vector2(14f, -56f); txtTiempo.rectTransform.offsetMax = new Vector2(-14f, -36f);
            txtTiempo.resizeTextForBestFit = true; txtTiempo.resizeTextMinSize = 9; txtTiempo.resizeTextMaxSize = 12;
            var bClave = UIFabrica.Boton(h, "Hitos clave", new Vector2(0f, 1f), new Vector2(0.5f, 1f), 15, UIFabrica.ColSel, () => { soloClave = true; PintarHitos(); });
            var rc = (RectTransform)bClave.transform; rc.offsetMin = new Vector2(14f, -100f); rc.offsetMax = new Vector2(-4f, -62f);
            fondoClave = bClave.GetComponent<Image>();
            var bTodos = UIFabrica.Boton(h, "Todos los eventos", new Vector2(0.5f, 1f), new Vector2(1f, 1f), 15, UIFabrica.ColCard, () => { soloClave = false; PintarHitos(); });
            var rt2 = (RectTransform)bTodos.transform; rt2.offsetMin = new Vector2(4f, -100f); rt2.offsetMax = new Vector2(-14f, -62f);
            fondoTodos = bTodos.GetComponent<Image>();
            foreach (var b in new[] { bClave, bTodos })
            {
                var e = UIFabrica.Etiqueta(b); e.resizeTextForBestFit = true; e.resizeTextMinSize = 10; e.resizeTextMaxSize = 15;
            }
            listaHitos = new ListaDesplazable("Lista", h, Vector2.zero, Vector2.one);
            listaHitos.Vista.offsetMin = new Vector2(10f, 10f); listaHitos.Vista.offsetMax = new Vector2(-10f, -108f);

            raiz.gameObject.SetActive(false);
        }

        void ConstruirDiagnostico(Transform c)
        {
            var d = UIFabrica.Tarjeta("Diagnostico", c, new Vector2(0.012f, 0.68f), new Vector2(0.988f, 0.924f), Tema.SuperficieAlta);
            Responsivo.En(d, new Vector2(0.02f, 0.70f), new Vector2(0.98f, 0.953f));
            var acento = UIFabrica.Panel("Acento", d, Vector2.zero, new Vector2(0f, 1f), Tema.AcentoPrediccion, 3);
            acento.offsetMax = new Vector2(6f, 0f);
            var tit = UIFabrica.Texto(d, "Titulo", new Vector2(0f, 1f), new Vector2(0.55f, 1f), 17, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            tit.rectTransform.offsetMin = new Vector2(20f, -38f); tit.rectTransform.offsetMax = new Vector2(0f, -6f);
            tit.text = "<b>DIAGNÓSTICO Y RECOMENDACIONES PERSONALIZADAS</b>";
            tit.resizeTextForBestFit = true; tit.resizeTextMinSize = 11; tit.resizeTextMaxSize = 17;
            Responsivo.En(tit, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -36f), new Vector2(-14f, -6f));
            txtOrigen = UIFabrica.Texto(d, "Origen", new Vector2(0.55f, 1f), new Vector2(1f, 1f), 13, TextAnchor.MiddleRight, Tema.TextoTenue);
            txtOrigen.rectTransform.offsetMin = new Vector2(0f, -38f); txtOrigen.rectTransform.offsetMax = new Vector2(-16f, -6f);
            txtOrigen.resizeTextForBestFit = true; txtOrigen.resizeTextMinSize = 9; txtOrigen.resizeTextMaxSize = 13;
            Responsivo.En(txtOrigen, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -62f), new Vector2(-14f, -36f));
            txtOrigen.alignment = TextAnchor.MiddleLeft;

            txtEstado = Columna(d, "ESTADO ACTUAL VS. IDEAL", new Vector2(0.01f, 0f), new Vector2(0.37f, 1f), new Vector2(0f, 0.64f), new Vector2(1f, 0.92f));
            txtAcciones = Columna(d, "ACCIONES PRIORITARIAS · HOY Y ESTA SEMANA", new Vector2(0.38f, 0f), new Vector2(0.69f, 1f), new Vector2(0f, 0.33f), new Vector2(1f, 0.63f));
            txtAlertas = Columna(d, "ALERTAS DE RIESGO", new Vector2(0.70f, 0f), new Vector2(0.99f, 1f), new Vector2(0f, 0.01f), new Vector2(1f, 0.32f));
        }

        // Columna del diagnóstico: en escritorio una de tres (lado a lado); en móvil un bloque apilado
        static Text Columna(Transform d, string titulo, Vector2 aMin, Vector2 aMax, Vector2 cMin, Vector2 cMax)
        {
            var col = UIFabrica.Panel("Columna", d, aMin, aMax, new Color(0, 0, 0, 0), 0);
            col.offsetMin = new Vector2(10f, 10f); col.offsetMax = new Vector2(-6f, -44f);
            Responsivo.En(col, cMin, cMax, new Vector2(10f, 4f), new Vector2(-10f, -2f));
            var t = UIFabrica.Texto(col, "Titulo", new Vector2(0f, 1f), new Vector2(1f, 1f), 13, TextAnchor.MiddleLeft, Tema.AcentoPrediccion);
            t.rectTransform.offsetMin = new Vector2(6f, -24f); t.rectTransform.offsetMax = new Vector2(0f, 0f);
            t.text = "<b>" + titulo + "</b>";
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 9; t.resizeTextMaxSize = 13;
            var cuerpo = UIFabrica.Texto(col, "Texto", Vector2.zero, Vector2.one, 14, TextAnchor.UpperLeft, Tema.TextoPrimario);
            cuerpo.rectTransform.offsetMin = new Vector2(6f, 0f); cuerpo.rectTransform.offsetMax = new Vector2(-2f, -26f);
            cuerpo.resizeTextForBestFit = true; cuerpo.resizeTextMinSize = 9; cuerpo.resizeTextMaxSize = 14;
            return cuerpo;
        }

        Grafica ConstruirGrafica(Transform c, Magnitud m, string titulo, string unidad, string ayuda,
                                 Vector2 aMin, Vector2 aMax, Vector2 cMin, Vector2 cMax)
        {
            var rango = Agregador.Rango(m);
            var g = new Grafica { M = m, Unidad = unidad, YMin = rango.min, YMax = rango.max };
            var card = UIFabrica.Panel("Curva " + m, c, aMin, aMax, Tema.SuperficieAlta);
            Responsivo.En(card, cMin, cMax);

            var t = UIFabrica.Texto(card, "Titulo", new Vector2(0f, 1f), new Vector2(1f, 1f), 16, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            t.rectTransform.offsetMin = new Vector2(14f, -30f); t.rectTransform.offsetMax = new Vector2(-14f, -6f);
            t.text = "<b>" + titulo + "</b>";
            var sub = UIFabrica.Texto(card, "Ayuda", new Vector2(0f, 1f), new Vector2(1f, 1f), 13, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            sub.rectTransform.offsetMin = new Vector2(14f, -50f); sub.rectTransform.offsetMax = new Vector2(-14f, -30f);
            sub.text = ayuda;
            sub.resizeTextForBestFit = true; sub.resizeTextMinSize = 9; sub.resizeTextMaxSize = 13;
            g.Leyenda = UIFabrica.Texto(card, "Leyenda", new Vector2(0f, 1f), new Vector2(1f, 1f), 12, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            g.Leyenda.rectTransform.offsetMin = new Vector2(14f, -70f); g.Leyenda.rectTransform.offsetMax = new Vector2(-14f, -50f);
            g.Leyenda.resizeTextForBestFit = true; g.Leyenda.resizeTextMinSize = 9; g.Leyenda.resizeTextMaxSize = 12;

            // Área de dibujo: margen izquierdo para el eje Y y abajo para las fechas
            var go = new GameObject("Plot", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(card, false);
            g.Plot = (RectTransform)go.transform;
            g.Plot.anchorMin = Vector2.zero; g.Plot.anchorMax = Vector2.one;
            g.Plot.offsetMin = new Vector2(58f, 30f); g.Plot.offsetMax = new Vector2(-16f, -76f);
            g.Img = go.GetComponent<RawImage>();
            g.Img.color = Color.clear;
            g.Img.raycastTarget = true;                        // recibe el cursor / el dedo
            var inter = go.AddComponent<InteraccionGrafica>();
            inter.AlApuntar += f => Apuntar(g, f);
            inter.AlSalir += OcultarGuia;

            for (int i = 0; i < g.EtiquetasY.Length; i++)
            {
                float f = i / 4f;
                var y = UIFabrica.Texto(g.Plot, "Y" + i, new Vector2(0f, f), new Vector2(0f, f), 12, TextAnchor.MiddleRight, Tema.TextoTenue);
                y.rectTransform.offsetMin = new Vector2(-58f, -9f); y.rectTransform.offsetMax = new Vector2(-6f, 9f);
                y.horizontalOverflow = HorizontalWrapMode.Overflow;
                g.EtiquetasY[i] = y;
            }

            g.Guia = UIFabrica.Panel("Guia", g.Plot, new Vector2(0f, 0f), new Vector2(0f, 1f), new Color(1f, 1f, 1f, 0.55f), 0);
            g.Guia.offsetMin = new Vector2(-1f, 0f); g.Guia.offsetMax = new Vector2(1f, 0f);
            g.Guia.GetComponent<Image>().raycastTarget = false;

            g.Ficha = UIFabrica.Panel("Ficha", g.Plot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Color(0.06f, 0.07f, 0.09f, 0.96f), Tema.RadioCampo);
            g.Ficha.sizeDelta = new Vector2(360f, 96f);
            g.Ficha.GetComponent<Image>().raycastTarget = false;
            var borde = g.Ficha.gameObject.AddComponent<Outline>();
            borde.effectColor = Tema.ConAlfa(Tema.AcentoPrediccion, 0.7f);
            borde.effectDistance = new Vector2(1f, -1f);
            g.TxtFicha = UIFabrica.Texto(g.Ficha, "Texto", Vector2.zero, Vector2.one, 13, TextAnchor.UpperLeft, Tema.TextoPrimario);
            g.TxtFicha.resizeTextForBestFit = true; g.TxtFicha.resizeTextMinSize = 9; g.TxtFicha.resizeTextMaxSize = 13;
            return g;
        }
    }
}
