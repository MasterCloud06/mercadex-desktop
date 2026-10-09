namespace Mercadex.Desktop.App.Models;

public sealed class ObligacionModel
{
    public Guid Id { get; set; }

    public Guid UnidadComercialId { get; set; }

    public Guid ConceptoCobroId { get; set; }

    public Guid MonedaId { get; set; }

    public decimal Monto { get; set; }

    public DateOnly FechaEmision { get; set; }

    public DateOnly? FechaVencimiento { get; set; }

    public int? PeriodoAnio { get; set; }

    public int? PeriodoMes { get; set; }

    public string Estado { get; set; } = string.Empty;
}

public sealed class CrearObligacionRequest
{
    public Guid UnidadComercialId { get; set; }

    public Guid ConceptoCobroId { get; set; }

    public Guid MonedaId { get; set; }

    public decimal Monto { get; set; }

    public DateOnly FechaEmision { get; set; }

    public DateOnly? FechaVencimiento { get; set; }

    public int? PeriodoAnio { get; set; }

    public int? PeriodoMes { get; set; }
}