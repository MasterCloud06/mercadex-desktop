using Mercadex.Desktop.App;
using Xunit;

namespace Mercadex.Desktop.Tests;

/// <summary>Tiempo de bloqueo por inactividad a elegir.</summary>
public sealed class BloqueoTests
{
    [Fact]
    public void Las_opciones_son_10_15_20_30_y_60_minutos()
    {
        Assert.Equal(new[] { 10, 15, 20, 30, 60 }, Preferencias.OpcionesBloqueo);
        Assert.Equal(15, Preferencias.BloqueoPorDefecto);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(60, 60)]
    [InlineData(0, 15)]
    [InlineData(5, 15)]
    [InlineData(999, 15)]
    [InlineData(-30, 15)]
    public void Un_valor_fuera_de_las_opciones_vuelve_a_15(int guardado, int esperado)
    {
        Assert.Equal(esperado, Preferencias.NormalizarBloqueo(guardado));
    }

    [Fact]
    public void El_boton_muestra_los_minutos()
    {
        Assert.Equal("Bloqueo: 30 min", MainWindow.TextoBloqueo(30));
    }
}
