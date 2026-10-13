using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Mercadex.Desktop.App.Views;

namespace Mercadex.Desktop.App;

/// <summary>
/// Avisos y confirmaciones con el estilo de Mercadex. Reemplaza a
/// MessageBox con los mismos parámetros. Las líneas "Dato: valor"
/// seguidas se muestran como una ficha de datos.
/// </summary>
public static class Dialogo
{
    public static MessageBoxResult Mostrar(string mensaje) =>
        Mostrar(null, mensaje, "Mercadex", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Mostrar(string mensaje, string titulo) =>
        Mostrar(null, mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Mostrar(string mensaje, string titulo, MessageBoxButton botones) =>
        Mostrar(null, mensaje, titulo, botones, MessageBoxImage.None);

    public static MessageBoxResult Mostrar(string mensaje, string titulo, MessageBoxButton botones, MessageBoxImage icono) =>
        Mostrar(null, mensaje, titulo, botones, icono);

    public static MessageBoxResult Mostrar(string mensaje, string titulo, MessageBoxButton botones, MessageBoxImage icono,
        MessageBoxResult predeterminado) =>
        Mostrar(null, mensaje, titulo, botones, icono, predeterminado);

    public static MessageBoxResult Mostrar(Window? duenio, string mensaje) =>
        Mostrar(duenio, mensaje, "Mercadex", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Mostrar(Window? duenio, string mensaje, string titulo) =>
        Mostrar(duenio, mensaje, titulo, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Mostrar(Window? duenio, string mensaje, string titulo, MessageBoxButton botones) =>
        Mostrar(duenio, mensaje, titulo, botones, MessageBoxImage.None);

    public static MessageBoxResult Mostrar(
        Window? duenio,
        string mensaje,
        string titulo,
        MessageBoxButton botones,
        MessageBoxImage icono,
        MessageBoxResult predeterminado = MessageBoxResult.None)
    {
        if (Application.Current is null)
            return MessageBox.Show(mensaje, titulo, botones, icono, predeterminado);

        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() =>
                Mostrar(duenio, mensaje, titulo, botones, icono, predeterminado));
        }

        var ventana = new VentanaDialogo(mensaje, titulo, botones, icono, predeterminado);

        var propietario = duenio ?? VentanaActiva();
        if (propietario is not null && propietario.IsVisible && !ReferenceEquals(propietario, ventana))
        {
            ventana.Owner = propietario;
            ventana.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        ventana.ShowDialog();
        return ventana.Resultado;
    }

    /// <summary>
    /// Pide un texto (por ejemplo, el motivo de una anulación).
    /// Devuelve null si se cancela.
    /// </summary>
    public static string? PedirTexto(Window? duenio, string titulo, string mensaje, string etiqueta,
        string textoAceptar = "Aceptar", int minimo = 5, bool peligro = false)
    {
        var ventana = new Window
        {
            Title = titulo,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White,
            FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font"),
            FontSize = 14
        };

        var propietario = duenio ?? VentanaActiva();
        if (propietario is not null && propietario.IsVisible)
            ventana.Owner = propietario;
        else
            ventana.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var pila = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        pila.Children.Add(new TextBlock { Text = titulo, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta });
        pila.Children.Add(UiKit.Ayuda(mensaje, 14));

        var caja = UiKit.Texto(420, 500);
        caja.Height = 80;
        caja.TextWrapping = TextWrapping.Wrap;
        caja.AcceptsReturn = true;
        caja.VerticalContentAlignment = VerticalAlignment.Top;
        pila.Children.Add(UiKit.Campo(etiqueta, caja, 0));

        var error = new TextBlock { Foreground = ColoresPanel.Hex("#A12A2A"), Margin = new Thickness(0, 0, 0, 8) };
        pila.Children.Add(error);

        string? resultado = null;
        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelar = UiKit.Boton("Cancelar");
        cancelar.IsCancel = true;
        cancelar.Margin = new Thickness(0, 0, 10, 0);
        var aceptar = UiKit.Boton(textoAceptar, principal: !peligro, peligro: peligro);
        aceptar.Click += (_, _) =>
        {
            var texto = caja.Text.Trim();
            if (texto.Length < minimo)
            {
                error.Text = $"Escribe al menos {minimo} letras.";
                caja.Focus();
                return;
            }

            resultado = texto;
            ventana.DialogResult = true;
        };
        botones.Children.Add(cancelar);
        botones.Children.Add(aceptar);
        pila.Children.Add(botones);

        ventana.Content = pila;
        ventana.Loaded += (_, _) => caja.Focus();
        ventana.ShowDialog();
        return resultado;
    }

    private static Window? VentanaActiva() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(v => v.IsActive)
        ?? Application.Current.MainWindow;
}

internal sealed class VentanaDialogo : Window
{
    private static readonly Regex LineaDato = new(@"^\s*([^:\n]{2,40}):\s+(.+)$", RegexOptions.Compiled);

    public MessageBoxResult Resultado { get; private set; }

    private readonly MessageBoxButton _botones;

    public VentanaDialogo(string mensaje, string titulo, MessageBoxButton botones, MessageBoxImage icono,
        MessageBoxResult predeterminado)
    {
        _botones = botones;
        Resultado = botones switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.No
        };

        Title = string.IsNullOrWhiteSpace(titulo) ? "Mercadex" : titulo;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var (acento, fondoIcono, simbolo) = Estilo(icono, botones);

        // ---------- Tarjeta ----------

        var tarjeta = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(18),
            BorderBrush = ColoresPanel.Hex("#DCE4EE"),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.22, Color = Color.FromRgb(11, 31, 51) }
        };

        var raiz = new DockPanel();

        // Franja de color según el tipo de aviso.
        var franja = new Border
        {
            Height = 5,
            Background = acento,
            CornerRadius = new CornerRadius(14, 14, 0, 0)
        };
        DockPanel.SetDock(franja, Dock.Top);
        raiz.Children.Add(franja);

        // ---------- Botones ----------

        var pie = new Border
        {
            Background = ColoresPanel.Hex("#F5F8FB"),
            BorderBrush = ColoresPanel.Hex("#E6ECF2"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 14, 14),
            Padding = new Thickness(24, 14, 24, 14)
        };
        DockPanel.SetDock(pie, Dock.Bottom);

        var botonesPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var peligro = icono is MessageBoxImage.Warning or MessageBoxImage.Error && botones != MessageBoxButton.OK;

        foreach (var (texto, resultado, principal) in Botones(botones))
        {
            var boton = new Button
            {
                Content = texto,
                MinWidth = 96,
                Height = 38,
                Padding = new Thickness(18, 0, 18, 0),
                Margin = new Thickness(10, 0, 0, 0),
                Style = (Style)Application.Current.FindResource(
                    principal ? (peligro ? "Mx.DangerButton" : "Mx.PrimaryButton") : "Mx.SecondaryButton")
            };

            if (predeterminado == resultado || (predeterminado == MessageBoxResult.None && principal))
                boton.IsDefault = true;

            // Esc equivale a No / Cancelar (o a cerrar un aviso).
            boton.IsCancel = resultado is MessageBoxResult.No or MessageBoxResult.Cancel ||
                             botones == MessageBoxButton.OK;

            var r = resultado;
            boton.Click += (_, _) => { Resultado = r; DialogResult = true; };
            botonesPanel.Children.Add(boton);
        }

        pie.Child = botonesPanel;
        raiz.Children.Add(pie);

        // ---------- Contenido ----------

        var cuerpo = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };

        var circulo = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = fondoIcono,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 16, 0),
            Child = new TextBlock
            {
                Text = simbolo,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = acento,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        DockPanel.SetDock(circulo, Dock.Left);
        cuerpo.Children.Add(circulo);

        var textos = new StackPanel();
        textos.Children.Add(new TextBlock
        {
            Text = Title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColoresPanel.Hex("#12304D"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 10)
        });

        AgregarMensaje(textos, mensaje ?? "");

        cuerpo.Children.Add(new ScrollViewer
        {
            Content = textos,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = SystemParameters.WorkArea.Height - 260
        });

        raiz.Children.Add(cuerpo);
        tarjeta.Child = raiz;
        Content = tarjeta;

        // Arrastrar desde cualquier parte que no sea un botón.
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            {
                try { DragMove(); } catch { /* clic sin arrastre */ }
            }
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                try { Clipboard.SetText($"{Title}\n\n{mensaje}"); } catch { /* portapapeles ocupado */ }
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Párrafos separados por líneas en blanco. Un grupo de líneas
    /// "Dato: valor" se dibuja como ficha; un dato solo en su línea
    /// ("Motivo:") toma el párrafo siguiente como valor.
    /// </summary>
    private static void AgregarMensaje(StackPanel destino, string mensaje)
    {
        var parrafos = Regex.Split(mensaje.Replace("\r", "").Trim(), @"\n\s*\n");

        for (var i = 0; i < parrafos.Length; i++)
        {
            var lineas = parrafos[i].Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
            if (lineas.Count == 0)
                continue;

            // "Motivo:" solo, con el texto en las líneas o el párrafo siguientes.
            var soloDato = lineas[0].EndsWith(':') &&
                           lineas[0].Length <= 41 &&
                           lineas[0].Count(c => c == ' ') <= 3;

            if (soloDato && (lineas.Count > 1 || i + 1 < parrafos.Length))
            {
                var valor = lineas.Count > 1 ? string.Join("\n", lineas.Skip(1)) : parrafos[++i].Trim();
                destino.Children.Add(Ficha(new[] { (lineas[0].TrimEnd(':'), valor) }));
                continue;
            }

            var datos = lineas.Select(l => LineaDato.Match(l)).ToList();

            if (datos.All(m => m.Success) && (lineas.Count >= 2 || destino.Children.OfType<Border>().Any()))
            {
                destino.Children.Add(Ficha(datos.Select(m => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()))));
                continue;
            }

            var esPregunta = i == parrafos.Length - 1 && lineas.Count == 1 && lineas[0].TrimEnd().EndsWith('?');

            destino.Children.Add(new TextBlock
            {
                Text = string.Join("\n", lineas),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                Foreground = esPregunta ? ColoresPanel.Hex("#12304D") : ColoresPanel.Hex("#3D4F63"),
                FontWeight = esPregunta ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }
    }

    private static Border Ficha(IEnumerable<(string Dato, string Valor)> filas)
    {
        var grilla = new Grid();
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var fila = 0;
        foreach (var (dato, valor) in filas)
        {
            grilla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var etiqueta = new TextBlock
            {
                Text = dato,
                Foreground = ColoresPanel.Hex("#60748A"),
                Margin = new Thickness(0, 3, 16, 3),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(etiqueta, fila);
            grilla.Children.Add(etiqueta);

            var texto = new TextBlock
            {
                Text = valor,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColoresPanel.Hex("#12304D"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 3)
            };
            Grid.SetRow(texto, fila);
            Grid.SetColumn(texto, 1);
            grilla.Children.Add(texto);

            fila++;
        }

        return new Border
        {
            Background = ColoresPanel.Hex("#F3F6FA"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 9, 14, 9),
            Margin = new Thickness(0, 0, 0, 12),
            Child = grilla
        };
    }

    private static IEnumerable<(string Texto, MessageBoxResult Resultado, bool Principal)> Botones(MessageBoxButton botones) =>
        botones switch
        {
            MessageBoxButton.OKCancel => new[]
            {
                ("Cancelar", MessageBoxResult.Cancel, false),
                ("Aceptar", MessageBoxResult.OK, true)
            },
            MessageBoxButton.YesNo => new[]
            {
                ("No", MessageBoxResult.No, false),
                ("Sí, continuar", MessageBoxResult.Yes, true)
            },
            MessageBoxButton.YesNoCancel => new[]
            {
                ("Cancelar", MessageBoxResult.Cancel, false),
                ("No", MessageBoxResult.No, false),
                ("Sí", MessageBoxResult.Yes, true)
            },
            _ => new[] { ("Entendido", MessageBoxResult.OK, true) }
        };

    private static (Brush Acento, Brush FondoIcono, string Simbolo) Estilo(MessageBoxImage icono, MessageBoxButton botones) =>
        icono switch
        {
            MessageBoxImage.Error => (ColoresPanel.Hex("#C0392B"), ColoresPanel.Hex("#FDECEC"), ""),
            MessageBoxImage.Warning => (ColoresPanel.Hex("#B7791F"), ColoresPanel.Hex("#FFF4D6"), ""),
            MessageBoxImage.Question => (ColoresPanel.Hex("#1F4E79"), ColoresPanel.Hex("#E7F0F9"), ""),
            MessageBoxImage.Information => (ColoresPanel.Hex("#1F7A43"), ColoresPanel.Hex("#E7F5E7"), ""),
            _ => botones == MessageBoxButton.OK
                ? (ColoresPanel.Hex("#1F4E79"), ColoresPanel.Hex("#E7F0F9"), "")
                : (ColoresPanel.Hex("#1F4E79"), ColoresPanel.Hex("#E7F0F9"), "")
        };
}
