namespace Mercadex.Desktop.App.Models;


public class ReciboIngresoModel
{

    public long Numero { get; set; }


    public DateTime Fecha { get; set; }


    public string Pagador { get; set; } = string.Empty;


    public string FormaPago { get; set; } = string.Empty;


    public decimal Total { get; set; }


    public string Estado { get; set; } = string.Empty;

}