namespace Mercadex.Desktop.App.Models;

public class IngresoModel
{
    public Guid Id { get; set; }

    public DateTime Fecha { get; set; }

    public string Caseta { get; set; } = string.Empty;

    public string Bloque { get; set; } = string.Empty;

    public string Persona { get; set; } = string.Empty;

    public string Concepto { get; set; } = string.Empty;

    public decimal Monto { get; set; }

    public string Moneda { get; set; } = "Bs";
}