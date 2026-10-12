namespace Mercadex.Desktop.App.Models;

// Respuesta de GET /api/mapa-mercado.

public sealed class MapaMercadoRespuesta
{
    public DateTimeOffset Generado { get; set; }
    public List<MapaBloque> Bloques { get; set; } = new();
}

public sealed class MapaBloque
{
    public string Bloque { get; set; } = "";
    public List<MapaUnidad> Unidades { get; set; } = new();
}

public sealed class MapaUnidad
{
    public Guid Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Bloque { get; set; } = "";
    public int Tipo { get; set; }
    public bool Activa { get; set; }
    public string? Titular { get; set; }
    public int Obligaciones { get; set; }
    public int DiasDeuda { get; set; }

    /// <summary>al-dia, reciente, atrasada, critica o inactiva.</summary>
    public string Situacion { get; set; } = "al-dia";

    public List<MapaDeuda> Deudas { get; set; } = new();

    public string TipoTexto => Tipo switch
    {
        2 => "Comedor",
        3 => "Ambulante",
        _ => "Caseta"
    };
}

public sealed class MapaDeuda
{
    public string Moneda { get; set; } = "";
    public decimal Total { get; set; }
}
