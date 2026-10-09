
namespace Mercadex.Desktop.App.Models;

public sealed class SocioModel
{
    public Guid Id { get; set; }

    public string NombreCompleto { get; set; }
        = string.Empty;

    public string DocumentoIdentidad { get; set; }
        = string.Empty;

    public string? Telefono { get; set; }

    public DateTimeOffset FechaRegistro { get; set; }

    public bool Activo { get; set; }
}
