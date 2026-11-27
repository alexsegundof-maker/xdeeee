using System.Globalization;
using Composta.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Ajustes del proceso durante el monitoreo de un lote en modo Manual.
    // Todo pasa por SimuladorComposta: el motor recalcula temperatura, humedad y fases,
    // y cada ajuste queda en la bitácora del lote (se reproduce al reanudar).
    public sealed class PanelAjustesManuales : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        SimuladorComposta sim;
        RectTransform raiz;
        Text txtEstado, txtMensaje;
        InputField campoLitros, campoAmbiente;
        float proximoRefresco;

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;

        public void Abrir(SimuladorComposta simulador)
        {
            sim = simulador;
            Asegurar();
            campoLitros.text = sim.Sistema.Perfil.LitrosPorRiego.ToString("0.#", Inv);   // sugerencia: riego estándar
            campoAmbiente.text = sim.Modelo.TempAmbiente.ToString("0.#", Inv);
            txtMensaje.text = "";
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            RefrescarEstado();
            UIFabrica.Enfocar(campoLitros);
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
            if (!Abierto) return;
            if (Time.unscaledTime >= proximoRefresco) RefrescarEstado();     // la simulación sigue corriendo detrás
            var k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame && !Notificador.DialogoAbierto) Cerrar();
        }

        void RefrescarEstado()
        {
            proximoRefresco = Time.unscaledTime + 0.5f;
            if (sim == null || sim.Sistema == null) return;
            var m = sim.Modelo;
            txtEstado.text = string.Format(Inv,
                "Día {0:F1}  ·  {1}\nTemperatura {2:F1} °C  ·  Humedad {3:F0} %  ·  O2 {4:F1} %  ·  pH {5:F2}\n" +
                "Ambiente {6:F1} °C  ·  Peso {7:0.#} kg  ·  Control {8}",
                m.Dias, m.Etapa, m.Temperatura, m.Humedad, m.Oxigeno, m.PH,
                m.TempAmbiente, sim.Sistema.Perfil.PesoKg, sim.modoAutomatico ? "automático" : "manual");
        }

        void Regar()
        {
            if (!RangosParametros.IntentarLeer(campoLitros.text, out float litros))
            {
                Mensaje("Escribe los litros como número (por ejemplo 12,5).", false);
                return;
            }
            float humAntes = sim.Modelo.Humedad;
            if (sim.RegarLitros(litros))
                Mensaje(string.Format(Inv, "Riego de {0:0.#} L aplicado: humedad {1:F0} % → {2:F0} %.", litros, humAntes, sim.Modelo.Humedad), true);
            else Mensaje($"Los litros deben estar entre {SimuladorComposta.LitrosMin} y {SimuladorComposta.LitrosMax:F0}.", false);
            RefrescarEstado();
        }

        void AplicarAmbiente()
        {
            if (!RangosParametros.IntentarLeer(campoAmbiente.text, out float grados))
            {
                Mensaje("Escribe la temperatura como número (por ejemplo 18 o -2,5).", false);
                return;
            }
            var r = RangosParametros.De(ModoLote.Manual, Parametro.TempAmbiente);
            if (sim.CambiarTempAmbiente(grados))
                Mensaje(string.Format(Inv, "Temperatura ambiente ajustada a {0:0.#} °C. El simulador recalcula la evolución.", grados), true);
            else Mensaje($"La temperatura ambiente debe estar entre {r.Min:F0} y {r.Max:F0} °C.", false);
            RefrescarEstado();
        }

        void Voltear()
        {
            sim.Voltear();
            Mensaje("Volteo aplicado: oxígeno al máximo y la pila se enfría un poco.", true);
            RefrescarEstado();
        }

        void Mensaje(string t, bool ok)
        {
            txtMensaje.text = t;
            txtMensaje.color = ok ? UIFabrica.ColExito : UIFabrica.ColError;
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Ajustes", transform, 40);
            var fondo = UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.75f));
            var w = UIFabrica.Panel("Marco", fondo, new Vector2(0.25f, 0.18f), new Vector2(0.75f, 0.82f), UIFabrica.ColPanel);

            UIFabrica.Texto(w, "Titulo", new Vector2(0.03f, 0.88f), new Vector2(0.85f, 0.98f), 30, TextAnchor.MiddleLeft, Color.white)
                .text = "Ajustes manuales del proceso";
            UIFabrica.IconoInfo(w, "Ajustes disponibles en lotes en modo Manual. Cada ajuste se aplica al simulador de inmediato " +
                                   "y queda registrado en la bitácora del lote con tu usuario.",
                                Vector2.one, Vector2.one, new Vector2(-14f, -18f));

            var estado = UIFabrica.Panel("Estado", w, new Vector2(0.03f, 0.64f), new Vector2(0.97f, 0.86f), UIFabrica.ColCampo);
            txtEstado = UIFabrica.Texto(estado, "Texto", Vector2.zero, Vector2.one, 19, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.9f));

            // Riego con litros exactos
            UIFabrica.Texto(w, "LblRiego", new Vector2(0.03f, 0.53f), new Vector2(0.6f, 0.61f), 20, TextAnchor.LowerLeft, Color.white).text = "Riego personalizado (litros)";
            campoLitros = UIFabrica.Campo(w, "Litros", new Vector2(0.04f, 0.43f), new Vector2(0.55f, 0.52f), "Ej.: 40", false, 10);
            UIFabrica.Boton(w, "Regar", new Vector2(0.58f, 0.43f), new Vector2(0.96f, 0.52f), 22, UIFabrica.ColBoton, Regar);

            // Temperatura ambiente
            UIFabrica.Texto(w, "LblAmb", new Vector2(0.03f, 0.32f), new Vector2(0.6f, 0.40f), 20, TextAnchor.LowerLeft, Color.white).text = "Temperatura ambiente (°C)";
            campoAmbiente = UIFabrica.Campo(w, "Ambiente", new Vector2(0.04f, 0.22f), new Vector2(0.55f, 0.31f), "Ej.: 18", false, 8);
            UIFabrica.Boton(w, "Aplicar", new Vector2(0.58f, 0.22f), new Vector2(0.96f, 0.31f), 22, UIFabrica.ColBoton, AplicarAmbiente);

            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.03f, 0.12f), new Vector2(0.97f, 0.21f), 18, TextAnchor.MiddleLeft, UIFabrica.ColExito);
            UIFabrica.Boton(w, "Voltear ahora", new Vector2(0.04f, 0.025f), new Vector2(0.40f, 0.11f), 22, UIFabrica.ColPrimario, Voltear);
            UIFabrica.Boton(w, "Cerrar", new Vector2(0.70f, 0.025f), new Vector2(0.96f, 0.11f), 22, UIFabrica.ColBoton, Cerrar);
            raiz.gameObject.SetActive(false);
        }
    }
}
