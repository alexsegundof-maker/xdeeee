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
| 0 | Canvas UI (PanelUI) | tarjetas, gráficas, alertas, 10 botones inferiores |
| 10 | Canvas Build (PanelBuild) | botón «Nueva composta» y ventana de builds |
| 20 | Canvas Sesion | `Nombre · Rol` + «Usuarios/Mi cuenta» + «Salir» (x 0.405–0.655 de la barra superior) |
| 40 | Canvas Aportes / Usuarios | modales |
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
| Inyectar fallas de prueba, gestionar cuentas | | | ✔ |

Los permisos se comprueban dos veces: en la UI (botones deshabilitados) y en `SimuladorComposta`
(métodos públicos y atajos), así una tecla o una llamada desde otro script no los salta.

## Datos en disco (`Application.persistentDataPath/datos`)

```
usuarios.json(.bak)                 cuentas: hash PBKDF2-SHA256 (100k iteraciones) + sal; nunca la contraseña
lotes/{id}.json(.bak)               un archivo por lote: parámetros, semilla, bitácora y aportes
exportaciones/{id}_{fecha}/*.csv    resumen, bitácora, aportes, historial
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

## Modo Manual: sin sensores, con informe semanal y calibración

- Los lotes Manual usan el **modelo de campo** (`SistemaComposta(..., modoCampo: true)`): no hay sensores
  simulados; los valores del tablero son estimaciones (`≈`). El modo Simular usa el modelo clásico y da
  resultados idénticos a los de antes (test `ModoSimular_DaLosMismosResultadosQueAntes`).
- El modelo de campo agrega la **masa** (pierde material degradable según temperatura y humedad) y tres
  perillas ocultas: `FactorActividad` (≈ C/N), `FactorEvaporacion` (≈ aislamiento/exposición) y
  `FactorDescomposicion`. Las fases se deducen de la temperatura y del material restante.
- **Informe semanal** (temperatura, humedad, peso) → `CalibracionCampo.Aplicar`:
  1. ajusta las perillas según el error de predicción (tope ±25 % por informe; con poco O2 el efecto de la
     actividad sobre la temperatura se invierte);
  2. corrige el estado con una ganancia de 0,5 a 0,95 que crece con el tiempo sin medir;
  3. reparte la corrección en el historial para que las gráficas no den saltos (puntos medidos = ●).
- El informe se guarda en `Lote.Informes` y como comando `Informe` en la bitácora: al reanudar se reaplica igual.
- Ritmo: **calendario real** (1 h simulada = 1 h real; al abrir la app se pone al día) o **demostración**
  (cada 168 h simuladas se pausa y pide el informe). Estado: al día / pendiente (≤24 h) / atrasado.
- Límite conocido: sin medir oxígeno, la temperatura sola no distingue una pila "muy activa pero ahogada" de
  una "poco activa". Con volteos regulares la calibración converge; con volteo solo semanal, evaporación y
  descomposición convergen y la temperatura queda corregida por el informe.
