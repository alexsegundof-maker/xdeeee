using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Seguridad;
using Composta.Core.Textos;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Composta.App
{
    // Historial de compostas realizadas: tarjetas por lote (activos, en maduración, cosechados, simulaciones),
    // búsqueda y filtros, comparación rápida de 2–3 lotes y una vista de auditoría completa por lote
    // con descarga de su bitácora. También descarga la bitácora consolidada y carga o quita los datos DEMO.
    public sealed class PanelHistorial : MonoBehaviour
    {
        const int MaxComparar = 3;
        static readonly CultureInfo C = CultureInfo.InvariantCulture;
        static readonly Color32[] ColoresComparar = { new Color32(229, 83, 75, 255), new Color32(76, 141, 255, 255), new Color32(240, 162, 74, 255) };

        enum Filtro { Todos, Activos, Maduracion, Cosechados, Simulaciones }
        static readonly string[] NombresFiltro = { "Todos", "Activos", "En maduración", "Cosechados", "Simulaciones" };

        // Datos derivados de un lote (todo se calcula, nada se guarda aparte)
        sealed class Ficha
        {
            public Lote L;
            public BalanceMezcla B;
            public EstimacionMaduracion M;
            public ResultadoCosecha R;
            public string Estado;
            public Color ColorEstado;
            public int Rango;                       // orden: activos primero
            public List<(float x, float v)> Temp = new List<(float, float)>();
            public List<(float x, float v)> Hum = new List<(float, float)>();
            public float TempMax = float.NaN;
        }

        GestorLotes gestor;
        Func<Permiso, bool> puede;
        readonly List<Ficha> fichas = new List<Ficha>();
        readonly HashSet<string> seleccion = new HashSet<string>();
        readonly Dictionary<string, Historial> historiales = new Dictionary<string, Historial>();
        readonly List<Texture2D> texturas = new List<Texture2D>();
        Filtro filtro = Filtro.Todos;
        char filtroCalidad = '*';
        string aviso;

        RectTransform raiz, raizDetalle, raizComparar;
        Text txtResumen, txtComparar, lblCalidad, lblDemo;
        InputField campoBuscar;
        Button btnNueva, btnCerrar, btnComparar, btnDemo;
        readonly Image[] fondoFiltro = new Image[5];
        readonly Text[] textoFiltro = new Text[5];
        CuadriculaDesplazable cuadricula;
        bool layoutCompacto;

        // Detalle
        Text dTitulo, dSub, dKpis, dLeyenda;
        RawImage dGrafica;
        ListaDesplazable dLinea;
        Button dDescargar, dAbrir, dAnular;
        Ficha fichaDetalle;

        // Comparación
        Text cLeyenda;
        RawImage cGrafica;
        RectTransform cColumnas;

        public bool Abierto => raiz != null && raiz.gameObject.activeSelf;
        public event Action PedirNueva;
        public event Action<Lote> PedirAbrir;
        public event Action AlCambiarDatos;              // se quitaron lotes demo, se anuló una cosecha...
        public Func<bool> HayLoteAbierto = () => false;

        public void Abrir(GestorLotes g, Func<Permiso, bool> permisos) => Abrir(g, permisos, null);

        void Abrir(GestorLotes g, Func<Permiso, bool> permisos, Filtro? inicial)
        {
            gestor = g;
            puede = permisos;
            Asegurar();
            if (inicial.HasValue) filtro = inicial.Value;
            raiz.gameObject.SetActive(true);
            GuardiaEntrada.Bloquear(this);
            Recargar();
            cuadricula.IrAlInicio();
        }

        public void AbrirActivos(GestorLotes g, Func<Permiso, bool> permisos) => Abrir(g, permisos, Filtro.Activos);

        public void Cerrar()
        {
            if (raiz == null) return;
            CerrarDetalle();
            CerrarComparacion();
            raiz.gameObject.SetActive(false);
            GuardiaEntrada.Liberar(this);
            LiberarTexturas();
        }

        void OnEnable() => AdaptadorPantalla.AlCambiar += AlCambiarPantalla;
        void OnDisable() => AdaptadorPantalla.AlCambiar -= AlCambiarPantalla;

        void OnDestroy()
        {
            GuardiaEntrada.Liberar(this);
            LiberarTexturas();
        }

        void AlCambiarPantalla()
        {
            if (!Abierto || layoutCompacto == AdaptadorPantalla.Compacto) return;
            Pintar();
            if (fichaDetalle != null && raizDetalle.gameObject.activeSelf) MostrarDetalle(fichaDetalle);
        }

        void LiberarTexturas()
        {
            foreach (var t in texturas) if (t != null) Destroy(t);
            texturas.Clear();
        }

        void Update()
        {
            if (!Abierto || Notificador.DialogoAbierto) return;
            var k = Keyboard.current;
            if (k == null || !k.escapeKey.wasPressedThisFrame) return;
            if (raizDetalle.gameObject.activeSelf) CerrarDetalle();
            else if (raizComparar.gameObject.activeSelf) CerrarComparacion();
            else if (HayLoteAbierto()) Cerrar();
        }

        // ---------------- Datos ----------------
        void Recargar()
        {
            fichas.Clear();
            historiales.Clear();
            var ahora = gestor.AhoraUtc;
            foreach (var l in gestor.ListarLotes(out aviso)) fichas.Add(CrearFicha(l, ahora));
            fichas.Sort((a, b) => a.Rango != b.Rango ? a.Rango.CompareTo(b.Rango) : b.L.ActualizadoUtc.CompareTo(a.L.ActualizadoUtc));
            seleccion.RemoveWhere(id => !fichas.Exists(f => f.L.Id == id));
            Pintar();
        }

        static Ficha CrearFicha(Lote l, DateTime ahora)
        {
            var f = new Ficha { L = l, B = BalanceLote.Calcular(l), M = Maduracion.Estimar(l, ahora) };
            if (l.TieneCosecha) f.R = EvaluacionCosecha.Evaluar(l);
            if (l.TieneCosecha) { f.Estado = "COSECHADO"; f.ColorEstado = Tema.Exito; f.Rango = 3; }
            else if (l.Estado != EstadoLote.Activo) { f.Estado = "ARCHIVADO"; f.ColorEstado = Tema.TextoTenue; f.Rango = 4; }
            else if (l.Modo == ModoLote.Simular) { f.Estado = "SIMULACIÓN"; f.ColorEstado = Tema.AcentoSimulacion; f.Rango = 2; }
            else if (l.BloqueadoMaduracion) { f.Estado = "EN MADURACIÓN"; f.ColorEstado = Tema.Aviso; f.Rango = 1; }
            else { f.Estado = "ACTIVO"; f.ColorEstado = Tema.AcentoManual; f.Rango = 0; }
            var inicio = f.M.InicioUtc.Ticks;
            foreach (var r in AnalisisCampo.Ordenados(l))
            {
                float dia = (float)new TimeSpan(r.FechaUtc - inicio).TotalDays;
                f.Temp.Add((dia, r.Temperatura));
                f.Hum.Add((dia, r.Humedad));
                f.TempMax = float.IsNaN(f.TempMax) ? r.Temperatura : Math.Max(f.TempMax, r.Temperatura);
            }
            return f;
        }

        // Simulaciones: su curva sale de reconstruir la bitácora (una vez por apertura del historial)
        void CompletarSimulacion(Ficha f)
        {
            if (f.L.Modo != ModoLote.Simular || f.Temp.Count > 0) return;
            if (!historiales.TryGetValue(f.L.Id, out var h))
            {
                try
                {
                    var copia = EditorLote.Clonar(f.L);
                    ReproductorLote.Sanear(copia);
                    h = ReproductorLote.Reconstruir(copia, out _).Historial;
                }
                catch (Exception e) { Debug.LogException(e); h = null; }
                historiales[f.L.Id] = h;
            }
            if (h == null) return;
            for (int i = 0; i < h.Reales.Count; i += 12)
            {
                var r = h.Reales[i];
                f.Temp.Add((r.Dias, r.Temperatura));
                f.Hum.Add((r.Dias, r.Humedad));
                f.TempMax = float.IsNaN(f.TempMax) ? r.Temperatura : Math.Max(f.TempMax, r.Temperatura);
            }
        }

        bool Coincide(Ficha f)
        {
            switch (filtro)
            {
                case Filtro.Activos: if (f.Rango != 0) return false; break;
                case Filtro.Maduracion: if (f.Rango != 1) return false; break;
                case Filtro.Cosechados: if (f.Rango != 3) return false; break;
                case Filtro.Simulaciones: if (f.L.Modo != ModoLote.Simular) return false; break;
            }
            if (filtroCalidad != '*' && (f.R == null || f.R.Calidad != filtroCalidad)) return false;
            string q = (campoBuscar.text ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return true;
            string texto = ($"{f.L.Etiqueta} {f.L.Nombre} {f.L.Compostera} {Nombres.Tipo(f.L.Tipo)} {f.Estado} " +
                            (f.L.Numero > 0 ? "#" + f.L.Numero.ToString("000", C) : "")).ToLowerInvariant();
            return texto.Contains(q);
        }

        // ---------------- Vista principal ----------------
        void Pintar()
        {
            if (raiz == null) return;
            layoutCompacto = AdaptadorPantalla.Compacto;
            LiberarTexturas();
            cuadricula.Limpiar();
            for (int i = 0; i < fondoFiltro.Length; i++)
            {
                bool sel = (int)filtro == i;
                fondoFiltro[i].color = sel ? UIFabrica.ColSel : UIFabrica.ColCard;
                textoFiltro[i].color = sel ? Color.white : Tema.TextoSecundario;
            }
            lblCalidad.text = filtroCalidad == '*' ? "Calidad: todas" : "Calidad: " + filtroCalidad;
            bool hayDemo = fichas.Exists(f => f.L.Demo);
            lblDemo.text = hayDemo ? "Quitar datos DEMO" : "Cargar datos DEMO";
            btnDemo.interactable = puede(Permiso.CrearLote);
            btnNueva.interactable = puede(Permiso.CrearLote);
            btnCerrar.interactable = HayLoteAbierto();

            int activos = fichas.FindAll(f => f.Rango <= 2).Count, cosechados = 0;
            float sumaRend = 0f;
            foreach (var f in fichas) if (f.R != null) { cosechados++; sumaRend += f.R.RendimientoPct; }
            txtResumen.text = string.Format(C, "{0} {1} · {2} en curso · {3} {4}{5}", fichas.Count, fichas.Count == 1 ? "lote" : "lotes", activos,
                                            cosechados, cosechados == 1 ? "cosechado" : "cosechados",
                                            cosechados > 0 ? string.Format(C, " · rendimiento medio {0:F0} %", sumaRend / cosechados) : "") +
                              (aviso != null ? $"  <color={Tema.HexDe(Tema.Aviso)}>· {aviso}</color>" : "");

            int visibles = 0;
            foreach (var f in fichas) if (Coincide(f)) { TarjetaLote(f); visibles++; }
            if (fichas.Count == 0)
                cuadricula.Aviso("Todavía no hay compostas guardadas.\nCrea una con «Nueva composta» o carga los datos de demostración para explorar el historial.");
            else if (visibles == 0) cuadricula.Aviso("Ningún lote coincide con la búsqueda o los filtros.");
            ActualizarBarraComparar();
        }

        void TarjetaLote(Ficha f)
        {
            var l = f.L;
            bool sel = seleccion.Contains(l.Id);
            var t = cuadricula.Tarjeta(sel ? Color.Lerp(Tema.SuperficieAlta, Tema.AcentoPrediccion, 0.14f) : Tema.Superficie);
            if (sel)
            {
                var borde = t.gameObject.AddComponent<Outline>();
                borde.effectColor = Tema.AcentoPrediccion;
                borde.effectDistance = new Vector2(2f, -2f);
            }
            var titulo = UIFabrica.Texto(t, "Titulo", new Vector2(0.03f, 0.865f), new Vector2(0.66f, 0.975f), 17, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            titulo.text = "<b>" + UIFabrica.SinRich(l.Etiqueta) + "</b>";
            titulo.resizeTextForBestFit = true; titulo.resizeTextMinSize = 11; titulo.resizeTextMaxSize = 17;
            UIFabrica.Pildora(t, "Estado", new Vector2(0.66f, 0.88f), new Vector2(0.97f, 0.965f), f.Estado, Tema.ConAlfa(f.ColorEstado, 0.18f), f.ColorEstado, 12);
            if (l.Demo) UIFabrica.Pildora(t, "Demo", new Vector2(0.76f, 0.795f), new Vector2(0.97f, 0.87f), "DEMO", Tema.ConAlfa(Tema.AcentoPrediccion, 0.2f), Tema.AcentoPrediccion, 11);
            var sub = UIFabrica.Texto(t, "Sub", new Vector2(0.03f, 0.79f), new Vector2(l.Demo ? 0.75f : 0.97f, 0.87f), 13, TextAnchor.MiddleLeft, Tema.TextoTenue);
            sub.text = UIFabrica.SinRich(Nombres.Tipo(l.Tipo)) + " · " + Nombres.Modo(l.Modo).ToLowerInvariant();
            sub.resizeTextForBestFit = true; sub.resizeTextMinSize = 9; sub.resizeTextMaxSize = 13;

            // Mini curva de temperatura medida (Control Manual) o, en simulaciones, texto
            var img = Imagen(t, new Vector2(0.03f, 0.53f), new Vector2(0.97f, 0.78f));
            if (f.Temp.Count > 0)
            {
                var tex = DibujanteGrafica.DibujarSeries(new List<(IList<(float, float)>, Color32)> { (f.Temp, DibujanteGrafica.ColorDe(Magnitud.Temperatura)) },
                                                         0f, 75f, (45f, 65f), 360, 90, null, false);
                texturas.Add(tex);
                img.texture = tex; img.color = Color.white;
            }
            else
            {
                img.color = Tema.SuperficieCampo;
                var sin = UIFabrica.Texto(img.transform, "Sin", Vector2.zero, Vector2.one, 13, TextAnchor.MiddleCenter, Tema.TextoTenue);
                sin.text = l.Modo == ModoLote.Simular ? "Simulación: abre el detalle para ver su curva" : "Sin mediciones todavía";
            }

            var kpi = UIFabrica.Texto(t, "Kpis", new Vector2(0.03f, 0.17f), new Vector2(0.97f, 0.525f), 14, TextAnchor.UpperLeft, Tema.TextoSecundario);
            kpi.resizeTextForBestFit = true; kpi.resizeTextMinSize = 10; kpi.resizeTextMaxSize = 14;
            kpi.text = LineasTarjeta(f);

            var detalle = BotonTarjeta(t, "Ver detalle", new Vector2(0.03f, 0.03f), new Vector2(0.355f, 0.15f), Tema.BotonNeutro, () => MostrarDetalle(f));
            if (l.Estado == EstadoLote.Activo)
                BotonTarjeta(t, "Abrir", new Vector2(0.365f, 0.03f), new Vector2(0.635f, 0.15f), Tema.ConAlfa(f.ColorEstado, 0.85f), () => AbrirLote(f.L));
            BotonTarjeta(t, sel ? "✔ Comparar" : "Comparar", new Vector2(0.645f, 0.03f), new Vector2(0.97f, 0.15f),
                         sel ? Tema.ConAlfa(Tema.AcentoPrediccion, 0.85f) : Tema.BotonNeutro, () => AlternarSeleccion(f.L.Id));
            _ = detalle;
        }

        string LineasTarjeta(Ficha f)
        {
            var l = f.L;
            var zona = gestor.Zona;
            string ini = Fecha(f.M.InicioUtc.Ticks, zona);
            string b = Tema.HexDe(Tema.TextoPrimario);
            var sb = new StringBuilder();
            if (f.R != null)
            {
                string col = Tema.HexDe(f.R.Calidad == 'A' ? Tema.Exito : f.R.Calidad == 'B' ? Tema.Aviso : Tema.Critico);
                sb.AppendFormat(C, "{0} → {1} · <color={2}>{3:F0} días</color>\n", ini, Fecha(l.Cosecha.FechaUtc, zona), b, f.R.DuracionDias);
                sb.AppendFormat(C, "Entrada <color={0}>{1:0.#} kg</color> → cosechado <color={0}>{2:0.#} kg</color>\n", b, f.R.PesoEntradaKg, l.Cosecha.PesoKg);
                sb.AppendFormat(C, "Rendimiento <color={0}><b>{1:F0} %</b></color> · calidad <color={2}><b>{3}</b></color>", b, f.R.RendimientoPct, col, f.R.Calidad);
            }
            else
            {
                float dias = (float)(gestor.AhoraUtc - f.M.InicioUtc).TotalDays;
                sb.AppendFormat(C, "Inicio {0} · día {1:F0}\n", ini, Math.Max(0f, l.Modo == ModoLote.Simular ? l.DiasSimulados : dias));
                sb.AppendFormat(C, "Entrada <color={0}>{1:0.#} kg</color> · {2} aport. · C:N {3}\n", b, f.B.PesoEntradaKg, l.NumeroAportaciones, CN(f.B));
                if (l.Modo == ModoLote.Manual && l.Estado == EstadoLote.Activo)
                    sb.AppendFormat(C, "Madurez estimada {0} ({1})", Fecha(f.M.FechaMadurezUtc.Ticks, zona),
                                    f.M.DiasRestantes < 1f ? "ya debería estar" : $"faltan {Math.Ceiling(f.M.DiasRestantes):F0} d");
                else sb.Append(l.Modo == ModoLote.Simular ? "Simulación" : "Archivado sin cosecha");
            }
            return sb.ToString();
        }

        static string CN(BalanceMezcla b)
        {
            if (!b.TieneDatos) return "—";
            var col = b.Nivel == NivelCN.Optima ? Tema.Exito : b.Nivel == NivelCN.ExcesoNitrogeno || b.Nivel == NivelCN.ExcesoCarbono ? Tema.Critico : Tema.Aviso;
            return $"<color={Tema.HexDe(col)}><b>{b.RelacionCN.ToString("F0", C)}</b></color>";
        }

        static string Fecha(long ticks, TimeZoneInfo zona)
        {
            string f = ValidadorFechaMedicion.Formatear(ticks, zona);
            return f.Length >= 10 ? f.Substring(0, 10) : f;
        }

        Button BotonTarjeta(Transform padre, string texto, Vector2 aMin, Vector2 aMax, Color color, Action accion)
        {
            var b = UIFabrica.Boton(padre, texto, aMin, aMax, 15, color, () => accion());
            var e = UIFabrica.Etiqueta(b); e.resizeTextForBestFit = true; e.resizeTextMinSize = 10; e.resizeTextMaxSize = 15;
            return b;
        }

        static RawImage Imagen(Transform padre, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject("Grafica", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = new Vector2(6, 2); rt.offsetMax = new Vector2(-6, -2);
            var img = go.GetComponent<RawImage>();
            img.color = Color.clear;
            img.raycastTarget = false;
            return img;
        }

        void AbrirLote(Lote l)
        {
            CerrarDetalle();
            Cerrar();
            PedirAbrir?.Invoke(l);
        }

        void AlternarSeleccion(string id)
        {
            if (seleccion.Contains(id)) seleccion.Remove(id);
            else if (seleccion.Count >= MaxComparar) { Notificador.Mostrar($"Puedes comparar hasta {MaxComparar} lotes a la vez.", TipoMensaje.Info); return; }
            else seleccion.Add(id);
            Pintar();
        }

        void ActualizarBarraComparar()
        {
            int n = seleccion.Count;
            btnComparar.interactable = n >= 2;
            UIFabrica.Etiqueta(btnComparar).text = n >= 2 ? $"Comparar ({n})" : "Comparar";
            txtComparar.text = n == 0 ? "Marca 2 o 3 lotes con «Comparar» para verlos lado a lado."
                             : n == 1 ? "Marca al menos un lote más para comparar."
                             : $"{n} lotes seleccionados.";
        }

        // ---------------- Acciones de la cabecera ----------------
        void Consolidada()
        {
            if (!gestor.ExportarConsolidado(out var r, out string error)) { Notificador.Mostrar("No se pudo exportar: " + error, TipoMensaje.Error); return; }
            ControlAcceso.MostrarExportacion("Bitácora consolidada lista", r);
        }

        void Demo()
        {
            bool hayDemo = fichas.Exists(f => f.L.Demo);
            if (!hayDemo)
            {
                Notificador.Dialogo("Cargar datos de demostración",
                    "Se agregarán <b>7 lotes de ejemplo</b> marcados como <b>DEMO</b>: 3 composteras activas (una con exceso de nitrógeno, " +
                    "una en fase termófila y una estabilizada) y 4 cosechadas con distinta calidad.\n\n" +
                    "Aparecen en el historial y en la bitácora consolidada con la marca DEMO, y se quitan de un solo toque.",
                    new OpcionDialogo("Cancelar"),
                    new OpcionDialogo("Cargar DEMO", () =>
                    {
                        bool ok = gestor.CargarDemo(out string msg);
                        Notificador.Mostrar(msg, ok ? TipoMensaje.Exito : TipoMensaje.Error);
                        filtro = Filtro.Todos;
                        Recargar();
                        AlCambiarDatos?.Invoke();
                    }, true));
                return;
            }
            Notificador.Dialogo("Quitar datos de demostración",
                "Se quitarán todos los lotes marcados como DEMO (se mueven a la papelera de la carpeta de datos). Tus lotes reales no cambian.",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Quitar DEMO", () =>
                {
                    bool ok = gestor.QuitarDemo(out string msg);
                    Notificador.Mostrar(msg, ok ? TipoMensaje.Exito : TipoMensaje.Error);
                    Recargar();
                    AlCambiarDatos?.Invoke();
                }, true));
        }

        void CambiarCalidad()
        {
            filtroCalidad = filtroCalidad == '*' ? 'A' : filtroCalidad == 'A' ? 'B' : filtroCalidad == 'B' ? 'C' : '*';
            Pintar();
        }

        // ---------------- Detalle (auditoría completa de un lote) ----------------
        void MostrarDetalle(Ficha f)
        {
            fichaDetalle = f;
            CompletarSimulacion(f);
            var l = f.L;
            var zona = gestor.Zona;
            raizDetalle.gameObject.SetActive(true);
            dTitulo.text = $"<b>{UIFabrica.SinRich(l.Etiqueta)}</b>  <color={Tema.HexDe(f.ColorEstado)}>{f.Estado}</color>" +
                           (l.Demo ? $"  <color={Tema.HexDe(Tema.AcentoPrediccion)}>DEMO</color>" : "");
            dSub.text = $"{UIFabrica.SinRich(l.Nombre)} · {Nombres.Tipo(l.Tipo)} · {Nombres.Modo(l.Modo).ToLowerInvariant()} · creado por {UIFabrica.SinRich(l.CreadoPor)}";
            dKpis.text = TextoKpis(f, zona);
            dAbrir.gameObject.SetActive(l.Estado == EstadoLote.Activo);
            dAnular.gameObject.SetActive(l.TieneCosecha && puede(Permiso.EditarRegistros));
            dDescargar.interactable = puede(Permiso.Exportar);

            var series = new List<(IList<(float, float)>, Color32)>
            {
                (f.Temp, DibujanteGrafica.ColorDe(Magnitud.Temperatura)),
                (f.Hum, DibujanteGrafica.ColorDe(Magnitud.Humedad))
            };
            var tex = DibujanteGrafica.DibujarSeries(series, 0f, 100f, null, 720, 300, null, l.Modo == ModoLote.Manual);
            texturas.Add(tex);
            dGrafica.texture = tex; dGrafica.color = Color.white;
            dLeyenda.text = $"<color={Tema.HexDe(DibujanteGrafica.ColorDe(Magnitud.Temperatura))}>●</color> Temperatura (°C)   " +
                            $"<color={Tema.HexDe(DibujanteGrafica.ColorDe(Magnitud.Humedad))}>●</color> Humedad (%)   " +
                            $"<color={Tema.HexDe(Tema.TextoTenue)}>eje: días desde el inicio · escala 0–100</color>";
            if (f.Temp.Count == 0) dLeyenda.text = "Sin mediciones: todavía no hay curva de temperatura y humedad.";
            LineaDeTiempo(f, zona);
        }

        string TextoKpis(Ficha f, TimeZoneInfo zona)
        {
            var l = f.L;
            var b = f.B;
            string tenue = Tema.HexDe(Tema.TextoSecundario);
            var sb = new StringBuilder();
            void Fila(string k, string v) => sb.Append($"<color={tenue}>{k}:</color> {v}\n");
            Fila("Inicio", Fecha(f.M.InicioUtc.Ticks, zona));
            if (f.R != null)
            {
                Fila("Cosecha", $"{Fecha(l.Cosecha.FechaUtc, zona)} · {f.R.DuracionDias.ToString("F0", C)} días");
                Fila("Entrada total", $"<b>{f.R.PesoEntradaKg.ToString("0.##", C)} kg</b> → cosechado <b>{l.Cosecha.PesoKg.ToString("0.##", C)} kg</b>");
                Fila("Rendimiento", $"<b>{f.R.RendimientoPct.ToString("F1", C)} %</b> (esperado {EvaluacionCosecha.RendimientoMin:F0}–{EvaluacionCosecha.RendimientoMax:F0} %)");
                Fila("Calidad", $"<b>{EvaluacionCosecha.DescribirCalidad(f.R.Calidad)}</b> · {f.R.Puntos} de {f.R.PuntosMaximos} criterios");
                foreach (var cr in f.R.Criterios)
                    sb.Append($"   <color={Tema.HexDe(cr.Cumple ? Tema.Exito : Tema.Critico)}>{(cr.Cumple ? "✔" : "✘")}</color> {cr.Nombre} <color={tenue}>· {cr.Detalle}</color>\n");
            }
            else
            {
                Fila("Entrada total", $"<b>{b.PesoEntradaKg.ToString("0.##", C)} kg</b> (carga {b.CargaBaseKg.ToString("0.##", C)} + {b.AportadoKg.ToString("0.##", C)} kg aportados)");
                if (l.Modo == ModoLote.Manual && l.Estado == EstadoLote.Activo)
                    Fila("Madurez estimada", $"{Fecha(f.M.FechaMadurezUtc.Ticks, zona)} ({(f.M.DiasRestantes < 1f ? "ya debería estar madura" : "faltan " + Math.Ceiling(f.M.DiasRestantes).ToString("F0", C) + " días")})" +
                                             (f.M.Reajustada ? $" <color={tenue}>· recorrida por una aportación</color>" : ""));
            }
            Fila("Balance C:N", b.TieneDatos ? $"{CN(b)} ({BalanceLote.NombreNivel(b.Nivel).ToLowerInvariant()}) · humedad de la mezcla {b.HumedadMezclaPct.ToString("F0", C)} %" : "sin datos");
            Fila("Registros", $"{l.Informes.Count} mediciones · {l.NumeroAportaciones} aportaciones · {l.Cambios.Count} cambios auditados" +
                              (float.IsNaN(f.TempMax) ? "" : $" · temperatura máxima {f.TempMax.ToString("F0", C)} °C"));
            if (l.Modo == ModoLote.Manual && l.Estado == EstadoLote.Activo)
            {
                var e = AnalisisCampo.Analizar(l, gestor.AhoraUtc);
                if (e.Recomendaciones.Count > 0)
                    Fila("Recomendación", $"<b>{UIFabrica.SinRich(e.Recomendaciones[0].Titulo)}</b> <color={tenue}>· {UIFabrica.SinRich(e.Recomendaciones[0].Detalle)}</color>");
            }
            return sb.ToString();
        }

        void LineaDeTiempo(Ficha f, TimeZoneInfo zona)
        {
            var l = f.L;
            dLinea.Limpiar();
            var eventos = new List<(long t, Color c, string texto)>();
            foreach (var a in BalanceLote.AportesOrdenados(l))
            {
                var d = Materiales.De(Materiales.Resolver(a));
                eventos.Add((a.FechaUtc, Tema.AcentoManual, $"<b>{(a.EsCargaInicial ? "Carga inicial" : "Aportación")}</b> · {a.Kg.ToString("0.##", C)} kg de {d.Nombre.ToLowerInvariant()} " +
                                                          $"({Materiales.HumedadDe(a).ToString("F0", C)} % humedad) · {UIFabrica.SinRich(a.Usuario)}"));
            }
            foreach (var r in AnalisisCampo.Ordenados(l))
                eventos.Add((r.FechaUtc, Tema.AcentoSimulacion, $"<b>Medición</b> · {r.Temperatura.ToString("F1", C)} °C · {r.Humedad.ToString("F0", C)} % · {r.PesoKg.ToString("0.##", C)} kg · {UIFabrica.SinRich(r.Usuario)}" +
                                                                (string.IsNullOrEmpty(r.Nota) ? "" : $" · <i>{UIFabrica.SinRich(r.Nota)}</i>")));
            foreach (var c in l.Comandos ?? new List<Comando>())
                if (c != null && c.Tipo != TipoComando.Aporte && c.Tipo != TipoComando.ProbFalla)
                    eventos.Add((c.FechaUtc, Tema.TextoSecundario, $"<b>{Nombres.Comando(c.Tipo)}</b> · día simulado {(c.Tick / 24f).ToString("F1", C)} · {Nombres.DetalleComando(c)} · {UIFabrica.SinRich(c.Usuario)}"));
            foreach (var c in l.Cambios ?? new List<CambioLote>())
                if (c != null && c.Accion != AccionCambio.Creacion)
                    eventos.Add((c.FechaUtc, c.Accion == AccionCambio.Eliminacion ? Tema.Critico : Tema.Aviso,
                                 $"<b>{Capital(Nombres.Accion(c.Accion))} de {Nombres.Entidad(c.Entidad)}</b> · {UIFabrica.SinRich(c.Usuario)} · {UIFabrica.SinRich(c.Detalle)}"));
            eventos.Sort((x, y) => y.t.CompareTo(x.t));
            if (eventos.Count == 0) { dLinea.Aviso("Sin eventos registrados.", 70f); return; }
            string tenue = Tema.HexDe(Tema.TextoTenue);
            int n = 0;
            foreach (var ev in eventos)
            {
                if (n++ >= 400) { dLinea.Aviso("Se muestran los 400 eventos más recientes. La bitácora en Excel incluye todos.", 60f); break; }
                var fila = dLinea.Fila(layoutCompacto ? 96f : 58f, Tema.SuperficieAlta);
                var franja = UIFabrica.Panel("Color", fila, Vector2.zero, new Vector2(0f, 1f), ev.c, 2);
                franja.offsetMax = new Vector2(5f, 0f);
                var t = UIFabrica.Texto(fila, "Texto", Vector2.zero, Vector2.one, 14, TextAnchor.MiddleLeft, Tema.TextoPrimario);
                t.rectTransform.offsetMin = new Vector2(16f, 2f);
                t.text = $"<color={tenue}>{ValidadorFechaMedicion.Formatear(ev.t, zona)}</color>  {ev.texto}";
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = 14;
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }
            dLinea.IrAlInicio();
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        void CerrarDetalle()
        {
            if (raizDetalle != null) raizDetalle.gameObject.SetActive(false);
            fichaDetalle = null;
        }

        void DescargarDetalle()
        {
            if (fichaDetalle == null) return;
            if (!gestor.ExportarLote(fichaDetalle.L, out var r, out string error)) { Notificador.Mostrar("No se pudo exportar: " + error, TipoMensaje.Error); return; }
            ControlAcceso.MostrarExportacion("Bitácora del lote lista", r);
        }

        void AnularCosecha()
        {
            var f = fichaDetalle;
            if (f == null) return;
            Notificador.Dialogo("¿Anular la cosecha?",
                $"<b>{UIFabrica.SinRich(f.L.Etiqueta)}</b> volverá a estar activo, en maduración, y se borrarán el peso cosechado y la calidad. " +
                "La anulación queda en el historial de cambios.",
                new OpcionDialogo("Cancelar"),
                new OpcionDialogo("Anular cosecha", () =>
                {
                    bool ok = gestor.AnularCosecha(f.L, out string msg);
                    Notificador.Mostrar(msg, ok ? TipoMensaje.Exito : TipoMensaje.Error);
                    if (!ok) return;
                    CerrarDetalle();
                    Recargar();
                    AlCambiarDatos?.Invoke();
                }, true));
        }

        // ---------------- Comparación rápida ----------------
        void MostrarComparacion()
        {
            var elegidas = fichas.FindAll(f => seleccion.Contains(f.L.Id));
            if (elegidas.Count < 2) return;
            raizComparar.gameObject.SetActive(true);
            var zona = gestor.Zona;
            var series = new List<(IList<(float, float)>, Color32)>();
            var ley = new StringBuilder();
            for (int i = 0; i < elegidas.Count; i++)
            {
                CompletarSimulacion(elegidas[i]);
                series.Add((elegidas[i].Temp, ColoresComparar[i]));
                ley.Append($"<color={Tema.HexDe(ColoresComparar[i])}>●</color> {UIFabrica.SinRich(elegidas[i].L.Etiqueta)}    ");
            }
            var tex = DibujanteGrafica.DibujarSeries(series, 0f, 80f, (45f, 65f), 900, 300, null, true);
            texturas.Add(tex);
            cGrafica.texture = tex; cGrafica.color = Color.white;
            cLeyenda.text = ley + $"<color={Tema.HexDe(Tema.TextoTenue)}>· temperatura (°C) por días desde el inicio; franja verde = 45–65 °C</color>";

            for (int i = cColumnas.childCount - 1; i >= 0; i--) UIFabrica.Destruir(cColumnas.GetChild(i).gameObject);
            bool compacto = AdaptadorPantalla.Compacto;
            for (int i = 0; i < elegidas.Count; i++)
            {
                var f = elegidas[i];
                Vector2 aMin, aMax;
                if (compacto) { float alto = 1f / elegidas.Count; aMin = new Vector2(0f, 1f - (i + 1) * alto); aMax = new Vector2(1f, 1f - i * alto); }
                else { float ancho = 1f / elegidas.Count; aMin = new Vector2(i * ancho, 0f); aMax = new Vector2((i + 1) * ancho, 1f); }
                var col = UIFabrica.Tarjeta("Columna", cColumnas, aMin, aMax, Tema.Superficie);
                col.offsetMin += new Vector2(6f, 6f); col.offsetMax -= new Vector2(6f, 6f);
                var franja = UIFabrica.Panel("Color", col, new Vector2(0f, 1f), Vector2.one, ColoresComparar[i], 3);
                franja.offsetMin = new Vector2(0f, -6f);
                var t = UIFabrica.Texto(col, "Datos", Vector2.zero, Vector2.one, 15, TextAnchor.UpperLeft, Tema.TextoPrimario);
                t.rectTransform.offsetMin = new Vector2(14f, 8f); t.rectTransform.offsetMax = new Vector2(-14f, -14f);
                t.resizeTextForBestFit = true; t.resizeTextMinSize = 10; t.resizeTextMaxSize = 15;
                t.text = TextoComparacion(f, zona);
            }
        }

        string TextoComparacion(Ficha f, TimeZoneInfo zona)
        {
            var l = f.L;
            string tenue = Tema.HexDe(Tema.TextoSecundario);
            var sb = new StringBuilder();
            sb.Append($"<b>{UIFabrica.SinRich(l.Etiqueta)}</b>  <color={Tema.HexDe(f.ColorEstado)}>{f.Estado}</color>{(l.Demo ? "  DEMO" : "")}\n");
            sb.Append($"<color={tenue}>{Nombres.Tipo(l.Tipo)}</color>\n\n");
            void Fila(string k, string v) => sb.Append($"<color={tenue}>{k}</color>  {v}\n");
            Fila("Inicio", Fecha(f.M.InicioUtc.Ticks, zona));
            Fila("Duración", f.R != null ? f.R.DuracionDias.ToString("F0", C) + " días" : "en curso");
            Fila("Entrada total", (f.R != null ? f.R.PesoEntradaKg : f.B.PesoEntradaKg).ToString("0.#", C) + " kg");
            Fila("Aportaciones", $"{l.NumeroAportaciones} ({f.B.AportadoKg.ToString("0.#", C)} kg)");
            Fila("C:N de la mezcla", CN(f.B));
            Fila("Temperatura máx.", float.IsNaN(f.TempMax) ? "—" : f.TempMax.ToString("F0", C) + " °C");
            Fila("Peso cosechado", f.R != null ? l.Cosecha.PesoKg.ToString("0.#", C) + " kg" : "—");
            Fila("Rendimiento", f.R != null ? "<b>" + f.R.RendimientoPct.ToString("F0", C) + " %</b>" : "—");
            Fila("Calidad", f.R != null ? $"<b>{f.R.Calidad}</b> ({f.R.Puntos}/{f.R.PuntosMaximos})" : "—");
            return sb.ToString();
        }

        void CerrarComparacion()
        {
            if (raizComparar != null) raizComparar.gameObject.SetActive(false);
        }

        // ---------------- Construcción ----------------
        void Asegurar()
        {
            if (raiz != null) return;
            // Debajo de la barra de sesión (20) y de «+ Nueva composta» (25): siempre se puede salir o crear un lote
            raiz = UIFabrica.Canvas("Canvas Historial", transform, 15);
            UIFabrica.Panel("Fondo", raiz, Vector2.zero, Vector2.one, Tema.Fondo);
            var w = UIFabrica.Panel("Marco", raiz, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.93f), new Color(0, 0, 0, 0), 0);
            Responsivo.En(w, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -136f));

            var titulo = UIFabrica.Texto(w, "Titulo", new Vector2(0f, 0.925f), new Vector2(0.45f, 0.995f), Tema.TxtTitulo, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            titulo.text = "<b>Historial de compostas</b>";
            titulo.resizeTextForBestFit = true; titulo.resizeTextMinSize = 16; titulo.resizeTextMaxSize = Tema.TxtTitulo;
            Responsivo.En(titulo, new Vector2(0f, 0.955f), new Vector2(0.62f, 0.997f));
            txtResumen = UIFabrica.Texto(w, "Resumen", new Vector2(0f, 0.88f), new Vector2(0.62f, 0.925f), 15, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            txtResumen.resizeTextForBestFit = true; txtResumen.resizeTextMinSize = 10; txtResumen.resizeTextMaxSize = 15;
            Responsivo.En(txtResumen, new Vector2(0f, 0.925f), new Vector2(1f, 0.955f));

            btnNueva = Boton(w, "+ Nueva composta", new Vector2(0.62f, 0.93f), new Vector2(0.75f, 0.99f), Tema.BotonPrimario, () => { Cerrar(); PedirNueva?.Invoke(); },
                             new Vector2(0f, 0.885f), new Vector2(0.49f, 0.922f));
            Boton(w, "Bitácora consolidada", new Vector2(0.76f, 0.93f), new Vector2(0.89f, 0.99f), Tema.BotonNeutro, Consolidada,
                  new Vector2(0.51f, 0.885f), new Vector2(1f, 0.922f));
            btnCerrar = Boton(w, "Cerrar", new Vector2(0.90f, 0.93f), new Vector2(1f, 0.99f), Tema.BotonNeutro, Cerrar,
                              new Vector2(0.64f, 0.958f), new Vector2(1f, 0.995f));

            campoBuscar = UIFabrica.Campo(w, "Buscar", new Vector2(0f, 0.815f), new Vector2(0.25f, 0.87f), "Buscar lote, compostera o #número…", false, 40, 17);
            Responsivo.En(campoBuscar, new Vector2(0f, 0.845f), new Vector2(0.58f, 0.881f));
            campoBuscar.onValueChanged.AddListener(_ => Pintar());
            for (int i = 0; i < NombresFiltro.Length; i++)
            {
                var f = (Filtro)i;
                float x0 = 0.26f + i * 0.092f;
                var b = Boton(w, NombresFiltro[i], new Vector2(x0, 0.815f), new Vector2(x0 + 0.087f, 0.87f), UIFabrica.ColCard, () => { filtro = f; Pintar(); },
                              new Vector2(i * 0.2f, 0.805f), new Vector2(i * 0.2f + 0.19f, 0.84f));
                fondoFiltro[i] = b.GetComponent<Image>();
                textoFiltro[i] = UIFabrica.Etiqueta(b);
            }
            var bCal = Boton(w, "Calidad: todas", new Vector2(0.73f, 0.815f), new Vector2(0.84f, 0.87f), UIFabrica.ColCard, CambiarCalidad,
                             new Vector2(0.60f, 0.845f), new Vector2(0.79f, 0.881f));
            lblCalidad = UIFabrica.Etiqueta(bCal);
            btnDemo = Boton(w, "Cargar datos DEMO", new Vector2(0.85f, 0.815f), new Vector2(1f, 0.87f), Tema.ConAlfa(Tema.AcentoPrediccion, 0.55f), Demo,
                            new Vector2(0.80f, 0.845f), new Vector2(1f, 0.881f));
            lblDemo = UIFabrica.Etiqueta(btnDemo);

            var barra = UIFabrica.Tarjeta("BarraComparar", w, new Vector2(0f, 0.755f), new Vector2(1f, 0.805f), Tema.Superficie);
            Responsivo.En(barra, new Vector2(0f, 0.762f), new Vector2(1f, 0.8f));
            txtComparar = UIFabrica.Texto(barra, "Texto", new Vector2(0f, 0f), new Vector2(0.62f, 1f), 15, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            txtComparar.resizeTextForBestFit = true; txtComparar.resizeTextMinSize = 10; txtComparar.resizeTextMaxSize = 15;
            btnComparar = Boton(barra, "Comparar", new Vector2(0.63f, 0.1f), new Vector2(0.83f, 0.9f), Tema.ConAlfa(Tema.AcentoPrediccion, 0.85f), MostrarComparacion, null, null);
            Boton(barra, "Limpiar", new Vector2(0.84f, 0.1f), new Vector2(0.995f, 0.9f), Tema.BotonNeutro, () => { seleccion.Clear(); Pintar(); }, null, null);

            cuadricula = new CuadriculaDesplazable("Lotes", w, new Vector2(0f, 0f), new Vector2(1f, 0.745f), 330f, 320f, 330f);
            Responsivo.En(cuadricula.Vista, new Vector2(0f, 0f), new Vector2(1f, 0.756f));

            ConstruirDetalle();
            ConstruirComparacion();
            raiz.gameObject.SetActive(false);
        }

        void ConstruirDetalle()
        {
            raizDetalle = UIFabrica.Canvas("Canvas Detalle Lote", transform, 37);
            UIFabrica.Panel("Velo", raizDetalle, Vector2.zero, Vector2.one, Tema.Velo);
            var w = UIFabrica.ModalDesplazable(raizDetalle, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), 2300f);

            dTitulo = UIFabrica.Texto(w, "Titulo", new Vector2(0.02f, 0.925f), new Vector2(0.52f, 0.99f), Tema.TxtSubtitulo + 2, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            dTitulo.resizeTextForBestFit = true; dTitulo.resizeTextMinSize = 13; dTitulo.resizeTextMaxSize = Tema.TxtSubtitulo + 2;
            Responsivo.En(dTitulo, new Vector2(0.02f, 0.965f), new Vector2(0.98f, 0.995f));
            dSub = UIFabrica.Texto(w, "Sub", new Vector2(0.02f, 0.88f), new Vector2(0.98f, 0.925f), 14, TextAnchor.MiddleLeft, Tema.TextoTenue);
            dSub.resizeTextForBestFit = true; dSub.resizeTextMinSize = 10; dSub.resizeTextMaxSize = 14;
            Responsivo.En(dSub, new Vector2(0.02f, 0.945f), new Vector2(0.98f, 0.965f));

            dDescargar = Boton(w, "Descargar bitácora (Excel)", new Vector2(0.53f, 0.93f), new Vector2(0.70f, 0.985f), Tema.BotonPrimario, DescargarDetalle,
                               new Vector2(0.02f, 0.92f), new Vector2(0.50f, 0.942f));
            dAbrir = Boton(w, "Abrir en el tablero", new Vector2(0.71f, 0.93f), new Vector2(0.85f, 0.985f), Tema.ConAlfa(Tema.AcentoManual, 0.85f),
                           () => { if (fichaDetalle != null) AbrirLote(fichaDetalle.L); }, new Vector2(0.51f, 0.92f), new Vector2(0.98f, 0.942f));
            dAnular = Boton(w, "Anular cosecha", new Vector2(0.71f, 0.93f), new Vector2(0.85f, 0.985f), Tema.BotonPeligro, AnularCosecha,
                            new Vector2(0.51f, 0.92f), new Vector2(0.98f, 0.942f));
            Boton(w, "Cerrar", new Vector2(0.86f, 0.93f), new Vector2(0.98f, 0.985f), Tema.BotonNeutro, CerrarDetalle,
                  new Vector2(0.02f, 0.895f), new Vector2(0.98f, 0.916f));

            var marcoKpi = UIFabrica.Panel("Kpis", w, new Vector2(0.02f, 0.47f), new Vector2(0.49f, 0.87f), UIFabrica.ColCampo);
            Responsivo.En(marcoKpi, new Vector2(0.02f, 0.66f), new Vector2(0.98f, 0.888f));
            dKpis = UIFabrica.Texto(marcoKpi, "Texto", Vector2.zero, Vector2.one, 15, TextAnchor.UpperLeft, Tema.TextoPrimario);
            dKpis.rectTransform.offsetMin = new Vector2(14f, 8f); dKpis.rectTransform.offsetMax = new Vector2(-14f, -10f);
            dKpis.resizeTextForBestFit = true; dKpis.resizeTextMinSize = 10; dKpis.resizeTextMaxSize = 15;

            var marcoGraf = UIFabrica.Panel("Grafica", w, new Vector2(0.02f, 0.07f), new Vector2(0.49f, 0.46f), UIFabrica.ColCampo);
            Responsivo.En(marcoGraf, new Vector2(0.02f, 0.49f), new Vector2(0.98f, 0.652f));
            dGrafica = Imagen(marcoGraf, Vector2.zero, Vector2.one);
            dLeyenda = UIFabrica.Texto(w, "Leyenda", new Vector2(0.02f, 0.015f), new Vector2(0.49f, 0.065f), 13, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            dLeyenda.resizeTextForBestFit = true; dLeyenda.resizeTextMinSize = 9; dLeyenda.resizeTextMaxSize = 13;
            Responsivo.En(dLeyenda, new Vector2(0.02f, 0.465f), new Vector2(0.98f, 0.488f));

            var marcoLinea = UIFabrica.Panel("Linea", w, new Vector2(0.51f, 0.015f), new Vector2(0.98f, 0.87f), UIFabrica.ColCampo);
            Responsivo.En(marcoLinea, new Vector2(0.02f, 0.005f), new Vector2(0.98f, 0.46f));
            var tit = UIFabrica.Texto(marcoLinea, "Titulo", new Vector2(0f, 1f), new Vector2(1f, 1f), 15, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            tit.rectTransform.offsetMin = new Vector2(12f, -42f); tit.rectTransform.offsetMax = new Vector2(-12f, -6f);
            tit.text = "<b>LÍNEA DE TIEMPO</b>  ·  aportaciones, mediciones, acciones y correcciones";
            tit.resizeTextForBestFit = true; tit.resizeTextMinSize = 10; tit.resizeTextMaxSize = 15;
            dLinea = new ListaDesplazable("Eventos", marcoLinea, Vector2.zero, Vector2.one);
            dLinea.Vista.offsetMin = new Vector2(8f, 8f); dLinea.Vista.offsetMax = new Vector2(-8f, -46f);
            raizDetalle.gameObject.SetActive(false);
        }

        void ConstruirComparacion()
        {
            raizComparar = UIFabrica.Canvas("Canvas Comparar Lotes", transform, 37);
            UIFabrica.Panel("Velo", raizComparar, Vector2.zero, Vector2.one, Tema.Velo);
            var w = UIFabrica.ModalDesplazable(raizComparar, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), 2100f);
            var titulo = UIFabrica.Texto(w, "Titulo", new Vector2(0.02f, 0.92f), new Vector2(0.80f, 0.99f), Tema.TxtSubtitulo + 2, TextAnchor.MiddleLeft, Tema.TextoPrimario);
            titulo.text = "<b>Comparar lotes</b>";
            Responsivo.En(titulo, new Vector2(0.02f, 0.96f), new Vector2(0.60f, 0.995f));
            Boton(w, "Cerrar", new Vector2(0.84f, 0.93f), new Vector2(0.98f, 0.985f), Tema.BotonNeutro, CerrarComparacion,
                  new Vector2(0.62f, 0.962f), new Vector2(0.98f, 0.993f));
            var marco = UIFabrica.Panel("Grafica", w, new Vector2(0.02f, 0.56f), new Vector2(0.98f, 0.91f), UIFabrica.ColCampo);
            Responsivo.En(marco, new Vector2(0.02f, 0.79f), new Vector2(0.98f, 0.955f));
            cGrafica = Imagen(marco, Vector2.zero, Vector2.one);
            cLeyenda = UIFabrica.Texto(w, "Leyenda", new Vector2(0.02f, 0.50f), new Vector2(0.98f, 0.555f), 14, TextAnchor.MiddleLeft, Tema.TextoSecundario);
            cLeyenda.resizeTextForBestFit = true; cLeyenda.resizeTextMinSize = 9; cLeyenda.resizeTextMaxSize = 14;
            Responsivo.En(cLeyenda, new Vector2(0.02f, 0.75f), new Vector2(0.98f, 0.788f));
            cColumnas = UIFabrica.Panel("Columnas", w, new Vector2(0.01f, 0.01f), new Vector2(0.99f, 0.49f), new Color(0, 0, 0, 0), 0);
            Responsivo.En(cColumnas, new Vector2(0.01f, 0.005f), new Vector2(0.99f, 0.745f));
            raizComparar.gameObject.SetActive(false);
        }

        static Button Boton(Transform padre, string texto, Vector2 aMin, Vector2 aMax, Color color, Action accion, Vector2? cMin, Vector2? cMax)
        {
            var b = UIFabrica.Boton(padre, texto, aMin, aMax, 16, color, () => accion());
            var e = UIFabrica.Etiqueta(b); e.resizeTextForBestFit = true; e.resizeTextMinSize = 10; e.resizeTextMaxSize = 16;
            if (cMin.HasValue && cMax.HasValue) Responsivo.En(b, cMin.Value, cMax.Value);
            return b;
        }
    }
}
