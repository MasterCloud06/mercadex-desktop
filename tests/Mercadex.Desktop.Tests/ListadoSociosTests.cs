using System.Text.Json;
using Mercadex.Desktop.App.Services;
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

    [Fact]
    public void Lee_la_respuesta_real_del_padron()
    {
        // Forma de GET /api/socios/padron: un objeto con la lista en «socios».
        const string json = """
            {"total":2,"activos":2,"noSocios":0,"inactivos":0,"socios":[
              {"id":"0193a5b2-0000-7000-8000-000000000001","nombres":"Jose","apellidos":"Sequeiros Delgado","nombreCompleto":"Jose Sequeiros Delgado",
               "documento":"123","activo":true,"casetas":["13","2","7"],"deuda":[],"deudasPendientes":0,"app":"Sin cuenta"},
              {"id":"0193a5b2-0000-7000-8000-000000000002","nombres":"Rocio","apellidos":"Loayza Bustillos","nombreCompleto":"Rocio Loayza Bustillos",
               "documento":"456","activo":true,"casetas":["1"],"deuda":[],"deudasPendientes":0,"app":"Activada"}]}
            """;

        var r = JsonSerializer.Deserialize<V.PadronRespuesta>(json, ApiJson.Opciones)!;
        var filas = V.Filas(r.Socios, false);

        Assert.Equal(new[] { "LOAYZA BUSTILLOS ROCIO", "SEQUEIROS DELGADO JOSE" }, filas.Select(f => f.Celdas[1]));
        Assert.Equal("2  7  13", filas[1].Celdas[2]);
    }
}
