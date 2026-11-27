using System;
using System.Globalization;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Seguridad;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Todas las mediciones del lote (Control Manual), de la más reciente a la más antigua,
    // con acciones para corregirlas o eliminarlas. Cada cambio queda en la auditoría del lote.
    public sealed class PanelMediciones : MonoBehaviour
    {
        const int MaxFilas = 400;                       // ~8 años de mediciones semanales
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        GestorLotes gestor;
        RectTransform raiz;
        Text txtTitulo, txtResumen;
        Button btnNueva;
        ListaDesplazable lista;
        bool layoutCompacto;

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;
        public Func<bool> HayVentanaEncima;            // el formulario de edición se abre por encima
        public event Action<InformeCampo> PedirEditar;
        public event Action PedirNueva;

        public void Abrir(GestorLotes g)
        {
            Asegurar();
            Desuscribir();
            gestor = g;
            gestor.AlCambiarRegistros += Refrescar;
            gestor.AlCambiarLote += AlCambiarLote;
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            Refrescar(gestor.LoteActual);
            lista.IrAlInicio();
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
            gestor.AlCambiarRegistros -= Refrescar;
            gestor.AlCambiarLote -= AlCambiarLote;
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
            if (l == null || l.Modo != ModoLote.Manual) Cerrar();      // el lote se eliminó o se abrió otro
            else Refrescar(l);
        }

        void Update()
        {
            if (!Abierto || Notificador.DialogoAbierto || (HayVentanaEncima != null && HayVentanaEncima())) return;
            var k = Keyboard.current;
            if (k != null && k.escapeKey.wasPressedThisFrame) Cerrar();
        }

        void Refrescar(Lote l)
        {
            if (raiz == null || !Abierto) return;
            lista.Limpiar();
            layoutCompacto = AdaptadorPantalla.Compacto;
            if (l == null || gestor == null) { txtResumen.text = "No hay un lote abierto."; return; }

            bool puedeEditar = gestor.Puede(Permiso.EditarRegistros);
            bool puedeRegistrar = gestor.Puede(Permiso.RegistrarInformes) && l.Estado == EstadoLote.Activo;
            btnNueva.interactable = puedeRegistrar;
            txtTitulo.text = "Mediciones · " + UIFabrica.SinRich(l.Nombre);

            var regs = AnalisisCampo.Ordenados(l);
            txtResumen.text = regs.Count == 0 ? "Sin mediciones registradas."
                : string.Format(C, "{0} {1}{2}", regs.Count, regs.Count == 1 ? "medición" : "mediciones",
                                puedeEditar ? " · toca «Editar» para corregir un dato" : " · tu rol solo permite consultar");
            if (regs.Count == 0)
            {
                lista.Aviso("Todavía no hay mediciones. Registra la primera con el botón «Nueva medición».", 90f);
                return;
            }

            var zona = gestor.Zona;
            int n = 0;
            for (int i = regs.Count - 1; i >= 0 && n < MaxFilas; i--, n++) Fila(regs[i], zona, puedeEditar && l.Estado == EstadoLote.Activo);
            if (regs.Count > MaxFilas) lista.Aviso($"Se muestran las {MaxFilas} más recientes. El archivo exportado incluye todas.", 60f);
        }

        void Fila(InformeCampo r, TimeZoneInfo zona, bool editable)
        {
            float alto = layoutCompacto ? 168f : 78f;
            var fila = lista.Fila(alto, Tema.SuperficieAlta);
            string tenue = Tema.HexDe(Tema.TextoTenue);

            string principal = string.Format(C, "<b>{0}</b>   {1:F1} °C  ·  {2:F0} %  ·  {3:0.##} kg",
                                             ValidadorFechaMedicion.Formatear(r.FechaUtc, zona), r.Temperatura, r.Humedad, r.PesoKg);
            string detalle = "Registró " + UIFabrica.SinRich(r.Usuario);
            if (!string.IsNullOrEmpty(r.EditadoPor))
                detalle += $" · corregida por {UIFabrica.SinRich(r.EditadoPor)} el {ValidadorFechaMedicion.Formatear(r.EditadoUtc, zona)}";
            if (!string.IsNullOrEmpty(r.Nota)) detalle += " · <i>" + UIFabrica.SinRich(r.Nota) + "</i>";

            var txt = UIFabrica.Texto(fila, "Texto", layoutCompacto ? new Vector2(0f, 0.42f) : Vector2.zero,
                                      layoutCompacto ? Vector2.one : new Vector2(editable ? 0.70f : 1f, 1f),
                                      Tema.TxtPequeno + 2, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txt.text = principal + $"\n<size={Tema.TxtPequeno - 1}><color={tenue}>{detalle}</color></size>";
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Truncate;

            if (!editable) return;
            var copia = r;
            var editar = UIFabrica.Boton(fila, "Editar", layoutCompacto ? new Vector2(0.02f, 0.07f) : new Vector2(0.71f, 0.16f),
                                         layoutCompacto ? new Vector2(0.49f, 0.39f) : new Vector2(0.845f, 0.84f), 17, Tema.BotonNeutro,
                                         () => PedirEditar?.Invoke(copia));
            var eliminar = UIFabrica.Boton(fila, "Eliminar", layoutCompacto ? new Vector2(0.51f, 0.07f) : new Vector2(0.855f, 0.16f),
                                           layoutCompacto ? new Vector2(0.98f, 0.39f) : new Vector2(0.99f, 0.84f), 17, Tema.BotonPeligro,
                                           () => ConfirmarEliminar(copia));
            foreach (var b in new[] { editar, eliminar })
            {
                var e = UIFabrica.Etiqueta(b);
                e.resizeTextForBestFit = true; e.resizeTextMinSize = 11; e.resizeTextMaxSize = 17;
            }
        }

        void ConfirmarEliminar(InformeCampo r)
        {
            if (gestor == null) return;
            string cuando = ValidadorFechaMedicion.Formatear(r.FechaUtc, gestor.Zona);
            Notificador.Dialogo("¿Eliminar esta medición?",
                string.Format(C, "<b>{0}</b> · {1:F1} °C · {2:F0} % · {3:0.##} kg\n\n", cuando, r.Temperatura, r.Humedad, r.PesoKg) +
                "Las tendencias, la fase estimada y las recomendaciones se recalcularán sin ella. " +
                "La eliminación queda anotada en el historial de cambios del lote.",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Eliminar medición", () =>
                {
                    bool ok = gestor.EliminarMedicion(r.Id, out string msg);
                    Notificador.Mostrar(msg, ok ? TipoMensaje.Exito : TipoMensaje.Error);
                }, true));
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Mediciones", transform, 38);    // debajo del formulario de edición (40)
            UIFabrica.Panel("Velo", raiz, Vector2.zero, Vector2.one, Tema.Velo);
            var w = UIFabrica.Tarjeta("Marco", raiz, new Vector2(0.17f, 0.07f), new Vector2(0.83f, 0.93f));
            Responsivo.En(w, new Vector2(0.01f, 0.005f), new Vector2(0.99f, 0.995f));
            var franja = UIFabrica.Panel("Acento", w, new Vector2(0f, 1f), Vector2.one, Tema.AcentoManual, 3);
            franja.offsetMin = new Vector2(0f, -5f);

            // Cabecera de alto fijo (en unidades) para que la lista use todo el espacio restante
            txtTitulo = UIFabrica.Texto(w, "Titulo", new Vector2(0f, 1f), new Vector2(1f, 1f), Tema.TxtSubtitulo + 2, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txtTitulo.rectTransform.offsetMin = new Vector2(20f, -66f); txtTitulo.rectTransform.offsetMax = new Vector2(-20f, -12f);
            txtTitulo.fontStyle = FontStyle.Bold;
            txtTitulo.resizeTextForBestFit = true; txtTitulo.resizeTextMinSize = 14; txtTitulo.resizeTextMaxSize = Tema.TxtSubtitulo + 2;
            txtResumen = UIFabrica.Texto(w, "Resumen", new Vector2(0f, 1f), new Vector2(1f, 1f), Tema.TxtPequeno, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            txtResumen.rectTransform.offsetMin = new Vector2(20f, -104f); txtResumen.rectTransform.offsetMax = new Vector2(-20f, -66f);
            txtResumen.resizeTextForBestFit = true; txtResumen.resizeTextMinSize = 10; txtResumen.resizeTextMaxSize = Tema.TxtPequeno;

            lista = new ListaDesplazable("Lista", w, Vector2.zero, Vector2.one);
            lista.Vista.offsetMin = new Vector2(16f, 92f); lista.Vista.offsetMax = new Vector2(-16f, -110f);

            // Pie: acciones
            var pie = UIFabrica.Panel("Pie", w, Vector2.zero, new Vector2(1f, 0f), new Color(0, 0, 0, 0), 0);
            pie.offsetMin = new Vector2(16f, 14f); pie.offsetMax = new Vector2(-16f, 80f);
            btnNueva = UIFabrica.Boton(pie, "Nueva medición", new Vector2(0f, 0f), new Vector2(0.49f, 1f), 20, Tema.AcentoManual, () => PedirNueva?.Invoke());
            UIFabrica.Etiqueta(btnNueva).color = Tema.Fondo;
            UIFabrica.Etiqueta(btnNueva).fontStyle = FontStyle.Bold;
            UIFabrica.Boton(pie, "Cerrar", new Vector2(0.51f, 0f), new Vector2(1f, 1f), 20, Tema.BotonNeutro, Cerrar);
            raiz.gameObject.SetActive(false);
        }
    }
}
