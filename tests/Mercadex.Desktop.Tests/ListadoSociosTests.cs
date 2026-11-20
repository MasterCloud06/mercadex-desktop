using Mercadex.Desktop.App.Views.Reportes;
using Xunit;
using V = Mercadex.Desktop.App.Views.Reportes.ListadoSociosReporteView;

namespace Mercadex.Desktop.Tests;

/// <summary>Listado de socios como el «Listado de personas» anterior.</summary>
public sealed class ListadoSociosTests
{
    private static V.SocioPadron S(string apellidos, string nombres, params string[] locales) =>
        new() { Apellidos = apellidos, Nombres = nombres, Activo = true, Casetas = locales.ToList() };

    private static readonly V.SocioPadron[] Socios =
    {
        S("Sequeiros Delgado", "Jose", "13", "2", "7"),
        S("Loayza Bustillos", "Rocio", "1"),
        S("Rojas Argandoña", "Zenon", "24"),
        S("Cadima de Villarroel", "Emilia Cristina", "356", "25", "355"),
        S("Sin Local", "Pedro"),
        S("Ambulante", "Ana", "A-3"),
        new() { Apellidos = "Inactivo", Nombres = "Juan", Activo = false, Casetas = new() { "3" } }
    };

    [Fact]
    public void Un_socio_por_fila_ordenado_por_su_primer_local()
    {
        var filas = V.Filas(Socios, incluirSinLocal: false);

        Assert.Equal(new[] { "1", "2", "24", "25", "A-3" }, filas.Select(f => f.Celdas[0]));
        Assert.Equal("SEQUEIROS DELGADO JOSE", filas[1].Celdas[1]);
        Assert.Equal("2  7  13", filas[1].Celdas[2]);
        Assert.Equal("25  355  356", filas[3].Celdas[2]);
    }

    [Fact]
    public void Los_que_no_tienen_local_van_al_final_solo_si_se_piden()
    {
        Assert.DoesNotContain(V.Filas(Socios, false), f => f.Celdas[1] == "SIN LOCAL PEDRO");

        var filas = V.Filas(Socios, incluirSinLocal: true);
        Assert.Equal("SIN LOCAL PEDRO", filas[^1].Celdas[1]);
        Assert.Equal("", filas[^1].Celdas[0]);
    }

    [Fact]
    public void Los_inactivos_no_salen()
    {
        Assert.DoesNotContain(V.Filas(Socios, true), f => f.Celdas[1].StartsWith("INACTIVO"));
    }
}
