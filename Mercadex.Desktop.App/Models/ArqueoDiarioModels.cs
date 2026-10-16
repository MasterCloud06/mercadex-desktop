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

    /// <summary>De quién es la caja consultada: id de la cajera o "general".</summary>
    public string? Caja { get; set; }

    public string? CajaNombre { get; set; }

    public bool EsCajaGeneral { get; set; } = true;

    /// <summary>Cajas de cada cajera en ese día (servidor 1.6.3 o posterior).</summary>
    public List<CajaDiaModel> Cajas { get; set; } = new();

    /// <summary>Cobros sin cajera identificada (sin sesión o anteriores a la 1.6.3).</summary>
    public int SinCajera { get; set; }
}

public sealed class CajaDiaModel
{
    public string CajeroId { get; set; } = "";

    public string Nombre { get; set; } = "";

    public int CantidadCobros { get; set; }

    public int CantidadEgresos { get; set; }

    public List<CajaTotalModel>? Totales { get; set; }

    public bool Cerrada { get; set; }

    public bool EsMia { get; set; }
}

public sealed class CajaTotalModel
{
    public string Moneda { get; set; } = "";

    public decimal Total { get; set; }

    public decimal Efectivo { get; set; }
}

/// <summary>Opción del selector de caja.</summary>
public sealed record OpcionCaja(string Valor, string Texto)
{
    public override string ToString() => Texto;
}

public sealed class ArqueoMonedaResumen
{
    public Guid MonedaId { get; set; }

    public bool Cerrado { get; set; }

    public CierreCajaModel? Cierre { get; set; }

    public bool CambiosPosteriores { get; set; }

    public string EstadoTexto => Cerrado ? "Cerrada" : "Abierta";

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

    /// <summary>Gastos pagados en efectivo ese día (salen de la caja).</summary>
    public decimal EgresosEfectivo { get; set; }

    public int CantidadEgresos { get; set; }

    /// <summary>Efectivo que debe haber en caja.</summary>
    public decimal EfectivoEsperado => Efectivo - EgresosEfectivo;
}

public sealed class ArqueoIncidenciaResumen
{
    public string Moneda { get; set; } = string.Empty;

    public int Cantidad { get; set; }

    public decimal Monto { get; set; }
}

public sealed class CierreCajaModel
{
    public Guid Id { get; set; }

    public string Fecha { get; set; } = string.Empty;

    public Guid MonedaId { get; set; }

    public string Moneda { get; set; } = string.Empty;

    public decimal Efectivo { get; set; }

    public decimal Qr { get; set; }

    public decimal Otros { get; set; }

    public decimal Total { get; set; }

    public int CantidadPagos { get; set; }

    public int CantidadEfectivo { get; set; }

    public int CantidadQr { get; set; }

    public int CantidadOtros { get; set; }

    public decimal EgresosEfectivo { get; set; }

    public int CantidadEgresos { get; set; }

    public decimal EfectivoEsperado => Efectivo - EgresosEfectivo;

    public decimal EfectivoContado { get; set; }

    public decimal Diferencia { get; set; }

    public string Responsable { get; set; } = string.Empty;

    public string? Observacion { get; set; }

    public DateTimeOffset FechaRegistro { get; set; }

    public string FechaTexto =>
        DateOnly.TryParse(Fecha, System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? f.ToString("dd/MM/yyyy")
            : Fecha;

    public string RegistradoTexto =>
        FechaRegistro.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    public string? Caja { get; set; }

    public string? CajaNombre { get; set; }

    public bool EsCajaGeneral { get; set; } = true;

    public string CajaTexto => EsCajaGeneral ? "General" : CajaNombre ?? Responsable;
}

public sealed record RegistrarCierreRequest(
    DateOnly Fecha,
    Guid MonedaId,
    decimal EfectivoContado,
    string Responsable,
    string? Observacion,
    string? Caja = null);

/// <summary>Datos que se imprimen en el acta de arqueo.</summary>
public sealed record ActaArqueo(
    DateOnly Fecha,
    string Moneda,
    decimal Efectivo,
    int CantidadEfectivo,
    decimal Qr,
    int CantidadQr,
    decimal Otros,
    int CantidadOtros,
    decimal Total,
    int CantidadPagos,
    decimal? EfectivoContado,
    decimal? Diferencia,
    string? Responsable,
    string? Observacion,
    bool Cerrado,
    DateTimeOffset? FechaCierre,
    decimal EgresosEfectivo = 0m,
    int CantidadEgresos = 0,
    string? Caja = null);
