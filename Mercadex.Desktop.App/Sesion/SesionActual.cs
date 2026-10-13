using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Sesion;

/// <summary>
/// Usuario que inició sesión en este equipo. El token viaja en cada
/// petición a la API (ver <see cref="SesionHandler"/>).
/// </summary>
public static class SesionActual
{
    private static int _avisoVencida;

    public static string? Token { get; private set; }

    public static string Usuario { get; private set; } = "";

    public static string Nombre { get; private set; } = "";

    public static string Rol { get; private set; } = "";

    public static bool Principal { get; private set; }

    public static DateTime ExpiraUtc { get; private set; }

    public static bool Iniciada => Token is not null;

    public static bool EsAdministrador => Rol == "Administrador";

    public static bool PuedeCobrar => Rol is "Administrador" or "Cajero";

    public static bool SoloConsulta => Rol == "Consulta";

    public static string RolTexto => Rol switch
    {
        "Administrador" => "Administrador",
        "Cajero" => "Cajero",
        "Consulta" => "Solo consulta",
        _ => Rol
    };

    /// <summary>La API rechazó la sesión (vencida, usuario desactivado o contraseña cambiada).</summary>
    public static event Action? SesionVencida;

    internal static void Establecer(RespuestaSesion r)
    {
        Token = r.Token;
        Usuario = r.Usuario ?? "";
        Nombre = string.IsNullOrWhiteSpace(r.Nombre) ? Usuario : r.Nombre!;
        Rol = r.Rol ?? "";
        Principal = r.Principal;
        ExpiraUtc = r.Expira.Kind == DateTimeKind.Utc ? r.Expira : r.Expira.ToUniversalTime();
        Interlocked.Exchange(ref _avisoVencida, 0);
    }

    public static void Cerrar()
    {
        Token = null;
        Nombre = "";
        Rol = "";
        Principal = false;
    }

    internal static void NotificarVencida()
    {
        if (Token is null || Interlocked.Exchange(ref _avisoVencida, 1) == 1)
            return;

        Application.Current?.Dispatcher.BeginInvoke(() => SesionVencida?.Invoke());
    }

    // ---------------- Llamadas a la API ----------------

    private static readonly HttpClient Http = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(75));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Inicia sesión. Devuelve la respuesta o lanza una excepción con un mensaje para el usuario.</summary>
    public static async Task<RespuestaSesion> IngresarAsync(string usuario, string contrasena)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { usuario, password = contrasena })
        };

        var r = await EnviarAsync<RespuestaSesion>(solicitud);
        Establecer(r);
        return r;
    }

    /// <summary>Cambia la contraseña del usuario actual y renueva la sesión.</summary>
    public static async Task CambiarContrasenaAsync(string actual, string nueva)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, "api/auth/contrasena")
        {
            Content = JsonContent.Create(new { actual, nueva })
        };

        if (Token is not null)
            solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

        var r = await EnviarAsync<RespuestaSesion>(solicitud);
        Establecer(r);
    }

    private static async Task<T> EnviarAsync<T>(HttpRequestMessage solicitud)
    {
        HttpResponseMessage respuesta;

        try
        {
            respuesta = await Http.SendAsync(solicitud);
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException(
                "El servidor tardó demasiado en responder. Si estuvo inactivo, puede tardar hasta un minuto: vuelve a intentar.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("No hay conexión con el servidor. Revisa el internet y vuelve a intentar.");
        }

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode)
                return (await respuesta.Content.ReadFromJsonAsync<T>(Json))!;

            var mensaje = respuesta.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Usuario o contraseña incorrectos.",
                HttpStatusCode.TooManyRequests => "Demasiados intentos. Espera unos minutos y vuelve a intentar.",
                HttpStatusCode.ServiceUnavailable => "El inicio de sesión no está configurado en el servidor.",
                _ => $"No se pudo completar (error {(int)respuesta.StatusCode})."
            };

            try
            {
                var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaError>(Json);
                if (!string.IsNullOrWhiteSpace(cuerpo?.Mensaje))
                    mensaje = cuerpo!.Mensaje!;
            }
            catch
            {
                // Respuesta sin JSON: se usa el mensaje genérico.
            }

            throw new InvalidOperationException(mensaje);
        }
    }

    private sealed record RespuestaError(string? Mensaje);
}

public sealed class RespuestaSesion
{
    public string Token { get; set; } = "";

    public string? Usuario { get; set; }

    public string? Nombre { get; set; }

    public string? Rol { get; set; }

    public DateTime Expira { get; set; }

    public bool DebeCambiarContrasena { get; set; }

    public bool Principal { get; set; }
}

/// <summary>
/// Agrega el token de la sesión a cada petición y avisa si la API
/// responde que la sesión ya no vale.
/// </summary>
public sealed class SesionHandler : DelegatingHandler
{
    public SesionHandler()
        : base(new HttpClientHandler())
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = SesionActual.Token;

        if (token is not null && request.Headers.Authorization is null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var respuesta = await base.SendAsync(request, cancellationToken);

        if (respuesta.StatusCode == HttpStatusCode.Unauthorized &&
            token is not null &&
            request.RequestUri?.AbsolutePath.Contains("/api/auth/", StringComparison.OrdinalIgnoreCase) != true)
        {
            SesionActual.NotificarVencida();
        }

        return respuesta;
    }
}
