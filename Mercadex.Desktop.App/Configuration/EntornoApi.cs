using System.Net.Http;

namespace Mercadex.Desktop.App.Configuration;

/// <summary>
/// Reglas comunes para decidir si la API configurada es valida.
/// Produccion: HTTPS (https://mercadex-api.onrender.com/).
/// Desarrollo: se admite HTTP solo contra la propia computadora
/// (localhost / 127.0.0.1), configurado en appsettings.json.
/// </summary>
public static class EntornoApi
{
    public static bool ConexionValida(Uri? uri)
    {
        if (uri is null)
            return false;

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }

    public static bool ConexionValida(HttpClient api)
    {
        return ConexionValida(api.BaseAddress);
    }

    public static bool ConexionAutenticada(HttpClient api)
    {
        return ConexionValida(api) &&
               api.DefaultRequestHeaders.Authorization is not null;
    }
}
