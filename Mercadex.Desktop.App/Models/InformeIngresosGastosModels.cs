namespace Mercadex.Desktop.App.Models;

public sealed class InformeIngresosGastos
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public DateTimeOffset Generado { get; set; }
    public List<InformeMoneda> Monedas { get; set; } = new();
}

public sealed class InformeMoneda
{
    public Guid MonedaId { get; set; }
    public string Moneda { get; set; } = "";
    public int Recibos { get; set; }
    public List<InformeLineaIngreso> Ingresos { get; set; } = new();
    public decimal TotalIngresos { get; set; }
    public List<InformeLineaEgreso> Egresos { get; set; } = new();
    public decimal TotalEgresos { get; set; }
    public decimal Saldo { get; set; }
}

public sealed class InformeLineaIngreso
{
    public string Concepto { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal Total { get; set; }
}

public sealed class InformeLineaEgreso
{
    public string Categoria { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal Total { get; set; }
}
