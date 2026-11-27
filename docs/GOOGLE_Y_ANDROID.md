# Google (Time Zone + Calendar) y compilación para Android

## Arquitectura

```
Core/Pronostico/   (C# puro, con tests)
  MotorPronostico   proyección aislada: instancia PROPIA de SistemaComposta (nunca el simulador en vivo
                    ni un lote de Control Manual; los lotes se leen y se copian)
  PlanCalendario    hitos → eventos (fecha local, recordatorio, color) → JSON de Calendar API v3
  UtilGoogle        PKCE (RFC 7636), URL de autorización, parámetros de retorno, encabezado HTTP Date
App/Google/        (Unity, solo UnityWebRequest: sin DLLs de Google → compatible con IL2CPP/Android)
  ConfiguracionGoogle    ScriptableObject con claves e IDs (Assets/Resources/ConfiguracionGoogle.asset)
  HttpGoogle             UnityWebRequest → Task
  ServicioTiempoGoogle   Time Zone API + hora real (encabezado Date de Google) · respaldo: dispositivo
  AutenticacionGoogle    OAuth 2.0 + PKCE · Android: deep link · Editor/PC: 127.0.0.1 (loopback)
  ServicioCalendarioGoogle  leer / borrar / crear eventos (idempotente por lote)
App/VentanaPrediccion   ventana dedicada (sin controles de la simulación)
App/Movil/             AdaptadorPantalla, AreaSegura, Responsivo, ContenidoDesplazable, Carrusel, AreaTactilMinima
```

**Aislamiento.** Los tres botones "Predicción" (Nueva composta, tablero de Simulación, tablero de Control Manual)
llaman a `MotorPronostico`, que crea su propio `SistemaComposta`. Los tests `Pronostico_NoModificaUnLoteManual`
y `Pronostico_DesdeLoteSimulado_NoAlteraElLoteNiSuSimulacion` comprueban que el lote serializado no cambia ni un byte.

**Qué aporta cada API.** La Time Zone API da la zona IANA y el desplazamiento (con horario de verano) de la
ubicación; la hora UTC real se toma del encabezado `Date` de la respuesta de Google (así un reloj del teléfono
mal puesto no desplaza las fechas). Los eventos se crean con `timeZone` IANA, por lo que Google aplica el horario
de verano correcto en cada fecha futura. Sin conexión o sin clave se usan la hora y la zona del dispositivo.

## 1. Google Cloud Console

1. Crea un proyecto en https://console.cloud.google.com/.
2. **APIs y servicios › Biblioteca**: habilita **Google Calendar API** y **Time Zone API**
   (la Time Zone API es de Google Maps Platform y requiere una cuenta de facturación; tiene cuota gratuita mensual).
3. **Pantalla de consentimiento de OAuth** (Google Auth Platform): tipo *Externo*, nombre de la app, correo de soporte.
   Agrega el alcance `https://www.googleapis.com/auth/calendar.events` y, mientras esté en modo *Prueba*,
   agrega como **usuarios de prueba** las cuentas que usarán la app. Para publicarla para cualquiera, Google
   exige verificar la app (ese alcance es "sensible").
4. **Credenciales › Crear clave de API** (para Time Zone): restríngela a la **Time Zone API** y a **apps Android**
   (nombre de paquete + huella SHA‑1 del certificado de firma).
5. **Credenciales › Crear ID de cliente de OAuth**:
   - **Android**: nombre de paquete + SHA‑1 (obtén la huella con
     `keytool -list -v -keystore tu.keystore -alias tu_alias`). En **Configuración avanzada**, activa
     **"Habilitar esquema de URI personalizado"** (Google lo desactiva por defecto en clientes nuevos).
   - **Escritorio** (para probar en el Editor de Unity o en Windows): no necesita más datos; copia ID y secreto.

## 2. Unity

1. `Assets > Create > Composta > Configuración de Google` y guárdalo como
   **`Assets/Resources/ConfiguracionGoogle.asset`** (está en `.gitignore`).
2. Rellena: clave de Time Zone, `clientIdAndroid`, `clientIdEscritorio` + `clientSecretEscritorio`,
   latitud/longitud de tu comunidad (respaldo si no hay permiso de ubicación) y la hora de los recordatorios.
3. **Player Settings › Android**:
   - *Identification › Package Name*: el mismo del cliente OAuth Android (por ejemplo `com.tucomunidad.composta`;
     hoy está el de la plantilla `com.UnityTechnologies...`). Usa **minúsculas**: también es el esquema del deep link.
   - *Configuration*: **Scripting Backend IL2CPP**, *Target Architectures* **ARM64**, *Minimum API Level* 26,
     *Target API Level* el más alto instalado.
   - *Resolution and Presentation › Default Orientation*: **Auto Rotation** (Portrait, Landscape Left/Right).
   - *Publishing Settings*: firma con tu keystore y marca **Custom Main Manifest**; en el
     `Assets/Plugins/Android/AndroidManifest.xml` generado, pega el intent-filter de
     `docs/android/AndroidManifest.intent-filter.xml` dentro de la actividad principal.
   - Los permisos `INTERNET` y `ACCESS_COARSE_LOCATION`/`FINE_LOCATION` los agrega Unity solo
     (UnityWebRequest e `Input.location`). La ubicación se pide en tiempo de ejecución y es opcional.
4. Compila: *File › Build Profiles › Android › Build*.

## 3. Probar sin teléfono

- **Diseño móvil en el Editor:** usa la ventana **Device Simulator** (Window › General › Device Simulator) o
  pulsa **F9** en Play para alternar el diseño compacto.
- **Google en el Editor:** con el cliente de *Escritorio* configurado, "Exportar hitos a Google Calendar" abre el
  navegador, pides permiso y vuelves a la app (se usa 127.0.0.1 con un puerto libre).

## 4. Diseño móvil

| Pieza | Qué hace |
|---|---|
| `AdaptadorPantalla` | Escritorio: 1920×1080 escalado. Móvil: escala física (texto de 18 u ≈ 14 sp). Si el lienzo es estrecho o bajo → diseño **compacto**. |
| `AreaSegura` | Cada lienzo dibuja dentro de `Screen.safeArea` (notch, perforación, barra de navegación). |
| `Responsivo` | Anclas para diseño amplio y compacto por elemento. |
| `ContenidoDesplazable` / `Carrusel` | Pantallas apiladas con desplazamiento vertical; tipos de compostera en carrusel horizontal. |
| `AreaTactilMinima` | Zona táctil de al menos 48 dp en botones e íconos pequeños. |
| Tooltips | En pantallas táctiles se abren con un toque y se cierran al tocar fuera o a los 7 s; nunca más anchos que la pantalla. |
| Botón Atrás | Cierra ventanas (llega como Escape). |

## 5. Límites conocidos

- Google restringe el esquema de URI personalizado en clientes Android nuevos (hay que activarlo en la configuración
  avanzada del cliente). Si tu proyecto no lo permite, la alternativa recomendada por Google es un plugin nativo
  con Google Identity Services (`AuthorizationClient`); la app ya separa ese paso detrás de `IReceptorRedireccion`.
- El token de actualización se guarda en `PlayerPrefs` (almacenamiento privado de la app en Android). Para mayor
  seguridad puede migrarse al Android Keystore con un plugin nativo.
- La predicción supone manejo ideal (volteo y riego automáticos) y clima constante; las fechas son orientativas.
