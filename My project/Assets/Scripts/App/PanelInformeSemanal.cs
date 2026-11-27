using System.Collections.Generic;
using System.Globalization;
using Composta.Core.Campo;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Ventana del informe semanal: solo las 3 variables que se pueden medir en campo
    public sealed class PanelInformeSemanal : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        GestorLotes gestor;
        RectTransform raiz;
        Text txtSituacion, txtMensaje;
        readonly Text[] txtEstimado = new Text[3];
        InputField campoTemp, campoHum, campoPeso, campoNota;
        Button btnEnviar;
        bool enviando;

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;

        public void Abrir(GestorLotes g)
        {
            gestor = g;
            Asegurar();
            foreach (var c in new[] { campoTemp, campoHum, campoPeso, campoNota }) c.text = "";
            txtMensaje.text = "";
            var m = gestor.Modelo;
            txtEstimado[0].text = string.Format(Inv, "El modelo estima {0:F1} °C", m.Temperatura);
            txtEstimado[1].text = string.Format(Inv, "El modelo estima {0:F0} %", m.Humedad);
            txtEstimado[2].text = string.Format(Inv, "El modelo estima {0:0.#} kg", m.MasaKg);
            txtSituacion.text = gestor.Situacion().Texto();
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            UIFabrica.Enfocar(campoTemp);
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
            if (k == null) return;
            if (k.escapeKey.wasPressedThisFrame) Cerrar();
            else if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) Enviar();
        }

        void Enviar()
        {
            if (enviando || gestor == null || !gestor.EsLoteDeCampo) return;
            var advertencias = new List<string>();
            if (!ValidadorInforme.Validar(campoTemp.text, campoHum.text, campoPeso.text, gestor.Modelo,
                                          gestor.Calibracion.MasaUltimoInforme, out float t, out float h, out float p,
                                          out string error, advertencias))
            {
                Mensaje(error, false);
                return;
            }
            string nota = campoNota.text;
            if (advertencias.Count == 0) { Registrar(t, h, p, nota); return; }

            // Un dato raro puede ser real: se pide confirmación en vez de bloquear
            Notificador.Dialogo("Revisa los datos antes de enviar", "· " + string.Join("\n· ", advertencias),
                new OpcionDialogo("Corregir"),
                new OpcionDialogo("Enviar igual", () => Registrar(t, h, p, nota), true));
        }

        void Registrar(float t, float h, float p, string nota)
        {
            enviando = true;
            var r = gestor.RegistrarInforme(t, h, p, nota, out string error);
            enviando = false;
            if (r == null) { Mensaje(error, false); return; }

            Cerrar();
            Notificador.Mostrar("Informe semanal registrado. El modelo se recalibró.", TipoMensaje.Exito);
            Notificador.Dialogo("Informe registrado", r.Resumen() +
                string.Format(Inv, "\n\nPeso dado a tu medición: {0:F0} % (crece cuanto más tiempo pasa sin informe).", r.Ganancia * 100f));
        }

        void Mensaje(string texto, bool ok)
        {
            txtMensaje.text = texto;
            txtMensaje.color = ok ? UIFabrica.ColExito : UIFabrica.ColError;
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Informe", transform, 40);
            var fondo = UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.75f));
            var w = UIFabrica.Panel("Marco", fondo, new Vector2(0.27f, 0.10f), new Vector2(0.73f, 0.90f), UIFabrica.ColPanel);

            UIFabrica.Texto(w, "Titulo", new Vector2(0.04f, 0.90f), new Vector2(0.96f, 0.98f), 30, TextAnchor.MiddleLeft, Color.white)
                .text = "Informe semanal de campo";
            var sub = UIFabrica.Texto(w, "Sub", new Vector2(0.04f, 0.80f), new Vector2(0.96f, 0.90f), 17, TextAnchor.UpperLeft, UIFabrica.ColTextoTenue);
            sub.text = "Sin sensores conectados, el simulador se calibra con estas tres mediciones. Mide siempre en el mismo lugar y a la misma hora.";
            txtSituacion = UIFabrica.Texto(w, "Situacion", new Vector2(0.04f, 0.75f), new Vector2(0.96f, 0.80f), 17, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.5f));

            campoTemp = Fila(w, 0, 0.73f, "Temperatura (°C)", "Ej.: 55", "°C",
                "Clava un termómetro de compost (sonda larga) en el centro de la pila, a 30-50 cm de profundidad. " +
                "Espera 1-2 minutos a que se estabilice. Si puedes, mide en 2-3 puntos y anota el promedio.");
            campoHum = Fila(w, 1, 0.56f, "Humedad (%)", "Ej.: 55", "%",
                "Con higrómetro de suelo, mide en el centro. Sin instrumento, usa la prueba del puño: aprieta un puñado. " +
                "Si escurren gotas: más de 65 %. Si queda compacto y húmedo sin gotear: 50-60 %. Si se desmorona: menos de 40 %.");
            campoPeso = Fila(w, 2, 0.39f, "Peso de la pila (kg)", "Ej.: 420", "kg",
                "Pesa el contenedor o, en pilas grandes, estima: volumen (m³) × densidad (~400-600 kg/m³ según humedad). " +
                "Si agregaste material esta semana, regístralo también como aporte.");

            UIFabrica.Texto(w, "LblNota", new Vector2(0.04f, 0.27f), new Vector2(0.96f, 0.32f), 18, TextAnchor.LowerLeft, Color.white).text = "Nota (opcional)";
            campoNota = UIFabrica.Campo(w, "Nota", new Vector2(0.05f, 0.205f), new Vector2(0.95f, 0.27f), "Olor, color, si se volteó, clima…", false, 200, 19);

            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.04f, 0.115f), new Vector2(0.96f, 0.195f), 17, TextAnchor.MiddleLeft, UIFabrica.ColError);
            UIFabrica.Boton(w, "Cancelar", new Vector2(0.05f, 0.025f), new Vector2(0.35f, 0.105f), 22, UIFabrica.ColBoton, Cerrar);
            btnEnviar = UIFabrica.Boton(w, "Enviar informe", new Vector2(0.45f, 0.025f), new Vector2(0.95f, 0.105f), 24, UIFabrica.ColPrimario, Enviar);
            raiz.gameObject.SetActive(false);
        }

        InputField Fila(Transform w, int i, float top, string etiqueta, string ejemplo, string unidad, string ayuda)
        {
            var lbl = UIFabrica.Texto(w, "Lbl" + i, new Vector2(0.04f, top - 0.05f), new Vector2(0.60f, top), 20, TextAnchor.LowerLeft, Color.white);
            lbl.text = etiqueta;
            UIFabrica.IconoJuntoATexto(lbl, ayuda);
            var campo = UIFabrica.Campo(w, "Campo" + i, new Vector2(0.05f, top - 0.125f), new Vector2(0.40f, top - 0.055f), ejemplo, false, 10);
            UIFabrica.Texto(w, "Unidad" + i, new Vector2(0.40f, top - 0.125f), new Vector2(0.48f, top - 0.055f), 20, TextAnchor.MiddleLeft, UIFabrica.ColTextoTenue).text = unidad;
            txtEstimado[i] = UIFabrica.Texto(w, "Est" + i, new Vector2(0.50f, top - 0.125f), new Vector2(0.96f, top - 0.055f), 17, TextAnchor.MiddleLeft, UIFabrica.ColTextoTenue);
            return campo;
        }
    }
}
