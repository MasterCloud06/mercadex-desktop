using System.Collections.Generic;

namespace Mercadex.Desktop.App.Models;

public sealed class ArqueoPreliminarResponse
{
    public string Fecha { get; set; } = string.Empty;

    public string ZonaHoraria { get; set; } = string.Empty;

    public string Estado { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public int CantidadPagosValidos { get; set; }

    public int CantidadIncidencias { get; set; }

    public List<ArqueoMonedaResumen> Resumen { get; set; } = new();

    public List<ArqueoIncidenciaResumen> PagosPorRevisar { get; set; } = new();
}

public sealed class ArqueoMonedaResumen
{
    public string Moneda { get; set; } = string.Empty;

    public string Simbolo { get; set; } = string.Empty;

    public decimal Efectivo { get; set; }

    public decimal Qr { get; set; }

    public decimal Otros { get; set; }

    public decimal Total { get; set; }

    public int Cantidad { get; set; }

    public int CantidadEfectivo { get; set; }

    public int CantidadQr { get; set; }

    public int CantidadOtros { get; set; }
}

public sealed class ArqueoIncidenciaResumen
{
    public string Moneda { get; set; } = string.Empty;

    public int Cantidad { get; set; }

    public decimal Monto { get; set; }
}