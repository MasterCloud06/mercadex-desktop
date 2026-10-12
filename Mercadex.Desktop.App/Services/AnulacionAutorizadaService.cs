using Mercadex.Desktop.App.Api;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Anula un recibo en la API. La API anula recibo y pago,
/// restituye las obligaciones como pendientes y registra el
/// comprobante de anulación en una sola transacción.
/// </summary>
public sealed class AnulacionAutorizadaService
{
    private readonly ApiClient _api = new();

    public async Task AnularAsync(
        Guid reciboId,
        string motivo)
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

        await _api.PutAsync(
            $"/api/Caja/recibos/{reciboId}/anular",
            new
            {
                Motivo = motivo
            });
    }
}
