using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Api;

public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient()
    {
        _httpClient = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(45));
    }

    public async Task<T?> GetAsync<T>(string url)
    {
        using var response =
            await _httpClient.GetAsync(url);

        await ValidarRespuestaAsync(response);

        return await response.Content
            .ReadFromJsonAsync<T>();
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest data,
        string? token = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            url)
        {
            Content = JsonContent.Create(data)
        };

        // La autorización se aplica únicamente a esta solicitud.
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);
        }

        using var response =
            await _httpClient.SendAsync(request);

        await ValidarRespuestaAsync(response);

        return await response.Content
            .ReadFromJsonAsync<TResponse>();
    }

    public async Task PutAsync<TRequest>(
        string url,
        TRequest data,
        string? token = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            url)
        {
            Content = JsonContent.Create(data)
        };

        // La autorización se aplica únicamente a esta solicitud.
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);
        }

        using var response =
            await _httpClient.SendAsync(request);

        await ValidarRespuestaAsync(response);
    }

    private static async Task ValidarRespuestaAsync(
        HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var mensaje = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "No se pudo autenticar la solicitud. " +
                "Comprueba tus credenciales o inicia sesión nuevamente.",

            HttpStatusCode.Forbidden =>
                "Tu usuario no tiene permiso para realizar esta operación.",

            HttpStatusCode.NotFound =>
                "No se encontró el recurso solicitado. " +
                "Comprueba que la API tenga publicada esta función.",

            HttpStatusCode.Conflict =>
                "El registro ya existe o entra en conflicto con otro.",

            HttpStatusCode.BadRequest =>
                "Revisa los datos introducidos.",

            _ =>
                $"El servidor respondió con el error " +
                $"HTTP {(int)response.StatusCode}."
        };

        // Leer mensajes de validación sin mostrar páginas HTML
        // ni detalles internos de errores del servidor.
        if (response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.Conflict)
        {
            var contenido =
                await response.Content.ReadAsStringAsync();

            try
            {
                using var json = JsonDocument.Parse(contenido);

                var raiz = json.RootElement;

                if (raiz.ValueKind == JsonValueKind.Object)
                {
                    foreach (var campo in new[]
                    {
                        "mensaje",
                        "message",
                        "detail",
                        "title"
                    })
                    {
                        if (raiz.TryGetProperty(
                                campo,
                                out var valor) &&
                            valor.ValueKind == JsonValueKind.String)
                        {
                            var texto = valor.GetString();

                            if (!string.IsNullOrWhiteSpace(texto))
                            {
                                mensaje = texto;
                                break;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Mantener el mensaje general si no es JSON.
            }
        }

        throw new HttpRequestException(
            mensaje,
            null,
            response.StatusCode);
    }
}