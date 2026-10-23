using Mercadex.Desktop.App.Dtos;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Datos del recibo que pide el manual (tipo de ingreso, tipo de cambio,
/// cantidad, número de transacción QR y quién cobró), iguales en el PDF,
/// la vista previa y la reimpresión.
/// </summary>
public static class TextoRecibo
{
    public static string TipoIngreso(ReciboDto recibo)
    {
        var conceptos = (recibo.Detalles ?? new())
            .Select(d => d.Concepto?.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return conceptos.Count switch
        {
            0 => "Tipo de ingreso: Cobranza",
            1 => $"Tipo de ingreso: {conceptos[0]}",
            _ => $"Tipo de ingreso: Varios servicios ({conceptos.Count})"
        };
    }

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

    public static string Cantidad(ReciboDto recibo) =>
        $"Cantidad de conceptos: {(recibo.Detalles ?? new()).Count}";

    /// <summary>Línea de un concepto: cantidad, concepto, período e importe.</summary>
    public static string Concepto(DetalleReciboDto detalle, string moneda) =>
        $"1 x {detalle.Concepto} | {detalle.Periodo} | {detalle.Monto:N2} {moneda}";

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

    public static string? CobradoPor(ReciboDto recibo) =>
        string.IsNullOrWhiteSpace(recibo.RegistradoPor) ? null : $"Elaborado por: {recibo.RegistradoPor}";

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
