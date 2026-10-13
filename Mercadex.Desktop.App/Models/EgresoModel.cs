namespace Mercadex.Desktop.App.Models;

public sealed class EgresoModel
{
    public Guid Id { get; set; }

    public long Numero { get; set; }

    public string NumeroTexto => Numero > 0 ? Numero.ToString("000000") : "—";

    public DateOnly Fecha { get; set; }

    public string Categoria { get; set; } = string.Empty;

    public string Concepto { get; set; } = string.Empty;

    public string Beneficiario { get; set; } = string.Empty;

    public decimal Monto { get; set; }

    public Guid MonedaId { get; set; }

    public string FormaPago { get; set; } = string.Empty;

    public string? NumeroComprobante { get; set; }

    public string? Observacion { get; set; }

    public DateTimeOffset FechaRegistro { get; set; }

    public bool Anulado { get; set; }

    public DateTimeOffset? FechaAnulacion { get; set; }

    public string? MotivoAnulacion { get; set; }

    public string? RegistradoPor { get; set; }

    public string? AnuladoPor { get; set; }

    public string RegistradoPorTexto => string.IsNullOrWhiteSpace(RegistradoPor) ? "—" : RegistradoPor!;

    /// <summary>Detalle de la anulación (quién, cuándo y por qué).</summary>
    public string? DetalleAnulacion => Anulado
        ? $"Anulado el {FechaAnulacion?.ToLocalTime():dd/MM/yyyy HH:mm}" +
          (string.IsNullOrWhiteSpace(AnuladoPor) ? "" : $" por {AnuladoPor}") +
          $". Motivo: {MotivoAnulacion}"
        : null;

    // Solo para mostrar en Desktop.
    public string MonedaCodigo { get; set; } = string.Empty;

    public string Estado =>
        Anulado
            ? "Anulado"
            : "Registrado";

    public string FechaTexto =>
        Fecha.ToString("dd/MM/yyyy");
}

public sealed class CrearEgresoModel
{
    public DateOnly Fecha { get; set; }

    public string Categoria { get; set; } = string.Empty;

    public string Concepto { get; set; } = string.Empty;

    public string Beneficiario { get; set; } = string.Empty;

    public decimal Monto { get; set; }

    public Guid MonedaId { get; set; }

    public string FormaPago { get; set; } = string.Empty;

    public string? NumeroComprobante { get; set; }

    public string? Observacion { get; set; }
}

public sealed class AnularEgresoModel
{
    public string Motivo { get; set; } = string.Empty;
}

public sealed class ResumenEgresoModel
{
    public string Categoria { get; set; } = string.Empty;

    public Guid MonedaId { get; set; }

    public int Cantidad { get; set; }

    public decimal Total { get; set; }

    public string MonedaCodigo { get; set; } = string.Empty;
}