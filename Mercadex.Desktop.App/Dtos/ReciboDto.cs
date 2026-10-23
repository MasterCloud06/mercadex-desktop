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


    /// <summary>Usuario que registró el cobro (desde la 1.6.2).</summary>
    public string? RegistradoPor { get; set; }

    /// <summary>Cargo de quien emitió el recibo (desde la 1.6.3).</summary>
    public string? CargoEmisor { get; set; }

    /// <summary>Observaciones del cobro, opcionales.</summary>
    public string? Observaciones { get; set; }

    public string? MotivoAnulacion { get; set; }

    public DateTimeOffset? FechaAnulacion { get; set; }

    public string? AnuladoPor { get; set; }

    public List<DetalleReciboDto> Detalles { get; set; }
        = new();

}