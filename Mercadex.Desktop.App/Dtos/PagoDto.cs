namespace Mercadex.Desktop.App.Dtos;


public class PagoDto
{

    public Guid Id { get; set; }


    public Guid MonedaId { get; set; }


    public decimal Monto { get; set; }


    public string FormaPago { get; set; } = string.Empty;


    public string NombrePagador { get; set; } = string.Empty;


    public string? Referencia { get; set; }


    public decimal TipoCambio { get; set; }


    public DateTimeOffset FechaRegistro { get; set; }


    public string Estado { get; set; } = string.Empty;

}