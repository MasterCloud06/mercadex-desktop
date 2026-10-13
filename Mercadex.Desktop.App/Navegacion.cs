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

    public static event Action<string>? DeudasSolicitadas;

    /// <summary>Abre Obligaciones → Deudas registradas de esa caseta.</summary>
    public static void AbrirDeudas(string codigo) =>
        DeudasSolicitadas?.Invoke(codigo);

    public static event Action<Guid>? KardexSolicitado;

    /// <summary>Abre el Kardex con la caseta indicada.</summary>
    public static void AbrirKardex(Guid unidadId) =>
        KardexSolicitado?.Invoke(unidadId);

    public static event Action<Guid>? SocioSolicitado;

    /// <summary>Abre Socios con la ficha de ese socio.</summary>
    public static void AbrirSocio(Guid socioId) =>
        SocioSolicitado?.Invoke(socioId);

    public static event Action<Guid>? CasetaSolicitada;

    /// <summary>Abre Casetas con la ficha de esa unidad.</summary>
    public static void AbrirCaseta(Guid unidadId) =>
        CasetaSolicitada?.Invoke(unidadId);
}
