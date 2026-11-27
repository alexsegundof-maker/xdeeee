using System.Text;

namespace Composta.Core.Exportacion
{
    // Nombres de archivo seguros en Windows, Android y servicios en la nube: sin acentos, sin espacios
    // ni símbolos. "Compostera doméstica · Huerto Ñandú" -> "compostera-domestica-huerto-nandu".
    // La tabla de acentos es explícita (no depende de string.Normalize, que no todas las plataformas traen).
    public static class NombreArchivo
    {
        const string ConAcento = "áàäâãåéèëêíìïîóòöôõúùüûñçý";
        const string SinAcento = "aaaaaaeeeeiiiiooooouuuuncy";

        public static string Slug(string texto, int maximo = 40, string siVacio = "lote")
        {
            var sb = new StringBuilder();
            bool guion = false;
            foreach (char original in (texto ?? "").ToLowerInvariant())
            {
                char ch = original;
                int i = ConAcento.IndexOf(ch);
                if (i >= 0) ch = SinAcento[i];
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                {
                    if (guion && sb.Length > 0) sb.Append('-');
                    guion = false;
                    sb.Append(ch);
                    if (sb.Length >= maximo) break;
                }
                else guion = true;
            }
            string r = sb.ToString().Trim('-');
            return r.Length == 0 ? siVacio : r;
        }
    }
}
