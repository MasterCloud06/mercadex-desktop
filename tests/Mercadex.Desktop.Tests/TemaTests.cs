using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Mercadex.Desktop.App;
using Xunit;

namespace Mercadex.Desktop.Tests;

/// <summary>
/// Modo oscuro: la conversión de colores y la copia oscura de Styles.xaml.
/// No activan el modo oscuro (las demás pruebas siguen en modo claro).
/// </summary>
public sealed class TemaTests
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static double Luz(Color c) => (Math.Max(c.R, Math.Max(c.G, c.B)) + Math.Min(c.R, Math.Min(c.G, c.B))) / 510.0;

    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#F3F6FA")]
    [InlineData("#EEF3F9")]
    public void Los_fondos_claros_pasan_a_oscuros(string hex) =>
        Assert.True(Luz(Tema.AOscuro(C(hex), Tema.Rol.Fondo)) < 0.30);

    [Theory]
    [InlineData("#1F4E79")] // botón principal
    [InlineData("#163A5B")] // menú lateral
    [InlineData("#F2B705")] // acento
    public void Los_colores_fuertes_se_conservan(string hex) =>
        Assert.Equal(C(hex), Tema.AOscuro(C(hex), Tema.Rol.Fondo));

    [Theory]
    [InlineData("#1E2A38")]
    [InlineData("#12304D")]
    [InlineData("#5A6B7D")]
    [InlineData("#000000")]
    public void Los_textos_oscuros_pasan_a_claros(string hex) =>
        Assert.True(Luz(Tema.AOscuro(C(hex), Tema.Rol.Texto)) > 0.60);

    [Fact]
    public void El_texto_blanco_de_los_botones_se_conserva() =>
        Assert.Equal(Colors.White, Tema.AOscuro(Colors.White, Tema.Rol.Texto));

    [Fact]
    public void Los_bordes_claros_pasan_a_oscuros() =>
        Assert.True(Luz(Tema.AOscuro(C("#E1E7EF"), Tema.Rol.Borde)) < 0.40);

    [Fact]
    public void Convertir_dos_veces_no_cambia_mas()
    {
        foreach (var rol in Enum.GetValues<Tema.Rol>())
        foreach (var hex in new[] { "#FFFFFF", "#F3F6FA", "#1E2A38", "#E1E7EF", "#C62828" })
        {
            var una = Tema.AOscuro(C(hex), rol);
            Assert.Equal(una, Tema.AOscuro(una, rol));
        }
    }

    [Fact]
    public void En_modo_claro_no_cambia_nada()
    {
        Assert.False(Tema.Oscuro);
        var pincel = new SolidColorBrush(Colors.White);
        Assert.Same(pincel, Tema.Pincel(pincel, Tema.Rol.Fondo));
    }

    [Fact]
    public void La_copia_oscura_de_los_estilos_se_carga()
    {
        Exception? error = null;
        var hilo = new Thread(() =>
        {
            try
            {
                using var flujo = typeof(Tema).Assembly.GetManifestResourceStream("Mercadex.Estilos.xaml");
                Assert.NotNull(flujo);
                var original = new StreamReader(flujo!).ReadToEnd();
                var oscuro = Tema.ConvertirXaml(original);

                Assert.NotEqual(original, oscuro);
                var estilos = (ResourceDictionary)XamlReader.Parse(oscuro);

                Assert.True(estilos.Contains("Mx.TextBox"));
                Assert.True(estilos.Contains("Mx.DataGrid"));
                Assert.True(Luz(((SolidColorBrush)estilos["Mx.SurfaceBrush"]).Color) < 0.30);
                Assert.True(Luz(((SolidColorBrush)estilos["Mx.TextBrush"]).Color) > 0.60);
                Assert.Equal(C("#1F4E79"), ((SolidColorBrush)estilos["Mx.PrimaryBrush"]).Color);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();

        if (error != null)
            throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
