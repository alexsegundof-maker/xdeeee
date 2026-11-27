using System;
using System.Collections.Generic;
using System.Globalization;
using Composta.Core;
using Composta.Core.Lotes;
using Composta.Core.Seguridad;
using Composta.Core.Textos;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Datos del lote abierto: nombre, (en Control Manual) tipo de compostera, material y cobertura,
    // historial de cambios (auditoría) y, solo para administración, eliminar el lote.
    public sealed class PanelLote : MonoBehaviour
    {
        const int MaxCambiosVisibles = 200;
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        static readonly TipoComposta[] Tipos =
            { TipoComposta.Domestica, TipoComposta.Cerrada, TipoComposta.PilaAbierta, TipoComposta.Industrial, TipoComposta.Vermicomposta };

        GestorLotes gestor;
        RectTransform raiz;
        Text txtTitulo, txtInfo, txtNotaSimulacion, txtMensaje, txtModo;
        Image fondoModo, franjaAcento;
        InputField campoNombre, campoCompostera;
        Button btnMaduracion, btnCosecha;
        GameObject seccionManual;
        Button btnGuardar, btnEliminar;
        ListaDesplazable listaCambios;
        bool layoutCompacto;

        TipoComposta tipoSel;
        float cnSel, aislSel;
        readonly List<(Image fondo, Text texto, TipoComposta valor)> opcionesTipo = new List<(Image, Text, TipoComposta)>();
        readonly List<(Image fondo, Text texto, float valor)> opcionesMaterial = new List<(Image, Text, float)>();
        readonly List<(Image fondo, Text texto, float valor)> opcionesTapada = new List<(Image, Text, float)>();

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;
        public event Action AlEliminarLote;
        public event Action PedirCosecha;

        public void Abrir(GestorLotes g)
        {
            if (g?.LoteActual == null) { Notificador.Mostrar("No hay un lote abierto.", TipoMensaje.Info); return; }
            Asegurar();
            Desuscribir();
            gestor = g;
            gestor.AlCambiarLote += AlCambiarLote;
            gestor.AlCambiarRegistros += Refrescar;
            gestor.AlCambiarAportes += Refrescar;
            Cargar(gestor.LoteActual);
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            Refrescar(gestor.LoteActual);
            listaCambios.IrAlInicio();
        }

        public void Cerrar()
        {
            if (raiz == null) return;
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
            Desuscribir();
        }

        void Desuscribir()
        {
            if (gestor == null) return;
            gestor.AlCambiarLote -= AlCambiarLote;
            gestor.AlCambiarRegistros -= Refrescar;
            gestor.AlCambiarAportes -= Refrescar;
        }

        void OnEnable() => AdaptadorPantalla.AlCambiar += AlCambiarPantalla;
        void OnDisable() => AdaptadorPantalla.AlCambiar -= AlCambiarPantalla;

        void OnDestroy()
        {
            Desuscribir();
            GuardiaEntrada.Liberar(this);
        }

        void AlCambiarPantalla()
        {
            if (Abierto && layoutCompacto != AdaptadorPantalla.Compacto) Refrescar(gestor?.LoteActual);
        }

        void AlCambiarLote(Lote l)
        {
            if (l == null) { Cerrar(); return; }
            Refrescar(l);
        }

        void Update()
        {
            if (!Abierto || Notificador.DialogoAbierto) return;
            var k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame) Cerrar();
        }

        // Valores del formulario a partir del lote (al abrir, o tras guardar)
        void Cargar(Lote l)
        {
            campoNombre.text = l.Nombre ?? "";
            campoCompostera.text = l.Compostera ?? "";
            tipoSel = l.Tipo; cnSel = l.RelacionCN; aislSel = l.Aislamiento;
            txtMensaje.text = "";
            bool manual = l.Modo == ModoLote.Manual;
            Color acento = manual ? Tema.AcentoManual : Tema.AcentoSimulacion;
            txtModo.text = manual ? "CONTROL MANUAL" : "SIMULACIÓN";
            txtModo.color = acento;
            fondoModo.color = Tema.ConAlfa(acento, 0.18f);
            franjaAcento.color = acento;
            seccionManual.SetActive(manual);
            txtNotaSimulacion.gameObject.SetActive(!manual);
            PintarOpciones();
        }

        void Refrescar(Lote l)
        {
            if (raiz == null || !Abierto || l == null || gestor == null) return;
            layoutCompacto = AdaptadorPantalla.Compacto;
            var zona = gestor.Zona;
            bool activo = l.Estado == EstadoLote.Activo;
            bool editable = activo && gestor.Puede(Permiso.EditarRegistros);

            txtTitulo.text = UIFabrica.SinRich(l.Nombre);
            string conteo = l.Modo == ModoLote.Manual
                ? $"{l.Informes.Count} {(l.Informes.Count == 1 ? "medición" : "mediciones")}"
                : string.Format(C, "día simulado {0:F1}", l.DiasSimulados);
            var b = Composta.Core.Campo.BalanceLote.Calcular(l);
            string estado = l.TieneCosecha ? "cosechado" : !activo ? "cerrado" : l.BloqueadoMaduracion ? "en maduración" : "activo";
            string madurez = "";
            if (l.Modo == ModoLote.Manual && activo)
            {
                var m = Composta.Core.Campo.Maduracion.Estimar(l, gestor.AhoraUtc);
                madurez = string.Format(C, " · madurez estimada {0} ({1})", ValidadorFechaMedicion.Formatear(m.FechaMadurezUtc.Ticks, zona).Substring(0, 10),
                                        m.DiasRestantes < 1f ? "ya debería estar madura" : $"faltan {Math.Ceiling(m.DiasRestantes):F0} días");
            }
            txtInfo.text = $"{UIFabrica.SinRich(l.Etiqueta)} · creado por <b>{UIFabrica.SinRich(l.CreadoPor)}</b> el {ValidadorFechaMedicion.Formatear(l.CreadoUtc, zona)} · <b>{estado}</b>{(l.Demo ? " · DEMO" : "")}\n" +
                           string.Format(C, "{0} · entrada {1:0.##} kg ({2} {3}) · C:N ≈ {4}{5}", conteo, b.PesoEntradaKg, l.NumeroAportaciones,
                                         l.NumeroAportaciones == 1 ? "aportación" : "aportaciones",
                                         b.TieneDatos ? b.RelacionCN.ToString("F1", C) : "—", madurez);

            bool manualActivo = activo && l.Modo == ModoLote.Manual;
            btnMaduracion.gameObject.SetActive(manualActivo);
            btnCosecha.gameObject.SetActive(manualActivo);
            btnMaduracion.interactable = btnCosecha.interactable = editable;
            UIFabrica.Etiqueta(btnMaduracion).text = l.BloqueadoMaduracion ? "Quitar bloqueo de maduración" : "Iniciar maduración";

            campoNombre.interactable = editable;
            campoCompostera.interactable = editable;
            foreach (var o in opcionesTipo) o.fondo.GetComponent<Button>().interactable = editable;
            foreach (var o in opcionesMaterial) o.fondo.GetComponent<Button>().interactable = editable;
            foreach (var o in opcionesTapada) o.fondo.GetComponent<Button>().interactable = editable;
            btnGuardar.interactable = editable;
            btnEliminar.gameObject.SetActive(gestor.Puede(Permiso.EliminarLotes));
            if (!editable && txtMensaje.text.Length == 0)
            {
                txtMensaje.color = Tema.TextoSecundario;
                txtMensaje.text = activo ? "Tu rol solo permite consultar los datos del lote." : "El lote está cerrado: solo se puede consultar.";
            }

            // Auditoría, de lo más reciente a lo más antiguo
            listaCambios.Limpiar();
            var cambios = l.Cambios ?? new List<CambioLote>();
            if (cambios.Count == 0)
            {
                listaCambios.Aviso("Sin cambios registrados. Aquí aparecerá quién corrige o elimina mediciones y aportes, y cuándo.", 90f);
                return;
            }
            string tenue = Tema.HexDe(Tema.TextoTenue);
            for (int i = cambios.Count - 1, n = 0; i >= 0 && n < MaxCambiosVisibles; i--, n++)
            {
                var c = cambios[i];
                if (c == null) continue;
                var fila = listaCambios.Fila(layoutCompacto ? 112f : 64f, Tema.SuperficieAlta);
                Color col = c.Accion == AccionCambio.Eliminacion ? Tema.Critico : c.Accion == AccionCambio.Edicion ? Tema.Aviso : Tema.Exito;
                var t = UIFabrica.Texto(fila, "Texto", Vector2.zero, Vector2.one, 14, TextAnchor.MiddleLeft, Tema.TextoPrimario);
                t.text = $"<color={Tema.HexDe(col)}>●</color> <b>{Capital(Nombres.Accion(c.Accion))} de {Nombres.Entidad(c.Entidad)}</b>" +
                         $"  <color={tenue}>{ValidadorFechaMedicion.Formatear(c.FechaUtc, zona)} · {UIFabrica.SinRich(c.Usuario)}</color>\n" +
                         $"<color={Tema.HexDe(Tema.TextoSecundario)}>{UIFabrica.SinRich(c.Detalle)}</color>";
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }
            if (cambios.Count > MaxCambiosVisibles)
                listaCambios.Aviso($"Se muestran los {MaxCambiosVisibles} cambios más recientes. «cambios.csv» al exportar incluye todos.", 60f);
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        void Guardar()
        {
            if (gestor?.LoteActual == null) return;
            bool ok = gestor.ActualizarLote(campoNombre.text, campoCompostera.text, tipoSel, cnSel, aislSel, out string msg);
            if (ok) Cargar(gestor.LoteActual);              // muestra el nombre ya limpio, tal como quedó guardado
            txtMensaje.color = ok ? UIFabrica.ColExito : UIFabrica.ColError;
            txtMensaje.text = msg;
            if (ok) Refrescar(gestor.LoteActual);
        }

        void AlternarMaduracion()
        {
            var l = gestor?.LoteActual;
            if (l == null) return;
            if (l.BloqueadoMaduracion)
            {
                bool ok = gestor.BloquearMaduracion(false, out string msg);
                Mostrar(msg, ok);
                return;
            }
            Notificador.Dialogo("¿Iniciar la maduración?",
                "Desde ahora el lote <b>no admitirá nuevas aportaciones</b>: el material fresco reiniciaría la descomposición y " +
                "retrasaría la cosecha. Sigue registrando mediciones hasta que esté listo. Puedes quitar el bloqueo si fue un error.",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Iniciar maduración", () => { bool ok = gestor.BloquearMaduracion(true, out string msg); Mostrar(msg, ok); }, true));
        }

        void Mostrar(string msg, bool ok)
        {
            txtMensaje.color = ok ? UIFabrica.ColExito : UIFabrica.ColError;
            txtMensaje.text = msg;
            if (gestor?.LoteActual != null) Refrescar(gestor.LoteActual);
        }

        void ConfirmarEliminar()
        {
            var l = gestor?.LoteActual;
            if (l == null) return;
            string contenido = l.Modo == ModoLote.Manual
                ? $"{l.Informes.Count} mediciones y {l.Aportes.Count} aportes"
                : string.Format(C, "{0:F1} días simulados y {1} aportes", l.DiasSimulados, l.Aportes.Count);
            Notificador.Dialogo("¿Eliminar este lote?",
                $"<b>{UIFabrica.SinRich(l.Nombre)}</b>\n{contenido}.\n\n" +
                "El lote desaparecerá de la app. Se guardará una copia en la carpeta «papelera» de los datos de la aplicación " +
                "por si necesitas recuperarla. Si quieres conservar los datos en hojas de cálculo, exporta antes.",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Eliminar lote", () =>
                {
                    bool ok = gestor.EliminarLoteActual(out string msg);
                    Notificador.Mostrar(msg, ok ? TipoMensaje.Exito : TipoMensaje.Error);
                    if (!ok) return;
                    Cerrar();
                    AlEliminarLote?.Invoke();
                }, true));
        }

        void ElegirTipo(TipoComposta t) { tipoSel = t; PintarOpciones(); }
        void ElegirMaterial(float v) { cnSel = v; PintarOpciones(); }
        void ElegirTapada(float v) { aislSel = v; PintarOpciones(); }

        void PintarOpciones()
        {
            foreach (var (img, txt, v) in opcionesTipo) Opcion(img, txt, v == tipoSel);
            foreach (var (img, txt, v) in opcionesMaterial) Opcion(img, txt, Mathf.Abs(cnSel - v) < 0.01f);
            foreach (var (img, txt, v) in opcionesTapada) Opcion(img, txt, Mathf.Abs(aislSel - v) < 0.01f);
        }

        static void Opcion(Image img, Text txt, bool sel)
        {
            img.color = sel ? Tema.ConAlfa(Tema.AcentoManual, 0.22f) : Tema.SuperficieAlta;
            txt.color = sel ? Tema.AcentoManual : Tema.TextoSecundario;
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Lote", transform, 38);
            UIFabrica.Panel("Velo", raiz, Vector2.zero, Vector2.one, Tema.Velo);
            var w = UIFabrica.ModalDesplazable(raiz, new Vector2(0.12f, 0.06f), new Vector2(0.88f, 0.94f), 1900f);
            var franja = UIFabrica.Panel("Acento", w, new Vector2(0f, 1f), Vector2.one, Tema.AcentoManual, 3);
            franja.offsetMin = new Vector2(0f, -5f);
            franjaAcento = franja.GetComponent<Image>();

            // Cabecera
            txtModo = UIFabrica.Pildora(w, "Modo", new Vector2(0.025f, 0.925f), new Vector2(0.21f, 0.965f), "CONTROL MANUAL",
                                        Tema.ConAlfa(Tema.AcentoManual, 0.18f), Tema.AcentoManual, 13);
            fondoModo = txtModo.transform.parent.GetComponent<Image>();
            Responsivo.En(txtModo.transform.parent, new Vector2(0.03f, 0.958f), new Vector2(0.45f, 0.982f));
            txtTitulo = UIFabrica.Texto(w, "Titulo", new Vector2(0.02f, 0.86f), new Vector2(0.98f, 0.925f), Tema.TxtSubtitulo + 4, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtTitulo.fontStyle = FontStyle.Bold;
            txtTitulo.resizeTextForBestFit = true; txtTitulo.resizeTextMinSize = 14; txtTitulo.resizeTextMaxSize = Tema.TxtSubtitulo + 4;
            Responsivo.En(txtTitulo, new Vector2(0.02f, 0.92f), new Vector2(0.98f, 0.957f));
            txtInfo = UIFabrica.Texto(w, "Info", new Vector2(0.02f, 0.79f), new Vector2(0.98f, 0.86f), Tema.TxtPequeno, TextAnchor.UpperLeft, Tema.TextoSecundario);
            // Maduración y cosecha (solo lotes de Control Manual activos)
            btnMaduracion = UIFabrica.Boton(w, "Iniciar maduración", new Vector2(0.51f, 0.735f), new Vector2(0.74f, 0.785f), 16, Tema.BotonNeutro, AlternarMaduracion);
            Responsivo.En(btnMaduracion, new Vector2(0.02f, 0.395f), new Vector2(0.49f, 0.432f));
            btnCosecha = UIFabrica.Boton(w, "Registrar cosecha", new Vector2(0.75f, 0.735f), new Vector2(0.98f, 0.785f), 16,
                                         Tema.ConAlfa(Tema.Exito, 0.85f), () => PedirCosecha?.Invoke());
            Responsivo.En(btnCosecha, new Vector2(0.51f, 0.395f), new Vector2(0.98f, 0.432f));
            foreach (var bt in new[] { btnMaduracion, btnCosecha })
            {
                var e = UIFabrica.Etiqueta(bt); e.resizeTextForBestFit = true; e.resizeTextMinSize = 10; e.resizeTextMaxSize = 16;
            }
            txtInfo.resizeTextForBestFit = true; txtInfo.resizeTextMinSize = 10; txtInfo.resizeTextMaxSize = Tema.TxtPequeno;
            Responsivo.En(txtInfo, new Vector2(0.02f, 0.885f), new Vector2(0.98f, 0.92f));

            // Columna izquierda: formulario
            var lblNombre = UIFabrica.Texto(w, "LblNombre", new Vector2(0.02f, 0.735f), new Vector2(0.31f, 0.78f), 18, TextAnchor.LowerLeft, Tema.TextoPrimario);
            lblNombre.text = "<b>Nombre del lote</b>";
            Responsivo.En(lblNombre, new Vector2(0.02f, 0.86f), new Vector2(0.58f, 0.882f));
            campoNombre = UIFabrica.Campo(w, "Nombre", new Vector2(0.03f, 0.67f), new Vector2(0.30f, 0.73f), "Ej.: Composta del huerto", false, EditorLote.NombreMax, 18);
            Responsivo.En(campoNombre, new Vector2(0.03f, 0.828f), new Vector2(0.58f, 0.857f));
            var lblComp = UIFabrica.Texto(w, "LblCompostera", new Vector2(0.31f, 0.735f), new Vector2(0.49f, 0.78f), 18, TextAnchor.LowerLeft, Tema.TextoPrimario);
            lblComp.text = "<b>Compostera</b>";
            Responsivo.En(lblComp, new Vector2(0.60f, 0.86f), new Vector2(0.98f, 0.882f));
            campoCompostera = UIFabrica.Campo(w, "Compostera", new Vector2(0.31f, 0.67f), new Vector2(0.48f, 0.73f), "Ej.: Cubeta A", false, EditorLote.ComposteraMax, 18);
            Responsivo.En(campoCompostera, new Vector2(0.60f, 0.828f), new Vector2(0.97f, 0.857f));

            txtNotaSimulacion = UIFabrica.Texto(w, "NotaSimulacion", new Vector2(0.02f, 0.50f), new Vector2(0.49f, 0.66f), 15, TextAnchor.UpperLeft, Tema.TextoSecundario);
            txtNotaSimulacion.text = "En un lote de simulación solo se puede cambiar el nombre: el tipo de compostera, el peso y los demás " +
                                     "parámetros definen la simulación ya calculada. Para probar otros valores, crea una composta nueva.";
            Responsivo.En(txtNotaSimulacion, new Vector2(0.02f, 0.74f), new Vector2(0.98f, 0.825f));

            var man = UIFabrica.Panel("Manual", w, new Vector2(0f, 0.20f), new Vector2(0.5f, 0.665f), new Color(0, 0, 0, 0), 0);
            Responsivo.En(man, new Vector2(0f, 0.555f), new Vector2(1f, 0.825f));
            seccionManual = man.gameObject;
            ConstruirSeccionManual(man);

            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.02f, 0.13f), new Vector2(0.49f, 0.20f), 15, TextAnchor.MiddleLeft, UIFabrica.ColError);
            txtMensaje.resizeTextForBestFit = true; txtMensaje.resizeTextMinSize = 10; txtMensaje.resizeTextMaxSize = 15;
            Responsivo.En(txtMensaje, new Vector2(0.02f, 0.52f), new Vector2(0.98f, 0.553f));
            btnGuardar = UIFabrica.Boton(w, "Guardar cambios", new Vector2(0.03f, 0.05f), new Vector2(0.30f, 0.12f), 20, Tema.BotonPrimario, Guardar);
            Responsivo.En(btnGuardar, new Vector2(0.03f, 0.48f), new Vector2(0.97f, 0.515f));
            btnEliminar = UIFabrica.Boton(w, "Eliminar lote", new Vector2(0.31f, 0.05f), new Vector2(0.48f, 0.12f), 18, Tema.BotonPeligro, ConfirmarEliminar);
            Responsivo.En(btnEliminar, new Vector2(0.03f, 0.44f), new Vector2(0.47f, 0.472f));
            var cerrar = UIFabrica.Boton(w, "Cerrar", new Vector2(0.80f, 0.03f), new Vector2(0.97f, 0.10f), 20, Tema.BotonNeutro, Cerrar);
            Responsivo.En(cerrar, new Vector2(0.53f, 0.44f), new Vector2(0.97f, 0.472f));
            foreach (var b in new[] { btnGuardar, btnEliminar, cerrar })
            {
                var e = UIFabrica.Etiqueta(b); e.resizeTextForBestFit = true; e.resizeTextMinSize = 11; e.resizeTextMaxSize = 20;
            }

            // Columna derecha: historial de cambios (auditoría)
            var marco = UIFabrica.Panel("Cambios", w, new Vector2(0.51f, 0.12f), new Vector2(0.98f, 0.72f), UIFabrica.ColCampo);
            Responsivo.En(marco, new Vector2(0.02f, 0.01f), new Vector2(0.98f, 0.39f));
            var tit = UIFabrica.Texto(marco, "Titulo", new Vector2(0f, 1f), new Vector2(1f, 1f), 16, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            tit.rectTransform.offsetMin = new Vector2(12f, -44f); tit.rectTransform.offsetMax = new Vector2(-12f, -6f);
            tit.text = "<b>HISTORIAL DE CAMBIOS</b>";
            listaCambios = new ListaDesplazable("Lista", marco, Vector2.zero, Vector2.one);
            listaCambios.Vista.offsetMin = new Vector2(8f, 8f); listaCambios.Vista.offsetMax = new Vector2(-8f, -48f);
            raiz.gameObject.SetActive(false);
        }

        // Mismas preguntas que al crear un lote de Control Manual (sin hablar de C/N ni coeficientes)
        void ConstruirSeccionManual(Transform padre)
        {
            var lblTipo = UIFabrica.Texto(padre, "LblTipo", new Vector2(0.04f, 0.88f), new Vector2(0.98f, 0.99f), 17, TextAnchor.LowerLeft, Tema.TextoPrimario);
            lblTipo.text = "<b>Tipo de compostera</b>";
            UIFabrica.IconoJuntoATexto(lblTipo, "Cambia el rango de peso recomendado y la interpretación de tus mediciones. " +
                                                "Las mediciones ya registradas no se modifican: solo se vuelven a evaluar.");
            Responsivo.En(lblTipo, new Vector2(0.02f, 0.89f), new Vector2(0.98f, 0.99f));
            for (int i = 0; i < Tipos.Length; i++)
            {
                var t = Tipos[i];
                int fila = i < 3 ? 0 : 1, col = i < 3 ? i : i - 3;
                float x0 = 0.04f + col * 0.315f, y1 = fila == 0 ? 0.865f : 0.725f;
                var b = UIFabrica.Boton(padre, PerfilComposta.Crear(t).Nombre, new Vector2(x0, y1 - 0.125f), new Vector2(x0 + 0.305f, y1), 14,
                                        Tema.SuperficieAlta, () => ElegirTipo(t));
                Responsivo.En(b, new Vector2(0.02f + col * 0.325f, y1 - 0.125f), new Vector2(0.02f + col * 0.325f + 0.315f, y1));
                var et = UIFabrica.Etiqueta(b); et.resizeTextForBestFit = true; et.resizeTextMinSize = 10; et.resizeTextMaxSize = 14;
                opcionesTipo.Add((b.GetComponent<Image>(), et, t));
            }
            Pregunta(padre, "¿Qué material predomina?", AyudaBuild.AyudaMaterial, 0.575f, AyudaBuild.OpcionesMaterial(), ElegirMaterial, opcionesMaterial);
            Pregunta(padre, "¿Está tapada o cubierta?", AyudaBuild.AyudaTapada, 0.29f, AyudaBuild.OpcionesTapada(), ElegirTapada, opcionesTapada);
        }

        static void Pregunta(Transform padre, string titulo, string ayuda, float top, (string texto, float valor)[] ops,
                             Action<float> elegir, List<(Image, Text, float)> destino)
        {
            var lbl = UIFabrica.Texto(padre, "Lbl " + titulo, new Vector2(0.04f, top - 0.10f), new Vector2(0.98f, top), 17, TextAnchor.LowerLeft, Tema.TextoPrimario);
            lbl.text = "<b>" + titulo + "</b>";
            UIFabrica.IconoJuntoATexto(lbl, ayuda);
            Responsivo.En(lbl, new Vector2(0.02f, top - 0.10f), new Vector2(0.98f, top));
            float ancho = 0.94f / ops.Length;
            for (int i = 0; i < ops.Length; i++)
            {
                float v = ops[i].valor, x0 = 0.04f + i * ancho;
                var b = UIFabrica.Boton(padre, ops[i].texto, new Vector2(x0, top - 0.235f), new Vector2(x0 + ancho - 0.01f, top - 0.11f), 14,
                                        Tema.SuperficieAlta, () => elegir(v));
                float xc = 0.02f + i * (0.96f / ops.Length);
                Responsivo.En(b, new Vector2(xc, top - 0.235f), new Vector2(xc + 0.96f / ops.Length - 0.01f, top - 0.11f));
                var et = UIFabrica.Etiqueta(b); et.resizeTextForBestFit = true; et.resizeTextMinSize = 10; et.resizeTextMaxSize = 14;
                destino.Add((b.GetComponent<Image>(), et, v));
            }
        }
    }
}
