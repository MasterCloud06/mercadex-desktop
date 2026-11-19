using Mercadex.Desktop.App.Dtos;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Textos del recibo compartidos por el PDF, la vista previa, la reimpresión
/// y la pantalla de recibos (transacción QR, tipo de cambio, anulación, firma).
/// </summary>
public static class TextoRecibo
{
    public static string? Referencia(PagoDto pago)
    {
        if (string.IsNullOrWhiteSpace(pago.Referencia))
            return null;

        return string.Equals(pago.FormaPago, "QR", StringComparison.OrdinalIgnoreCase)
            ? $"N.º de transacción QR: {pago.Referencia}"
            : $"N.º de transacción/referencia: {pago.Referencia}";
    }

    public static string? TipoCambio(PagoDto pago) =>
        // Solo tiene sentido cuando se cobra en otra moneda (tipo de cambio distinto de 1).
        pago.TipoCambio > 0 && pago.TipoCambio != 1 ? $"Tipo de cambio: {pago.TipoCambio:N2}" : null;

    /// <summary>Encabezado de una reimpresión: no es un nuevo cobro.</summary>
    public const string Reimpresion = "REIMPRESIÓN DEL COMPROBANTE ORIGINAL — no es un nuevo cobro";

    /// <summary>Cuándo, quién y por qué se anuló (solo recibos anulados).</summary>
    public static string? Anulacion(ReciboDto recibo)
    {
        if (!string.Equals(recibo.Estado, "Anulado", StringComparison.OrdinalIgnoreCase))
            return null;

        return "ANULADO" +
               (recibo.FechaAnulacion is { } f ? $" el {f.ToLocalTime():dd/MM/yyyy HH:mm}" : "") +
               (string.IsNullOrWhiteSpace(recibo.AnuladoPor) ? "" : $" por {recibo.AnuladoPor}") +
               (string.IsNullOrWhiteSpace(recibo.MotivoAnulacion) ? "" : $". Motivo: {recibo.MotivoAnulacion}");
    }

    /// <summary>Observaciones que escribió la caja al cobrar (opcional).</summary>
    public static string? Observaciones(ReciboDto recibo) =>
        string.IsNullOrWhiteSpace(recibo.Observaciones) ? null : $"Observaciones: {recibo.Observaciones.Trim()}";

    /// <summary>
    /// Bloque de firma del recibo: nombre completo y cargo de quien lo emitió
    /// (observación de la directiva). Null en recibos anteriores al inicio de sesión.
    /// </summary>
    public static (string Nombre, string? Cargo)? Emisor(ReciboDto recibo) =>
        string.IsNullOrWhiteSpace(recibo.RegistradoPor)
            ? null
            : (recibo.RegistradoPor!.Trim(), string.IsNullOrWhiteSpace(recibo.CargoEmisor) ? null : recibo.CargoEmisor!.Trim());

    public const string LineaFirma = "______________________________";
}
