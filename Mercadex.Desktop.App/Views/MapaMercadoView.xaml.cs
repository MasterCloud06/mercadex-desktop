using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Shapes;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Mapa del mercado: todas las unidades por bloque, coloreadas según
/// su situación de pago. Desde el detalle se puede ir a cobrar.
/// </summary>
public partial class MapaMercadoView : UserControl
{
    private static readonly HttpClient Http = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(60));
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    // Situación → (nombre, fondo, borde, texto). Colores de estado del panel principal, suavizados.
    private static readonly Dictionary<string, (string Nombre, string Fondo, string Borde, string Texto)> Situaciones = new()
    {
        ["al-dia"]   = ("Al día",            "#EAF6EA", "#BFE3C1", "#1D5C21"),
        ["reciente"] = ("Deuda reciente",    "#FFF5D9", "#F5D88A", "#6E4F00"),
        ["atrasada"] = ("Atrasada (31–90 días)", "#FDE9E0", "#F3B9A0", "#8A3417"),
        ["critica"]  = ("Más de 90 días",    "#D03B3B", "#B52F2F", "#FFFFFF"),
        ["inactiva"] = ("Inactiva",          "#EEF1F5", "#DCE2EA", "#8696A7"),
    };

    private MapaMercadoRespuesta? _datos;
    private MapaUnidad? _seleccionada;
    private string? _filtroSituacion;
    private bool _cargando;

    public MapaMercadoView()
    {
        InitializeComponent();
        Loaded += MapaMercadoView_Loaded;
    }

    private async void MapaMercadoView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MapaMercadoView_Loaded;
        await CargarAsync();
    }

    private async void Actualizar_Click(object sender, RoutedEventArgs e) => await CargarAsync();

    private async Task CargarAsync()
    {
        if (_cargando)
            return;

        _cargando = true;
        BtnActualizar.IsEnabled = false;
        TxtEstado.Text = "Cargando el mapa...";

        try
        {
            _datos = await Http.GetFromJsonAsync<MapaMercadoRespuesta>("/api/mapa-mercado")
                     ?? throw new InvalidOperationException("Respuesta vacía.");

            // Mantener la selección tras actualizar.
            if (_seleccionada != null)
                _seleccionada = Todas().FirstOrDefault(u => u.Id == _seleccionada.Id);

            DibujarFiltros();
            Dibujar();
            MostrarDetalle();

            var plano = _datos.Plano;
            BtnCargarPlano.Visibility =
                plano != null && plano.CasetasEnPlano > 0 && plano.Cargadas < plano.CasetasEnPlano &&
                (Sesion.SesionActual.EsAdministrador || !Sesion.SesionActual.Iniciada)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            if (plano != null && plano.Cargadas == 0)
                VerPlano.IsChecked = true;

            TxtEstado.Text = "Cada cuadro es una unidad. El color indica cómo está con sus pagos. Actualizado a las "
                             + DateTime.Now.ToString("HH:mm") + ".";
        }
        catch (OperationCanceledException)
        {
            TxtEstado.Text = "El servidor tardó demasiado. Pulsa Actualizar.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text = "No se pudo cargar el mapa: " + ex.Message;
        }
        finally
        {
            _cargando = false;
            BtnActualizar.IsEnabled = true;
        }
    }

    private IEnumerable<MapaUnidad> Todas() =>
        _datos?.Bloques.SelectMany(b => b.Unidades) ?? Enumerable.Empty<MapaUnidad>();

    // ---------------------------------------------------------------
    // Filtros

    private void DibujarFiltros()
    {
        PanelFiltros.Children.Clear();

        var conteo = Todas().GroupBy(u => u.Situacion).ToDictionary(g => g.Key, g => g.Count());

        PanelFiltros.Children.Add(CrearChip(null, "Todas", Todas().Count(), null));

        foreach (var (clave, s) in Situaciones)
        {
            var cantidad = conteo.GetValueOrDefault(clave);
            if (cantidad == 0 && clave == "inactiva")
                continue;

            PanelFiltros.Children.Add(CrearChip(clave, s.Nombre, cantidad, s.Fondo == "#D03B3B" ? s.Fondo : s.Borde));
        }
    }

    private ToggleButton CrearChip(string? clave, string nombre, int cantidad, string? color)
    {
        var contenido = new StackPanel { Orientation = Orientation.Horizontal };

        if (color != null)
        {
            contenido.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(3),
                Background = Pincel(color),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        contenido.Children.Add(new TextBlock { Text = nombre, VerticalAlignment = VerticalAlignment.Center });
        contenido.Children.Add(new TextBlock
        {
            Text = cantidad.ToString(Bo),
            Foreground = Pincel("#5A6B7D"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var chip = new ToggleButton
        {
            Content = contenido,
            Style = (Style)FindResource("Chip"),
            IsChecked = _filtroSituacion == clave,
            Tag = clave
        };

        chip.Click += (_, _) =>
        {
            _filtroSituacion = clave;
            foreach (var otro in PanelFiltros.Children.OfType<ToggleButton>())
                otro.IsChecked = Equals(otro.Tag, clave);
            Dibujar();
        };

        return chip;
    }

    private void Buscar_TextChanged(object sender, TextChangedEventArgs e)
    {
        Dibujar();
    }

    private void Vista_Changed(object sender, RoutedEventArgs e)
    {
        if (ScrollPlano == null)
            return;
        Dibujar();
    }

    private bool Coincide(MapaUnidad u)
    {
        if (_filtroSituacion != null && u.Situacion != _filtroSituacion)
            return false;

        var texto = TxtBuscar.Text.Trim();
        if (texto.Length == 0)
            return true;

        return u.Codigo.Contains(texto, StringComparison.OrdinalIgnoreCase)
               || (u.Titular?.Contains(texto, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    // ---------------------------------------------------------------
    // Mapa

    private void Dibujar()
    {
        var plano = VerPlano.IsChecked == true && _datos?.Plano is { Ancho: > 0 };
        ScrollPlano.Visibility = plano ? Visibility.Visible : Visibility.Collapsed;
        ScrollBloques.Visibility = plano ? Visibility.Collapsed : Visibility.Visible;

        if (plano)
            DibujarPlano();
        else
            DibujarBloques();
    }

    // ---------------------------------------------------------------
    // Plano oficial

    private readonly Dictionary<Guid, Border> _celdasPlano = new();

    private void DibujarPlano()
    {
        Plano.Children.Clear();
        _celdasPlano.Clear();
        PanelFueraPlano.Children.Clear();

        if (_datos?.Plano is not { } plano)
            return;

        Plano.Width = plano.Ancho;
        Plano.Height = plano.Alto;

        var enPlano = Todas().Where(u => u.PlanoX != null && u.PlanoY != null).ToList();

        if (enPlano.Count == 0)
        {
            var aviso = new TextBlock
            {
                Text = "Las casetas todavía no tienen su lugar en el plano.\nPulsa «Cargar casetas del plano» (arriba) para ubicarlas según el plano oficial.",
                FontSize = 34,
                Foreground = Pincel("#5A6B7D"),
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(aviso, 380);
            Canvas.SetTop(aviso, 520);
            Plano.Children.Add(aviso);
        }

        // Pasillos y galerías (texto vertical, como en el plano).
        foreach (var p in plano.Pasillos.Where(p => p.Sector != "frente"))
        {
            var etiqueta = new TextBlock
            {
                Text = p.Nombre.ToUpper(Bo),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Pincel("#7A8A9C"),
                LayoutTransform = new RotateTransform(90)
            };
            Canvas.SetLeft(etiqueta, p.X - 10);
            Canvas.SetTop(etiqueta, p.Y - 30);
            Plano.Children.Add(etiqueta);
        }

        var ingreso = new TextBlock { Text = "INGRESO", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Pincel("#7A8A9C"), LayoutTransform = new RotateTransform(90) };
        Canvas.SetLeft(ingreso, 1905);
        Canvas.SetTop(ingreso, 520);
        Plano.Children.Add(ingreso);

        // Casetas.
        foreach (var u in enPlano)
        {
            var s = Situaciones.GetValueOrDefault(u.Situacion, Situaciones["al-dia"]);
            var celda = new Border
            {
                Width = 52,
                Height = 64,
                Background = Pincel(s.Fondo),
                BorderBrush = Pincel(_seleccionada?.Id == u.Id ? "#12304D" : s.Borde),
                BorderThickness = new Thickness(_seleccionada?.Id == u.Id ? 4 : 1.5),
                CornerRadius = new CornerRadius(4),
                Cursor = Cursors.Hand,
                Opacity = Coincide(u) ? 1 : 0.18,
                ToolTip = $"{u.TipoTexto} {u.Codigo} · {u.Pasillo ?? "sin pasillo"}\n{u.Titular ?? "Sin titular registrado"}" +
                          (u.Obligaciones > 0 ? "\nDebe " + TextoDeuda(u, corto: false).Replace("\n", " + ") : ""),
                Child = new TextBlock
                {
                    Text = u.Codigo,
                    FontSize = u.Codigo.Length > 3 ? 13 : 17,
                    FontWeight = FontWeights.Bold,
                    Foreground = Pincel(s.Texto),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            celda.MouseLeftButtonUp += (_, _) => Seleccionar(u);
            celda.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                    Navegacion.AbrirCobro(u.Id, u.Codigo);
            };

            Canvas.SetLeft(celda, u.PlanoX!.Value - 26);
            Canvas.SetTop(celda, u.PlanoY!.Value - 32);
            Plano.Children.Add(celda);
            _celdasPlano[u.Id] = celda;
        }

        // Lugares que el plano numera dos veces: hay que confirmarlos.
        if (enPlano.Count > 0)
        {
            foreach (var c in plano.PorConfirmar)
            {
                var hueco = new Grid { Width = 52, Height = 64, ToolTip = $"El plano repite el número {c.Numero} en este lugar.\nConfirmen qué caseta es y regístrenla en Casetas." };
                hueco.Children.Add(new Rectangle
                {
                    Stroke = Pincel("#9AABBD"),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = Pincel("#F6F8FB")
                });
                hueco.Children.Add(new TextBlock
                {
                    Text = "?",
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = Pincel("#9AABBD"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                Canvas.SetLeft(hueco, c.X - 26);
                Canvas.SetTop(hueco, c.Y - 32);
                Plano.Children.Add(hueco);
            }
        }

        // Comedores, ambulantes y unidades que no figuran en el plano.
        var fuera = Todas().Where(u => u.PlanoX == null && Coincide(u))
            .OrderBy(u => u.Tipo).ThenBy(u => u.Codigo.Length).ThenBy(u => u.Codigo).ToList();
        if (fuera.Count > 0)
        {
            PanelFueraPlano.Children.Add(new TextBlock
            {
                Text = $"Fuera del plano: comedores, ambulantes y otras unidades ({fuera.Count})",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Pincel("#12304D"),
                Margin = new Thickness(0, 0, 0, 10)
            });
            var celdas = new WrapPanel();
            foreach (var u in fuera)
                celdas.Children.Add(CrearCelda(u));
            PanelFueraPlano.Children.Add(celdas);
        }

        if (plano.PorConfirmar.Count > 0 && enPlano.Count > 0)
        {
            PanelFueraPlano.Children.Add(new TextBlock
            {
                Text = $"Los {plano.PorConfirmar.Count} cuadros con «?» son lugares que el plano numera dos veces (" +
                       string.Join(", ", plano.PorConfirmar.Select(c => c.Numero).Distinct()) +
                       "). Confirmen qué caseta es cada uno.",
                Foreground = Pincel("#5A6B7D"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });
        }
    }

    private void Seleccionar(MapaUnidad u)
    {
        if (_seleccionada is { } anterior && _celdasPlano.TryGetValue(anterior.Id, out var celdaAnterior))
        {
            var s = Situaciones.GetValueOrDefault(anterior.Situacion, Situaciones["al-dia"]);
            celdaAnterior.BorderBrush = Pincel(s.Borde);
            celdaAnterior.BorderThickness = new Thickness(1.5);
        }

        _seleccionada = u;
        if (_celdasPlano.TryGetValue(u.Id, out var celda))
        {
            celda.BorderBrush = Pincel("#12304D");
            celda.BorderThickness = new Thickness(4);
        }

        foreach (var otra in PanelBloques.Children.OfType<WrapPanel>().Concat(PanelFueraPlano.Children.OfType<WrapPanel>())
                     .SelectMany(w => w.Children.OfType<Button>()))
            otra.Tag = null;

        MostrarDetalle();
    }

    // ---------------------------------------------------------------
    // Botones del plano

    private async void CargarPlano_Click(object sender, RoutedEventArgs e)
    {
        var plano = _datos?.Plano;
        if (plano == null)
            return;

        if (Dialogo.Mostrar(Window.GetWindow(this),
                $"Se cargarán las {plano.CasetasEnPlano} casetas del plano oficial:\n\n" +
                "• Se crean las casetas que todavía no están registradas.\n" +
                "• A cada caseta se le pone su pasillo o galería y su lugar en el mapa.\n" +
                "• No se cambian titulares, deudas, recibos ni el bloque.\n\n¿Continuar?",
                "Cargar casetas del plano", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes)
            return;

        BtnCargarPlano.IsEnabled = false;
        try
        {
            var r = await ApiJson.PostAsync<ResultadoPlano>("/api/mapa-mercado/cargar-plano", new { });
            var texto = r?.Mensaje ?? "Listo.";
            if (r?.Conflictos is { Count: > 0 } conflictos)
                texto += $"\n\nNo se tocaron estos números porque ya son de un comedor o ambulante: {string.Join(", ", conflictos)}.";
            if (r?.PorConfirmar is { Count: > 0 } dudas)
                texto += $"\n\nEl plano repite estos números en dos lugares; quedan marcados con «?» para que los confirmen: " +
                         string.Join(", ", dudas.Select(d => d.Numero).Distinct()) + ".";

            Dialogo.Mostrar(Window.GetWindow(this), texto, "Casetas del plano", MessageBoxButton.OK, MessageBoxImage.Information);
            await CargarAsync();
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se cargaron las casetas", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnCargarPlano.IsEnabled = true;
        }
    }

    private void PlanoOficial_Click(object sender, RoutedEventArgs e)
    {
        var ruta = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "plano-mercado.pdf");
        if (!File.Exists(ruta))
        {
            Dialogo.Mostrar(Window.GetWindow(this), "No se encontró el plano oficial en esta instalación.", "Plano oficial",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = ruta, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), "No se pudo abrir el PDF: " + ex.Message, "Plano oficial",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Reporte_Click(object sender, RoutedEventArgs e)
    {
        if (_datos == null)
            return;
        MapaPdfService.Guardar(Window.GetWindow(this), _datos);
    }

    private sealed class ResultadoPlano
    {
        public string? Mensaje { get; set; }
        public List<string>? Conflictos { get; set; }
        public List<MapaPorConfirmar>? PorConfirmar { get; set; }
    }

    // ---------------------------------------------------------------
    // Por bloque

    private void DibujarBloques()
    {
        PanelBloques.Children.Clear();

        if (_datos == null)
            return;

        var hayAlguna = false;

        foreach (var bloque in _datos.Bloques)
        {
            var visibles = bloque.Unidades.Where(Coincide).ToList();
            if (visibles.Count == 0)
                continue;

            hayAlguna = true;

            var conDeuda = bloque.Unidades.Count(u => u.Obligaciones > 0);

            var encabezado = new DockPanel { Margin = new Thickness(0, 14, 0, 10) };
            var resumen = new TextBlock
            {
                Text = $"{bloque.Unidades.Count} {(bloque.Unidades.Count == 1 ? "unidad" : "unidades")} · {conDeuda} con deuda",
                Foreground = Pincel("#5A6B7D"),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            DockPanel.SetDock(resumen, Dock.Right);
            encabezado.Children.Add(resumen);
            encabezado.Children.Add(new TextBlock
            {
                Text = bloque.Bloque == "Sin bloque" ? "Sin bloque asignado" : "Bloque " + bloque.Bloque,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Pincel("#12304D")
            });

            var celdas = new WrapPanel();
            foreach (var unidad in visibles)
                celdas.Children.Add(CrearCelda(unidad));

            PanelBloques.Children.Add(encabezado);
            PanelBloques.Children.Add(celdas);
        }

        if (!hayAlguna)
        {
            PanelBloques.Children.Add(new TextBlock
            {
                Text = Todas().Any()
                    ? "Ninguna unidad coincide con la búsqueda o el filtro."
                    : "Todavía no hay unidades registradas. Regístralas en Casetas.",
                Foreground = Pincel("#5A6B7D"),
                Margin = new Thickness(0, 18, 0, 0)
            });
        }
    }

    private Button CrearCelda(MapaUnidad u)
    {
        var s = Situaciones.GetValueOrDefault(u.Situacion, Situaciones["al-dia"]);
        var texto = Pincel(s.Texto);

        var linea2 = u.Situacion switch
        {
            "inactiva" => "Inactiva",
            "al-dia" => "Al día",
            _ => TextoDeuda(u, corto: true)
        };

        var contenido = new StackPanel();
        contenido.Children.Add(new TextBlock
        {
            Text = u.Codigo,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = texto,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        contenido.Children.Add(new TextBlock
        {
            Text = linea2,
            FontSize = 11.5,
            Foreground = texto,
            Opacity = 0.9,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var celda = new Button
        {
            Style = (Style)FindResource("Celda"),
            Background = Pincel(s.Fondo),
            BorderBrush = Pincel(s.Borde),
            Content = contenido,
            Tag = _seleccionada?.Id == u.Id ? "seleccionada" : null,
            ToolTip = $"{u.TipoTexto} {u.Codigo} · {u.Titular ?? "sin titular registrado"}"
        };

        celda.Click += (_, _) =>
        {
            Seleccionar(u);
            celda.Tag = "seleccionada";
        };

        celda.MouseDoubleClick += (_, _) => Navegacion.AbrirCobro(u.Id, u.Codigo);

        return celda;
    }

    // ---------------------------------------------------------------
    // Detalle

    private void MostrarDetalle()
    {
        if (_seleccionada == null)
            return;

        var u = _seleccionada;
        var s = Situaciones.GetValueOrDefault(u.Situacion, Situaciones["al-dia"]);

        PanelDetalle.Children.Clear();

        PanelDetalle.Children.Add(new TextBlock
        {
            Text = $"{u.TipoTexto} {u.Codigo}",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Pincel("#12304D")
        });
        PanelDetalle.Children.Add(new TextBlock
        {
            Text = (u.Pasillo ?? "Sin pasillo") + " · " + (u.Bloque == "Sin bloque" ? "sin bloque" : "Bloque " + u.Bloque),
            Foreground = Pincel("#5A6B7D"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 12)
        });

        PanelDetalle.Children.Add(new Border
        {
            Background = Pincel(s.Fondo),
            BorderBrush = Pincel(s.Borde),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = s.Nombre, Foreground = Pincel(s.Texto), FontWeight = FontWeights.SemiBold, FontSize = 12.5 }
        });

        Fila("Titular", u.Titular ?? "Sin titular registrado");

        if (u.Obligaciones > 0)
        {
            Fila("Debe", TextoDeuda(u, corto: false));
            Fila("Obligaciones pendientes", u.Obligaciones.ToString(Bo));
            Fila("La más antigua", u.DiasDeuda == 1 ? "hace 1 día" : $"hace {u.DiasDeuda} días");
        }
        else if (u.Activa)
        {
            Fila("Debe", "Nada. Está al día.");
        }

        var cobrar = new Button
        {
            Content = u.Obligaciones > 0 ? "Cobrar esta unidad" : "Abrir en Ingresos",
            Style = (Style)FindResource(u.Obligaciones > 0 ? "Mx.PrimaryButton" : "Mx.SecondaryButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 18, 0, 0),
            IsEnabled = u.Activa
        };
        cobrar.Click += (_, _) => Navegacion.AbrirCobro(u.Id, u.Codigo);
        PanelDetalle.Children.Add(cobrar);

        PanelDetalle.Children.Add(new TextBlock
        {
            Text = "Doble clic en un cuadro también abre el cobro.",
            Foreground = Pincel("#7A8A9C"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
    }

    private void Fila(string etiqueta, string valor)
    {
        PanelDetalle.Children.Add(new TextBlock
        {
            Text = etiqueta,
            Foreground = Pincel("#5A6B7D"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 14, 0, 2)
        });
        PanelDetalle.Children.Add(new TextBlock
        {
            Text = valor,
            Foreground = Pincel("#1E2A38"),
            FontSize = 14.5,
            TextWrapping = TextWrapping.Wrap
        });
    }

    private static string TextoDeuda(MapaUnidad u, bool corto)
    {
        if (u.Deudas.Count == 0)
            return "";

        return string.Join(corto ? " + " : "\n", u.Deudas.Select(d =>
        {
            var simbolo = d.Moneda == "BOB" ? "Bs" : d.Moneda == "USD" ? "$us" : d.Moneda;
            var formato = corto && d.Total == Math.Round(d.Total) ? "N0" : "N2";
            return $"{simbolo} {d.Total.ToString(formato, Bo)}";
        }));
    }

    private static SolidColorBrush Pincel(string hex)
    {
        var pincel = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        pincel.Freeze();
        return pincel;
    }
}
