using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Llamadas JSON a la API con mensajes claros: si la API responde
/// { "mensaje": "..." } se muestra ese texto.
/// </summary>
public static class ApiJson
{
    private static readonly HttpClient Http = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(90));

    public static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    public static Task<T?> GetAsync<T>(string ruta) => EnviarAsync<T>(HttpMethod.Get, ruta, null);

    public static Task<T?> PostAsync<T>(string ruta, object cuerpo) => EnviarAsync<T>(HttpMethod.Post, ruta, cuerpo);

    public static Task<T?> PutAsync<T>(string ruta, object cuerpo) => EnviarAsync<T>(HttpMethod.Put, ruta, cuerpo);

    public static async Task<T?> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo)
    {
        using var solicitud = new HttpRequestMessage(metodo, ruta);

        if (cuerpo is not null)
            solicitud.Content = JsonContent.Create(cuerpo, options: Opciones);

        HttpResponseMessage respuesta;

        try
        {
            respuesta = await Http.SendAsync(solicitud);
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException(
                "El servidor tardó demasiado en responder. Si estuvo inactivo puede tardar hasta un minuto: vuelve a intentar.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("No hay conexión con el servidor. Revisa el internet y vuelve a intentar.");
        }

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode)
            {
                if (respuesta.Content.Headers.ContentLength == 0)
                    return default;

                return await respuesta.Content.ReadFromJsonAsync<T>(Opciones);
            }

            var mensaje = (int)respuesta.StatusCode switch
            {
                401 => "Tu sesión terminó. Vuelve a ingresar.",
                403 => "Tu usuario no tiene permiso para esta acción.",
                404 => "No se encontró lo que buscabas. Puede que la API todavía no tenga esta función publicada.",
                _ => $"No se pudo completar (error {(int)respuesta.StatusCode})."
            };

            try
            {
                using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("mensaje", out var m) &&
                    m.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(m.GetString()))
                {
                    mensaje = m.GetString()!;
                }
            }
            catch
            {
                // Sin cuerpo JSON: se usa el mensaje general.
            }

            throw new InvalidOperationException(mensaje);
        }
    }

    /// <summary>Descarga un archivo (CSV, PDF…) de la API con la sesión iniciada.</summary>
    public static async Task<byte[]> BytesAsync(string ruta)
    {
        HttpResponseMessage respuesta;

        try
        {
            respuesta = await Http.GetAsync(ruta);
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("El servidor tardó demasiado en responder. Vuelve a intentar.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("No hay conexión con el servidor. Revisa el internet y vuelve a intentar.");
        }

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode)
                return await respuesta.Content.ReadAsByteArrayAsync();

            throw new InvalidOperationException((int)respuesta.StatusCode switch
            {
                401 => "Tu sesión terminó. Vuelve a ingresar.",
                403 => "Tu usuario no tiene permiso para esta acción.",
                404 => "La API todavía no tiene esta función publicada.",
                _ => $"No se pudo descargar (error {(int)respuesta.StatusCode})."
            });
        }
    }

    /// <summary>Lee un monto escrito con coma o punto. Null si no es válido.</summary>
    public static decimal? LeerMonto(string? texto)
    {
        var limpio = (texto ?? "").Trim().Replace(" ", "");
        if (limpio.Length == 0)
            return null;

        // "1.234,50" o "1234,50" o "1234.50"
        if (limpio.Contains(',') && limpio.Contains('.'))
            limpio = limpio.Replace(".", "").Replace(',', '.');
        else
            limpio = limpio.Replace(',', '.');

        return decimal.TryParse(limpio, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) &&
               valor > 0 && decimal.Round(valor, 2) == valor
            ? valor
            : null;
    }
}
