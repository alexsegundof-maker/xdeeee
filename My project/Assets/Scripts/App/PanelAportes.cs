using System;
using System.Globalization;
using Composta.Core.Lotes;
using Composta.Core.Seguridad;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Bitácora de residuos que entran al lote (peso + tipo de material + nota).
    // Cada aporte se puede corregir o eliminar; el cambio queda en la auditoría del lote y el total se recalcula.
    public sealed class PanelAportes : MonoBehaviour
    {
        const int MaxFilas = 300;
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        GestorLotes gestor;
        bool puedeRegistrar;
        TipoMaterial material = TipoMaterial.Verde;
        Aporte editando;                                // null = aporte nuevo

        RectTransform raiz;
        Text txtTitulo, txtMensaje, txtTotal, lblFormulario;
        InputField campoKg, campoNota;
        Button btnRegistrar;
        GameObject barraEdicion;
        ListaDesplazable lista;
        bool layoutCompacto;
        readonly Image[] fondoMaterial = new Image[3];

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;

        public void Abrir(GestorLotes g, bool permitirRegistro)
        {
            Asegurar();
            Desuscribir();
            gestor = g;
            gestor.AlCambiarAportes += Refrescar;
            gestor.AlCambiarLote += AlCambiarLote;
            puedeRegistrar = permitirRegistro;

            SalirDeEdicion();
            txtMensaje.text = "";
            foreach (var c in new[] { campoKg, campoNota }) c.interactable = puedeRegistrar;
            btnRegistrar.interactable = puedeRegistrar;
            if (!puedeRegistrar) Mensaje("Tu rol solo permite consultar los aportes.", false);

            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            Refrescar(gestor.LoteActual);
            lista.IrAlInicio();
            if (puedeRegistrar) UIFabrica.Enfocar(campoKg);
        }

        public void Cerrar()
        {
            if (raiz == null) return;
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
            Desuscribir();
            editando = null;
        }

        void Desuscribir()
        {
            if (gestor == null) return;
            gestor.AlCambiarAportes -= Refrescar;
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
            if (l == null) { Cerrar(); return; }
            SalirDeEdicion();
            Refrescar(l);
        }

        void Update()
        {
            if (!Abierto || Notificador.DialogoAbierto) return;
            var k = Keyboard.current;
            if (k == null) return;
            if (k.escapeKey.wasPressedThisFrame) { if (editando != null) SalirDeEdicion(); else Cerrar(); }
            else if (puedeRegistrar && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) Registrar();
        }

        void Registrar()
        {
            if (gestor == null) return;
            bool edicion = editando != null;
            bool ok = edicion ? gestor.ActualizarAporte(editando.Id, campoKg.text, material, campoNota.text, out string msg)
                              : gestor.RegistrarAporte(campoKg.text, material, campoNota.text, out msg);
            Mensaje(ok && edicion ? msg + " El total se recalculó." : msg, ok);
            if (!ok) return;
            SalirDeEdicion();
            UIFabrica.Enfocar(campoKg);
        }

        void EmpezarEdicion(Aporte a)
        {
            if (gestor == null || a == null) return;
            editando = a;
            campoKg.text = a.Kg.ToString("0.##", C);
            campoNota.text = a.Nota ?? "";
            ElegirMaterial(a.Material);
            lblFormulario.text = $"Corrigiendo el aporte del {ValidadorFechaMedicion.Formatear(a.FechaUtc, gestor.Zona)}";
            lblFormulario.color = Tema.Aviso;
            btnRegistrar.gameObject.SetActive(false);
            barraEdicion.SetActive(true);
            txtMensaje.text = "";
            UIFabrica.Enfocar(campoKg);
        }

        void SalirDeEdicion()
        {
            editando = null;
            if (raiz == null) return;
            campoKg.text = ""; campoNota.text = "";
            ElegirMaterial(material);
            lblFormulario.text = "Peso (kg)";
            lblFormulario.color = Color.white;
            btnRegistrar.gameObject.SetActive(true);
            barraEdicion.SetActive(false);
        }

        void ConfirmarEliminar(Aporte a)
        {
            if (gestor == null || a == null) return;
            Notificador.Dialogo("¿Eliminar este aporte?",
                string.Format(C, "<b>{0:0.##} kg</b> · {1} · {2}\nRegistrado por {3}\n\n", a.Kg, ValidadorAporte.Nombre(a.Material),
                              ValidadorFechaMedicion.Formatear(a.FechaUtc, gestor.Zona), UIFabrica.SinRich(a.Usuario)) +
                "El total aportado se recalculará sin él. La eliminación queda anotada en el historial de cambios del lote.",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Eliminar aporte", () =>
                {
                    bool ok = gestor.EliminarAporte(a.Id, out string msg);
                    if (ok && editando != null && editando.Id == a.Id) SalirDeEdicion();
                    Mensaje(msg, ok);
                }, true));
        }

        void Mensaje(string texto, bool exito)
        {
            txtMensaje.text = texto;
            txtMensaje.color = exito ? UIFabrica.ColExito : UIFabrica.ColError;
        }

        void ElegirMaterial(TipoMaterial m)
        {
            material = m;
            for (int i = 0; i < fondoMaterial.Length; i++)
                fondoMaterial[i].color = i == (int)m ? UIFabrica.ColSel : UIFabrica.ColCard;
        }

        void Refrescar(Lote l)
        {
            if (raiz == null || !Abierto) return;
            lista.Limpiar();
            layoutCompacto = AdaptadorPantalla.Compacto;
            if (l == null)
            {
                txtTitulo.text = "Aportes de residuos";
                txtTotal.text = "";
                lista.Aviso("No hay un lote activo.", 80f);
                return;
            }
            txtTitulo.text = "Aportes · " + UIFabrica.SinRich(l.Nombre);
            txtTotal.text = string.Format(C, "Total aportado: <b>{0:0.##} kg</b> en {1} {2}", l.TotalAportadoKg, l.Aportes.Count,
                                          l.Aportes.Count == 1 ? "registro" : "registros");
            if (l.Aportes.Count == 0) { lista.Aviso("Todavía no hay aportes registrados en este lote.", 80f); return; }

            bool editable = gestor.Puede(Permiso.EditarRegistros) && l.Estado == EstadoLote.Activo;
            var zona = gestor.Zona;
            for (int i = l.Aportes.Count - 1, n = 0; i >= 0 && n < MaxFilas; i--, n++)
                if (l.Aportes[i] != null) Fila(l.Aportes[i], zona, editable);
            if (l.Aportes.Count > MaxFilas) lista.Aviso($"Se muestran los {MaxFilas} más recientes. El archivo exportado incluye todos.", 60f);
        }

        void Fila(Aporte a, TimeZoneInfo zona, bool editable)
        {
            float alto = layoutCompacto ? 150f : 70f;
            var fila = lista.Fila(alto, Tema.SuperficieAlta);
            string detalle = UIFabrica.SinRich(a.Usuario);
            if (!string.IsNullOrEmpty(a.EditadoPor)) detalle += " · corregido por " + UIFabrica.SinRich(a.EditadoPor);
            if (!string.IsNullOrEmpty(a.Nota)) detalle += " · <i>" + UIFabrica.SinRich(a.Nota) + "</i>";

            var txt = UIFabrica.Texto(fila, "Texto", layoutCompacto ? new Vector2(0f, 0.44f) : Vector2.zero,
                                      layoutCompacto ? Vector2.one : new Vector2(editable ? 0.66f : 1f, 1f), 16, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txt.text = string.Format(C, "<b>{0:0.##} kg</b> · {1}  <color={2}>{3}</color>\n<size=13><color={2}>{4}</color></size>",
                                     a.Kg, ValidadorAporte.Nombre(a.Material), Tema.HexDe(Tema.TextoTenue),
                                     ValidadorFechaMedicion.Formatear(a.FechaUtc, zona), detalle);
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            if (!editable) return;

            var copia = a;
            var editar = UIFabrica.Boton(fila, "Editar", layoutCompacto ? new Vector2(0.02f, 0.07f) : new Vector2(0.67f, 0.15f),
                                         layoutCompacto ? new Vector2(0.49f, 0.41f) : new Vector2(0.825f, 0.85f), 16, Tema.BotonNeutro,
                                         () => EmpezarEdicion(copia));
            var eliminar = UIFabrica.Boton(fila, "Eliminar", layoutCompacto ? new Vector2(0.51f, 0.07f) : new Vector2(0.835f, 0.15f),
                                           layoutCompacto ? new Vector2(0.98f, 0.41f) : new Vector2(0.99f, 0.85f), 16, Tema.BotonPeligro,
                                           () => ConfirmarEliminar(copia));
            foreach (var b in new[] { editar, eliminar })
            {
                var e = UIFabrica.Etiqueta(b);
                e.resizeTextForBestFit = true; e.resizeTextMinSize = 10; e.resizeTextMaxSize = 16;
            }
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Aportes", transform, 40);
            var fondo = UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.75f));
            var w = UIFabrica.ModalDesplazable(fondo, new Vector2(0.12f, 0.08f), new Vector2(0.88f, 0.92f), 1600f);

            // En móvil: formulario arriba (una columna) y lista de aportes debajo
            txtTitulo = UIFabrica.Texto(w, "Titulo", new Vector2(0.02f, 0.90f), new Vector2(0.98f, 0.98f), 30, TextAnchor.MiddleLeft, Color.white);
            txtTitulo.resizeTextForBestFit = true; txtTitulo.resizeTextMinSize = 14; txtTitulo.resizeTextMaxSize = 30;
            Responsivo.En(txtTitulo, new Vector2(0.03f, 0.945f), new Vector2(0.97f, 0.995f));

            // Formulario (izquierda)
            lblFormulario = UIFabrica.Texto(w, "LblKg", new Vector2(0.02f, 0.82f), new Vector2(0.42f, 0.87f), 20, TextAnchor.LowerLeft, Color.white);
            lblFormulario.text = "Peso (kg)";
            lblFormulario.resizeTextForBestFit = true; lblFormulario.resizeTextMinSize = 12; lblFormulario.resizeTextMaxSize = 20;
            Responsivo.En(lblFormulario, new Vector2(0.03f, 0.90f), new Vector2(0.97f, 0.94f));
            campoKg = UIFabrica.Campo(w, "CampoKg", new Vector2(0.03f, 0.74f), new Vector2(0.41f, 0.815f), "Ej.: 12,5", false, 10);
            Responsivo.En(campoKg, new Vector2(0.03f, 0.845f), new Vector2(0.97f, 0.895f));
            // Sin CharacterValidation.Decimal: según la versión de uGUI rechaza la coma decimal.
            // La validación real (coma o punto, rango, NaN) la hace ValidadorAporte.

            var lblMat = UIFabrica.Texto(w, "LblMaterial", new Vector2(0.02f, 0.66f), new Vector2(0.42f, 0.71f), 20, TextAnchor.LowerLeft, Color.white);
            lblMat.text = "Tipo de material";
            Responsivo.En(lblMat, new Vector2(0.03f, 0.795f), new Vector2(0.97f, 0.835f));
            var tipos = new[] { TipoMaterial.Verde, TipoMaterial.Cafe, TipoMaterial.Mixto };
            for (int i = 0; i < tipos.Length; i++)
            {
                var t = tipos[i];
                float x0 = 0.03f + i * 0.128f;
                var b = UIFabrica.Boton(w, ValidadorAporte.Nombre(t), new Vector2(x0, 0.58f), new Vector2(x0 + 0.122f, 0.655f), 16, UIFabrica.ColCard, () => ElegirMaterial(t));
                Responsivo.En(b, new Vector2(0.03f + i * 0.315f, 0.725f), new Vector2(0.03f + i * 0.315f + 0.30f, 0.79f));
                var et = UIFabrica.Etiqueta(b); et.resizeTextForBestFit = true; et.resizeTextMinSize = 11; et.resizeTextMaxSize = 16;
                fondoMaterial[i] = b.GetComponent<Image>();
            }

            var lblNota = UIFabrica.Texto(w, "LblNota", new Vector2(0.02f, 0.50f), new Vector2(0.42f, 0.55f), 20, TextAnchor.LowerLeft, Color.white);
            lblNota.text = "Nota (opcional)";
            Responsivo.En(lblNota, new Vector2(0.03f, 0.675f), new Vector2(0.97f, 0.715f));
            campoNota = UIFabrica.Campo(w, "CampoNota", new Vector2(0.03f, 0.42f), new Vector2(0.41f, 0.495f), "Origen, estado del material…", false, ValidadorAporte.NotaMax, 20);
            Responsivo.En(campoNota, new Vector2(0.03f, 0.62f), new Vector2(0.97f, 0.67f));

            btnRegistrar = UIFabrica.Boton(w, "Registrar aporte", new Vector2(0.03f, 0.31f), new Vector2(0.41f, 0.39f), 24, UIFabrica.ColPrimario, Registrar);
            Responsivo.En(btnRegistrar, new Vector2(0.03f, 0.555f), new Vector2(0.97f, 0.61f));

            // Mismo lugar que "Registrar aporte" mientras se corrige uno existente
            var edicion = UIFabrica.Panel("Edicion", w, new Vector2(0.03f, 0.31f), new Vector2(0.41f, 0.39f), new Color(0, 0, 0, 0), 0);
            Responsivo.En(edicion, new Vector2(0.03f, 0.555f), new Vector2(0.97f, 0.61f));
            var guardar = UIFabrica.Boton(edicion, "Guardar cambios", new Vector2(0f, 0f), new Vector2(0.64f, 1f), 22, UIFabrica.ColPrimario, Registrar);
            var eg = UIFabrica.Etiqueta(guardar); eg.resizeTextForBestFit = true; eg.resizeTextMinSize = 12; eg.resizeTextMaxSize = 22;
            var cancelar = UIFabrica.Boton(edicion, "Cancelar", new Vector2(0.66f, 0f), new Vector2(1f, 1f), 20, Tema.BotonNeutro, SalirDeEdicion);
            var ec = UIFabrica.Etiqueta(cancelar); ec.resizeTextForBestFit = true; ec.resizeTextMinSize = 11; ec.resizeTextMaxSize = 20;
            barraEdicion = edicion.gameObject;
            barraEdicion.SetActive(false);

            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.02f, 0.18f), new Vector2(0.42f, 0.30f), 18, TextAnchor.UpperLeft, UIFabrica.ColError);
            Responsivo.En(txtMensaje, new Vector2(0.03f, 0.505f), new Vector2(0.97f, 0.55f));

            // Historial (derecha): lista desplazable con acciones por aporte
            var panelLista = UIFabrica.Panel("Lista", w, new Vector2(0.44f, 0.11f), new Vector2(0.98f, 0.87f), UIFabrica.ColCampo);
            Responsivo.En(panelLista, new Vector2(0.03f, 0.075f), new Vector2(0.97f, 0.495f));
            txtTotal = UIFabrica.Texto(panelLista, "Total", new Vector2(0f, 1f), new Vector2(1f, 1f), 19, TextAnchor.MiddleLeft, Color.white);
            txtTotal.rectTransform.offsetMin = new Vector2(12f, -48f); txtTotal.rectTransform.offsetMax = new Vector2(-12f, -6f);
            lista = new ListaDesplazable("Registros", panelLista, Vector2.zero, Vector2.one);
            lista.Vista.offsetMin = new Vector2(8f, 8f); lista.Vista.offsetMax = new Vector2(-8f, -52f);

            var cerrar = UIFabrica.Boton(w, "Cerrar", new Vector2(0.80f, 0.015f), new Vector2(0.98f, 0.09f), 22, UIFabrica.ColBoton, Cerrar);
            Responsivo.En(cerrar, new Vector2(0.55f, 0.008f), new Vector2(0.97f, 0.062f));
            raiz.gameObject.SetActive(false);
        }
    }
}
