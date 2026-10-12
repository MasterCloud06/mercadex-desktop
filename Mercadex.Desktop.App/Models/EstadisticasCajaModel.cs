namespace Mercadex.Desktop.App.Models;


public class EstadisticasCajaModel
{

    public DateTime Inicio { get; set; }


    public DateTime Fin { get; set; }


    public int CantidadPagos { get; set; }


    public int CantidadRecibos { get; set; }


    public decimal TotalRecaudado { get; set; }


    public decimal PromedioPago { get; set; }


    public List<EstadisticaFormaPagoModel> FormasPago { get; set; }
        = new();

}



public class EstadisticaFormaPagoModel
{

    public string FormaPago { get; set; } = string.Empty;


    public int Cantidad { get; set; }


    public decimal Total { get; set; }

}