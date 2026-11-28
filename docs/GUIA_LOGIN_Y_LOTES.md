# Login, lotes persistentes y auditoría: guía técnica

## Arquitectura por capas

```
Assets/Scripts/
├── Core/                  (asmdef Composta.Core · C# puro, sin UnityEngine, con tests)
│   ├── (modelo, sensores, reglas, predicción, control: ya existían)
│   ├── Seguridad/         Roles + PoliticaAcceso, Usuario, HasherPbkdf2, ValidadorCredenciales,
│   │                      ServicioAutenticacion, Sesion
│   ├── Persistencia/      IAlmacenDatos/ISerializador/IReloj, AlmacenArchivos (escritura atómica),
│   │                      RepositorioUsuariosJson, RepositorioLotesJson
│   └── Lotes/             Lote/Comando/Aporte, ValidadorAporte, ReproductorLote, ExportadorCsv
├── App/                   (Unity: composición, flujo y vistas)
│   ├── ServiciosApp       raíz de composición (único "new" de implementaciones concretas)
│   ├── ControlAcceso      flujo login → lote → uso → cierre / inactividad (se crea solo)
│   ├── GestorLotes        bitácora de acciones, autoguardado, reanudar, aportes, exportar
│   ├── PanelLogin         vista del login (3 modos + error fatal)
│   ├── PanelSesion        franja superior: persona conectada, Cuenta, Salir
│   ├── PanelAportes       registro de residuos pesados
│   ├── PanelUsuarios      administración de cuentas + cambio de contraseña propia
│   ├── Notificador        toasts y diálogos visibles en la build (antes todo iba a Debug.Log)
│   ├── GuardiaEntrada     bloquea atajos de teclado con modales o campos de texto enfocados
│   └── UIFabrica          helpers de UI por código (mismo estilo que PanelUI/PanelBuild)
├── Sim/                   (existente, con integraciones mínimas)
└── Tests/EditMode         61 tests NUnit (Window > General > Test Runner)
```

## Estructura de la UI (orden de dibujo, `Canvas.sortingOrder`)

| Orden | Canvas | Contenido |
|---|---|---|
| 0 | Canvas UI (PanelUI) | tarjetas, gráficas, alertas, 13 botones inferiores (incluye «Predicción», «Lote» e «Historial») |
| 15 | Canvas Historial | historial de compostas (tarjetas), debajo de la sesión y de «+ Nueva composta» |
| 10 | Canvas Build (PanelBuild) | botón «Nueva composta» y ventana de builds |
| 20 | Canvas Sesion | `Nombre · Rol` + «Usuarios/Mi cuenta» + «Salir» (x 0.405–0.655 de la barra superior) |
| 37 | Canvas Detalle / Comparar lotes | auditoría completa de un lote y comparación de 2–3 lotes |
| 38 | Canvas Mediciones / Lote | lista de mediciones y datos del lote (debajo del formulario de edición) |
| 41 | Canvas Cosecha | registrar la cosecha con rendimiento y calidad en vivo |
| 40 | Canvas Aportes / Usuarios / Registro | modales |
| 60 | Canvas Dialogo | confirmaciones y reportes |
| 100 | Canvas Login | fondo opaco + tarjeta centrada (x 0.34–0.66, y 0.12–0.88) |
| 300 | Canvas Toasts | mensajes efímeros (no bloquean clics) |

Tarjeta de login (de arriba a abajo): título · subtítulo según el modo · filas dinámicas
(etiqueta + `InputField`) · «Mostrar contraseña» · línea de error · botón principal · pie.

| Modo | Campos | Botón |
|---|---|---|
| `ConfiguracionInicial` (primer uso) | Nombre, Usuario, Contraseña, Confirmar | Crear cuenta de administración |
| `Login` | Usuario (recordado), Contraseña | Ingresar |
| `CambioObligatorio` (contraseña temporal) | Nueva, Confirmar | Guardar contraseña · Cancelar |
| `ErrorFatal` (archivo de cuentas dañado) | — | Reintentar |

Teclado: `Tab`/`Shift+Tab` cambian de campo, `Enter` envía y `Esc` cierra los modales.

## Roles

| Permiso | Consulta | Operación | Administración |
|---|:-:|:-:|:-:|
| Ver panel, exportar | ✔ | ✔ | ✔ |
| Voltear, regar, modo automático, crear lote, aportes, reparar sensores | | ✔ | ✔ |
| Corregir o eliminar mediciones y aportes, renombrar el lote (queda en la auditoría) | | ✔ | ✔ |
| Inyectar fallas de prueba, gestionar cuentas, eliminar lotes completos | | | ✔ |

Los permisos se comprueban dos veces: en la UI (botones deshabilitados) y en `SimuladorComposta`
(métodos públicos y atajos), así una tecla o una llamada desde otro script no los salta.

## Datos en disco (`Application.persistentDataPath/datos`)

```
usuarios.json(.bak)                 cuentas: hash PBKDF2-SHA256 (100k iteraciones) + sal; nunca la contraseña
lotes/{id}.json(.bak)               un archivo por lote: parámetros, semilla, bitácora, aportes, mediciones y auditoría
papelera/{id}-{fecha}.json(.bak)    lotes eliminados (para recuperar uno, cópialo de vuelta a lotes/{id}.json)
exportaciones/{fecha}_{lote}/       libro {lote}_{fecha}.xlsx + CSV: resumen, aportes, cambios y mediciones o bitácora + historial
```

**Cómo se reanuda un lote:** el modelo es determinista (semilla + mismas acciones a la misma hora
simulada), así que se guarda la *bitácora de acciones humanas*, no el estado interno.
`ReproductorLote` reconstruye el sistema hora a hora. Las acciones del control automático no se
guardan: se vuelven a producir solas. Requisito: no cambiar `ModeloComposta`/`PanelSensores` de forma
que alteren los resultados, o los lotes viejos se reproducirán distinto. Si eso ocurre, sube
`Lote.VersionActual` y migra.

## Puesta en marcha

1. Abre el proyecto en Unity 6000.6. Se importan los scripts nuevos y los dos `.asmdef`.
2. **No hay que tocar la escena**: `ControlAcceso` se crea solo al arrancar (`RuntimeInitializeOnLoadMethod`).
   Para desactivar el login, define `COMPOSTA_SIN_LOGIN` (Project Settings › Player › Scripting Define Symbols).
3. Pulsa Play y crea la cuenta de administración. Desde «Usuarios» crea cuentas de Operación/Consulta
   (cada persona cambia su contraseña temporal en el primer ingreso).
4. Tests: Window › General › Test Runner › EditMode › Run All. Si no aparece, instala
   `com.unity.test-framework` desde el Package Manager.
5. Sube a git los `.meta` que Unity genere para las carpetas y scripts nuevos.

## Recomendaciones pendientes (no aplicadas)

- Dejar de versionar carpetas generadas (ver comandos en la respuesta / `.gitignore`).
- Borrar `Assets/Scripts/Core/NewMonoBehaviourScript.cs` (plantilla vacía) y `Assets/_Recovery/0.unity`
  (escena de recuperación de un crash). Después puedes poner `"noEngineReferences": true` en
  `Composta.Core.asmdef` para impedir que Core dependa de Unity.
- Player Settings: `companyName`/`productName` siguen en «DefaultCompany/My project» (definen la carpeta de
  datos; cámbialos **antes** de distribuir), `runInBackground: 0` detiene la simulación al perder el foco
  y `resizableWindow: 0` impide redimensionar la ventana.

## Simulación y Control Manual: separación estricta

| | Simulación (azul) | Control Manual (ámbar) |
|---|---|---|
| Motor | `SimuladorComposta` avanza hora a hora | **detenido** (`DetenerMotor`): cero horas sintéticas |
| Datos | modelo + sensores simulados, bitácora de comandos reproducible | solo mediciones reales (`Lote.Informes`) |
| Vista | `PanelUI` | `TableroManual` (estado vacío "Esperando primer registro" → KPIs, gráficas de puntos, recomendaciones, historial) |
| Interpretación | el propio modelo | `AnalisisCampo`: fase por reglas (≥45 °C termofílica; tras fase caliente → enfriamiento; ≥21 días, <35 °C y pérdida de peso <1,5 %/semana → maduración), tendencias y recomendaciones |
| Calendario | tiempo simulado | `PlanInformes`: una medición cada 7 días reales (sin registros / al día / pendiente ≤24 h / atrasado) |

`GestorLotes.CrearLoteManual` crea el lote sin pasar por el simulador. Las vistas se eligen en `ControlAcceso.ActualizarVista`;
nunca se muestran ambas. El motor de calibración (`CalibracionCampo`, modelo de campo) sigue en Core con sus tests, pero la
app ya no lo usa en Control Manual.

## Pesos: recomendado vs. exagerado

`PerfilComposta.EvaluarPeso` devuelve `Optimo` (barra verde), `FueraDeOptimo` (consejo, no bloquea) o `Exagerado` (fuera de los
límites físicos del recipiente; bloquea el inicio de una simulación y marca la medición como crítica).

| Tipo | Recomendado | Límites físicos (fuera = PESO EXAGERADO) |
|---|---|---|
| Compostera doméstica | 15–60 kg | 3–120 kg |
| Compostera cerrada | 50–200 kg | 10–400 kg |
| Pila abierta | 200–1000 kg | 50–3000 kg |
| Hilera industrial | 2000–20000 kg | 500–100000 kg |
| Vermicomposta | 5–50 kg | 1–150 kg |

Los pesos por defecto no cambiaron, así que la simulación por defecto da los mismos resultados (test dorado).

## Sistema de diseño

`App/Tema.cs` concentra los tokens: 3 niveles de superficie, texto primario/secundario/tenue, acento azul (Simulación) y
ámbar (Control Manual), estados verde/ámbar/rojo (siempre con texto, no solo color), radios y tamaños de letra.
`UIFabrica` genera en memoria sprites redondeados 9-slice y círculos (uGUI no los trae en una build) y ofrece
`Tarjeta` (sombra suave), `Pildora` (badges) y estilos de botón con hover/presionado. Toda la UI hecha con `UIFabrica`
hereda el estilo; cambiar la identidad visual es editar `Tema.cs`.

## Corrección de datos y auditoría (versión 1.1.2)

- **Mediciones** (Control Manual): «Editar mediciones» en el tablero abre la lista completa. Cada fila
  tiene «Editar» (mismo formulario, precargado) y «Eliminar» (con confirmación).
- **Fecha opcional**: al registrar o corregir se puede escribir cuándo se midió (`dd/mm/aaaa hh:mm`,
  también `dd-mm-aaaa` o `aaaa-mm-dd`; vacía = ahora), en la zona horaria del dispositivo. Se rechazan
  fechas futuras (más de 5 min) y de hace más de un año; se avisa (sin bloquear) si es anterior a la
  creación del lote o si ya hay otra medición en la misma hora.
- **Aportes**: cada fila de la bitácora tiene «Editar» y «Eliminar»; el total se recalcula al momento.
- **Lote**: botón «Lote» (tablero manual y barra de simulación). Permite renombrarlo; en Control Manual
  también cambiar tipo de compostera, material y cobertura (las mediciones no cambian, solo se
  reevalúan). En Simulación solo el nombre: los parámetros definen la simulación ya calculada.
  Administración puede eliminar el lote: se mueve a `papelera/`, nunca se borra del disco.
- **Auditoría**: toda creación, corrección o eliminación queda en `Lote.Cambios` (quién, cuándo, qué
  cambió de qué valor a cuál). Se ve en «Lote › Historial de cambios» y se exporta en `cambios.csv`.
- **Integridad**: `EditorLote` (Core, sin Unity) valida y aplica cada cambio; `GestorLotes` guarda y,
  si el guardado falla, devuelve el lote exactamente a su estado anterior (copia profunda previa).
  Indicadores, fase estimada, tendencias y recomendaciones se recalculan siempre desde los datos guardados.

## Exportación a Excel (versión 1.1.2)

«Exportar» genera un **libro de Excel (.xlsx)** y, junto a él, los mismos datos en **CSV**.

- **Por qué .xlsx y no solo CSV:** Excel abre un CSV con el separador de la configuración regional
  (coma en México, punto y coma en España) y, sin BOM, rompe los acentos. Un .xlsx no depende de la
  región: los números se guardan como números (cada quien ve su separador decimal), las fechas como
  fechas reales en hora local del dispositivo y el texto en UTF-8. Abre en Excel, Google Sheets,
  LibreOffice y WPS (Android).
- **Hojas:** Resumen (datos del lote, totales, situación y recomendaciones, zona horaria y versión) ·
  Mediciones (Control Manual) o Bitácora + Historial (Simulación) · Aportes · Cambios (auditoría).
  Encabezados en español, fila de títulos fija, filtros y ajuste de impresión a una página de ancho.
- **Hojas sin datos:** llevan una nota visible («Todavía no hay mediciones…») y el aviso aparece
  también en el mensaje de exportación. Nunca se bloquea exportar un lote vacío.
- **CSV:** UTF-8 con BOM (acentos correctos en Excel también en PC), separados por comas, fechas en
  UTC y punto decimal: el formato estable para otros programas. Los valores van en español
  («Riego», «Café (carbono)», «Fase termofílica (caliente)»).
- **Dónde quedan:** en PC, `datos/exportaciones/{fecha}_{lote}/` (el aviso ofrece «Abrir en Excel» y
  «Abrir carpeta»). En Android, el libro en **Descargas › Composta** y los CSV en
  **Descargas › Composta › CSV › {fecha}_{lote}**.
- **Nombres de archivo:** sin acentos ni símbolos («Huerto Ñandú» → `huerto-nandu_20261127_103000.xlsx`).
- **Implementación:** `Core/Exportacion` (sin Unity): `LibroExcel` escribe SpreadsheetML y
  `ZipSinCompresion` empaqueta sin depender de bibliotecas nativas de compresión (IL2CPP);
  `ExportadorExcel` arma las hojas; `Core/Textos/Nombres` da los nombres en español que usan
  pantallas, Excel y CSV.

## Aportaciones, cosecha e historial de compostas (versión 1.1.2)

### Aportaciones como motor de cálculo
- Cada aportación guarda **material específico** (restos de cocina, césped, posos de café, estiércol, hojarasca, paja,
  aserrín, cartón, papel o mezcla), peso, **humedad** (vacía = típica del material) y **fecha**. El grupo verde/café se
  deduce del material. Tabla de valores en `Core/Lotes/Materiales.cs` (C:N, % de N y humedad típica de referencia).
- Al crear un lote de Control Manual se piden la **compostera** («Cubeta A») y la **carga inicial** en kg: es la
  primera aportación (marcada «CARGA INICIAL»; se corrige pero no se elimina).
- `Core/Campo/BalanceLote.cs`, ponderado por **materia seca**:
  `seca = kg × (1 − h)`, `N = seca × %N`, `C = N × C:N` → **C:N de la mezcla = ΣC / ΣN**;
  humedad de la mezcla = Σ(kg × h) / Σkg; la última medición real de humedad manda y solo se le suma lo que entró después.
  Clasificación: < 20 exceso de nitrógeno · 20–25 algo baja · 25–35 óptima · 35–40 algo alta · > 40 exceso de carbono.
- **Kilos para corregir**: `x = (30·N − C) / (s·(c − 30·n))` con los datos del material sugerido (hojarasca, cartón o restos de cocina).
- **Peso de entrada total** = carga inicial + aportaciones. La pérdida de peso de las mediciones descuenta lo aportado entre ellas.
- **Madurez** (`Maduracion.cs`): duración base por tipo (doméstica 84 días, cerrada 70, pila 112, industrial 63, vermicomposta 90).
  El verde fresco recorre la fecha: `f = kg verde / peso de la pila`, `candidato = mín(base, f / 0.30 × fase activa)`
  (mezcla cuenta a la mitad; el café no recorre la fecha).
- **Maduración**: «Lote › Iniciar maduración» bloquea nuevas aportaciones (se pueden corregir las existentes). La app lo sugiere
  cuando el lote está estabilizado (temperatura baja y peso casi sin cambio).
- En **simulaciones**, cada aportación también entra al modelo (masa, humedad, temperatura y C:N) en la hora simulada en que
  se registra (`TipoComando.Aporte`); si se corrige o elimina, la simulación se recalcula desde la bitácora. Los lotes
  anteriores no tienen aportaciones en su bitácora y se reproducen igual que antes.

### Cosecha
- «Lote › Registrar cosecha»: peso cosechado, fecha y revisión (olor a tierra, sin restos reconocibles, textura suelta).
- **Rendimiento** = peso cosechado / peso de entrada total. **Calidad**: 6 criterios (los 3 de la revisión + temperatura
  final ≤ máx(30, ambiente + 8) °C + humedad final 35–55 % + rendimiento 30–60 %) → A con 5–6, B con 3–4, C con 2 o menos.
- Se rechaza un peso mayor al 120 % de lo que entró. La cosecha se puede anular desde el historial (vuelve a maduración).

### Historial de compostas
- Tarjetas por lote con estado (activo, en maduración, simulación, cosechado, archivado), mini curva de temperatura y
  sus indicadores. Búsqueda, filtros por estado y calidad.
- **Comparar**: marca 2 o 3 lotes → indicadores lado a lado y curvas de temperatura superpuestas.
- **Ver detalle**: indicadores, criterios de calidad, gráfica de temperatura y humedad, línea de tiempo (carga, aportaciones,
  mediciones, acciones de simulación y correcciones) y descarga de la bitácora de ese lote en Excel.
- **Bitácora consolidada**: un Excel con la hoja «Lotes» (una fila por lote) y todas las mediciones, aportaciones y cambios
  con columna «Lote».
- Varias composteras de Control Manual pueden estar activas a la vez; la simulación sigue siendo de una a la vez.

### Datos de demostración
- Al iniciar sin lotes se ofrece **«Cargar datos DEMO»** (también en el historial): 3 composteras activas (exceso de
  nitrógeno, fase termófila óptima y estabilizada) y 4 cosechadas (calidad A, B, A y C). Se crean con las mismas validaciones
  que los datos reales (`DatosDemo.cs`), se marcan **DEMO** en tarjetas, tablero y Excel, y «Quitar datos DEMO» los mueve a la papelera.

## Ventana de Predicción (versión 1.1.2)

- **Diagnóstico y recomendaciones personalizadas** (arriba, a todo lo ancho; `Core/Pronostico/DiagnosticoPronostico.cs`):
  estado actual vs. ideal (peso, humedad, temperatura y potencial térmico, balance C:N) con semáforo; hasta 3 acciones
  prioritarias (medición atrasada, riego o material seco, kilos para corregir la C:N, próximo volteo con el oxígeno mínimo
  previsto, hasta cuándo aportar verde); alertas de riesgo (humedad crítica, medición atrasada, aportación reciente que
  movió la C:N, fecha de madurez recorrida, diferencia entre la cosecha del pronóstico y la de tus registros).
  Usa la última medición real; si no hay, el estado simulado o los parámetros elegidos, y lo dice.
- El pronóstico de un lote de Control Manual parte de la **C:N real de la mezcla** (carga inicial + aportaciones).
- **Gráficas**: curva hora a hora, eje Y con valores y unidad, fechas reales en el eje X (cada semana; cada 2 si el
  horizonte supera 70 días), franja verde rotulada con la zona del perfil, volteos programados como líneas punteadas
  ámbar en las 3 gráficas y una frase de ayuda por gráfica. Al pasar el cursor (o tocar) aparece una línea guía en las
  3 gráficas y una ficha con fecha, valores, fase y eventos del día.
- **Hitos**: «Hitos clave» (fase caliente, pico, fin, maduración, cosecha) o «Todos los eventos»; el próximo se marca
  como SIGUIENTE.
