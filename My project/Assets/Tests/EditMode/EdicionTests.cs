using System;
using System.IO;
using Composta.Core;
using Composta.Core.Campo;
using Composta.Core.Lotes;
using Composta.Core.Persistencia;
using Composta.Core.Seguridad;
using NUnit.Framework;

namespace Composta.Tests
{
    // Edición y eliminación de mediciones, aportes y lotes (v1.1.2): validación, auditoría y recálculo
    public class EditorLoteTests
    {
        static readonly DateTime Ahora = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        static Lote LoteManual()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 0, "ana", Ahora.AddDays(-30), ModoLote.Manual);
            return l;
        }

        static string Agregar(Lote l, float t, float h, float p, DateTime fecha, string usuario = "ana")
        {
            var r = EditorLote.AgregarMedicion(l, t, h, p, "", fecha, usuario, Ahora);
            Assert.IsTrue(r.Exito, r.Mensaje);
            return r.IdEntidad;
        }

        [Test]
        public void AgregarMedicion_GuardaFechaDeMedicionYDeRegistro_YQuedaEnAuditoria()
        {
            var l = LoteManual();
            var fecha = Ahora.AddDays(-3);
            string id = Agregar(l, 55f, 60f, 40f, fecha);
            var m = l.Informes[0];
            Assert.AreEqual(id, m.Id);
            Assert.AreEqual(fecha.Ticks, m.FechaUtc);
            Assert.AreEqual(Ahora.Ticks, m.RegistradoUtc);
            Assert.AreEqual(1, l.Cambios.Count);
            Assert.AreEqual(AccionCambio.Creacion, l.Cambios[0].Accion);
            Assert.AreEqual(EntidadCambio.Medicion, l.Cambios[0].Entidad);
            Assert.AreEqual("ana", l.Cambios[0].Usuario);
        }

        [TestCase(-50f, 60f, 40f)]
        [TestCase(120f, 60f, 40f)]
        [TestCase(55f, 101f, 40f)]
        [TestCase(55f, -1f, 40f)]
        [TestCase(55f, 60f, -3f)]
        [TestCase(55f, 60f, 0f)]
        [TestCase(float.NaN, 60f, 40f)]
        [TestCase(55f, 60f, float.PositiveInfinity)]
        public void MedicionInvalida_SeRechaza_SinTocarElLote(float t, float h, float p)
        {
            var l = LoteManual();
            var r = EditorLote.AgregarMedicion(l, t, h, p, "", Ahora, "ana", Ahora);
            Assert.IsFalse(r.Exito);
            Assert.IsNotEmpty(r.Mensaje);
            Assert.AreEqual(0, l.Informes.Count);
            Assert.AreEqual(0, l.Cambios.Count);
        }

        [Test]
        public void FechaFutura_OMuyAntigua_SeRechaza()
        {
            var l = LoteManual();
            Assert.IsFalse(EditorLote.AgregarMedicion(l, 50f, 50f, 30f, "", Ahora.AddHours(1), "ana", Ahora).Exito);
            Assert.IsFalse(EditorLote.AgregarMedicion(l, 50f, 50f, 30f, "", Ahora.AddDays(-400), "ana", Ahora).Exito);
            Assert.IsTrue(EditorLote.AgregarMedicion(l, 50f, 50f, 30f, "", Ahora.AddMinutes(2), "ana", Ahora).Exito, "tolerancia de reloj");
        }

        [Test]
        public void MedicionEnLoteDeSimulacion_SeRechaza()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 1, "ana", Ahora, ModoLote.Simular);
            Assert.IsFalse(EditorLote.AgregarMedicion(l, 50f, 50f, 30f, "", Ahora, "ana", Ahora).Exito);
        }

        [Test]
        public void LoteCerrado_NoAdmiteCambios()
        {
            var l = LoteManual();
            string id = Agregar(l, 50f, 50f, 30f, Ahora.AddDays(-1));
            l.Estado = EstadoLote.Cerrado;
            Assert.IsFalse(EditorLote.EditarMedicion(l, id, 40f, 50f, 30f, "", Ahora.AddDays(-1), "luis", Ahora).Exito);
            Assert.IsFalse(EditorLote.EliminarMedicion(l, id, "luis", Ahora).Exito);
            Assert.IsFalse(EditorLote.AgregarAporte(l, 2f, MaterialEspecifico.RestosCocina, 0f, "", Ahora, 0, "luis", Ahora).Exito);
            Assert.IsFalse(EditorLote.EditarLote(l, "Otro", "Cubeta A", l.Tipo, l.RelacionCN, l.Aislamiento, "luis", Ahora).Exito);
        }

        [Test]
        public void EditarMedicion_AnotaQuienYQueCambio_YRecalculaElAnalisis()
        {
            var l = LoteManual();
            Agregar(l, 30f, 60f, 40f, Ahora.AddDays(-14));
            string id = Agregar(l, 35f, 60f, 38f, Ahora.AddDays(-7));
            Assert.AreEqual(35f, AnalisisCampo.Analizar(l).Ultimo.Temperatura);

            var r = EditorLote.EditarMedicion(l, id, 62f, 55f, 36f, "corrección", Ahora.AddDays(-7), "luis", Ahora);
            Assert.IsTrue(r.Exito, r.Mensaje);
            var m = l.Informes.Find(x => x.Id == id);
            Assert.AreEqual(62f, m.Temperatura);
            Assert.AreEqual("luis", m.EditadoPor);
            Assert.AreEqual(Ahora.Ticks, m.EditadoUtc);
            Assert.AreEqual("ana", m.Usuario, "quien registró no cambia");

            var c = l.Cambios[l.Cambios.Count - 1];
            Assert.AreEqual(AccionCambio.Edicion, c.Accion);
            Assert.AreEqual("luis", c.Usuario);
            StringAssert.Contains("temperatura 35 → 62", c.Detalle);
            StringAssert.Contains("humedad 60 → 55", c.Detalle);

            // Los indicadores salen de los datos corregidos
            var e = AnalisisCampo.Analizar(l);
            Assert.AreEqual(62f, e.Ultimo.Temperatura);
            Assert.AreEqual(32f, e.DeltaTemp.Value, 1e-4f);
        }

        [Test]
        public void EditarSinCambios_NoEnsuciaLaAuditoria()
        {
            var l = LoteManual();
            string id = Agregar(l, 50f, 50f, 30f, Ahora.AddDays(-1));
            int antes = l.Cambios.Count;
            var r = EditorLote.EditarMedicion(l, id, 50f, 50f, 30f, "", Ahora.AddDays(-1), "luis", Ahora);
            Assert.IsTrue(r.Exito);
            Assert.IsTrue(r.SinCambios);
            Assert.AreEqual(antes, l.Cambios.Count);
            Assert.IsNull(l.Informes[0].EditadoPor);
        }

        [Test]
        public void EditarFecha_ReordenaLasMediciones()
        {
            var l = LoteManual();
            string a = Agregar(l, 40f, 50f, 30f, Ahora.AddDays(-10));
            Agregar(l, 50f, 50f, 29f, Ahora.AddDays(-5));
            // La primera pasa a ser la más reciente
            Assert.IsTrue(EditorLote.EditarMedicion(l, a, 40f, 50f, 30f, "", Ahora.AddDays(-1), "ana", Ahora).Exito);
            Assert.AreEqual(a, AnalisisCampo.Analizar(l).Ultimo.Id);
        }

        [Test]
        public void EliminarMedicion_LaQuitaYRecalcula()
        {
            var l = LoteManual();
            Agregar(l, 30f, 60f, 40f, Ahora.AddDays(-14));
            string id = Agregar(l, 65f, 60f, 38f, Ahora.AddDays(-7));
            var r = EditorLote.EliminarMedicion(l, id, "luis", Ahora);
            Assert.IsTrue(r.Exito, r.Mensaje);
            Assert.AreEqual(1, l.Informes.Count);
            var e = AnalisisCampo.Analizar(l);
            Assert.AreEqual(30f, e.Ultimo.Temperatura);
            Assert.IsNull(e.Anterior);
            var c = l.Cambios[l.Cambios.Count - 1];
            Assert.AreEqual(AccionCambio.Eliminacion, c.Accion);
            StringAssert.Contains("65", c.Detalle);
            StringAssert.Contains("ana", c.Detalle, "el detalle conserva quién la había registrado");
            Assert.IsFalse(EditorLote.EliminarMedicion(l, id, "luis", Ahora).Exito, "no se elimina dos veces");
        }

        [Test]
        public void EliminarLaUnicaMedicion_VuelveAlEstadoSinRegistros()
        {
            var l = LoteManual();
            string id = Agregar(l, 30f, 60f, 40f, Ahora.AddDays(-1));
            EditorLote.EliminarMedicion(l, id, "ana", Ahora);
            Assert.AreEqual(EstadoInforme.SinRegistros, PlanInformes.Evaluar(l, Ahora).Estado);
            Assert.AreEqual(0, AnalisisCampo.Analizar(l).Registros);
        }

        [Test]
        public void Aportes_EditarYEliminar_RecalculanElTotal()
        {
            var l = LoteManual();
            var a1 = EditorLote.AgregarAporte(l, 10f, MaterialEspecifico.RestosCocina, 0f, "", Ahora, 0, "ana", Ahora);
            var a2 = EditorLote.AgregarAporte(l, 5.5f, MaterialEspecifico.Hojarasca, 0f, "hojas", Ahora, 0, "ana", Ahora);
            Assert.IsTrue(a1.Exito && a2.Exito);
            Assert.AreEqual(15.5f, l.TotalAportadoKg, 1e-4f);

            var e = EditorLote.EditarAporte(l, a1.IdEntidad, 12.25f, MaterialEspecifico.MezclaGenerica, 0f, "", Ahora, "luis", Ahora);
            Assert.IsTrue(e.Exito, e.Mensaje);
            Assert.AreEqual(17.75f, l.TotalAportadoKg, 1e-4f);
            var ap = l.Aportes.Find(x => x.Id == a1.IdEntidad);
            Assert.AreEqual(TipoMaterial.Mixto, ap.Material);
            Assert.AreEqual("luis", ap.EditadoPor);

            Assert.IsTrue(EditorLote.EliminarAporte(l, a2.IdEntidad, "luis", Ahora).Exito);
            Assert.AreEqual(12.25f, l.TotalAportadoKg, 1e-4f);
            Assert.AreEqual(4, l.Cambios.Count);
        }

        [TestCase(0f)]
        [TestCase(-2f)]
        [TestCase(0.05f)]
        [TestCase(6000f)]
        [TestCase(float.NaN)]
        public void AporteInvalido_SeRechaza(float kg)
        {
            var l = LoteManual();
            var a = EditorLote.AgregarAporte(l, 3f, MaterialEspecifico.RestosCocina, 0f, "", Ahora, 0, "ana", Ahora);
            Assert.IsFalse(EditorLote.AgregarAporte(l, kg, MaterialEspecifico.RestosCocina, 0f, "", Ahora, 0, "ana", Ahora).Exito);
            Assert.IsFalse(EditorLote.EditarAporte(l, a.IdEntidad, kg, MaterialEspecifico.RestosCocina, 0f, "", Ahora, "ana", Ahora).Exito);
            Assert.AreEqual(3f, l.TotalAportadoKg, 1e-4f);
        }

        [Test]
        public void MaterialFueraDelCatalogo_SeRechaza()
        {
            var l = LoteManual();
            Assert.IsFalse(EditorLote.AgregarAporte(l, 3f, (MaterialEspecifico)99, 0f, "", Ahora, 0, "ana", Ahora).Exito);
            Assert.IsFalse(EditorLote.AgregarAporte(l, 3f, MaterialEspecifico.SinEspecificar, 0f, "", Ahora, 0, "ana", Ahora).Exito);
        }

        [Test]
        public void EditarLote_Manual_CambiaTipoYNombre()
        {
            var l = LoteManual();
            var r = EditorLote.EditarLote(l, "  Huerto  norte ", "Cubeta A", TipoComposta.PilaAbierta, 40f, 1.3f, "luis", Ahora);
            Assert.IsTrue(r.Exito, r.Mensaje);
            Assert.AreEqual("Huerto norte", l.Nombre);
            Assert.AreEqual(TipoComposta.PilaAbierta, l.Tipo);
            Assert.AreEqual(40f, l.RelacionCN);
            StringAssert.Contains("Pila abierta", l.Cambios[l.Cambios.Count - 1].Detalle);
        }

        [Test]
        public void EditarLote_NombreVacioOLargo_SeRechaza()
        {
            var l = LoteManual();
            Assert.IsFalse(EditorLote.EditarLote(l, "   ", "Cubeta A", l.Tipo, l.RelacionCN, l.Aislamiento, "ana", Ahora).Exito);
            Assert.IsFalse(EditorLote.EditarLote(l, new string('x', EditorLote.NombreMax + 1), "Cubeta A", l.Tipo, l.RelacionCN, l.Aislamiento, "ana", Ahora).Exito);
        }

        [Test]
        public void EditarLote_Simulacion_SoloPermiteElNombre()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 1, "ana", Ahora, ModoLote.Simular);
            Assert.IsFalse(EditorLote.EditarLote(l, l.Nombre, "Cubeta A", TipoComposta.Industrial, l.RelacionCN, l.Aislamiento, "ana", Ahora).Exito);
            Assert.AreEqual(TipoComposta.Domestica, l.Tipo);
            Assert.IsTrue(EditorLote.EditarLote(l, "Prueba de verano", "Cubeta A", l.Tipo, l.RelacionCN, l.Aislamiento, "ana", Ahora).Exito);
            Assert.AreEqual("Prueba de verano", l.Nombre);
        }

        [Test]
        public void Clonar_EsCopiaProfunda()
        {
            var l = LoteManual();
            string id = Agregar(l, 50f, 50f, 30f, Ahora.AddDays(-1));
            EditorLote.AgregarAporte(l, 2f, MaterialEspecifico.RestosCocina, 0f, "", Ahora, 0, "ana", Ahora);
            var copia = EditorLote.Clonar(l);
            EditorLote.EditarMedicion(l, id, 20f, 50f, 30f, "", Ahora.AddDays(-1), "luis", Ahora);
            l.Aportes[0].Kg = 99f;
            Assert.AreEqual(50f, copia.Informes[0].Temperatura);
            Assert.AreEqual(2f, copia.Aportes[0].Kg);
            Assert.AreEqual(2, copia.Cambios.Count);
            Assert.AreEqual(l.Id, copia.Id);
        }

        [Test]
        public void Auditoria_SobreviveAGuardarYCargar()
        {
            var almacen = new AlmacenMemoria();
            var repo = new RepositorioLotesJson(almacen, new SerializadorPruebas());
            var l = LoteManual();
            string id = Agregar(l, 50f, 50f, 30f, Ahora.AddDays(-1));
            EditorLote.EditarMedicion(l, id, 52f, 50f, 30f, "", Ahora.AddDays(-1), "luis", Ahora);
            repo.Guardar(l);
            var leido = repo.Cargar(l.Id);
            Assert.AreEqual(2, leido.Cambios.Count);
            Assert.AreEqual(AccionCambio.Edicion, leido.Cambios[1].Accion);
            Assert.AreEqual("luis", leido.Informes[0].EditadoPor);
            Assert.AreEqual(Ahora.Ticks, leido.Informes[0].RegistradoUtc);
        }

        [Test]
        public void LoteAntiguoSinAuditoria_SeSanea()
        {
            var l = LoteManual();
            l.Cambios = null;
            ReproductorLote.Sanear(l);
            Assert.IsNotNull(l.Cambios);
            Assert.IsTrue(EditorLote.AgregarAporte(l, 1f, MaterialEspecifico.RestosCocina, 0f, "", Ahora, 0, "ana", Ahora).Exito);
        }
    }

    public class ValidadorFechaMedicionTests
    {
        static readonly DateTime Ahora = new DateTime(2026, 6, 15, 18, 0, 0, DateTimeKind.Utc);
        static readonly TimeZoneInfo Mexico = TimeZoneInfo.CreateCustomTimeZone("Prueba-6", TimeSpan.FromHours(-6), "UTC-6", "UTC-6");

        [Test]
        public void Vacia_EsAhora()
        {
            Assert.IsTrue(ValidadorFechaMedicion.Validar("  ", Mexico, Ahora, out var f, out _));
            Assert.AreEqual(Ahora, f);
        }

        [TestCase("14/06/2026 09:30")]
        [TestCase("14/6/2026 9:30")]
        [TestCase("14-06-2026 09:30")]
        [TestCase("2026-06-14 09:30")]
        [TestCase("14/06/26 09:30")]
        [TestCase(" 14/06/2026   09:30 ")]
        public void Formatos_AceptadosEnHoraLocal(string texto)
        {
            Assert.IsTrue(ValidadorFechaMedicion.Validar(texto, Mexico, Ahora, out var f, out string e), e);
            Assert.AreEqual(new DateTime(2026, 6, 14, 15, 30, 0, DateTimeKind.Utc), f);   // 09:30 en UTC-6
        }

        [Test]
        public void SoloFecha_DeOtroDia_UsaMediodia()
        {
            Assert.IsTrue(ValidadorFechaMedicion.Validar("10/06/2026", Mexico, Ahora, out var f, out _));
            Assert.AreEqual(new DateTime(2026, 6, 10, 18, 0, 0, DateTimeKind.Utc), f);
        }

        [Test]
        public void SoloFecha_DeHoy_UsaLaHoraActual()
        {
            Assert.IsTrue(ValidadorFechaMedicion.Validar("15/06/2026", Mexico, Ahora, out var f, out _));
            Assert.AreEqual(Ahora, f);
        }

        [TestCase("31/02/2026")]
        [TestCase("32/01/2026 10:00")]
        [TestCase("15/13/2026")]
        [TestCase("15/06/2026 25:00")]
        [TestCase("mañana")]
        [TestCase("15/06")]
        public void Invalidas_SeRechazan(string texto)
        {
            Assert.IsFalse(ValidadorFechaMedicion.Validar(texto, Mexico, Ahora, out _, out string e));
            Assert.IsNotEmpty(e);
        }

        [Test]
        public void Futura_SeRechaza()
        {
            Assert.IsFalse(ValidadorFechaMedicion.Validar("16/06/2026 10:00", Mexico, Ahora, out _, out string e));
            StringAssert.Contains("futura", e);
        }

        [Test]
        public void MasDeUnAnio_SeRechaza()
        {
            Assert.IsFalse(ValidadorFechaMedicion.Validar("01/01/2025", Mexico, Ahora, out _, out string e));
            StringAssert.Contains("un año", e);
        }

        [Test]
        public void Formatear_EsInversoDeValidar()
        {
            Assert.IsTrue(ValidadorFechaMedicion.Validar("14/06/2026 09:30", Mexico, Ahora, out var f, out _));
            Assert.AreEqual("14/06/2026 09:30", ValidadorFechaMedicion.Formatear(f.Ticks, Mexico));
        }

        [Test]
        public void Advertencias_AnteriorAlLote_YDuplicada()
        {
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 0, "ana", Ahora.AddDays(-5), ModoLote.Manual);
            var adv = new System.Collections.Generic.List<string>();
            ValidadorFechaMedicion.Advertencias(l, Ahora.AddDays(-8), null, Mexico, adv);
            Assert.AreEqual(1, adv.Count);
            StringAssert.Contains("anterior a la creación", adv[0]);

            var r = EditorLote.AgregarMedicion(l, 50f, 50f, 30f, "", Ahora.AddDays(-1), "ana", Ahora);
            adv.Clear();
            ValidadorFechaMedicion.Advertencias(l, Ahora.AddDays(-1).AddMinutes(20), null, Mexico, adv);
            Assert.AreEqual(1, adv.Count);
            StringAssert.Contains("dos veces", adv[0]);

            adv.Clear();   // al editar esa misma medición no se compara consigo misma
            ValidadorFechaMedicion.Advertencias(l, Ahora.AddDays(-1), r.IdEntidad, Mexico, adv);
            Assert.AreEqual(0, adv.Count);
        }
    }

    public class EliminacionTests
    {
        string dir;

        [SetUp] public void Preparar() => dir = Path.Combine(Path.GetTempPath(), "composta_tests_" + Guid.NewGuid().ToString("N"));
        [TearDown] public void Limpiar() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

        [Test]
        public void EliminarLote_VaALaPapelera_YDejaDeListarse()
        {
            var almacen = new AlmacenArchivos(dir);
            var repo = new RepositorioLotesJson(almacen, new SerializadorPruebas());
            var ahora = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 0, "ana", ahora, ModoLote.Manual);
            repo.Guardar(l);
            repo.Guardar(l);                                   // genera también el .bak

            string clave = repo.Eliminar(l.Id, ahora);
            Assert.AreEqual($"papelera/{l.Id}-20260615120000.json", clave);
            Assert.IsNull(repo.Cargar(l.Id));
            Assert.AreEqual(0, repo.Listar().Count);
            Assert.IsTrue(almacen.Existe(clave), "la copia se puede recuperar");
            Assert.IsNotNull(almacen.LeerRespaldo(clave), "el respaldo se movió con ella");
            Assert.IsFalse(File.Exists(almacen.RutaAbsoluta($"lotes/{l.Id}.json") + ".bak"));
        }

        [Test]
        public void EliminarLoteInexistente_Falla()
        {
            var repo = new RepositorioLotesJson(new AlmacenArchivos(dir), new SerializadorPruebas());
            Assert.Throws<FileNotFoundException>(() => repo.Eliminar(Guid.NewGuid().ToString("N"), DateTime.UtcNow));
            Assert.Throws<ArgumentException>(() => repo.Eliminar("../usuarios", DateTime.UtcNow));
        }
    }

    public class PermisosEdicionTests
    {
        [TestCase(Rol.Consulta, false, false)]
        [TestCase(Rol.Operador, true, false)]
        [TestCase(Rol.Administrador, true, true)]
        public void EditarYEliminar(Rol rol, bool editar, bool eliminarLote)
        {
            Assert.AreEqual(editar, PoliticaAcceso.Tiene(rol, Permiso.EditarRegistros));
            Assert.AreEqual(eliminarLote, PoliticaAcceso.Tiene(rol, Permiso.EliminarLotes));
        }
    }

    public class ExportacionAuditoriaTests
    {
        [Test]
        public void CambiosCsv_EnEspanol_YVacioConEncabezado()
        {
            var ahora = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 0, "ana", ahora, ModoLote.Manual);
            Assert.AreEqual("fecha_utc,usuario,accion,elemento,id_elemento,detalle\n", ExportadorCsv.Cambios(l));

            var a = EditorLote.AgregarAporte(l, 2f, MaterialEspecifico.Hojarasca, 0f, "", ahora, 0, "ana", ahora);
            EditorLote.EliminarAporte(l, a.IdEntidad, "josé", ahora);
            string csv = ExportadorCsv.Cambios(l);
            StringAssert.Contains(",creación,aporte,", csv);
            StringAssert.Contains("josé,eliminación,aporte,", csv);
            StringAssert.Contains("hojarasca seca", csv);
        }

        [Test]
        public void MedicionesCsv_IncluyeRegistroYCorreccion()
        {
            var ahora = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var l = Lote.Crear(PerfilComposta.Crear(TipoComposta.Domestica), 0, "ana", ahora.AddDays(-3), ModoLote.Manual);
            var r = EditorLote.AgregarMedicion(l, 50f, 50f, 30f, "", ahora.AddDays(-2), "ana", ahora);
            EditorLote.EditarMedicion(l, r.IdEntidad, 51f, 50f, 30f, "", ahora.AddDays(-2), "luis", ahora);
            var lineas = ExportadorCsv.Mediciones(l).Split('\n');
            StringAssert.EndsWith("registrado_utc,editado_por,editado_utc", lineas[0]);
            StringAssert.EndsWith("2026-06-15 12:00:00Z,luis,2026-06-15 12:00:00Z", lineas[1]);
        }
    }
}
