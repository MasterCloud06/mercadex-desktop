using System.Globalization;
using Mercadex.Desktop.App.Dtos;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Recibo oficial de caja como el del sistema anterior (pedido de contabilidad):
/// solo lo importante y en el orden de una factura. Cabecera (N.º, bloque,
/// fecha, quién pagó, caseta), detalle con su monto, el total en letras y las
/// firmas. La vista previa, la impresión y el PDF salen de aquí, así que los
/// tres muestran lo mismo.
/// </summary>
public sealed record ReciboImpreso(
    long Numero,
    string Titulo,
    string? Reimpresion,
    string? Anulacion,
    string Fecha,
    string? Bloque,
    string RecibimosDe,
    string Caseta,
    string? FormaPago,
    string? TipoCambio,
    string Observacion,
    IReadOnlyList<(string Detalle, string Monto)> Lineas,
    string Total,
    string Son,
    string? Emisor,
    string? CargoEmisor,
    string ImpresoFecha,
    string ImpresoHora)
{
    public bool Anulado => Anulacion is not null;
}

public static class FormatoRecibo
{
    public const string Asociacion = "ASOCIACIÓN DE COMERCIANTES 13 DE JULIO";
    public const string EntregueConforme = "ENTREGUE CONFORME";

    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private static readonly string[] MesesCortos = { "ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC" };

    public static ReciboImpreso Crear(
        ReciboDto recibo, PagoDto pago, string caseta, string bloque, string moneda,
        DateTime impreso, bool reimpresion = false)
    {
        var anulacion = TextoRecibo.Anulacion(recibo);

        // Efectivo es lo normal y no se escribe; QR (u otra forma) sí, con su transacción.
        string? formaPago = null;
        if (!string.Equals(pago.FormaPago, "Efectivo", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(pago.Referencia))
        {
            formaPago = (pago.FormaPago ?? "").Trim().ToUpper(Bo);
            if (!string.IsNullOrWhiteSpace(pago.Referencia))
                formaPago += $"   TRANSACCIÓN: {pago.Referencia.Trim()}";
        }

        var (nombre, cargo) = TextoRecibo.Emisor(recibo) is var (n, c) ? (n, c) : ((string?)null, (string?)null);

        return new ReciboImpreso(
            Numero: recibo.Numero,
            Titulo: anulacion is null ? "RECIBO OFICIAL DE CAJA" : "RECIBO ANULADO",
            Reimpresion: reimpresion ? TextoRecibo.Reimpresion : null,
            Anulacion: anulacion,
            Fecha: recibo.FechaEmision.ToLocalTime().ToString("dd/MM/yyyy", Bo),
            Bloque: string.IsNullOrWhiteSpace(bloque) ? null : bloque.Trim().ToUpper(Bo),
            RecibimosDe: (pago.NombrePagador ?? "").Trim().ToUpper(Bo),
            Caseta: caseta.Trim(),
            FormaPago: formaPago,
            TipoCambio: pago.TipoCambio > 0 && pago.TipoCambio != 1 ? pago.TipoCambio.ToString("N2", Bo) : null,
            Observacion: (recibo.Observaciones ?? "").Trim(),
            Lineas: recibo.Detalles
                .Select(d => (ConceptoConPeriodo(d.Concepto, d.PeriodoAnio, d.PeriodoMes), d.Monto.ToString("N2", Bo)))
                .ToList(),
            Total: recibo.Total.ToString("N2", Bo),
            Son: EnLetras(recibo.Total, moneda),
            Emisor: nombre?.ToUpper(Bo),
            CargoEmisor: cargo,
            ImpresoFecha: impreso.ToString("dd/MM/yyyy", Bo),
            ImpresoHora: impreso.ToString("HH:mm", Bo));
    }

    /// <summary>«APORTE MENSUAL MAR/2026», como en el recibo y los reportes anteriores.</summary>
    public static string ConceptoConPeriodo(string concepto, int? anio, int? mes)
    {
        var nombre = (concepto ?? "").Trim().ToUpper(Bo);
        return anio is { } a && mes is >= 1 and <= 12 ? $"{nombre} {MesesCortos[mes.Value - 1]}/{a}" : nombre;
    }

    /// <summary>«SETENTA Y UN 00/100 BS».</summary>
    public static string EnLetras(decimal total, string moneda)
    {
        var redondeado = Math.Round(Math.Abs(total), 2, MidpointRounding.AwayFromZero);
        var entero = (long)Math.Truncate(redondeado);
        var centavos = (int)((redondeado - entero) * 100);
        var unidad = (moneda ?? "").Trim().ToUpperInvariant() switch
        {
            "BOB" or "BS" or "" => "BS",
            "USD" => "DÓLARES",
            var otra => otra
        };
        return $"{Letras(entero)} {centavos:00}/100 {unidad}";
    }

    private static readonly string[] Unidades =
    {
        "CERO", "UN", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE",
        "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", "DIECIOCHO", "DIECINUEVE",
        "VEINTE", "VEINTIÚN", "VEINTIDÓS", "VEINTITRÉS", "VEINTICUATRO", "VEINTICINCO", "VEINTISÉIS", "VEINTISIETE", "VEINTIOCHO", "VEINTINUEVE"
    };

    private static readonly string[] Decenas = { "", "", "", "TREINTA", "CUARENTA", "CINCUENTA", "SESENTA", "SETENTA", "OCHENTA", "NOVENTA" };

    private static readonly string[] Centenas =
        { "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS" };

    public static string Letras(long n)
    {
        if (n < 30)
            return Unidades[n];
        if (n < 100)
            return Decenas[n / 10] + (n % 10 == 0 ? "" : " Y " + Unidades[n % 10]);
        if (n == 100)
            return "CIEN";
        if (n < 1000)
            return Centenas[n / 100] + (n % 100 == 0 ? "" : " " + Letras(n % 100));
        if (n < 1_000_000)
        {
            var miles = n / 1000;
            var resto = n % 1000;
            return (miles == 1 ? "MIL" : Letras(miles) + " MIL") + (resto == 0 ? "" : " " + Letras(resto));
        }

        var millones = n / 1_000_000;
        var r = n % 1_000_000;
        return (millones == 1 ? "UN MILLÓN" : Letras(millones) + " MILLONES") + (r == 0 ? "" : " " + Letras(r));
    }
}
