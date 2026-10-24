using System.IO;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;
using Xunit;

namespace Mercadex.Desktop.Tests;

/// <summary>Pintar el mapa por cuenta y su reporte PDF.</summary>
public sealed class CuentasMapaTests
{
    private static readonly MapaConcepto Mensualidad = new() { Id = Guid.NewGuid(), Nombre = "Mensualidades", Unidades = 2 };
    private static readonly MapaConcepto Escombros = new() { Id = Guid.NewGuid(), Nombre = "Techo-escombro", Unidades = 2 };

    private static MapaUnidad Unidad(string codigo, int x, params MapaConcepto[] debe) => new()
    {
        Id = Guid.NewGuid(),
        Codigo = codigo,
        Activa = true,
        Titular = "Titular " + codigo,
        Situacion = debe.Length == 0 ? "al-dia" : "atrasada",
        Obligaciones = debe.Length,
        PlanoX = x,
        PlanoY = 100,
        Deudas = debe.Length == 0 ? new() : new() { new MapaDeuda { Moneda = "BOB", Total = 150 * debe.Length } },
        Conceptos = debe.Select(c => new MapaUnidadConcepto
        {
            Id = c.Id,
            Obligaciones = 1,
            Deudas = new() { new MapaDeuda { Moneda = "BOB", Total = 150 } }
        }).ToList()
    };

    private static readonly MapaUnidad SoloMensualidad = Unidad("1", 100, Mensualidad);
    private static readonly MapaUnidad SoloEscombros = Unidad("2", 200, Escombros);
    private static readonly MapaUnidad Ambas = Unidad("3", 300, Mensualidad, Escombros);
    private static readonly MapaUnidad AlDia = Unidad("4", 400);

    private static CuentasMapa Pintar(params MapaConcepto[] marcadas) =>
        new(new[] { Mensualidad, Escombros }, marcadas.Select(c => c.Id).ToHashSet());

    [Fact]
    public void Sin_cuentas_marcadas_no_pinta() => Assert.False(Pintar().Activa);

    [Fact]
    public void Una_cuenta_pinta_solo_las_que_la_deben()
    {
        var p = Pintar(Mensualidad);
        var color = CuentasMapa.ColorDe(0);

        Assert.Equal(color, p.ColoresDe(SoloMensualidad));
        Assert.Equal(color, p.ColoresDe(Ambas));
        Assert.Equal(CuentasMapa.Ninguna, p.ColoresDe(SoloEscombros));
        Assert.Equal(CuentasMapa.Ninguna, p.ColoresDe(AlDia));
    }

    [Fact]
    public void La_que_debe_varias_marcadas_va_en_rojo()
    {
        var p = Pintar(Mensualidad, Escombros);

        Assert.Equal(CuentasMapa.ColorDe(0), p.ColoresDe(SoloMensualidad));
        Assert.Equal(CuentasMapa.ColorDe(1), p.ColoresDe(SoloEscombros));
        Assert.Equal(CuentasMapa.Varias, p.ColoresDe(Ambas));
        Assert.Equal(1, p.ConVarias(new[] { SoloMensualidad, SoloEscombros, Ambas, AlDia }));
    }

    [Fact]
    public void Cada_cuenta_conserva_su_color_aunque_se_marque_sola()
    {
        Assert.Equal(CuentasMapa.ColorDe(1), Pintar(Escombros).ColoresDe(SoloEscombros));
        Assert.NotEqual(CuentasMapa.ColorDe(0), CuentasMapa.ColorDe(1));
    }

    [Fact]
    public void El_reporte_por_cuenta_se_genera()
    {
        var datos = new MapaMercadoRespuesta
        {
            Conceptos = new() { Mensualidad, Escombros },
            Bloques = new() { new MapaBloque { Bloque = "A", Unidades = new() { SoloMensualidad, SoloEscombros, Ambas, AlDia } } },
            Plano = new MapaPlano { Fuente = "prueba", Ancho = 1960, Alto = 1220, CasetasEnPlano = 4, Cargadas = 4 }
        };

        foreach (var cuentas in new[] { null, Pintar(Mensualidad, Escombros) })
        {
            var ruta = Path.Combine(Path.GetTempPath(), $"mapa-prueba-{Guid.NewGuid():N}.pdf");
            try
            {
                MapaPdfService.Escribir(ruta, datos, cuentas);
                Assert.True(new FileInfo(ruta).Length > 1000);
            }
            finally
            {
                File.Delete(ruta);
            }
        }
    }
}
