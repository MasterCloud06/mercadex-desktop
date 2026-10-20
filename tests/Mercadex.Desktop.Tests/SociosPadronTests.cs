using System.Globalization;
using Mercadex.Desktop.App.Views;
using Xunit;

namespace Mercadex.Desktop.Tests;

public sealed class SociosPadronTests
{
    [Fact]
    public void Ordena_las_casetas_como_numeros_y_conserva_los_grupos()
    {
        var filas = new[]
        {
            Fila("Persona cien", "100"),
            Fila("Persona diez", "11", "10"),
            Fila("Persona dos", "2"),
            Fila("Persona uno", "1"),
            Fila("Persona doce", "117", "12")
        };

        var ordenadas = SociosView.OrdenarPorCasetas(filas);

        Assert.Equal(new[] { "1", "2", "10, 11", "12, 117", "100" },
            ordenadas.Select(f => f.CasetasTexto));
        Assert.Equal(filas.Length, ordenadas.Count);
        Assert.Equal("Persona diez", ordenadas[2].NombreCompleto);
    }

    [Fact]
    public void Las_ultimas_entradas_conservan_todos_sus_locales()
    {
        var persona = Fila("Persona de prueba", "354");
        var asociacion = Fila("Asociacion de prueba", "365", "362", "358", "361", "364", "359", "363", "360");

        var ordenadas = SociosView.OrdenarPorCasetas(new[] { asociacion, persona });

        Assert.Same(persona, ordenadas[0]);
        Assert.Equal("354", ordenadas[0].CasetasTexto);
        Assert.Same(asociacion, ordenadas[1]);
        Assert.Equal("358, 359, 360, 361, 362, 363, 364, 365", ordenadas[1].CasetasTexto);
        Assert.Equal("Asociacion de prueba", ordenadas[1].NombreCompleto);
    }

    [Fact]
    public void Una_persona_nueva_muestra_guiones_en_datos_ausentes_y_estado_activo()
    {
        var fila = Fila("ANA APELLIDO", "1");

        Assert.Equal("---", fila.DocumentoTexto);
        Assert.Equal("---", fila.TelefonoTexto);
        Assert.Equal("---", fila.DeudaTexto);
        Assert.Equal("Activo", fila.EstadoTexto);
        Assert.Empty(fila.Deuda);
        Assert.Equal("ANA APELLIDO", fila.NombreCompleto);
    }

    [Fact]
    public void Los_guiones_no_ocultan_una_deuda_o_un_estado_existentes()
    {
        var fila = Fila("Persona de prueba", "1");
        fila.Deuda.Add(new SociosView.MontoMoneda { Moneda = "BOB", Monto = 12.50m, Cantidad = 1 });

        Assert.Equal("BOB " + 12.50m.ToString("N2", CultureInfo.GetCultureInfo("es-BO")), fila.DeudaTexto);
        fila.Fallecido = true;
        Assert.Equal("Fallecido", fila.EstadoTexto);
        fila.Activo = false;
        Assert.Equal("De baja", fila.EstadoTexto);
    }

    [Fact]
    public void Los_registros_sin_caseta_quedan_al_final()
    {
        var sinCaseta = Fila("Sin caseta");
        var conCaseta = Fila("Con caseta", "1");

        var ordenadas = SociosView.OrdenarPorCasetas(new[] { sinCaseta, conCaseta });

        Assert.Same(conCaseta, ordenadas[0]);
        Assert.Same(sinCaseta, ordenadas[1]);
        Assert.Equal("---", sinCaseta.CasetasTexto);
    }

    private static SociosView.SocioFila Fila(string nombre, params string[] casetas) => new()
    {
        NombreCompleto = nombre,
        Activo = true,
        Casetas = casetas.ToList()
    };
}
