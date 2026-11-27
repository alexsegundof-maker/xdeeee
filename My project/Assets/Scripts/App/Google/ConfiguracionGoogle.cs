using UnityEngine;

namespace Composta.App.Google
{
    // Credenciales y preferencias de Google. Crear con: Assets > Create > Composta > Configuración de Google,
    // y guardarlo EXACTAMENTE en Assets/Resources/ConfiguracionGoogle.asset (se carga con Resources.Load).
    // IMPORTANTE: ese archivo está en .gitignore; no subas claves al repositorio.
    [CreateAssetMenu(fileName = "ConfiguracionGoogle", menuName = "Composta/Configuración de Google")]
    public sealed class ConfiguracionGoogle : ScriptableObject
    {
        public const string NombreRecurso = "ConfiguracionGoogle";

        [Header("Time Zone API (clave de API restringida a esa API y a tu app Android)")]
        public string claveApiTimeZone = "";

        [Header("OAuth 2.0 · cliente tipo Android (para la app en el teléfono)")]
        public string clientIdAndroid = "";
        [Tooltip("Esquema de la URI de retorno. Por defecto, el nombre del paquete (Player Settings > Identification)")]
        public string esquemaRedireccionAndroid = "";

        [Header("OAuth 2.0 · cliente tipo Escritorio (para probar en el Editor / Windows)")]
        public string clientIdEscritorio = "";
        [Tooltip("Google entrega un 'secreto' para clientes de escritorio; no es confidencial en apps instaladas")]
        public string clientSecretEscritorio = "";

        [Header("Calendario")]
        [Tooltip("'primary' = calendario principal de la cuenta conectada")]
        public string idCalendario = "primary";
        [Range(0, 23)] public int horaRecordatorios = 9;

        [Header("Ubicación para la zona horaria")]
        [Tooltip("En Android se intenta primero la ubicación del dispositivo (pide permiso)")]
        public bool usarUbicacionDelDispositivo = true;
        public double latitudPorDefecto = 19.4326;      // Ciudad de México: cámbialo por tu comunidad
        public double longitudPorDefecto = -99.1332;

        [Header("Red")]
        [Range(5, 60)] public int segundosTimeout = 15;

        public const string AlcanceCalendario = "https://www.googleapis.com/auth/calendar.events";   // mínimo privilegio

        public bool TieneTimeZone => !string.IsNullOrWhiteSpace(claveApiTimeZone);

        public bool TieneOAuth
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return !string.IsNullOrWhiteSpace(clientIdAndroid);
#else
                return !string.IsNullOrWhiteSpace(clientIdEscritorio);
#endif
            }
        }

        public string EsquemaAndroid => string.IsNullOrWhiteSpace(esquemaRedireccionAndroid) ? Application.identifier : esquemaRedireccionAndroid.Trim();

        static ConfiguracionGoogle cache;
        public static ConfiguracionGoogle Cargar()
        {
            if (cache == null) cache = Resources.Load<ConfiguracionGoogle>(NombreRecurso);
            return cache;
        }
    }
}
