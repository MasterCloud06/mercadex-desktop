namespace Mercadex.Desktop.App.Models;

public sealed class UnidadIngresoModel
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int Tipo { get; set; }
    public Guid? PasilloId { get; set; }
    public string? Bloque { get; set; }
    public bool Activa { get; set; }
}

public sealed class MonedaIngresoModel
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Simbolo { get; set; } = string.Empty;
}

public sealed class ObligacionIngresoModel
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

public sealed class ItemCobroIngresoModel
{
    public Guid ObligacionId { get; set; }
    public string NombreConcepto { get; set; } = string.Empty;
}

public sealed class ProcesarCobroIngresoModel
{
    public long NumeroRecibo { get; set; }
    public Guid UnidadComercialId { get; set; }
    public Guid MonedaId { get; set; }
    public decimal Monto { get; set; }
    public int FormaPago { get; set; }
    public string NombrePagador { get; set; } = string.Empty;
    public decimal TipoCambio { get; set; }
    public string? Referencia { get; set; }
    public List<ItemCobroIngresoModel> Items { get; set; } = new();
}

public sealed class ResultadoCobroIngresoModel
{
    public PagoRegistradoIngresoModel? Pago { get; set; }
    public ReciboRegistradoIngresoModel? Recibo { get; set; }
}

public sealed class PagoRegistradoIngresoModel
{
    public Guid Id { get; set; }
    public decimal Monto { get; set; }
    public string Estado { get; set; } = string.Empty;
}

public sealed class ReciboRegistradoIngresoModel
{
    public Guid Id { get; set; }
    public long Numero { get; set; }
    public decimal Total { get; set; }
    public string Estado { get; set; } = string.Empty;
}
