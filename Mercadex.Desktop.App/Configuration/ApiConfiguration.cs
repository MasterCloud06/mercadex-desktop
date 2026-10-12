using Microsoft.Extensions.Configuration;

namespace Mercadex.Desktop.App.Configuration;

public static class ApiConfiguration
{
    private static readonly IConfigurationRoot Configuration =
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(
                "appsettings.json",
                optional: true,
                reloadOnChange: false)
            .Build();

    public static Uri BaseUri { get; } = CrearBaseUri();

    private static Uri CrearBaseUri()
    {
        // Produccion: appsettings.json -> https://mercadex-api.onrender.com/
        // Desarrollo: definir la variable de entorno MERCADEX_API_URL
        // (p. ej. http://127.0.0.1:5189/) sin modificar appsettings.json.
        var baseUrl =
            Environment.GetEnvironmentVariable("MERCADEX_API_URL");

        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = Configuration["ApiSettings:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "No se encontró ApiSettings:BaseUrl en appsettings.json.");
        }

        baseUrl = baseUrl.Trim();

        if (!baseUrl.EndsWith("/"))
        {
            baseUrl += "/";
        }

        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out var uri))
        {
            throw new InvalidOperationException(
                $"La URL de la API no es válida: {baseUrl}");
        }

        if (!EntornoApi.ConexionValida(uri))
        {
            throw new InvalidOperationException(
                "La URL de la API debe usar HTTPS " +
                "(HTTP solo se permite contra localhost).");
        }

        return uri;
    }
}