using Mercadex.Desktop.App.Api;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Anula un recibo en la API con autorización de administrador.
/// La API anula recibo y pago, restituye las obligaciones como
/// pendientes y registra el comprobante de anulación en una
/// sola transacción.
/// </summary>
public sealed class AnulacionAutorizadaService
{
    private readonly ApiClient _api = new();

    public async Task AnularAsync(
        Guid reciboId,
        string motivo,
        string usuario,
        string contrasena)
    {
        if (reciboId == Guid.Empty)
        {
            throw new ArgumentException(
                "Selecciona un recibo valido.");
        }

        motivo = motivo?.Trim() ?? "";

        if (motivo.Length < 5 || motivo.Length > 500)
        {
            throw new ArgumentException(
                "El motivo debe tener entre 5 y 500 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(usuario) ||
            string.IsNullOrWhiteSpace(contrasena))
        {
            throw new ArgumentException(
                "Se requiere usuario y contrasena de administrador.");
        }

        var sesion = await _api.PostAsync<
            object,
            SesionAnulacionRespuesta>(
            "/api/auth/login",
            new
            {
                usuario = usuario.Trim(),
                password = contrasena
            });

        if (string.IsNullOrWhiteSpace(sesion?.Token))
        {
            throw new InvalidOperationException(
                "No se recibio autorizacion de administrador.");
        }

        await _api.PutAsync(
            $"/api/Caja/recibos/{reciboId}/anular",
            new
            {
                Motivo = motivo
            },
            sesion.Token);
    }
}

public sealed class SesionAnulacionRespuesta
{
    public string Token { get; set; } = "";
}
