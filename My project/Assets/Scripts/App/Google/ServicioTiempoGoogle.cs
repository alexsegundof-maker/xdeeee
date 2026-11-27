using System;
using System.Globalization;
using System.Threading.Tasks;
using Composta.Core.Pronostico;
using UnityEngine;

namespace Composta.App.Google
{
    // Instantánea de tiempo real: la hora UTC (avanza con el reloj monotónico de la app) y la zona horaria
    public sealed class InfoTiempo
    {
        public DateTime UtcAlConsultar;
        public float SegundosAppAlConsultar;
        public ZonaHoraria Zona;
        public bool DeGoogle;
        public string Detalle = "";

        // No depende de que el usuario cambie la hora del teléfono después de consultar
        public DateTime AhoraUtc => UtcAlConsultar.AddSeconds(Time.realtimeSinceStartup - SegundosAppAlConsultar);
        public DateTime AhoraLocal => DateTime.SpecifyKind(AhoraUtc + Zona.Desplazamiento, DateTimeKind.Unspecified);
    }

    // Google Time Zone API: devuelve la zona IANA y el desplazamiento (incluido horario de verano) para una ubicación.
    // La hora UTC real se toma del encabezado "Date" de la respuesta de Google (no del reloj del teléfono).
    // Sin conexión o sin clave: hora y zona del dispositivo.
    public static class ServicioTiempoGoogle
    {
        const string Url = "https://maps.googleapis.com/maps/api/timezone/json";
        const double HorasCache = 6;

#pragma warning disable 0649   // DTOs que llena JsonUtility por reflexión
        [Serializable] class RespuestaTz { public string status; public string errorMessage; public double rawOffset; public double dstOffset; public string timeZoneId; public string timeZoneName; }
#pragma warning restore 0649

        static InfoTiempo cache;
        static Task<InfoTiempo> enCurso;

        public static InfoTiempo Actual => cache ?? Local("Hora del dispositivo (todavía no se consultó a Google)");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reiniciar() { cache = null; enCurso = null; }

        public static Task<InfoTiempo> ObtenerAsync(bool forzar = false)
        {
            if (!forzar && cache != null && cache.DeGoogle && (cache.AhoraUtc - cache.UtcAlConsultar).TotalHours < HorasCache)
                return Task.FromResult(cache);
            if (enCurso != null && !enCurso.IsCompleted) return enCurso;   // evita consultas duplicadas
            enCurso = Consultar();
            return enCurso;
        }

        static async Task<InfoTiempo> Consultar()
        {
            var cfg = ConfiguracionGoogle.Cargar();
            if (cfg == null || !cfg.TieneTimeZone)
                return cache = Local("Hora del dispositivo (configura la clave de Google Time Zone API)");

            var (lat, lng, origen) = await UbicacionGoogle.ObtenerAsync(cfg);
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string url = string.Format(CultureInfo.InvariantCulture, "{0}?location={1:0.######},{2:0.######}&timestamp={3}&key={4}",
                                       Url, lat, lng, ts, Uri.EscapeDataString(cfg.claveApiTimeZone));
            var r = await HttpGoogle.Get(url, null, cfg.segundosTimeout);
            if (!r.Exito) return cache = Local(r.SinRed ? "Sin conexión: hora del dispositivo" : $"Google Time Zone respondió {r.Codigo}: hora del dispositivo");

            RespuestaTz dto;
            try { dto = JsonUtility.FromJson<RespuestaTz>(r.Texto); }
            catch (Exception) { return cache = Local("Respuesta inválida de Google Time Zone: hora del dispositivo"); }
            if (dto == null || dto.status != "OK")
                return cache = Local($"Google Time Zone: {dto?.status} {dto?.errorMessage}".Trim() + " · hora del dispositivo");

            bool horaGoogle = Core.Pronostico.UtilGoogle.IntentarLeerFechaHttp(r.Encabezado("Date"), out var utcServidor);
            var info = new InfoTiempo
            {
                UtcAlConsultar = horaGoogle ? utcServidor : DateTime.UtcNow,
                SegundosAppAlConsultar = Time.realtimeSinceStartup,
                Zona = new ZonaHoraria(dto.timeZoneId, TimeSpan.FromSeconds(dto.rawOffset + dto.dstOffset), "Google Time Zone API"),
                DeGoogle = true,
                Detalle = $"Hora {(horaGoogle ? "de Google" : "del dispositivo")} · zona de Google ({origen})"
            };
            double desfaseMin = Math.Abs((DateTime.UtcNow - info.UtcAlConsultar).TotalMinutes);
            if (horaGoogle && desfaseMin > 2)
                info.Detalle += string.Format(CultureInfo.InvariantCulture, " · el reloj del teléfono difiere {0:F0} min", desfaseMin);
            return cache = info;
        }

        static InfoTiempo Local(string motivo) => new InfoTiempo
        {
            UtcAlConsultar = DateTime.UtcNow,
            SegundosAppAlConsultar = Time.realtimeSinceStartup,
            Zona = new ZonaHoraria(null, TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow), "dispositivo"),
            DeGoogle = false,
            Detalle = motivo
        };
    }

    // Ubicación aproximada (solo para elegir la zona horaria): GPS/red del teléfono o la configurada
    public static class UbicacionGoogle
    {
        public static async Task<(double lat, double lng, string origen)> ObtenerAsync(ConfiguracionGoogle cfg)
        {
            double lat = cfg.latitudPorDefecto, lng = cfg.longitudPorDefecto;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!cfg.usarUbicacionDelDispositivo) return (lat, lng, "ubicación configurada");
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.CoarseLocation))
            {
                var respuesta = new TaskCompletionSource<bool>();
                var cb = new UnityEngine.Android.PermissionCallbacks();
                cb.PermissionGranted += _ => respuesta.TrySetResult(true);
                cb.PermissionDenied += _ => respuesta.TrySetResult(false);
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.CoarseLocation, cb);
                if (!await respuesta.Task) return (lat, lng, "ubicación configurada (permiso denegado)");
            }
            if (!Input.location.isEnabledByUser) return (lat, lng, "ubicación configurada (ubicación del teléfono desactivada)");
            Input.location.Start(5000f, 5000f);                       // precisión baja: basta para la zona horaria
            for (int i = 0; i < 40 && Input.location.status == LocationServiceStatus.Initializing; i++) await Task.Delay(250);
            if (Input.location.status == LocationServiceStatus.Running)
            {
                var d = Input.location.lastData;
                Input.location.Stop();
                return (d.latitude, d.longitude, "ubicación del teléfono");
            }
            Input.location.Stop();
            return (lat, lng, "ubicación configurada (sin señal)");
#else
            await Task.CompletedTask;
            return (lat, lng, "ubicación configurada");
#endif
        }
    }
}
