using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mercadex.Desktop.App.Services;
using Microsoft.Win32;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Datos que la app muestra a los socios para pagar: QR de cobro de la
/// asociación (imagen que entrega el banco), cuenta e instrucciones.
/// </summary>
public sealed class ConfiguracionPagoAppWindow : Window
{
    private readonly TextBox _banco = Campo("Ej.: Banco Unión");
    private readonly TextBox _cuenta = Campo("Número de cuenta");
    private readonly TextBox _titular = Campo("A nombre de");
    private readonly TextBox _instrucciones;
    private readonly Image _qr = new() { Height = 220, Stretch = Stretch.Uniform };
    private readonly TextBlock _qrAyuda;
    private readonly TextBlock _estado;

    private string? _qrNuevoBase64;
    private bool _quitarQr;

    public ConfiguracionPagoAppWindow()
    {
        Title = "Datos de pago de la app";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        _instrucciones = new TextBox
        {
            Height = 80,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = 600,
            Tag = "Ej.: Paga el monto exacto y anota el número de transacción."
        };

        var raiz = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        raiz.Children.Add(new TextBlock { Text = "Datos de pago de la app", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = ColoresPanel.Hex("#12304D") });
        raiz.Children.Add(new TextBlock
        {
            Text = "Lo que ven los socios al pulsar «Pagar». Carga la imagen del QR de cobro que te da el banco de la asociación.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ColoresPanel.Hex("#5A6B7D"),
            Margin = new Thickness(0, 4, 0, 16)
        });

        var columnas = new Grid();
        columnas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columnas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

        var datos = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        Agregar(datos, "Banco", _banco);
        Agregar(datos, "Número de cuenta", _cuenta);
        Agregar(datos, "Titular de la cuenta", _titular);
        Agregar(datos, "Instrucciones para el socio", _instrucciones);
        columnas.Children.Add(datos);

        var panelQr = new StackPanel();
        Grid.SetColumn(panelQr, 1);
        panelQr.Children.Add(new TextBlock { Text = "QR de cobro", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex("#4A5E73"), Margin = new Thickness(0, 0, 0, 6) });
        panelQr.Children.Add(new Border
        {
            BorderBrush = ColoresPanel.Hex("#E1E7EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Child = _qr
        });
        _qrAyuda = new TextBlock { Text = "Sin QR cargado.", Foreground = ColoresPanel.Hex("#5A6B7D"), FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        panelQr.Children.Add(_qrAyuda);

        var botonesQr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var elegir = new Button { Content = "Elegir imagen", Style = (Style)Application.Current.FindResource("Mx.SecondaryButton"), MinWidth = 0, Margin = new Thickness(0, 0, 8, 0) };
        elegir.Click += (_, _) => ElegirQr();
        var quitar = new Button { Content = "Quitar", Style = (Style)Application.Current.FindResource("Mx.SecondaryButton"), MinWidth = 0 };
        quitar.Click += (_, _) =>
        {
            _quitarQr = true;
            _qrNuevoBase64 = null;
            _qr.Source = null;
            _qrAyuda.Text = "El QR se quitará al guardar.";
        };
        botonesQr.Children.Add(elegir);
        botonesQr.Children.Add(quitar);
        panelQr.Children.Add(botonesQr);
        columnas.Children.Add(panelQr);

        raiz.Children.Add(columnas);

        _estado = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), Foreground = ColoresPanel.Hex("#5A6B7D") };
        raiz.Children.Add(_estado);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cerrar = new Button { Content = "Cerrar", Style = (Style)Application.Current.FindResource("Mx.SecondaryButton"), Margin = new Thickness(0, 0, 10, 0) };
        cerrar.Click += (_, _) => Close();
        var guardar = new Button { Content = "Guardar", Style = (Style)Application.Current.FindResource("Mx.PrimaryButton") };
        guardar.Click += async (_, _) => await GuardarAsync(guardar);
        botones.Children.Add(cerrar);
        botones.Children.Add(guardar);
        raiz.Children.Add(botones);

        Content = raiz;
        Loaded += async (_, _) => await CargarAsync();
    }

    private static TextBox Campo(string ayuda) => new() { Height = 36, Tag = ayuda };

    private static void Agregar(Panel panel, string etiqueta, Control control)
    {
        panel.Children.Add(new TextBlock { Text = etiqueta, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex("#4A5E73"), Margin = new Thickness(0, 0, 0, 4) });
        control.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(control);
    }

    private async Task CargarAsync()
    {
        try
        {
            var r = await ApiSimple.GetAsync("/api/configuracion-pago-app");
            _banco.Text = r.Texto("banco");
            _cuenta.Text = r.Texto("numeroCuenta");
            _titular.Text = r.Texto("titularCuenta");
            _instrucciones.Text = r.Texto("instrucciones");

            var qr = r.Texto("qrBase64");
            if (qr.Length > 0)
            {
                _qr.Source = Imagen(Convert.FromBase64String(qr));
                _qrAyuda.Text = "QR actual.";
            }
        }
        catch (Exception ex)
        {
            _estado.Text = "No se pudo cargar: " + ex.Message;
        }
    }

    private void ElegirQr()
    {
        var dialogo = new OpenFileDialog { Title = "Imagen del QR de cobro", Filter = "Imágenes (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dialogo.ShowDialog(this) != true)
            return;

        var bytes = File.ReadAllBytes(dialogo.FileName);
        if (bytes.Length > 2 * 1024 * 1024)
        {
            _qrAyuda.Text = "La imagen pesa más de 2 MB. Usa una captura más liviana.";
            return;
        }

        try
        {
            _qr.Source = Imagen(bytes);
            _qrNuevoBase64 = Convert.ToBase64String(bytes);
            _quitarQr = false;
            _qrAyuda.Text = "Nuevo QR. Se aplicará al guardar.";
        }
        catch
        {
            _qrAyuda.Text = "No se pudo leer la imagen.";
        }
    }

    private async Task GuardarAsync(Button boton)
    {
        boton.IsEnabled = false;
        try
        {
            var r = await ApiSimple.PutAsync("/api/configuracion-pago-app", new
            {
                banco = _banco.Text,
                numeroCuenta = _cuenta.Text,
                titularCuenta = _titular.Text,
                instrucciones = _instrucciones.Text,
                qrBase64 = _qrNuevoBase64,
                quitarQr = _quitarQr
            });
            _estado.Foreground = ColoresPanel.Hex("#1D5C21");
            _estado.Text = r.Texto("mensaje");
            _qrNuevoBase64 = null;
            _quitarQr = false;
        }
        catch (Exception ex)
        {
            _estado.Foreground = ColoresPanel.Hex("#B42318");
            _estado.Text = ex.Message;
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    private static BitmapImage Imagen(byte[] bytes)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
