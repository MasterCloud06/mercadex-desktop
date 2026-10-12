namespace Mercadex.Desktop.App.Models;

// Respuesta de GET /api/dashboard.
// Los importes siempre van con su moneda; no se mezclan.

public sealed class DashboardRespuesta
{
    public DateTimeOffset Generado { get; set; }
    public DateOnly Hoy { get; set; }
    public DashboardUnidades Unidades { get; set; } = new();
    public List<DashboardRecaudacion> Recaudacion { get; set; } = new();
    public List<DashboardDeuda> Deuda { get; set; } = new();
    public List<DashboardDia> Diario { get; set; } = new();
    public List<DashboardMes> Mensual { get; set; } = new();
    public List<DashboardConcepto> PorConcepto { get; set; } = new();
    public List<DashboardFormaPago> FormasPago { get; set; } = new();
    public List<DashboardTramo> Antiguedad { get; set; } = new();
    public List<DashboardDeudor> TopDeudores { get; set; } = new();
    public List<DashboardCobro> UltimosCobros { get; set; } = new();
    public int AnulacionesMes { get; set; }
    public List<DashboardMonto> EgresosMes { get; set; } = new();
}

public sealed class DashboardUnidades
{
    public int Casetas { get; set; }
    public int Comedores { get; set; }
    public int Ambulantes { get; set; }
    public int Activas { get; set; }
    public int ConDeuda { get; set; }
    public int AlDia { get; set; }
}

public sealed class DashboardRecaudacion
{
    public string Moneda { get; set; } = "";
    public decimal Hoy { get; set; }
    public int RecibosHoy { get; set; }
    public decimal Mes { get; set; }
    public int RecibosMes { get; set; }
    public decimal MesAnterior { get; set; }
    public decimal MesAnteriorMismoTramo { get; set; }
}

public sealed class DashboardDeuda
{
    public string Moneda { get; set; } = "";
    public decimal Total { get; set; }
    public int Obligaciones { get; set; }
    public int Casetas { get; set; }
}

public sealed class DashboardDia
{
    public DateOnly Fecha { get; set; }
    public decimal Total { get; set; }
    public int Recibos { get; set; }
}

public sealed class DashboardMes
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public decimal Total { get; set; }
    public int Recibos { get; set; }
}

public sealed class DashboardConcepto
{
    public string Concepto { get; set; } = "";
    public decimal Total { get; set; }
    public int Pagos { get; set; }
}

public sealed class DashboardFormaPago
{
    public string Forma { get; set; } = "";
    public decimal Total { get; set; }
    public int Cantidad { get; set; }
}

public sealed class DashboardTramo
{
    public string Tramo { get; set; } = "";
    public int Orden { get; set; }
    public decimal Total { get; set; }
    public int Obligaciones { get; set; }
}

public sealed class DashboardDeudor
{
    public string Caseta { get; set; } = "";
    public string? Bloque { get; set; }
    public decimal Total { get; set; }
    public int Obligaciones { get; set; }
    public DateOnly Desde { get; set; }
}

public sealed class DashboardCobro
{
    public long Numero { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public string Caseta { get; set; } = "";
    public string Pagador { get; set; } = "";
    public decimal Total { get; set; }
    public string Moneda { get; set; } = "";
}

public sealed class DashboardMonto
{
    public string Moneda { get; set; } = "";
    public decimal Total { get; set; }
}
