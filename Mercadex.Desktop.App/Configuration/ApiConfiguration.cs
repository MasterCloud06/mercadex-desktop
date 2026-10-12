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
    {var baseUrl = Configuration["ApiSettings:BaseUrl"];

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

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "La URL de la API debe usar HTTP o HTTPS.");
        }

        return uri;
    }
}