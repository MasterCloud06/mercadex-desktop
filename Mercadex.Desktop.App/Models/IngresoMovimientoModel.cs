namespace Mercadex.Desktop.App.Models;


public sealed class IngresoMovimientoModel
{
    public DateTime Fecha { get; set; }

    public long NumeroRecibo { get; set; }

    public string CodigoUnidad { get; set; }
        = string.Empty;

    public string Bloque { get; set; }
        = string.Empty;

    public string NombreSocio { get; set; }
        = string.Empty;

    public string NombrePagador { get; set; }
        = string.Empty;

    public string FormaPago { get; set; }
        = string.Empty;

    public decimal Monto { get; set; }

    public string Estado { get; set; }
        = string.Empty;
}