
namespace Mercadex.Desktop.App.Models;

public sealed class CatalogosIngresosModel
{
    public List<FormaPagoIngresoModel> FormasPago { get; set; } = new();

    public List<ConceptoIngresoModel> Conceptos { get; set; } = new();
}

public sealed class FormaPagoIngresoModel
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;
}

public sealed class ConceptoIngresoModel
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;
}
