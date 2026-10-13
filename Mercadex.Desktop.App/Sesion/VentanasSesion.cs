using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Views;

namespace Mercadex.Desktop.App.Sesion;

/// <summary>Piezas comunes de las ventanas de sesión.</summary>
internal static class UiSesion
{
    public static readonly Brush Tinta = ColoresPanel.Hex("#12304D");
    public static readonly Brush Suave = ColoresPanel.Hex("#5A6B7D");
    public static readonly Brush Borde = ColoresPanel.Hex("#C9D3DF");
    public static readonly Brush BordeFoco = ColoresPanel.Hex("#1F4E79");
    public static readonly Brush FondoError = ColoresPanel.Hex("#FDECEC");
    public static readonly Brush TextoError = ColoresPanel.Hex("#A12A2A");
    public static readonly Brush Azul = ColoresPanel.Hex("#163A5B");
    public static readonly Brush Maiz = ColoresPanel.Hex("#F2B705");

    public static TextBlock Etiqueta(string texto) => new()
    {
        Text = texto,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = ColoresPanel.Hex("#4A5E73"),
        Margin = new Thickness(0, 0, 0, 5)
    };

    public static TextBox Texto()
    {
        var caja = new TextBox { Height = 40, FontSize = 15, Margin = new Thickness(0, 0, 0, 14) };
        caja.Style = (Style)Application.Current.FindResource("Mx.TextBox");
        return caja;
    }

    /// <summary>Campo de contraseña con el mismo aspecto que los campos de texto.</summary>
    public static Border Clave(out PasswordBox clave)
    {
        clave = new PasswordBox
        {
            Height = 38,
            FontSize = 15,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 0, 10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null
        };

        var marco = new Border
        {
            BorderBrush = Borde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.White,
            Margin = new Thickness(0, 0, 0, 14),
            Child = clave
        };

        clave.GotKeyboardFocus += (_, _) => { marco.BorderBrush = BordeFoco; marco.BorderThickness = new Thickness(2); };
        clave.LostKeyboardFocus += (_, _) => { marco.BorderBrush = Borde; marco.BorderThickness = new Thickness(1); };
        return marco;
    }

    public static Border Aviso(out TextBlock texto)
    {
        texto = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = TextoError, FontSize = 13.5 };
        return new Border
        {
            Background = FondoError,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 14),
            Visibility = Visibility.Collapsed,
            Child = texto
        };
    }

    public static Button Boton(string texto, bool principal)
    {
        var boton = new Button
        {
            Content = texto,
            Height = 42,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 0, 18, 0)
        };
        boton.Style = (Style)Application.Current.FindResource(principal ? "Mx.PrimaryButton" : "Mx.SecondaryButton");
        return boton;
    }

    /// <summary>Cabecera azul con el sello de Mercadex.</summary>
    public static Border Cabecera(string titulo, string bajada)
    {
        var sello = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            Background = Maiz,
            Child = new TextBlock
            {
                Text = "M",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Azul,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var textos = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        textos.Children.Add(new TextBlock { Text = titulo, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        textos.Children.Add(new TextBlock
        {
            Text = bajada,
            FontSize = 13.5,
            Foreground = ColoresPanel.Hex("#B9CCDD"),
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        var pila = new StackPanel();
        pila.Children.Add(sello);
        pila.Children.Add(textos);

        if (EsDesarrollo)
        {
            pila.Children.Add(new Border
            {
                Background = Maiz,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = $"Desarrollo · {ApiConfiguration.BaseUri.Host}",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Azul
                }
            });
        }

        return new Border
        {
            Background = (Brush)Application.Current.FindResource("Mx.SidebarBrush"),
            Padding = new Thickness(32, 28, 32, 26),
            Child = pila
        };
    }

    public static bool EsDesarrollo =>
        ApiConfiguration.BaseUri.IsLoopback;

    public static void MostrarError(Border aviso, TextBlock texto, string? mensaje)
    {
        texto.Text = mensaje ?? "";
        aviso.Visibility = string.IsNullOrEmpty(mensaje) ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>
/// Inicio de sesión. También se usa para desbloquear tras inactividad
/// o cuando la sesión venció.
/// </summary>
public sealed class VentanaIngreso : Window
{
    private readonly TextBox _usuario;
    private readonly PasswordBox _clave;
    private readonly Border _aviso;
    private readonly TextBlock _textoAviso;
    private readonly Button _ingresar;

    /// <param name="usuarioSugerido">Usuario a mostrar (al desbloquear).</param>
    /// <param name="motivo">Por qué se vuelve a pedir la sesión.</param>
    public VentanaIngreso(string? usuarioSugerido = null, string? motivo = null)
    {
        Title = "Mercadex · Iniciar sesión";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        ShowInTaskbar = true;

        var bloqueo = motivo is not null;

        var raiz = new DockPanel();

        var cabecera = UiSesion.Cabecera(
            bloqueo ? "Sesión bloqueada" : "Mercadex",
            bloqueo ? motivo! : "Ingresa con tu usuario para continuar.");
        DockPanel.SetDock(cabecera, Dock.Top);
        raiz.Children.Add(cabecera);

        var formulario = new StackPanel { Margin = new Thickness(32, 24, 32, 26) };

        formulario.Children.Add(_aviso = UiSesion.Aviso(out _textoAviso));

        formulario.Children.Add(UiSesion.Etiqueta("Usuario"));
        formulario.Children.Add(_usuario = UiSesion.Texto());
        _usuario.Text = usuarioSugerido ?? "";

        formulario.Children.Add(UiSesion.Etiqueta("Contraseña"));
        formulario.Children.Add(UiSesion.Clave(out _clave));

        _ingresar = UiSesion.Boton("Ingresar", principal: true);
        _ingresar.IsDefault = true;
        _ingresar.Margin = new Thickness(0, 4, 0, 0);
        _ingresar.Click += async (_, _) => await IngresarAsync();
        formulario.Children.Add(_ingresar);

        if (bloqueo)
        {
            var salir = UiSesion.Boton("Salir de Mercadex", principal: false);
            salir.Margin = new Thickness(0, 10, 0, 0);
            salir.Click += (_, _) => { DialogResult = false; };
            formulario.Children.Add(salir);
        }

        formulario.Children.Add(new TextBlock
        {
            Text = "¿Olvidaste tu contraseña? Pide al administrador que la restablezca.",
            FontSize = 12.5,
            Foreground = UiSesion.Suave,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        });

        raiz.Children.Add(formulario);
        Content = raiz;

        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(_usuario.Text))
                _usuario.Focus();
            else
                _clave.Focus();
        };
    }

    private async Task IngresarAsync()
    {
        var usuario = _usuario.Text.Trim();
        var clave = _clave.Password;

        if (usuario.Length == 0 || clave.Length == 0)
        {
            UiSesion.MostrarError(_aviso, _textoAviso, "Escribe tu usuario y tu contraseña.");
            (usuario.Length == 0 ? (Control)_usuario : _clave).Focus();
            return;
        }

        UiSesion.MostrarError(_aviso, _textoAviso, null);
        _ingresar.IsEnabled = false;
        _ingresar.Content = "Ingresando…";
        Cursor = Cursors.Wait;

        try
        {
            var respuesta = await SesionActual.IngresarAsync(usuario, clave);

            if (respuesta.DebeCambiarContrasena)
            {
                var cambio = new VentanaCambiarContrasena(obligatorio: true) { Owner = this };

                if (cambio.ShowDialog() != true)
                {
                    SesionActual.Cerrar();
                    _clave.Clear();
                    UiSesion.MostrarError(_aviso, _textoAviso,
                        "Para usar Mercadex primero debes elegir tu contraseña.");
                    return;
                }
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            _clave.Clear();
            _clave.Focus();
            UiSesion.MostrarError(_aviso, _textoAviso, ex.Message);
        }
        finally
        {
            _ingresar.IsEnabled = true;
            _ingresar.Content = "Ingresar";
            Cursor = null;
        }
    }
}

/// <summary>Cambio de la contraseña propia.</summary>
public sealed class VentanaCambiarContrasena : Window
{
    private readonly PasswordBox _actual;
    private readonly PasswordBox _nueva;
    private readonly PasswordBox _repetir;
    private readonly Border _aviso;
    private readonly TextBlock _textoAviso;
    private readonly Button _guardar;

    public VentanaCambiarContrasena(bool obligatorio = false)
    {
        Title = "Mercadex · Cambiar contraseña";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        ShowInTaskbar = false;

        var raiz = new DockPanel();

        var cabecera = UiSesion.Cabecera(
            obligatorio ? "Elige tu contraseña" : "Cambiar contraseña",
            obligatorio
                ? "Estás usando una contraseña temporal. Elige una propia para continuar."
                : $"{SesionActual.Nombre} · {SesionActual.RolTexto}");
        DockPanel.SetDock(cabecera, Dock.Top);
        raiz.Children.Add(cabecera);

        var formulario = new StackPanel { Margin = new Thickness(32, 24, 32, 26) };
        formulario.Children.Add(_aviso = UiSesion.Aviso(out _textoAviso));

        formulario.Children.Add(UiSesion.Etiqueta(obligatorio ? "Contraseña temporal" : "Contraseña actual"));
        formulario.Children.Add(UiSesion.Clave(out _actual));

        formulario.Children.Add(UiSesion.Etiqueta("Contraseña nueva"));
        formulario.Children.Add(UiSesion.Clave(out _nueva));

        formulario.Children.Add(UiSesion.Etiqueta("Repite la contraseña nueva"));
        formulario.Children.Add(UiSesion.Clave(out _repetir));

        formulario.Children.Add(new TextBlock
        {
            Text = "Mínimo 8 caracteres, con letras y números.",
            FontSize = 12.5,
            Foreground = UiSesion.Suave,
            Margin = new Thickness(0, -6, 0, 16)
        });

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var cancelar = UiSesion.Boton("Cancelar", principal: false);
        cancelar.IsCancel = true;
        cancelar.Margin = new Thickness(0, 0, 10, 0);
        botones.Children.Add(cancelar);

        _guardar = UiSesion.Boton("Guardar contraseña", principal: true);
        _guardar.IsDefault = true;
        _guardar.Click += async (_, _) => await GuardarAsync();
        botones.Children.Add(_guardar);

        formulario.Children.Add(botones);
        raiz.Children.Add(formulario);
        Content = raiz;

        Loaded += (_, _) => _actual.Focus();
    }

    private async Task GuardarAsync()
    {
        var nueva = _nueva.Password;

        string? problema =
            _actual.Password.Length == 0 ? "Escribe tu contraseña actual." :
            nueva.Length < 8 ? "La contraseña nueva debe tener al menos 8 caracteres." :
            !nueva.Any(char.IsLetter) || !nueva.Any(char.IsDigit) ? "La contraseña nueva debe tener letras y números." :
            nueva != _repetir.Password ? "Las contraseñas nuevas no coinciden." :
            null;

        if (problema is not null)
        {
            UiSesion.MostrarError(_aviso, _textoAviso, problema);
            return;
        }

        UiSesion.MostrarError(_aviso, _textoAviso, null);
        _guardar.IsEnabled = false;
        Cursor = Cursors.Wait;

        try
        {
            await SesionActual.CambiarContrasenaAsync(_actual.Password, nueva);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            UiSesion.MostrarError(_aviso, _textoAviso, ex.Message);
        }
        finally
        {
            _guardar.IsEnabled = true;
            Cursor = null;
        }
    }
}
