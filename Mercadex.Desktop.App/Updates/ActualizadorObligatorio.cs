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
        DatosVersion datos,
        IProgress<(long Descargado, long? Total)>? progreso = null)
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
                var total = respuesta.Content.Headers.ContentLength;
                var bufer = new byte[81920];
                long descargado = 0;
                int leidos;

                while ((leidos = await origen.ReadAsync(bufer)) > 0)
                {
                    await destino.WriteAsync(bufer.AsMemory(0, leidos));
                    descargado += leidos;
                    progreso?.Report((descargado, total));
                }
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

/// <summary>
/// Aviso de actualización obligatoria: muestra la versión nueva,
/// descarga el instalador con barra de progreso, verifica su
/// integridad y lo ejecuta.
/// </summary>
public sealed class VentanaActualizacionObligatoria : Window
{
    private static readonly Brush Azul = Pincel("#12304D");
    private static readonly Brush Gris = Pincel("#5A6B7D");
    private static readonly Brush Rojo = Pincel("#B42318");

    private readonly DatosVersion _datos;
    private readonly Button _btnInstalar;
    private readonly Button _btnCerrar;
    private readonly ProgressBar _barra;
    private readonly TextBlock _estado;
    private readonly StackPanel _panelProgreso;

    public VentanaActualizacionObligatoria(DatosVersion datos)
    {
        _datos = datos;

        Title = "Actualización de Mercadex";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.White;
        UseLayoutRounding = true;

        if (Application.Current?.TryFindResource("Mx.Font") is FontFamily fuente)
            FontFamily = fuente;

        var raiz = new StackPanel();

        // ---------- Franja superior con la marca ----------

        var franja = new Border
        {
            Background = Pincel("#163A5B"),
            Padding = new Thickness(28, 22, 28, 22)
        };

        var marca = new StackPanel { Orientation = Orientation.Horizontal };
        marca.Children.Add(new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            Background = Pincel("#F2B705"),
            Child = new TextBlock
            {
                Text = "M",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Pincel("#163A5B"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        var titulos = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titulos.Children.Add(new TextBlock
        {
            Text = "Hay una nueva versión de Mercadex",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White
        });
        titulos.Children.Add(new TextBlock
        {
            Text = "Instálala para seguir trabajando. Tarda un par de minutos.",
            FontSize = 13,
            Foreground = Pincel("#B9CCDD"),
            Margin = new Thickness(0, 3, 0, 0)
        });
        marca.Children.Add(titulos);
        franja.Child = marca;
        raiz.Children.Add(franja);

        // ---------- Cuerpo ----------

        var cuerpo = new StackPanel { Margin = new Thickness(28, 22, 28, 24) };

        var versiones = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        versiones.Children.Add(Etiqueta("Tienes la " + Corta(ActualizadorObligatorio.VersionActual), "#EEF1F5", "#5A6B7D"));
        versiones.Children.Add(new TextBlock
        {
            Text = "→",
            FontSize = 16,
            Foreground = Gris,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        versiones.Children.Add(Etiqueta("Nueva " + datos.LatestVersion, "#E3F1E4", "#1D5C21"));
        cuerpo.Children.Add(versiones);

        if (!string.IsNullOrWhiteSpace(datos.ReleaseNotes))
        {
            cuerpo.Children.Add(new TextBlock
            {
                Text = "Qué trae",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Gris,
                Margin = new Thickness(0, 0, 0, 4)
            });
            cuerpo.Children.Add(new TextBlock
            {
                Text = QuitarPrefijo(datos.ReleaseNotes, datos.LatestVersion),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 20,
                Foreground = Pincel("#1E2A38")
            });
        }

        // Progreso de la descarga (oculto hasta pulsar el botón).
        _panelProgreso = new StackPanel { Margin = new Thickness(0, 18, 0, 0), Visibility = Visibility.Collapsed };
        _barra = new ProgressBar
        {
            Height = 8,
            Minimum = 0,
            Maximum = 100,
            Foreground = Pincel("#1F4E79"),
            Background = Pincel("#E6ECF2"),
            BorderThickness = new Thickness(0)
        };
        _panelProgreso.Children.Add(_barra);
        cuerpo.Children.Add(_panelProgreso);

        _estado = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Gris,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        cuerpo.Children.Add(_estado);

        // ---------- Botones ----------

        var botones = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        botones.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        botones.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _btnCerrar = new Button
        {
            Content = "Cerrar Mercadex",
            Height = 42,
            Padding = new Thickness(18, 0, 18, 0)
        };
        AplicarEstilo(_btnCerrar, "Mx.SecondaryButton");
        _btnCerrar.Click += (_, _) => Close();
        botones.Children.Add(_btnCerrar);

        _btnInstalar = new Button
        {
            Content = "Descargar e instalar",
            Height = 42,
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true
        };
        AplicarEstilo(_btnInstalar, "Mx.PrimaryButton");
        _btnInstalar.Click += Instalar_Click;
        Grid.SetColumn(_btnInstalar, 1);
        botones.Children.Add(_btnInstalar);

        cuerpo.Children.Add(botones);
        raiz.Children.Add(cuerpo);

        Content = raiz;

        Loaded += (_, _) => _btnInstalar.Focus();
    }

    private async void Instalar_Click(object sender, RoutedEventArgs e)
    {
        _btnInstalar.IsEnabled = false;
        _btnCerrar.IsEnabled = false;
        _panelProgreso.Visibility = Visibility.Visible;
        _estado.Visibility = Visibility.Visible;
        _estado.Foreground = Gris;
        _barra.IsIndeterminate = true;
        _estado.Text = "Conectando...";

        var progreso = new Progress<(long Descargado, long? Total)>(p =>
        {
            var mb = p.Descargado / 1024d / 1024d;

            if (p.Total is > 0)
            {
                _barra.IsIndeterminate = false;
                _barra.Value = p.Descargado * 100d / p.Total.Value;
                _estado.Text = $"Descargando... {mb:0.0} de {p.Total.Value / 1024d / 1024d:0.0} MB ({_barra.Value:0} %)";
            }
            else
            {
                _estado.Text = $"Descargando... {mb:0.0} MB";
            }
        });

        try
        {
            var archivo = await ActualizadorObligatorio.DescargarAsync(_datos, progreso);

            _barra.IsIndeterminate = false;
            _barra.Value = 100;
            _estado.Text = "Descarga verificada. Abriendo el instalador; Mercadex se cerrará.";

            var proceso = Process.Start(new ProcessStartInfo
            {
                FileName = archivo,
                UseShellExecute = true,
                Verb = "runas"
            });

            if (proceso == null)
                throw new InvalidOperationException("No fue posible iniciar el instalador.");

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _panelProgreso.Visibility = Visibility.Collapsed;
            _estado.Foreground = Rojo;
            _estado.Text = ex is System.ComponentModel.Win32Exception
                ? "La instalación se canceló. Para continuar, pulsa de nuevo y acepta el permiso de Windows."
                : "No se pudo descargar la actualización: " + ex.Message + " Revisa la conexión a Internet y vuelve a intentarlo.";

            _btnInstalar.Content = "Reintentar";
            _btnInstalar.IsEnabled = true;
            _btnCerrar.IsEnabled = true;
        }
    }

    private static Border Etiqueta(string texto, string fondo, string color) => new()
    {
        Background = Pincel(fondo),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(12, 4, 12, 5),
        Child = new TextBlock
        {
            Text = texto,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Pincel(color)
        }
    };

    private static string Corta(Version v) =>
        v.Build >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";

    // "Mercadex 1.6.1 - Nuevo diseño..." → "Nuevo diseño..."
    private static string QuitarPrefijo(string notas, string version)
    {
        var prefijo = $"Mercadex {version} - ";
        return notas.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
            ? notas[prefijo.Length..]
            : notas;
    }

    private static void AplicarEstilo(Button boton, string clave)
    {
        if (Application.Current?.TryFindResource(clave) is Style estilo)
            boton.Style = estilo;
    }

    private static SolidColorBrush Pincel(string hex)
    {
        var pincel = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        pincel.Freeze();
        return pincel;
    }
}
