namespace Mercadex.Desktop.App;

/// <summary>
/// Saltos entre pantallas que no pasan por el menú
/// (por ejemplo: del Mapa del mercado o del Cobro rápido a Ingresos).
/// La ventana principal escucha y muestra la pantalla pedida.
/// </summary>
public static class Navegacion
{
    public static event Action<Guid?, string>? CobroSolicitado;

    /// <summary>Abre Ingresos con la caseta indicada ya buscada.</summary>
    public static void AbrirCobro(Guid? unidadId, string codigo) =>
        CobroSolicitado?.Invoke(unidadId, codigo);
}
