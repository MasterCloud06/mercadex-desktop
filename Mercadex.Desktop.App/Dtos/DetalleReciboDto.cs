
namespace Mercadex.Desktop.App.Dtos;

public sealed class DetalleReciboDto
{
    public Guid ObligacionId { get; set; }

    public Guid ConceptoCobroId { get; set; }

    public string Concepto { get; set; } = string.Empty;

    public decimal Monto { get; set; }

    public int? PeriodoAnio { get; set; }

    public int? PeriodoMes { get; set; }

    public string Periodo =>
        PeriodoAnio.HasValue && PeriodoMes.HasValue
            ? $"{PeriodoMes.Value:00}/{PeriodoAnio.Value}"
            : "Sin período";
}
