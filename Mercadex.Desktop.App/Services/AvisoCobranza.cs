using System.Globalization;

namespace Mercadex.Desktop.App.Services;

/// <summary>Una deuda pendiente para el aviso de cobranza.</summary>
public sealed record DeudaAviso(
    string Concepto,
    int? NumeroConcepto,
    int? Anio,
    int? Mes,
    string Moneda,
    decimal Saldo,
    bool TieneAbonos = false);

/// <summary>Las deudas de una moneda con su total.</summary>
public sealed record SeccionAviso(
    string Moneda,
    IReadOnlyList<(string Servicio, string Monto)> Lineas,
    string Total);

public sealed record AvisoImpreso(
    string Local,
    string? Bloque,
    string Socio,
    string ImpresoFecha,
    string ImpresoHora,
    IReadOnlyList<SeccionAviso> Secciones);

/// <summary>
/// Extracto de deuda con el formato del «Aviso de cobranza» del sistema
/// anterior: local, bloque, socio, moneda, una línea por servicio y mes
/// («APORTE MENSUAL feb/2024») con su monto, el total y la invitación a
/// pasar por oficina. La pantalla y la impresión salen de aquí.
/// </summary>
public static class AvisoCobranza
{
    public const string Titulo = "AVISO DE COBRANZA";
    public const string Pie = "SEÑOR SOCIO: PUEDE PASAR POR OFICINA A PAGAR SUS CUENTAS PENDIENTES.";

    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private static readonly string[] Meses = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

    /// <summary>«APORTE MENSUAL feb/2024» (como el aviso anterior); con abonos, «(saldo)».</summary>
    public static string Servicio(DeudaAviso d)
    {
        var texto = (d.Concepto ?? "").Trim().ToUpper(Bo);
        if (d.Anio is { } a && d.Mes is >= 1 and <= 12)
            texto += $" {Meses[d.Mes.Value - 1]}/{a}";
        return d.TieneAbonos ? texto + " (saldo)" : texto;
    }

    /// <summary>Mes por mes; dentro del mes, por el número del concepto (como en caja).</summary>
    public static List<DeudaAviso> Ordenar(IEnumerable<DeudaAviso> deudas) =>
        deudas
            .OrderBy(d => d.Anio ?? int.MaxValue)
            .ThenBy(d => d.Mes ?? int.MaxValue)
            .ThenBy(d => d.NumeroConcepto ?? int.MaxValue)
            .ThenBy(d => d.Concepto, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>«BS» para bolivianos, como el aviso anterior.</summary>
    public static string Moneda(string? codigo)
    {
        var c = (codigo ?? "").Trim().ToUpperInvariant();
        return c is "BOB" or "BS" or "" ? "BS" : c;
    }

    public static AvisoImpreso Crear(string local, string? bloque, string socio, IEnumerable<DeudaAviso> deudas, DateTime impreso)
    {
        var secciones = Ordenar(deudas)
            .GroupBy(d => Moneda(d.Moneda))
            .OrderBy(g => g.Key == "BS" ? 0 : 1).ThenBy(g => g.Key)
            .Select(g => new SeccionAviso(
                g.Key,
                g.Select(d => (Servicio(d), d.Saldo.ToString("N2", Bo))).ToList(),
                g.Sum(d => d.Saldo).ToString("N2", Bo)))
            .ToList();

        return new AvisoImpreso(
            local.Trim(),
            string.IsNullOrWhiteSpace(bloque) ? null : bloque.Trim().ToUpper(Bo),
            (socio ?? "").Trim().ToUpper(Bo),
            impreso.ToString("dd/MM/yyyy", Bo),
            impreso.ToString("HH:mm", Bo),
            secciones);
    }
}
