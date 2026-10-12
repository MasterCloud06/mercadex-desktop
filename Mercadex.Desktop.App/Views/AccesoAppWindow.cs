using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Acceso de un socio a la app móvil: ver el estado, generar el código
/// de activación (se muestra una sola vez) o retirar el acceso.
/// </summary>
public sealed class AccesoAppWindow : Window
{
    private readonly Guid _socioId;
    private readonly TextBlock _estado;
    private readonly Border _panelCodigo;
    private readonly TextBlock _codigo;
    private readonly TextBlock _codigoDetalle;
    private readonly Button _btnGenerar;
    private readonly Button _btnRetirar;

    public AccesoAppWindow(Guid socioId, string nombre, string documento)
    {
        _socioId = socioId;

        Title = "Acceso a la app de socios";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };

        raiz.Children.Add(new TextBlock { Text = nombre, FontSize = 21, FontWeight = FontWeights.Bold, Foreground = ColoresPanel.Hex("#12304D") });
        raiz.Children.Add(new TextBlock { Text = "Carnet: " + documento, Foreground = ColoresPanel.Hex("#5A6B7D"), Margin = new Thickness(0, 2, 0, 14) });

        _estado = new TextBlock { Text = "Consultando...", TextWrapping = TextWrapping.Wrap, Foreground = ColoresPanel.Hex("#1E2A38") };
        raiz.Children.Add(_estado);

        _codigo = new TextBlock
        {
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            Foreground = ColoresPanel.Hex("#12304D"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _codigoDetalle = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = ColoresPanel.Hex("#5A6B7D"),
            Margin = new Thickness(0, 6, 0, 0)
        };
        var contenidoCodigo = new StackPanel();
        contenidoCodigo.Children.Add(new TextBlock
        {
            Text = "Código de activación",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColoresPanel.Hex("#6E4F00")
        });
        contenidoCodigo.Children.Add(_codigo);
        contenidoCodigo.Children.Add(_codigoDetalle);

        _panelCodigo = new Border
        {
            Background = ColoresPanel.Hex("#FFF5D9"),
            BorderBrush = ColoresPanel.Hex("#F5D88A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 16, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = contenidoCodigo
        };
        raiz.Children.Add(_panelCodigo);

        raiz.Children.Add(new TextBlock
        {
            Text = "Cómo entra el socio: instala la app Mercadex, elige «Activar mi cuenta», escribe su carnet y este código, y crea su contraseña. " +
                   "Generar un código nuevo anula la contraseña anterior y cierra sus sesiones.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = ColoresPanel.Hex("#5A6B7D"),
            Margin = new Thickness(0, 16, 0, 0)
        });

        var botones = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };

        _btnGenerar = new Button { Content = "Generar código", Style = (Style)Application.Current.FindResource("Mx.PrimaryButton"), Margin = new Thickness(0, 0, 10, 0) };
        _btnGenerar.Click += async (_, _) => await GenerarAsync();
        botones.Children.Add(_btnGenerar);

        _btnRetirar = new Button { Content = "Retirar acceso", Style = (Style)Application.Current.FindResource("Mx.SecondaryButton"), Margin = new Thickness(0, 0, 10, 0) };
        _btnRetirar.Click += async (_, _) => await RetirarAsync();
        botones.Children.Add(_btnRetirar);

        var cerrar = new Button { Content = "Cerrar", Style = (Style)Application.Current.FindResource("Mx.SecondaryButton") };
        cerrar.Click += (_, _) => Close();
        botones.Children.Add(cerrar);

        raiz.Children.Add(botones);
        Content = raiz;

        Loaded += async (_, _) => await CargarEstadoAsync();
    }

    private async Task CargarEstadoAsync()
    {
        try
        {
            var r = await ApiSimple.GetAsync($"/api/cuentas-app/{_socioId}");
            _estado.Text = r.Texto("descripcion");
            _btnRetirar.IsEnabled = r.Texto("estado") != "sin-acceso";
            _btnGenerar.Content = r.Texto("estado") == "activa" ? "Generar código nuevo" : "Generar código";
        }
        catch (Exception ex)
        {
            _estado.Text = "No se pudo consultar: " + ex.Message;
        }
    }

    private async Task GenerarAsync()
    {
        if (_btnGenerar.Content as string == "Generar código nuevo" &&
            MessageBox.Show(this,
                "El socio ya usa la app. Con un código nuevo su contraseña actual deja de funcionar y debe activarla otra vez.\n\n¿Continuar?",
                "Generar código", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _btnGenerar.IsEnabled = false;
        try
        {
            var r = await ApiSimple.PostAsync($"/api/cuentas-app/{_socioId}/codigo");
            var codigo = r.Texto("codigo");
            _codigo.Text = codigo.Length == 8 ? $"{codigo[..4]} {codigo[4..]}" : codigo;
            var expira = r.TryGetProperty("expira", out var e) ? e.GetDateTimeOffset().ToString("dd/MM/yyyy HH:mm") : "";
            _codigoDetalle.Text = $"Entrégalo solo al socio. Vale hasta el {expira} y se usa una sola vez. No se volverá a mostrar.";
            _panelCodigo.Visibility = Visibility.Visible;
            await CargarEstadoAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudo generar el código", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _btnGenerar.IsEnabled = true;
        }
    }

    private async Task RetirarAsync()
    {
        if (MessageBox.Show(this, "El socio ya no podrá ingresar a la app y se cerrarán sus sesiones.\n\n¿Retirar el acceso?",
                "Retirar acceso", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var r = await ApiSimple.PostAsync($"/api/cuentas-app/{_socioId}/desactivar");
            _panelCodigo.Visibility = Visibility.Collapsed;
            await CargarEstadoAsync();
            _estado.Text = r.Texto("mensaje");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudo retirar el acceso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
