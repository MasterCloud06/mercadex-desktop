
namespace Mercadex.Desktop.App.Models;

public sealed record CrearSocioRequest(
    string Nombres,
    string Apellidos,
    string DocumentoIdentidad,
    string? Telefono
);
