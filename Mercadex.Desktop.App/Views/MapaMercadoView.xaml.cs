using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Models;

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
                Text = $"{bloque.Unidades.Count} unidades · {conDeuda} con deuda",
                Foreground = Pincel("#5A6B7D"),
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            DockPanel.SetDock(resumen, Dock.Right);
            encabezado.Children.Add(resumen);
            encabezado.Children.Add(new TextBlock
            {
                Text = "Bloque " + bloque.Bloque,
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
            _seleccionada = u;
            foreach (var otra in PanelBloques.Children.OfType<WrapPanel>().SelectMany(w => w.Children.OfType<Button>()))
                otra.Tag = null;
            celda.Tag = "seleccionada";
            MostrarDetalle();
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
            Text = "Bloque " + u.Bloque,
            Foreground = Pincel("#5A6B7D"),
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
