using System;
using System.Collections.Generic;
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
    // Aportaciones de residuos: material específico, peso, humedad y fecha. Cada aportación cambia el peso de
    // entrada, el balance C:N, la humedad estimada y la fecha de madurez; el panel muestra el efecto ANTES de guardar.
    public sealed class PanelAportes : MonoBehaviour
    {
        const int MaxFilas = 300;
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        static readonly Dictionary<MaterialEspecifico, string> NombreCorto = new Dictionary<MaterialEspecifico, string>
        {
            { MaterialEspecifico.RestosCocina, "Restos de cocina" }, { MaterialEspecifico.Cesped, "Césped" },
            { MaterialEspecifico.PososCafe, "Posos de café" }, { MaterialEspecifico.Estiercol, "Estiércol" },
            { MaterialEspecifico.Hojarasca, "Hojarasca" }, { MaterialEspecifico.Paja, "Paja" },
            { MaterialEspecifico.Aserrin, "Aserrín" }, { MaterialEspecifico.Carton, "Cartón" },
            { MaterialEspecifico.Papel, "Papel" }, { MaterialEspecifico.MezclaGenerica, "Mezcla" },
        };

        GestorLotes gestor;
        bool puedeRegistrar;
        MaterialEspecifico material = MaterialEspecifico.RestosCocina;
        Aporte editando;                                // null = aportación nueva

        RectTransform raiz;
        Text txtTitulo, txtMensaje, txtTotal, lblFormulario, txtInfoMaterial;
        InputField campoKg, campoNota, campoHumedad, campoFecha;
        GameObject grupoFecha;
        Button btnRegistrar;
        GameObject barraEdicion;
        ListaDesplazable lista;
        bool layoutCompacto;
        readonly List<(MaterialEspecifico m, Image fondo, Text texto)> botonesMaterial = new List<(MaterialEspecifico, Image, Text)>();

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
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            Refrescar(gestor.LoteActual);
            lista.IrAlInicio();
            if (FormularioHabilitado()) UIFabrica.Enfocar(campoKg);
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
            else if (FormularioHabilitado() && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) Registrar();
        }

        Lote Lote => gestor?.LoteActual;
        bool EnSimulacion => Lote != null && Lote.Modo == ModoLote.Simular;
        bool Bloqueado => Lote != null && Lote.BloqueadoMaduracion && editando == null;

        bool FormularioHabilitado()
            => Lote != null && Lote.Estado == EstadoLote.Activo && (editando != null ? gestor.Puede(Permiso.EditarRegistros) : puedeRegistrar && !Lote.BloqueadoMaduracion);

        void Registrar()
        {
            if (gestor == null || !FormularioHabilitado()) return;
            bool edicion = editando != null;
            string fecha = EnSimulacion ? "" : campoFecha.text;
            bool ok = edicion ? gestor.ActualizarAporte(editando.Id, campoKg.text, material, campoHumedad.text, fecha, campoNota.text, out string msg)
                              : gestor.RegistrarAporte(campoKg.text, material, campoHumedad.text, fecha, campoNota.text, out msg);
            if (ok && !edicion) msg += " El balance y las recomendaciones se recalcularon.";
            Mensaje(msg, ok);
            if (!ok) return;
            SalirDeEdicion(false);
            UIFabrica.Enfocar(campoKg);
        }

        void EmpezarEdicion(Aporte a)
        {
            if (gestor == null || a == null) return;
            editando = a;
            campoKg.text = a.Kg.ToString("0.##", C);
            campoHumedad.text = a.HumedadPct > 0f ? a.HumedadPct.ToString("0.#", C) : "";
            campoNota.text = a.Nota ?? "";
            campoFecha.text = ValidadorFechaMedicion.Formatear(a.FechaUtc, gestor.Zona);
            ElegirMaterial(Materiales.Resolver(a));
            lblFormulario.text = a.EsCargaInicial ? "Corrigiendo la carga inicial" : $"Corrigiendo la aportación del {ValidadorFechaMedicion.Formatear(a.FechaUtc, gestor.Zona)}";
            lblFormulario.color = Tema.Aviso;
            btnRegistrar.gameObject.SetActive(false);
            barraEdicion.SetActive(true);
            txtMensaje.text = "";
            AplicarHabilitado();
            UIFabrica.Enfocar(campoKg);
        }

        void SalirDeEdicion() => SalirDeEdicion(true);

        void SalirDeEdicion(bool limpiarMensaje)
        {
            editando = null;
            if (raiz == null) return;
            campoKg.text = ""; campoNota.text = ""; campoHumedad.text = ""; campoFecha.text = "";
            ElegirMaterial(material);
            lblFormulario.text = "Peso (kg)";
            lblFormulario.color = Color.white;
            btnRegistrar.gameObject.SetActive(true);
            barraEdicion.SetActive(false);
            if (limpiarMensaje) txtMensaje.text = "";
            AplicarHabilitado();
        }

        void AplicarHabilitado()
        {
            bool hab = FormularioHabilitado();
            foreach (var c in new[] { campoKg, campoNota, campoHumedad, campoFecha }) c.interactable = hab;
            foreach (var b in botonesMaterial) b.fondo.GetComponent<Button>().interactable = hab;
            btnRegistrar.interactable = hab;
            if (grupoFecha != null) grupoFecha.SetActive(!EnSimulacion);
            if (txtMensaje.text.Length > 0) return;
            if (Lote == null) return;
            if (Lote.Estado != EstadoLote.Activo) Mensaje("El lote ya no está activo: solo se puede consultar.", false);
            else if (Bloqueado) Mensaje("El lote está en maduración: ya no admite aportaciones. Quita el bloqueo en «Lote» si fue un error.", false);
            else if (!puedeRegistrar && editando == null) Mensaje("Tu rol solo permite consultar las aportaciones.", false);
        }

        void ConfirmarEliminar(Aporte a)
        {
            if (gestor == null || a == null) return;
            Notificador.Dialogo("¿Eliminar esta aportación?",
                string.Format(C, "<b>{0:0.##} kg</b> · {1} · {2}\nRegistrada por {3}\n\n", a.Kg, Materiales.NombreDe(a),
                              ValidadorFechaMedicion.Formatear(a.FechaUtc, gestor.Zona), UIFabrica.SinRich(a.Usuario)) +
                "El peso de entrada, el balance C:N, la humedad estimada y la fecha de madurez se recalcularán sin ella. " +
                "La eliminación queda anotada en el historial de cambios del lote.",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Eliminar aportación", () =>
                {
                    bool ok = gestor.EliminarAporte(a.Id, out string msg);
                    if (ok && editando != null && editando.Id == a.Id) SalirDeEdicion(false);
                    Mensaje(msg, ok);
                }, true));
        }

        void Mensaje(string texto, bool exito)
        {
            txtMensaje.text = texto;
            txtMensaje.color = exito ? UIFabrica.ColExito : UIFabrica.ColError;
        }

        void ElegirMaterial(MaterialEspecifico m)
        {
            material = m;
            foreach (var (mat, fondo, texto) in botonesMaterial)
            {
                bool sel = mat == m;
                var grupo = Materiales.De(mat).Grupo;
                fondo.color = sel ? UIFabrica.ColSel
                            : grupo == TipoMaterial.Verde ? Color.Lerp(UIFabrica.ColCard, Tema.Exito, 0.18f)
                            : grupo == TipoMaterial.Cafe ? Color.Lerp(UIFabrica.ColCard, Tema.AcentoManual, 0.16f) : UIFabrica.ColCard;
                texto.color = sel ? Color.white : Tema.TextoPrimario;
            }
            var d = Materiales.De(m);
            campoHumedad.placeholder.GetComponent<Text>().text = $"Típica: {d.HumedadTipica:F0}";
            Previsualizar();
        }

        // Efecto de la aportación en el balance antes de guardarla (sobre una copia: el lote no cambia)
        void Previsualizar()
        {
            if (txtInfoMaterial == null) return;
            var d = Materiales.De(material);
            string grupo = d.Grupo == TipoMaterial.Verde ? "verde (aporta nitrógeno)" : d.Grupo == TipoMaterial.Cafe ? "café (aporta carbono)" : "mezcla";
            string info = string.Format(C, "{0}: {1} · C:N {2:F0} · humedad típica {3:F0} %", d.Nombre, grupo, d.RelacionCN, d.HumedadTipica);
            var l = Lote;
            if (l != null && ValidadorAporte.IntentarLeerKg(campoKg.text, out float kg, out _) &&
                Materiales.IntentarLeerHumedad(campoHumedad.text, out float h, out _))
            {
                var antes = BalanceLote.Calcular(l);
                var copia = EditorLote.Clonar(l);
                Aporte a = editando != null ? copia.Aportes.Find(x => x.Id == editando.Id) : null;
                if (a == null) { a = new Aporte { Id = "previa", FechaUtc = DateTime.MaxValue.Ticks / 2 }; copia.Aportes.Add(a); }
                a.Kg = kg; a.Especifico = material; a.Material = d.Grupo; a.HumedadPct = h;
                var despues = BalanceLote.Calcular(copia);
                if (despues.TieneDatos)
                {
                    var nivel = despues.Nivel;
                    string col = Tema.HexDe(nivel == NivelCN.Optima ? Tema.Exito : nivel == NivelCN.ExcesoNitrogeno || nivel == NivelCN.ExcesoCarbono ? Tema.Critico : Tema.Aviso);
                    info += string.Format(C, "\nCon esta {0}: C:N {1} → <color={2}><b>{3:F1}</b> ({4})</color> · entrada {5:0.#} → {6:0.#} kg",
                                          editando != null ? "corrección" : "aportación",
                                          antes.TieneDatos ? antes.RelacionCN.ToString("F1", C) : "—", col, despues.RelacionCN,
                                          BalanceLote.NombreNivel(nivel).ToLowerInvariant(), antes.PesoEntradaKg, despues.PesoEntradaKg);
                }
            }
            txtInfoMaterial.text = info;
        }

        void Refrescar(Lote l)
        {
            if (raiz == null || !Abierto) return;
            lista.Limpiar();
            layoutCompacto = AdaptadorPantalla.Compacto;
            AplicarHabilitado();
            if (l == null)
            {
                txtTitulo.text = "Aportaciones de residuos";
                txtTotal.text = "";
                lista.Aviso("No hay un lote activo.", 80f);
                return;
            }
            txtTitulo.text = "Aportaciones · " + UIFabrica.SinRich(l.Etiqueta);
            var b = BalanceLote.Calcular(l);
            string nivelCol = Tema.HexDe(b.Nivel == NivelCN.Optima ? Tema.Exito : b.Nivel == NivelCN.ExcesoNitrogeno || b.Nivel == NivelCN.ExcesoCarbono ? Tema.Critico : Tema.Aviso);
            txtTotal.text = string.Format(C, "Entrada total: <b>{0:0.##} kg</b>  (carga {1:0.##} kg + {2:0.##} kg en {3} {4})", b.PesoEntradaKg, b.CargaBaseKg,
                                          b.AportadoKg, l.NumeroAportaciones, l.NumeroAportaciones == 1 ? "aportación" : "aportaciones") +
                            (b.TieneDatos
                                ? string.Format(C, "\nC:N de la mezcla ≈ <b>{0:F1}</b> <color={1}>{2}</color>  ·  humedad ≈ {3:F0} %", b.RelacionCN, nivelCol,
                                                BalanceLote.NombreNivel(b.Nivel), b.HumedadActualPct ?? b.HumedadMezclaPct)
                                : "\nSin datos suficientes para el balance C:N.");
            Previsualizar();

            var aportes = BalanceLote.AportesOrdenados(l);
            if (aportes.Count == 0) { lista.Aviso("Todavía no hay aportaciones registradas en este lote.", 80f); return; }

            bool editable = gestor.Puede(Permiso.EditarRegistros) && l.Estado == EstadoLote.Activo;
            var zona = gestor.Zona;
            for (int i = aportes.Count - 1, n = 0; i >= 0 && n < MaxFilas; i--, n++) Fila(aportes[i], zona, editable);
            if (aportes.Count > MaxFilas) lista.Aviso($"Se muestran las {MaxFilas} más recientes. El archivo exportado incluye todas.", 60f);
        }

        void Fila(Aporte a, TimeZoneInfo zona, bool editable)
        {
            float alto = layoutCompacto ? 160f : 74f;
            var fila = lista.Fila(alto, a.EsCargaInicial ? Color.Lerp(Tema.SuperficieAlta, Tema.AcentoManual, 0.10f) : Tema.SuperficieAlta);
            var d = Materiales.De(Materiales.Resolver(a));
            string tenue = Tema.HexDe(Tema.TextoTenue);
            string detalle = UIFabrica.SinRich(a.Usuario);
            if (!string.IsNullOrEmpty(a.EditadoPor)) detalle += " · corregido por " + UIFabrica.SinRich(a.EditadoPor);
            if (!string.IsNullOrEmpty(a.Nota)) detalle += " · <i>" + UIFabrica.SinRich(a.Nota) + "</i>";

            var txt = UIFabrica.Texto(fila, "Texto", layoutCompacto ? new Vector2(0f, 0.44f) : Vector2.zero,
                                      layoutCompacto ? Vector2.one : new Vector2(editable ? 0.66f : 1f, 1f), 16, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            txt.text = string.Format(C, "{0}<b>{1:0.##} kg</b> · {2} · {3:F0} %  <color={4}>{5}</color>\n<size=13><color={4}>{6}</color></size>",
                                     a.EsCargaInicial ? $"<color={Tema.HexDe(Tema.AcentoManual)}><b>CARGA INICIAL</b></color> · " : "",
                                     a.Kg, d.Nombre, Materiales.HumedadDe(a), tenue, ValidadorFechaMedicion.Formatear(a.FechaUtc, zona), detalle);
            txt.verticalOverflow = VerticalWrapMode.Truncate;
            txt.resizeTextForBestFit = true; txt.resizeTextMinSize = 11; txt.resizeTextMaxSize = 16;
            if (!editable) return;

            var copia = a;
            var editar = UIFabrica.Boton(fila, "Editar", layoutCompacto ? new Vector2(0.02f, 0.07f) : new Vector2(0.67f, 0.15f),
                                         layoutCompacto ? new Vector2(a.EsCargaInicial ? 0.98f : 0.49f, 0.41f) : new Vector2(a.EsCargaInicial ? 0.99f : 0.825f, 0.85f),
                                         16, Tema.BotonNeutro, () => EmpezarEdicion(copia));
            var et = UIFabrica.Etiqueta(editar); et.resizeTextForBestFit = true; et.resizeTextMinSize = 10; et.resizeTextMaxSize = 16;
            if (a.EsCargaInicial) return;           // la carga inicial se corrige, no se elimina
            var eliminar = UIFabrica.Boton(fila, "Eliminar", layoutCompacto ? new Vector2(0.51f, 0.07f) : new Vector2(0.835f, 0.15f),
                                           layoutCompacto ? new Vector2(0.98f, 0.41f) : new Vector2(0.99f, 0.85f), 16, Tema.BotonPeligro,
                                           () => ConfirmarEliminar(copia));
            var ee = UIFabrica.Etiqueta(eliminar); ee.resizeTextForBestFit = true; ee.resizeTextMinSize = 10; ee.resizeTextMaxSize = 16;
        }

        void Asegurar()
        {
            if (raiz != null) return;
            raiz = UIFabrica.Canvas("Canvas Aportes", transform, 40);
            var fondo = UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.75f));
            var w = UIFabrica.ModalDesplazable(fondo, new Vector2(0.10f, 0.06f), new Vector2(0.90f, 0.94f), 1750f);

            txtTitulo = UIFabrica.Texto(w, "Titulo", new Vector2(0.02f, 0.91f), new Vector2(0.98f, 0.985f), 28, TextAnchor.MiddleLeft, Color.white);
            txtTitulo.resizeTextForBestFit = true; txtTitulo.resizeTextMinSize = 14; txtTitulo.resizeTextMaxSize = 28;
            Responsivo.En(txtTitulo, new Vector2(0.03f, 0.958f), new Vector2(0.97f, 0.995f));

            // Peso y humedad
            lblFormulario = Etiqueta(w, "LblKg", "Peso (kg)", new Vector2(0.02f, 0.84f), new Vector2(0.21f, 0.88f), new Vector2(0.03f, 0.928f), new Vector2(0.48f, 0.955f));
            campoKg = UIFabrica.Campo(w, "CampoKg", new Vector2(0.03f, 0.775f), new Vector2(0.20f, 0.835f), "Ej.: 12,5", false, 10, 20);
            Responsivo.En(campoKg, new Vector2(0.03f, 0.888f), new Vector2(0.48f, 0.924f));
            campoKg.onValueChanged.AddListener(_ => Previsualizar());
            var lblHum = Etiqueta(w, "LblHumedad", "Humedad (%)", new Vector2(0.22f, 0.84f), new Vector2(0.42f, 0.88f), new Vector2(0.52f, 0.928f), new Vector2(0.97f, 0.955f));
            UIFabrica.IconoJuntoATexto(lblHum, "Opcional. Déjala vacía para usar la humedad típica del material. Escríbela si el material venía " +
                                               "muy mojado o muy seco: cambia la humedad estimada de la pila y su balance de materia seca.");
            campoHumedad = UIFabrica.Campo(w, "CampoHumedad", new Vector2(0.22f, 0.775f), new Vector2(0.41f, 0.835f), "Típica: 80", false, 5, 20);
            Responsivo.En(campoHumedad, new Vector2(0.52f, 0.888f), new Vector2(0.97f, 0.924f));
            campoHumedad.onValueChanged.AddListener(_ => Previsualizar());
            // Sin CharacterValidation.Decimal: según la versión de uGUI rechaza la coma decimal. Se valida al guardar.

            // Material específico (verdes con tinte verde, cafés con tinte ámbar)
            var lblMat = Etiqueta(w, "LblMaterial", "Material", new Vector2(0.02f, 0.715f), new Vector2(0.42f, 0.755f), new Vector2(0.03f, 0.856f), new Vector2(0.97f, 0.882f));
            UIFabrica.IconoJuntoATexto(lblMat, "Verdes (restos de cocina, césped, posos de café, estiércol) aportan nitrógeno y humedad. " +
                                               "Cafés (hojarasca, paja, aserrín, cartón, papel) aportan carbono y absorben agua. " +
                                               "El equilibrio ideal es una relación C:N de 25 a 35.");
            int idx = 0;
            foreach (var d in Materiales.Todos)
            {
                var m = d.Id;
                int fila = idx / 5, col = idx % 5;
                float x0 = 0.03f + col * 0.0765f, top = 0.708f - fila * 0.066f;
                var b = UIFabrica.Boton(w, NombreCorto[m], new Vector2(x0, top - 0.06f), new Vector2(x0 + 0.072f, top), 14, UIFabrica.ColCard, () => ElegirMaterial(m));
                int filaC = idx / 3, colC = idx % 3;
                float x0C = 0.03f + colC * 0.317f, topC = 0.85f - filaC * 0.04f;
                Responsivo.En(b, new Vector2(x0C, topC - 0.036f), new Vector2(x0C + 0.307f, topC));
                var et = UIFabrica.Etiqueta(b); et.resizeTextForBestFit = true; et.resizeTextMinSize = 10; et.resizeTextMaxSize = 14;
                botonesMaterial.Add((m, b.GetComponent<Image>(), et));
                idx++;
            }
            txtInfoMaterial = UIFabrica.Texto(w, "InfoMaterial", new Vector2(0.02f, 0.52f), new Vector2(0.42f, 0.58f), 14, TextAnchor.UpperLeft, Tema.TextoSecundario);
            txtInfoMaterial.resizeTextForBestFit = true; txtInfoMaterial.resizeTextMinSize = 10; txtInfoMaterial.resizeTextMaxSize = 14;
            Responsivo.En(txtInfoMaterial, new Vector2(0.03f, 0.655f), new Vector2(0.97f, 0.695f));

            // Fecha (solo Control Manual) y nota
            var fecha = UIFabrica.Panel("GrupoFecha", w, new Vector2(0.02f, 0.415f), new Vector2(0.16f, 0.515f), new Color(0, 0, 0, 0), 0);
            Responsivo.En(fecha, new Vector2(0.03f, 0.59f), new Vector2(0.40f, 0.652f));
            grupoFecha = fecha.gameObject;
            var lblFecha = UIFabrica.Texto(fecha, "LblFecha", new Vector2(0f, 0.6f), Vector2.one, 16, TextAnchor.LowerLeft, Color.white);
            lblFecha.text = "Fecha (opcional)";
            lblFecha.resizeTextForBestFit = true; lblFecha.resizeTextMinSize = 10; lblFecha.resizeTextMaxSize = 16;
            UIFabrica.IconoJuntoATexto(lblFecha, "Vacía = ahora. Si la aportación fue otro día, escribe dd/mm/aaaa hh:mm. " +
                                                 "La fecha decide si recorre la fecha de madurez y en qué orden entra al balance.");
            campoFecha = UIFabrica.Campo(fecha, "Fecha", new Vector2(0.03f, 0f), new Vector2(1f, 0.58f), "Ahora", false, 16, 18);
            var lblNota = Etiqueta(w, "LblNota", "Nota (opcional)", new Vector2(0.16f, 0.475f), new Vector2(0.42f, 0.515f), new Vector2(0.43f, 0.628f), new Vector2(0.97f, 0.652f));
            campoNota = UIFabrica.Campo(w, "CampoNota", new Vector2(0.17f, 0.415f), new Vector2(0.41f, 0.47f), "Origen, estado del material…", false, ValidadorAporte.NotaMax, 18);
            Responsivo.En(campoNota, new Vector2(0.43f, 0.59f), new Vector2(0.97f, 0.625f));

            btnRegistrar = UIFabrica.Boton(w, "Registrar aportación", new Vector2(0.03f, 0.32f), new Vector2(0.41f, 0.395f), 22, UIFabrica.ColPrimario, Registrar);
            Responsivo.En(btnRegistrar, new Vector2(0.03f, 0.538f), new Vector2(0.97f, 0.58f));

            // Mismo lugar que "Registrar aportación" mientras se corrige una existente
            var edicion = UIFabrica.Panel("Edicion", w, new Vector2(0.03f, 0.32f), new Vector2(0.41f, 0.395f), new Color(0, 0, 0, 0), 0);
            Responsivo.En(edicion, new Vector2(0.03f, 0.538f), new Vector2(0.97f, 0.58f));
            var guardar = UIFabrica.Boton(edicion, "Guardar cambios", new Vector2(0f, 0f), new Vector2(0.64f, 1f), 20, UIFabrica.ColPrimario, Registrar);
            var eg = UIFabrica.Etiqueta(guardar); eg.resizeTextForBestFit = true; eg.resizeTextMinSize = 12; eg.resizeTextMaxSize = 20;
            var cancelar = UIFabrica.Boton(edicion, "Cancelar", new Vector2(0.66f, 0f), new Vector2(1f, 1f), 18, Tema.BotonNeutro, SalirDeEdicion);
            var ec = UIFabrica.Etiqueta(cancelar); ec.resizeTextForBestFit = true; ec.resizeTextMinSize = 11; ec.resizeTextMaxSize = 18;
            barraEdicion = edicion.gameObject;
            barraEdicion.SetActive(false);

            txtMensaje = UIFabrica.Texto(w, "Mensaje", new Vector2(0.02f, 0.13f), new Vector2(0.42f, 0.31f), 16, TextAnchor.UpperLeft, UIFabrica.ColError);
            txtMensaje.resizeTextForBestFit = true; txtMensaje.resizeTextMinSize = 11; txtMensaje.resizeTextMaxSize = 16;
            Responsivo.En(txtMensaje, new Vector2(0.03f, 0.49f), new Vector2(0.97f, 0.534f));

            // Lista (derecha en escritorio; abajo en móvil) con el balance de la mezcla en la cabecera
            var panelLista = UIFabrica.Panel("Lista", w, new Vector2(0.44f, 0.11f), new Vector2(0.98f, 0.89f), UIFabrica.ColCampo);
            Responsivo.En(panelLista, new Vector2(0.03f, 0.07f), new Vector2(0.97f, 0.485f));
            txtTotal = UIFabrica.Texto(panelLista, "Total", new Vector2(0f, 1f), new Vector2(1f, 1f), 16, TextAnchor.MiddleLeft, Color.white);
            txtTotal.rectTransform.offsetMin = new Vector2(12f, -70f); txtTotal.rectTransform.offsetMax = new Vector2(-12f, -4f);
            txtTotal.resizeTextForBestFit = true; txtTotal.resizeTextMinSize = 11; txtTotal.resizeTextMaxSize = 16;
            lista = new ListaDesplazable("Registros", panelLista, Vector2.zero, Vector2.one);
            lista.Vista.offsetMin = new Vector2(8f, 8f); lista.Vista.offsetMax = new Vector2(-8f, -74f);

            var cerrar = UIFabrica.Boton(w, "Cerrar", new Vector2(0.80f, 0.015f), new Vector2(0.98f, 0.09f), 22, UIFabrica.ColBoton, Cerrar);
            Responsivo.En(cerrar, new Vector2(0.55f, 0.008f), new Vector2(0.97f, 0.06f));
            ElegirMaterial(material);
            raiz.gameObject.SetActive(false);
        }

        static Text Etiqueta(Transform w, string nombre, string texto, Vector2 aMin, Vector2 aMax, Vector2 cMin, Vector2 cMax)
        {
            var t = UIFabrica.Texto(w, nombre, aMin, aMax, 18, TextAnchor.LowerLeft, Color.white);
            t.text = texto;
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 11; t.resizeTextMaxSize = 18;
            Responsivo.En(t, cMin, cMax);
            return t;
        }
    }
}
