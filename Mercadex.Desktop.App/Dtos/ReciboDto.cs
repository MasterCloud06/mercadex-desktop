namespace Mercadex.Desktop.App.Dtos;


public class ReciboDto
{

    public Guid Id { get; set; }


    public long Numero { get; set; }


    public Guid UnidadComercialId { get; set; }


    public Guid PagoId { get; set; }


    public DateTimeOffset FechaEmision { get; set; }


    public string Estado { get; set; } = string.Empty;


    public decimal Total { get; set; }


    public List<DetalleReciboDto> Detalles { get; set; }
        = new();

}