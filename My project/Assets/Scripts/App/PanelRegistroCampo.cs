using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Formulario de medición de campo (Control Manual): solo las 3 variables medibles en la práctica.
    // No hay estimaciones de un modelo: se compara con la medición anterior, si existe.
    // Sirve también para CORREGIR una medición ya guardada (modo edición, con opción de eliminarla).
    public sealed class PanelRegistroCampo : MonoBehaviour
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        GestorLotes gestor;
        RectTransform raiz;
        Text txtTitulo, txtContexto, txtMensaje;
        readonly Text[] txtAnterior = new Text[3];
        InputField campoTemp, campoHum, campoPeso, campoNota, campoFecha;
        RectTransform rtCancelar, rtGuardar;
        Button btnEliminar;
        Text lblGuardar;
        bool enviando;
        InformeCampo editando;                  // null = medición nueva
        int cuadroCierre = -1;

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;
        // Evita que la misma tecla Esc cierre también la ventana que quedó debajo
        public bool CerradoEsteCuadro => cuadroCierre == Time.frameCount;
        public event Action AlRegistrar;

        public void Abrir(GestorLotes g)
        {
            gestor = g;
            Asegurar();
            editando = null;
            foreach (var c in new[] { campoTemp, campoHum, campoPeso, campoNota, campoFecha }) c.text = "";
            txtMensaje.text = "";

            var e = gestor.Analisis();
            var perfil = PerfilComposta.Crear(gestor.LoteActual.Tipo);
            txtTitulo.text = e.Registros == 0 ? "Primera medición" : "Medición semanal";
            txtContexto.text = Contexto(perfil);
            MostrarAnterior(e.Ultimo);
            ModoBotones(false);
            Mostrar();
        }

        // Corregir una medición guardada: se precargan sus valores y se compara con la medición previa a su fecha
        public void AbrirEdicion(GestorLotes g, InformeCampo r)
        {
            if (g == null || r == null) return;
            gestor = g;
            Asegurar();
            editando = r;
            campoTemp.text = r.Temperatura.ToString("0.#", Inv);
            campoHum.text = r.Humedad.ToString("0.#", Inv);
            campoPeso.text = r.PesoKg.ToString("0.##", Inv);
            campoNota.text = r.Nota ?? "";
            campoFecha.text = ValidadorFechaMedicion.Formatear(r.FechaUtc, gestor.Zona);
            txtMensaje.text = "";

            var perfil = PerfilComposta.Crear(gestor.LoteActual.Tipo);
            txtTitulo.text = "Corregir medición";
            string quien = "Registrada por " + UIFabrica.SinRich(r.Usuario) +
                           (string.IsNullOrEmpty(r.EditadoPor) ? "" : $" · última corrección: {UIFabrica.SinRich(r.EditadoPor)}");
            txtContexto.text = quien + "\n" + Contexto(perfil);
            MostrarAnterior(EditorLote.MedicionAnterior(gestor.LoteActual, r.FechaUtc, r.Id));
            ModoBotones(true);
            Mostrar();
        }

        void Mostrar()
        {
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            UIFabrica.Enfocar(campoTemp);
        }

        static string Contexto(PerfilComposta perfil)
            => string.Format(Inv, "{0} · peso recomendado {1:0.#}–{2:0.#} kg · límites físicos {3:0.#}–{4:0.#} kg",
                             perfil.Nombre, perfil.PesoMin, perfil.PesoMax, perfil.ExtremoMin, perfil.ExtremoMax);

        void MostrarAnterior(InformeCampo u)
        {
            txtAnterior[0].text = u == null ? "" : string.Format(Inv, "Anterior: {0:F1} °C", u.Temperatura);
            txtAnterior[1].text = u == null ? "" : string.Format(Inv, "Anterior: {0:F0} %", u.Humedad);
            txtAnterior[2].text = u == null ? "" : string.Format(Inv, "Anterior: {0:0.#} kg", u.PesoKg);
        }

        // Nueva: Cancelar | Guardar medición. Edición: Cancelar | Eliminar | Guardar cambios
        void ModoBotones(bool edicion)
        {
            btnEliminar.gameObject.SetActive(edicion);
            Anclar(rtCancelar, 0.05f, edicion ? 0.29f : 0.33f);
            Anclar(rtGuardar, edicion ? 0.57f : 0.38f, 0.95f);
            lblGuardar.text = edicion ? "Guardar cambios" : "Guardar medición";
        }

        static void Anclar(RectTransform rt, float x0, float x1)
        {
            rt.anchorMin = new Vector2(x0, rt.anchorMin.y);
            rt.anchorMax = new Vector2(x1, rt.anchorMax.y);
        }

        public void Cerrar()
        {
            if (raiz == null) return;
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
            editando = null;
            cuadroCierre = Time.frameCount;
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
            if (enviando || gestor == null || !gestor.EsLoteManual) return;
            var lote = gestor.LoteActual;
            if (!ValidadorFechaMedicion.Validar(campoFecha.text, gestor.Zona, gestor.AhoraUtc, out DateTime fechaUtc, out string error))
            {
                Mensaje(error, false);
                return;
            }
            // En edición sin tocar la fecha se conserva la original exacta (el texto solo muestra minutos)
            if (editando != null && campoFecha.text.Trim() == ValidadorFechaMedicion.Formatear(editando.FechaUtc, gestor.Zona))
                fechaUtc = new DateTime(editando.FechaUtc, DateTimeKind.Utc);

            var advertencias = new List<string>();
            var perfil = PerfilComposta.Crear(lote.Tipo);
            var anterior = EditorLote.MedicionAnterior(lote, fechaUtc.Ticks, editando?.Id);
            if (!ValidadorInforme.Validar(campoTemp.text, campoHum.text, campoPeso.text, perfil, anterior,
                                          out float t, out float h, out float p, out error, advertencias))
            {
                Mensaje(error, false);
                return;
            }
            ValidadorFechaMedicion.Advertencias(lote, fechaUtc, editando?.Id, gestor.Zona, advertencias);
            string nota = campoNota.text;
            if (advertencias.Count == 0) { Guardar(t, h, p, nota, fechaUtc); return; }

            // Un dato raro puede ser real: se pide confirmación en vez de bloquear
            Notificador.Dialogo("Revisa los datos antes de guardar", "· " + string.Join("\n· ", advertencias),
                new OpcionDialogo("Corregir"),
                new OpcionDialogo("Guardar igual", () => Guardar(t, h, p, nota, fechaUtc), true));
        }

        void Guardar(float t, float h, float p, string nota, DateTime fechaUtc)
        {
            enviando = true;
            bool edicion = editando != null;
            string mensaje;
            bool ok = edicion ? gestor.ActualizarMedicion(editando.Id, t, h, p, nota, fechaUtc, out mensaje)
                              : gestor.RegistrarMedicion(t, h, p, nota, fechaUtc, out mensaje);
            enviando = false;
            if (!ok) { Mensaje(mensaje, false); return; }
            Cerrar();
            Notificador.Mostrar(edicion ? mensaje + " Las tendencias y recomendaciones se recalcularon."
                                        : "Medición guardada. El tablero se actualizó con tus datos.", TipoMensaje.Exito);
            AlRegistrar?.Invoke();
        }

        void ConfirmarEliminar()
        {
            if (editando == null || gestor == null) return;
            var r = editando;
            Notificador.Dialogo("¿Eliminar esta medición?",
                string.Format(Inv, "<b>{0}</b> · {1:F1} °C · {2:F0} % · {3:0.##} kg\n\n",
                              ValidadorFechaMedicion.Formatear(r.FechaUtc, gestor.Zona), r.Temperatura, r.Humedad, r.PesoKg) +
                "Las tendencias, la fase estimada y las recomendaciones se recalcularán sin ella. " +
                "La eliminación queda anotada en el historial de cambios del lote.",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Eliminar medición", () =>
                {
                    bool ok = gestor.EliminarMedicion(r.Id, out string msg);
                    if (!ok) { Mensaje(msg, false); return; }
                    Cerrar();
                    Notificador.Mostrar(msg, TipoMensaje.Exito);
                    AlRegistrar?.Invoke();
                }, true));
        }

        void Mensaje(string texto, bool ok)
        {
            txtMensaje.text = texto;
            txtMensaje.color = ok ? UIFabrica.ColExito : UIFabrica.ColError;
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Registro", transform, 40);
            UIFabrica.Panel("Velo", raiz, Vector2.zero, Vector2.one, Tema.Velo);
            var w = UIFabrica.ModalDesplazable(raiz, new Vector2(0.29f, 0.09f), new Vector2(0.71f, 0.91f), 1250f);
            var franja = UIFabrica.Panel("Acento", w, new Vector2(0f, 1f), Vector2.one, Tema.AcentoManual, 3);
            franja.offsetMin = new Vector2(0f, -5f);

            UIFabrica.Pildora(w, "Modo", new Vector2(0.05f, 0.925f), new Vector2(0.32f, 0.965f), "CONTROL MANUAL",
                              Tema.ConAlfa(Tema.AcentoManual, 0.18f), Tema.AcentoManual, 13);
            txtTitulo = UIFabrica.Texto(w, "Titulo", new Vector2(0.04f, 0.85f), new Vector2(0.96f, 0.925f), Tema.TxtTitulo, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtTitulo.fontStyle = FontStyle.Bold;
            txtContexto = UIFabrica.Texto(w, "Contexto", new Vector2(0.04f, 0.80f), new Vector2(0.96f, 0.85f), 15, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            txtContexto.resizeTextForBestFit = true; txtContexto.resizeTextMinSize = 11; txtContexto.resizeTextMaxSize = 15;

            campoTemp = Fila(w, 0, 0.78f, "Temperatura", "Ej.: 55", "°C",
                "Clava un termómetro de compost (sonda larga) en el centro de la pila, a 30-50 cm de profundidad. " +
                "Espera 1-2 minutos. Si puedes, mide en 2-3 puntos y anota el promedio.");
            campoHum = Fila(w, 1, 0.615f, "Humedad", "Ej.: 55", "%",
                "Con higrómetro de suelo, mide en el centro. Sin instrumento, prueba del puño: si escurren gotas, más de 65 %; " +
                "si queda compacto y húmedo sin gotear, 50-60 %; si se desmorona, menos de 40 %.");
            campoPeso = Fila(w, 2, 0.45f, "Peso", "Ej.: 20", "kg",
                "Pesa el contenedor (restando su peso vacío) o, en pilas grandes, estima: volumen (m³) × ~500 kg/m³. " +
                "Si agregaste material esta semana, regístralo también como aporte.");

            UIFabrica.Texto(w, "LblNota", new Vector2(0.04f, 0.245f), new Vector2(0.58f, 0.29f), 17, TextAnchor.LowerLeft, Tema.TextoSecundario).text = "Nota (opcional)";
            campoNota = UIFabrica.Campo(w, "Nota", new Vector2(0.05f, 0.18f), new Vector2(0.57f, 0.24f), "Olor, color, si se volteó, clima…", false, 200, 18);
            var lblFecha = UIFabrica.Texto(w, "LblFecha", new Vector2(0.60f, 0.245f), new Vector2(0.96f, 0.29f), 17, TextAnchor.LowerLeft, Tema.TextoSecundario);
            lblFecha.text = "Fecha y hora (opcional)";
            lblFecha.resizeTextForBestFit = true; lblFecha.resizeTextMinSize = 11; lblFecha.resizeTextMaxSize = 17;
            UIFabrica.IconoJuntoATexto(lblFecha,
                "Déjala vacía para usar la fecha y hora actuales. Si mediste otro día y lo registras después, escribe cuándo " +
                "mediste: dd/mm/aaaa hh:mm (por ejemplo 25/11/2026 09:30). No se aceptan fechas futuras ni de hace más de un año.");
            campoFecha = UIFabrica.Campo(w, "Fecha", new Vector2(0.61f, 0.18f), new Vector2(0.95f, 0.24f), "Ahora", false, 16, 18);

            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.04f, 0.11f), new Vector2(0.96f, 0.175f), 16, TextAnchor.MiddleLeft, UIFabrica.ColError);
            rtCancelar = (RectTransform)UIFabrica.Boton(w, "Cancelar", new Vector2(0.05f, 0.03f), new Vector2(0.33f, 0.10f), 20, Tema.BotonNeutro, Cerrar).transform;
            btnEliminar = UIFabrica.Boton(w, "Eliminar", new Vector2(0.31f, 0.03f), new Vector2(0.55f, 0.10f), 20, Tema.BotonPeligro, ConfirmarEliminar);
            var guardar = UIFabrica.Boton(w, "Guardar medición", new Vector2(0.38f, 0.03f), new Vector2(0.95f, 0.10f), 22, Tema.AcentoManual, Enviar);
            rtGuardar = (RectTransform)guardar.transform;
            lblGuardar = UIFabrica.Etiqueta(guardar);
            lblGuardar.color = Tema.Fondo;
            lblGuardar.fontStyle = FontStyle.Bold;
            lblGuardar.resizeTextForBestFit = true; lblGuardar.resizeTextMinSize = 12; lblGuardar.resizeTextMaxSize = 22;
            btnEliminar.gameObject.SetActive(false);
            raiz.gameObject.SetActive(false);
        }

        InputField Fila(Transform w, int i, float top, string etiqueta, string ejemplo, string unidad, string ayuda)
        {
            var lbl = UIFabrica.Texto(w, "Lbl" + i, new Vector2(0.04f, top - 0.045f), new Vector2(0.60f, top), 19, TextAnchor.LowerLeft, Tema.TextoPrimario);
            lbl.text = "<b>" + etiqueta + "</b>";
            UIFabrica.IconoJuntoATexto(lbl, ayuda);
            var campo = UIFabrica.Campo(w, "Campo" + i, new Vector2(0.05f, top - 0.125f), new Vector2(0.42f, top - 0.05f), ejemplo, false, 10, 26);
            UIFabrica.Texto(w, "Unidad" + i, new Vector2(0.42f, top - 0.125f), new Vector2(0.52f, top - 0.05f), 20, TextAnchor.MiddleLeft, Tema.TextoSecundario).text = unidad;
            txtAnterior[i] = UIFabrica.Texto(w, "Ant" + i, new Vector2(0.53f, top - 0.125f), new Vector2(0.96f, top - 0.05f), 16, TextAnchor.MiddleLeft, Tema.TextoTenue);
            return campo;
        }
    }
}
