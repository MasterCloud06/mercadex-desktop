using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Llamadas JSON sencillas a la API con mensajes de error legibles
/// (toma el campo "mensaje" que devuelve el servidor).
/// </summary>
public static class ApiSimple
{
    private static readonly HttpClient Http = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(60));

    public static async Task<JsonElement> GetAsync(string url) =>
        await LeerAsync(await Http.GetAsync(url));

    public static async Task<JsonElement> PostAsync(string url, object? cuerpo = null) =>
        await LeerAsync(await Http.PostAsJsonAsync(url, cuerpo ?? new { }));

    public static async Task<JsonElement> PutAsync(string url, object cuerpo) =>
        await LeerAsync(await Http.PutAsJsonAsync(url, cuerpo));

    public static async Task<byte[]> GetBytesAsync(string url)
    {
        using var respuesta = await Http.GetAsync(url);
        if (!respuesta.IsSuccessStatusCode)
            await LeerAsync(respuesta);
        return await respuesta.Content.ReadAsByteArrayAsync();
    }

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage respuesta)
    {
        using (respuesta)
        {
            var texto = await respuesta.Content.ReadAsStringAsync();

            if (respuesta.IsSuccessStatusCode)
                return string.IsNullOrWhiteSpace(texto)
                    ? default
                    : JsonDocument.Parse(texto).RootElement.Clone();

            var mensaje = $"El servidor respondió {(int)respuesta.StatusCode}.";
            try
            {
                using var doc = JsonDocument.Parse(texto);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("mensaje", out var m) &&
                    m.GetString() is { Length: > 0 } t)
                    mensaje = t;
            }
            catch (JsonException)
            {
            }

            throw new InvalidOperationException(mensaje);
        }
    }

    public static string Texto(this JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
