using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mercadex.Desktop.App.Updates;

public sealed class DatosVersion
{
    public int SchemaVersion { get; set; }

    public string LatestVersion { get; set; } = "";

    public string MinimumRequiredVersion { get; set; } = "";

    public string InstallerUrl { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public string ReleaseNotes { get; set; } = "";
}

public static class ActualizadorObligatorio
{
    private const string UrlVersion =
        "https://raw.githubusercontent.com/" +
        "MasterCloud06/mercadex-updates/main/version.json";

    private static readonly HttpClient Cliente = new()
    {
        Timeout = TimeSpan.FromSeconds(35)
    };

    public static Version VersionActual =>
        typeof(ActualizadorObligatorio)
            .Assembly.GetName().Version
        ?? new Version(0, 0, 0, 0);

    public static async Task<DatosVersion> ConsultarAsync()
    {
        var url = UrlVersion + "?t=" +
            DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var respuesta = await Cliente.GetAsync(url);

        respuesta.EnsureSuccessStatusCode();

        var datos = await respuesta.Content
            .ReadFromJsonAsync<DatosVersion>();

        if (datos == null || datos.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                "El archivo de versiones no es valido.");
        }

        var ultima = ObtenerVersion(datos.LatestVersion);

        var minima = ObtenerVersion(
            datos.MinimumRequiredVersion);

        if (minima > ultima)
        {
            throw new InvalidOperationException(
                "La version minima supera la ultima version.");
        }

        if (ultima > VersionActual)
        {
            ValidarInstalador(datos);
        }

        return datos;
    }

    public static bool RequiereActualizacion(
        DatosVersion datos)
    {
        return ObtenerVersion(datos.LatestVersion)
            > VersionActual;
    }

    private static Version ObtenerVersion(string texto)
    {
        if (!Version.TryParse(texto, out var version))
        {
            throw new InvalidOperationException(
                "El numero de version no es valido.");
        }

        return version;
    }

    private static void ValidarInstalador(DatosVersion datos)
    {
        if (!Uri.TryCreate(
                datos.InstallerUrl,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(
                "github.com",
                StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                "/MasterCloud06/mercadex-updates/releases/download/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La direccion del instalador oficial no es valida.");
        }

        if (!Regex.IsMatch(
                datos.Sha256,
                "^[0-9a-fA-F]{64}$"))
        {
            throw new InvalidOperationException(
                "No existe una huella SHA-256 valida del instalador.");
        }
    }

    public static async Task<string> DescargarAsync(
        DatosVersion datos)
    {
        ValidarInstalador(datos);

        var carpeta = Path.Combine(
            Path.GetTempPath(),
            "Mercadex-Actualizaciones",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(carpeta);

        var archivo = Path.Combine(
            carpeta,
            "Mercadex-Setup-" +
            datos.LatestVersion +
            "-win-x64.exe");

        try
        {
            using var clienteDescarga = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(15)
            };

            using var respuesta = await clienteDescarga.GetAsync(
                datos.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead);

            respuesta.EnsureSuccessStatusCode();

            await using (var origen =
                await respuesta.Content.ReadAsStreamAsync())
            await using (var destino = new FileStream(
                archivo,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await origen.CopyToAsync(destino);
            }

            byte[] hash;

            await using (var archivoLeido = File.OpenRead(archivo))
            {
                hash = await SHA256.HashDataAsync(archivoLeido);
            }

            var esperado = Convert.FromHexString(datos.Sha256);

            if (!CryptographicOperations.FixedTimeEquals(
                    hash,
                    esperado))
            {
                throw new InvalidOperationException(
                    "La verificacion de integridad SHA-256 fallo. " +
                    "No se ejecutara el instalador.");
            }

            return archivo;
        }
        catch
        {
            if (File.Exists(archivo))
                File.Delete(archivo);

            throw;
        }
    }
}

public sealed class VentanaActualizacionObligatoria : Window
{
    private readonly DatosVersion _datos;

    private readonly Button _btnInstalar = new()
    {
        Content = "DESCARGAR E INSTALAR",
        Height = 43,
        Background = new SolidColorBrush(
            Color.FromRgb(23, 59, 92)),
        Foreground = Brushes.White,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 12, 0, 8)
    };

    private readonly TextBlock _estado = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brushes.DarkSlateGray,
        Margin = new Thickness(0, 8, 0, 0)
    };

    public VentanaActualizacionObligatoria(
        DatosVersion datos)
    {
        _datos = datos;

        Title = "Actualizacion obligatoria - Mercadex";
        Width = 540;
        Height = 385;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var contenido = new StackPanel
        {
            Margin = new Thickness(25)
        };

        contenido.Children.Add(new TextBlock
        {
            Text = "Actualizacion obligatoria",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(
                Color.FromRgb(23, 59, 92)),
            Margin = new Thickness(0, 0, 0, 12)
        });

        contenido.Children.Add(new TextBlock
        {
            Text =
                "Debes instalar la nueva version " +
                "para continuar utilizando Mercadex.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 15)
        });

        contenido.Children.Add(new TextBlock
        {
            Text =
                "Version instalada: " +
                ActualizadorObligatorio.VersionActual +
                "\nNueva version: " +
                datos.LatestVersion,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 12)
        });

        contenido.Children.Add(new TextBlock
        {
            Text = datos.ReleaseNotes,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _btnInstalar.Click += Instalar_Click;
        contenido.Children.Add(_btnInstalar);
        contenido.Children.Add(_estado);

        var salir = new Button
        {
            Content = "Cerrar Mercadex",
            Height = 32,
            Margin = new Thickness(0, 12, 0, 0)
        };

        salir.Click += (_, _) => Close();

        contenido.Children.Add(salir);

        Content = new ScrollViewer
        {
            Content = contenido,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto
        };
    }

    private async void Instalar_Click(
        object sender,
        RoutedEventArgs e)
    {
        _btnInstalar.IsEnabled = false;

        _estado.Text =
            "Descargando y verificando el instalador...";

        try
        {
            var archivo =
                await ActualizadorObligatorio.DescargarAsync(_datos);

            _estado.Text = "Abriendo instalador oficial...";

            var proceso = Process.Start(
                new ProcessStartInfo
                {
                    FileName = archivo,
                    UseShellExecute = true,
                    Verb = "runas"
                });

            if (proceso == null)
            {
                throw new InvalidOperationException(
                    "No fue posible iniciar el instalador.");
            }

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _estado.Text =
                "No se pudo instalar la actualizacion: " +
                ex.Message +
                "\nPuedes volver a intentarlo.";

            _btnInstalar.IsEnabled = true;
        }
    }
}