namespace Mercadex.Desktop.App.Models;

// Respuesta de GET /api/mapa-mercado.

public sealed class MapaMercadoRespuesta
{
    public DateTimeOffset Generado { get; set; }
    public List<MapaBloque> Bloques { get; set; } = new();

    /// <summary>Plano oficial (servidor 1.6.4 o posterior; null en versiones anteriores).</summary>
    public MapaPlano? Plano { get; set; }
}

public sealed class MapaPlano
{
    public string Fuente { get; set; } = "";
    public int Ancho { get; set; }
    public int Alto { get; set; }
    public string? Nota { get; set; }
    public List<MapaPasillo> Pasillos { get; set; } = new();
    public List<MapaPorConfirmar> PorConfirmar { get; set; } = new();
    public int CasetasEnPlano { get; set; }
    public int Cargadas { get; set; }
}

public sealed class MapaPasillo
{
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Sector { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class MapaPorConfirmar
{
    public string Numero { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public string Pasillo { get; set; } = "";
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

    public string? Pasillo { get; set; }
    public string? PasilloCodigo { get; set; }
    public int? PlanoX { get; set; }
    public int? PlanoY { get; set; }

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
