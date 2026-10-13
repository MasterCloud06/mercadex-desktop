using System.Net.Http;
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

    /// <summary>
    /// Clave interna de la aplicación. Se inyecta al compilar el instalador
    /// (variable MERCADEX_APP_KEY) y nunca se guarda en el repositorio.
    /// En desarrollo puede definirse con la variable de entorno MERCADEX_APP_KEY.
    /// </summary>
    public static string? ClaveApp { get; } = LeerClaveApp();

    public const string EncabezadoClave = "X-Mercadex-Key";

    /// <summary>
    /// Crea un HttpClient hacia la API de Mercadex con la clave interna.
    /// Todas las conexiones del escritorio deben usar este método.
    /// </summary>
    public static HttpClient CrearCliente(TimeSpan timeout)
    {
        // El manejador agrega el token de la sesión iniciada a cada petición.
        var cliente = new HttpClient(new Mercadex.Desktop.App.Sesion.SesionHandler())
        {
            BaseAddress = BaseUri,
            Timeout = timeout
        };

        if (!string.IsNullOrWhiteSpace(ClaveApp))
            cliente.DefaultRequestHeaders.Add(EncabezadoClave, ClaveApp);

        return cliente;
    }

    private static string? LeerClaveApp()
    {
        var entorno = Environment.GetEnvironmentVariable("MERCADEX_APP_KEY");

        if (!string.IsNullOrWhiteSpace(entorno))
            return entorno.Trim();

        return typeof(ApiConfiguration).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MercadexAppKey")?
            .Value is { Length: > 0 } valor
                ? valor
                : null;
    }

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