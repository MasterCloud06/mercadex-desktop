namespace Mercadex.Desktop.App.Models;

public sealed class UnidadIngresoModel
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int Tipo { get; set; }
    public Guid? PasilloId { get; set; }
    public string? Bloque { get; set; }
    public bool Activa { get; set; }

    public string? Titular { get; set; }

    // Texto que confirma al cajero que unidad esta cobrando.
    public string Descripcion
    {
        get
        {
            var tipo = Tipo switch
            {
                2 => "Comedor",
                3 => "Ambulante",
                _ => "Caseta"
            };

            var texto = string.IsNullOrWhiteSpace(Bloque)
                ? $"{tipo} {Codigo}"
                : $"{tipo} {Codigo}  ·  Bloque {Bloque}";

            return texto + "  ·  Titular: " +
                   (string.IsNullOrWhiteSpace(Titular) ? "sin registrar" : Titular);
        }
    }
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

    /// <summary>Veces que se modificó el monto (0 si nunca).</summary>
    public int Modificaciones { get; set; }

    /// <summary>Cuándo, quién y por qué se modificó por última vez.</summary>
    public string? UltimaModificacion { get; set; }
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

    /// <summary>Observaciones que se imprimen en el recibo (opcional).</summary>
    public string? Observaciones { get; set; }
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
