using System;
using System.Globalization;
using System.Text;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Registrar la cosecha: peso cosechado, fecha y revisión física. Muestra en vivo el rendimiento
    // (peso cosechado / peso de entrada total) y la calidad A/B/C con cada criterio que la decide.
    public sealed class PanelCosecha : MonoBehaviour
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        GestorLotes gestor;
        RectTransform raiz;
        Text txtContexto, txtEvaluacion, txtMensaje;
        InputField campoPeso, campoFecha, campoNota;
        readonly bool[] checks = new bool[3];
        readonly Image[] fondoCheck = new Image[3];
        readonly Text[] textoCheck = new Text[3];
        static readonly string[] NombresCheck = { "Huele a tierra", "No se reconocen restos", "Textura suelta, se desmorona" };

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;
        public event Action AlCosechar;

        public void Abrir(GestorLotes g)
        {
            var l = g?.LoteActual;
            if (l == null || l.Modo != ModoLote.Manual || l.Estado != EstadoLote.Activo) return;
            gestor = g;
            Asegurar();
            campoPeso.text = ""; campoFecha.text = ""; campoNota.text = ""; txtMensaje.text = "";
            for (int i = 0; i < 3; i++) Marcar(i, false);
            var b = BalanceLote.Calcular(l);
            txtContexto.text = string.Format(C, "<b>{0}</b>\nEntraron <b>{1:0.##} kg</b> (carga inicial {2:0.##} kg + {3:0.##} kg en {4} aportaciones). " +
                                                "Pesa todo el compost que sacas, ya sin restos grandes.",
                                             UIFabrica.SinRich(l.Etiqueta), b.PesoEntradaKg, b.CargaBaseKg, b.AportadoKg, l.NumeroAportaciones);
            Evaluar();
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            UIFabrica.Enfocar(campoPeso);
        }

        public void Cerrar()
        {
            if (raiz == null) return;
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
        }

        void OnDestroy() => GuardiaEntrada.Liberar(this);

        void Update()
        {
            if (!Abierto || Notificador.DialogoAbierto) return;
            var k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame) Cerrar();
        }

        void Marcar(int i, bool valor)
        {
            checks[i] = valor;
            fondoCheck[i].color = valor ? Tema.ConAlfa(Tema.Exito, 0.30f) : Tema.SuperficieAlta;
            textoCheck[i].text = (valor ? "✔  " : "○  ") + NombresCheck[i];
            textoCheck[i].color = valor ? Tema.TextoPrimario : Tema.TextoSecundario;
            Evaluar();
        }

        // Vista previa con los datos escritos (no guarda nada)
        void Evaluar()
        {
            var l = gestor?.LoteActual;
            if (l == null || txtEvaluacion == null) return;
            if (!ValidadorAporte.IntentarLeerKg(campoPeso.text, out float kg, out _))
            {
                txtEvaluacion.text = "Escribe el peso cosechado para calcular el rendimiento y la calidad.";
                return;
            }
            DateTime fecha = gestor.AhoraUtc;
            if (!ValidadorFechaMedicion.Validar(campoFecha.text, gestor.Zona, gestor.AhoraUtc, out fecha, out string errFecha))
            {
                txtEvaluacion.text = errFecha;
                return;
            }
            var c = new CosechaLote { FechaUtc = fecha.Ticks, PesoKg = kg, OlorTierra = checks[0], SinRestos = checks[1], TexturaSuelta = checks[2] };
            var r = EvaluacionCosecha.Evaluar(l, c);
            string colCal = Tema.HexDe(r.Calidad == 'A' ? Tema.Exito : r.Calidad == 'B' ? Tema.Aviso : Tema.Critico);
            var sb = new StringBuilder();
            sb.AppendFormat(C, "Rendimiento: <b>{0:F0} %</b>  ({1:0.##} de {2:0.##} kg)   ·   Calidad: <color={3}><b>{4}</b></color>  ({5} de {6} criterios)\n",
                            r.RendimientoPct, kg, r.PesoEntradaKg, colCal, EvaluacionCosecha.DescribirCalidad(r.Calidad), r.Puntos, r.PuntosMaximos);
            foreach (var cr in r.Criterios)
                sb.Append($"<color={Tema.HexDe(cr.Cumple ? Tema.Exito : Tema.TextoTenue)}>{(cr.Cumple ? "✔" : "✘")}</color> {cr.Nombre}: " +
                          $"<color={Tema.HexDe(Tema.TextoSecundario)}>{cr.Detalle}</color>\n");
            if (r.RendimientoPct > EditorLote.RendimientoMaximoAceptado)
                sb.Append($"<color={Tema.HexDe(Tema.Critico)}>Es más de lo que entró al lote: revisa el peso o las aportaciones.</color>");
            txtEvaluacion.text = sb.ToString();
        }

        void Confirmar()
        {
            var l = gestor?.LoteActual;
            if (l == null) return;
            Notificador.Dialogo("¿Registrar la cosecha?",
                "El lote pasará al historial como <b>cosechado</b> con su rendimiento y calidad, y ya no admitirá mediciones ni aportaciones. " +
                "Si fue un error, podrás anular la cosecha desde el historial.",
                new OpcionDialogo("Revisar"),
                new OpcionDialogo("Registrar cosecha", () =>
                {
                    bool ok = gestor.RegistrarCosecha(campoPeso.text, campoFecha.text, checks[0], checks[1], checks[2], campoNota.text, out string msg);
                    if (!ok) { txtMensaje.text = msg; return; }
                    Cerrar();
                    Notificador.Mostrar(msg, TipoMensaje.Exito);
                    AlCosechar?.Invoke();
                }, true));
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Cosecha", transform, 41);
            UIFabrica.Panel("Velo", raiz, Vector2.zero, Vector2.one, Tema.Velo);
            var w = UIFabrica.ModalDesplazable(raiz, new Vector2(0.25f, 0.06f), new Vector2(0.75f, 0.94f), 1500f);
            var franja = UIFabrica.Panel("Acento", w, new Vector2(0f, 1f), Vector2.one, Tema.Exito, 3);
            franja.offsetMin = new Vector2(0f, -5f);

            var titulo = UIFabrica.Texto(w, "Titulo", new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.97f), Tema.TxtTitulo, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            titulo.text = "<b>Registrar cosecha</b>";
            txtContexto = UIFabrica.Texto(w, "Contexto", new Vector2(0.04f, 0.80f), new Vector2(0.96f, 0.90f), 15, TextAnchor.UpperLeft, Tema.TextoSecundario);
            txtContexto.resizeTextForBestFit = true; txtContexto.resizeTextMinSize = 11; txtContexto.resizeTextMaxSize = 15;

            UIFabrica.Texto(w, "LblPeso", new Vector2(0.04f, 0.755f), new Vector2(0.48f, 0.795f), 17, TextAnchor.LowerLeft, Tema.TextoPrimario).text = "<b>Peso cosechado (kg)</b>";
            campoPeso = UIFabrica.Campo(w, "Peso", new Vector2(0.05f, 0.69f), new Vector2(0.47f, 0.75f), "Ej.: 14,5", false, 10, 22);
            campoPeso.onValueChanged.AddListener(_ => Evaluar());
            var lblFecha = UIFabrica.Texto(w, "LblFecha", new Vector2(0.52f, 0.755f), new Vector2(0.96f, 0.795f), 17, TextAnchor.LowerLeft, Tema.TextoPrimario);
            lblFecha.text = "<b>Fecha</b> (opcional)";
            UIFabrica.IconoJuntoATexto(lblFecha, "Vacía = hoy. Si cosechaste otro día, escribe dd/mm/aaaa.");
            campoFecha = UIFabrica.Campo(w, "Fecha", new Vector2(0.53f, 0.69f), new Vector2(0.95f, 0.75f), "Hoy", false, 16, 20);
            campoFecha.onValueChanged.AddListener(_ => Evaluar());

            var lblRev = UIFabrica.Texto(w, "LblRevision", new Vector2(0.04f, 0.635f), new Vector2(0.96f, 0.675f), 17, TextAnchor.LowerLeft, Tema.TextoPrimario);
            lblRev.text = "<b>Revisión del compost</b>";
            UIFabrica.IconoJuntoATexto(lblRev, "Toca cada casilla que se cumpla. Junto con la última medición y el rendimiento deciden la calidad: " +
                                               "A con 5–6 criterios, B con 3–4, C con 2 o menos.");
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var b = UIFabrica.Boton(w, NombresCheck[i], new Vector2(0.05f + i * 0.305f, 0.555f), new Vector2(0.345f + i * 0.305f, 0.625f), 15,
                                        Tema.SuperficieAlta, () => Marcar(k, !checks[k]));
                fondoCheck[i] = b.GetComponent<Image>();
                textoCheck[i] = UIFabrica.Etiqueta(b);
                textoCheck[i].resizeTextForBestFit = true; textoCheck[i].resizeTextMinSize = 10; textoCheck[i].resizeTextMaxSize = 15;
            }

            var marco = UIFabrica.Panel("Evaluacion", w, new Vector2(0.04f, 0.27f), new Vector2(0.96f, 0.54f), UIFabrica.ColCampo);
            txtEvaluacion = UIFabrica.Texto(marco, "Texto", Vector2.zero, Vector2.one, 15, TextAnchor.UpperLeft, Tema.TextoPrimario);
            txtEvaluacion.resizeTextForBestFit = true; txtEvaluacion.resizeTextMinSize = 10; txtEvaluacion.resizeTextMaxSize = 15;

            UIFabrica.Texto(w, "LblNota", new Vector2(0.04f, 0.215f), new Vector2(0.96f, 0.255f), 16, TextAnchor.LowerLeft, Tema.TextoSecundario).text = "Nota (opcional)";
            campoNota = UIFabrica.Campo(w, "Nota", new Vector2(0.05f, 0.155f), new Vector2(0.95f, 0.21f), "Uso del compost, cribado, observaciones…", false, ValidadorAporte.NotaMax, 17);
            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.04f, 0.105f), new Vector2(0.96f, 0.15f), 15, TextAnchor.MiddleLeft, UIFabrica.ColError);
            txtMensaje.resizeTextForBestFit = true; txtMensaje.resizeTextMinSize = 10; txtMensaje.resizeTextMaxSize = 15;

            UIFabrica.Boton(w, "Cancelar", new Vector2(0.05f, 0.025f), new Vector2(0.34f, 0.095f), 20, Tema.BotonNeutro, Cerrar);
            var reg = UIFabrica.Boton(w, "Registrar cosecha", new Vector2(0.38f, 0.025f), new Vector2(0.95f, 0.095f), 22, Tema.BotonPrimario, Confirmar);
            UIFabrica.Etiqueta(reg).fontStyle = FontStyle.Bold;
            raiz.gameObject.SetActive(false);
        }
    }
}
