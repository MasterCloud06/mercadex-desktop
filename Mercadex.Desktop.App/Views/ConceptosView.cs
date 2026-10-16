using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Conceptos de cobro (las cuentas de ingreso: Mensualidad, Alquiler de
/// caseta, Excedente de luz…). Observación de la directiva: retirar los que
/// ya no se usan. Un concepto no se borra, se DESACTIVA: deja de aparecer al
/// cargar deudas y tarifas, pero los recibos y reportes anteriores lo
/// siguen mostrando.
/// </summary>
public sealed class ConceptosView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly TextBlock _resumen = new() { Foreground = UiKit.Suave, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly StackPanel _ficha = new();
    private readonly StackPanel _filtros = new() { Orientation = Orientation.Horizontal };
    private readonly Border _vacioLista = new() { Visibility = Visibility.Collapsed };

    private string _filtro = "activos";
    private List<ConceptoFila> _todos = new();

    public ConceptosView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        // ---------- Encabezado ----------

        var arriba = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(arriba, Dock.Top);

        var titulo = new DockPanel();
        var nuevo = UiKit.Boton("+ Nuevo concepto", principal: true);
        nuevo.VerticalAlignment = VerticalAlignment.Center;
        nuevo.Click += async (_, _) => await NuevoAsync();
        DockPanel.SetDock(nuevo, Dock.Right);
        titulo.Children.Add(nuevo);
        titulo.Children.Add(UiKit.Encabezado("Conceptos de cobro"));
        arriba.Children.Add(titulo);
        arriba.Children.Add(UiKit.Ayuda(
            "Los servicios que se cobran (Mensualidad, Alquiler de caseta, Excedente de luz…). " +
            "Los que ya no se usan se desactivan: dejan de aparecer al cargar deudas, pero los recibos y reportes anteriores los conservan.", 12));

        // Filtro como pestañas compactas + mensaje de estado a la derecha.
        var barra = new DockPanel();
        DockPanel.SetDock(_filtros, Dock.Left);
        barra.Children.Add(_filtros);
        _resumen.Margin = new Thickness(16, 0, 0, 0);
        _resumen.TextAlignment = TextAlignment.Right;
        barra.Children.Add(_resumen);
        arriba.Children.Add(barra);
        raiz.Children.Add(arriba);

        // ---------- Cuerpo: lista y ficha ----------

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        ArmarTabla();

        var lista = new Grid();
        lista.Children.Add(_tabla);
        lista.Children.Add(_vacioLista);

        var tarjetaLista = new Border
        {
            Background = Brushes.White,
            BorderBrush = ColoresPanel.Hex("#E1E7EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = lista
        };
        // Esquinas redondeadas también para la tabla.
        tarjetaLista.Loaded += (_, _) => RecortarEsquinas(tarjetaLista, lista);
        tarjetaLista.SizeChanged += (_, _) => RecortarEsquinas(tarjetaLista, lista);
        Grid.SetColumn(tarjetaLista, 0);
        cuerpo.Children.Add(tarjetaLista);

        var panel = new Border
        {
            Style = UiKit.Estilo("Mx.Card"),
            Margin = new Thickness(0),
            Padding = new Thickness(22, 20, 22, 20),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _ficha
            }
        };
        Grid.SetColumn(panel, 2);
        cuerpo.Children.Add(panel);

        var desplazable = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = cuerpo
        };
        raiz.Children.Add(desplazable);
        Content = raiz;

        PintarFiltros();
        MostrarVacio();
        Loaded += async (_, _) => await CargarAsync();
    }

    private static void RecortarEsquinas(Border tarjeta, FrameworkElement contenido)
    {
        if (contenido.ActualWidth <= 0 || contenido.ActualHeight <= 0) return;
        contenido.Clip = new RectangleGeometry(new Rect(0, 0, contenido.ActualWidth, contenido.ActualHeight), 11, 11);
    }

    // =====================================================================
    // Tabla
    // =====================================================================

    private void ArmarTabla()
    {
        _tabla.BorderThickness = new Thickness(0);
        _tabla.RowHeight = 56;
        _tabla.ColumnHeaderHeight = 42;
        _tabla.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _tabla.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _tabla.CanUserResizeColumns = false;

        // Concepto: nombre y, debajo, el código.
        var concepto = new FrameworkElementFactory(typeof(StackPanel));
        concepto.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
        concepto.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 8, 0));
        var nombre = new FrameworkElementFactory(typeof(TextBlock));
        nombre.SetBinding(TextBlock.TextProperty, new Binding(nameof(ConceptoFila.Nombre)));
        nombre.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        nombre.SetValue(TextBlock.ForegroundProperty, UiKit.Tinta);
        nombre.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var codigo = new FrameworkElementFactory(typeof(TextBlock));
        codigo.SetBinding(TextBlock.TextProperty, new Binding(nameof(ConceptoFila.Codigo)));
        codigo.SetValue(TextBlock.FontSizeProperty, 12.0);
        codigo.SetValue(TextBlock.ForegroundProperty, UiKit.Suave);
        codigo.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        concepto.AppendChild(nombre);
        concepto.AppendChild(codigo);
        _tabla.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Concepto",
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 160,
            CellTemplate = new DataTemplate { VisualTree = concepto }
        });

        _tabla.Columns.Add(Numero("Deudas", nameof(ConceptoFila.Deudas), 76));
        _tabla.Columns.Add(Numero("Pendientes", nameof(ConceptoFila.Pendientes), 96));
        _tabla.Columns.Add(Numero("Tarifas", nameof(ConceptoFila.Tarifas), 76));

        // Estado como etiqueta de color.
        var chip = new FrameworkElementFactory(typeof(Border));
        chip.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        chip.SetValue(Border.PaddingProperty, new Thickness(10, 3, 10, 3));
        chip.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        chip.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        chip.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
        chip.SetBinding(Border.BackgroundProperty, new Binding(nameof(ConceptoFila.FondoEstado)));
        var chipTexto = new FrameworkElementFactory(typeof(TextBlock));
        chipTexto.SetBinding(TextBlock.TextProperty, new Binding(nameof(ConceptoFila.EstadoTexto)));
        chipTexto.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ConceptoFila.TintaEstado)));
        chipTexto.SetValue(TextBlock.FontSizeProperty, 12.0);
        chipTexto.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        chip.AppendChild(chipTexto);
        _tabla.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Estado",
            Width = new DataGridLength(118),
            CellTemplate = new DataTemplate { VisualTree = chip }
        });

        var estilo = new Style(typeof(DataGridRow), UiKit.Estilo("Mx.Row"));
        estilo.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));
        var inactivo = new DataTrigger { Binding = new Binding(nameof(ConceptoFila.Activo)), Value = false };
        inactivo.Setters.Add(new Setter(OpacityProperty, 0.65));
        estilo.Triggers.Add(inactivo);
        _tabla.RowStyle = estilo;
        _tabla.SelectionChanged += (_, _) => MostrarFicha();
    }

    private static DataGridTextColumn Numero(string titulo, string propiedad, double ancho)
    {
        var columna = UiKit.Columna(titulo, propiedad, 1, derecha: true);
        columna.Width = new DataGridLength(ancho);
        var celda = new Style(typeof(TextBlock), columna.ElementStyle);
        celda.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        celda.Setters.Add(new Setter(TextBlock.ForegroundProperty, UiKit.Tinta));
        columna.ElementStyle = celda;
        return columna;
    }

    // =====================================================================

    private async Task CargarAsync(Guid? seleccionar = null)
    {
        seleccionar ??= (_tabla.SelectedItem as ConceptoFila)?.Id;
        try
        {
            _resumen.Text = "Cargando…";
            _todos = await ApiJson.GetAsync<List<ConceptoFila>>("/api/conceptos-cobro") ?? new();
            _resumen.Text = "";
            PintarFiltros();
            Filtrar(seleccionar);
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudieron cargar los conceptos: " + ex.Message;
        }
    }

    /// <summary>Pestañas «En uso / Desactivados / Todos» con su cantidad.</summary>
    private void PintarFiltros()
    {
        _filtros.Children.Clear();
        foreach (var (valor, texto, n) in new[]
                 {
                     ("activos", "En uso", _todos.Count(c => c.Activo)),
                     ("inactivos", "Desactivados", _todos.Count(c => !c.Activo)),
                     ("todos", "Todos", _todos.Count)
                 })
        {
            var activo = _filtro == valor;
            var contenido = new StackPanel { Orientation = Orientation.Horizontal };
            contenido.Children.Add(new TextBlock { Text = texto, FontWeight = activo ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
            contenido.Children.Add(new Border
            {
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(7, 0, 7, 1),
                CornerRadius = new CornerRadius(9),
                Background = ColoresPanel.Hex(activo ? "#FFFFFF" : "#E4EAF1"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = n.ToString(Bo), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex(activo ? "#1F4E79" : "#4A5E73") }
            });

            var boton = new Button
            {
                Content = contenido,
                Height = 36,
                Padding = new Thickness(14, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Foreground = activo ? Brushes.White : UiKit.Etiqueta,
                Background = ColoresPanel.Hex(activo ? "#1F4E79" : "#FFFFFF"),
                BorderBrush = ColoresPanel.Hex(activo ? "#1F4E79" : "#D5DEE8"),
                BorderThickness = new Thickness(1),
                Template = PlantillaPildora()
            };
            boton.Click += (_, _) =>
            {
                _filtro = valor;
                PintarFiltros();
                Filtrar();
            };
            _filtros.Children.Add(boton);
        }
    }

    private static ControlTemplate PlantillaPildora()
    {
        var borde = new FrameworkElementFactory(typeof(Border));
        borde.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
        borde.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        borde.SetBinding(Border.BorderBrushProperty, new Binding(nameof(BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        borde.SetBinding(Border.BorderThicknessProperty, new Binding(nameof(BorderThickness)) { RelativeSource = RelativeSource.TemplatedParent });
        borde.SetBinding(Border.PaddingProperty, new Binding(nameof(Padding)) { RelativeSource = RelativeSource.TemplatedParent });
        var presentador = new FrameworkElementFactory(typeof(ContentPresenter));
        presentador.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        borde.AppendChild(presentador);
        return new ControlTemplate(typeof(Button)) { VisualTree = borde };
    }

    private void Filtrar(Guid? seleccionar = null)
    {
        seleccionar ??= (_tabla.SelectedItem as ConceptoFila)?.Id;
        var filas = _todos.Where(c => _filtro switch
        {
            "inactivos" => !c.Activo,
            "todos" => true,
            _ => c.Activo
        }).ToList();

        _tabla.ItemsSource = filas;

        // Lista vacía: mensaje en lugar de una tabla en blanco.
        _vacioLista.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _tabla.Visibility = filas.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (filas.Count == 0)
        {
            _vacioLista.Padding = new Thickness(24, 40, 24, 40);
            _vacioLista.Child = new TextBlock
            {
                Text = _filtro == "inactivos" ? "No hay conceptos desactivados." : "Todavía no hay conceptos.",
                Foreground = UiKit.Suave,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        if (seleccionar is { } id && filas.FirstOrDefault(f => f.Id == id) is { } fila)
            _tabla.SelectedItem = fila;
        else if (_tabla.SelectedItem is null)
            MostrarVacio();
    }

    // =====================================================================
    // Ficha del concepto
    // =====================================================================

    private void MostrarVacio()
    {
        _ficha.Children.Clear();
        var icono = new TextBlock
        {
            Text = "\uE8EC",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 28,
            Foreground = ColoresPanel.Hex("#9AABBD"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 10)
        };
        _ficha.Children.Add(icono);
        _ficha.Children.Add(new TextBlock
        {
            Text = "Elige un concepto de la lista para corregir su nombre o dejar de usarlo.",
            Foreground = UiKit.Suave,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(8, 0, 8, 24)
        });
    }

    private void MostrarFicha()
    {
        if (_tabla.SelectedItem is not ConceptoFila c)
        {
            MostrarVacio();
            return;
        }

        _ficha.Children.Clear();

        // Cabecera: nombre, código y estado.
        var cabeza = new DockPanel();
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(10, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = c.FondoEstado,
            Child = new TextBlock { Text = c.EstadoTexto, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = c.TintaEstado }
        };
        DockPanel.SetDock(chip, Dock.Right);
        cabeza.Children.Add(chip);
        cabeza.Children.Add(new TextBlock { Text = c.Nombre, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo, TextWrapping = TextWrapping.Wrap });
        _ficha.Children.Add(cabeza);
        _ficha.Children.Add(new TextBlock { Text = "Código " + c.Codigo, FontSize = 12.5, Foreground = UiKit.Suave, Margin = new Thickness(0, 2, 0, 14) });

        // Uso en cifras.
        var cifras = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 18) };
        cifras.Children.Add(Cifra("Deudas", c.Deudas, new Thickness(0, 0, 6, 0)));
        cifras.Children.Add(Cifra("Pendientes", c.Pendientes, new Thickness(3, 0, 3, 0)));
        cifras.Children.Add(Cifra("Tarifas", c.Tarifas, new Thickness(6, 0, 0, 0)));
        _ficha.Children.Add(cifras);

        // ---------- Corregir ----------

        _ficha.Children.Add(Seccion("Corregir"));

        var nombre = new TextBox { MaxLength = 150, Style = UiKit.Estilo("Mx.TextBox"), Text = c.Nombre };
        var descripcion = new TextBox { MaxLength = 300, Style = UiKit.Estilo("Mx.TextBox"), Text = c.Descripcion ?? "" };
        _ficha.Children.Add(CampoAncho("Nombre (como sale en el recibo)", nombre));
        _ficha.Children.Add(CampoAncho("Descripción (opcional)", descripcion));

        var guardar = UiKit.Boton("Guardar cambios", principal: true);
        guardar.HorizontalAlignment = HorizontalAlignment.Stretch;
        guardar.Margin = new Thickness(0, 4, 0, 0);
        guardar.Click += async (_, _) => await GuardarAsync(c, nombre.Text, descripcion.Text);
        _ficha.Children.Add(guardar);
        _ficha.Children.Add(new TextBlock
        {
            Text = "Los recibos ya emitidos conservan el nombre con el que se cobraron.",
            FontSize = 12.5,
            Foreground = UiKit.Suave,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });

        // ---------- Estado ----------

        _ficha.Children.Add(new Border { Height = 1, Background = ColoresPanel.Hex("#E6ECF2"), Margin = new Thickness(0, 18, 0, 16) });
        _ficha.Children.Add(Seccion(c.Activo ? "Dejar de usarlo" : "Volver a usarlo"));
        _ficha.Children.Add(new TextBlock
        {
            Text = c.Activo
                ? "Ya no aparecerá al cargar deudas ni al asignar tarifas. Sus deudas pendientes se pueden seguir cobrando y todo su historial se conserva."
                : "Vuelve a aparecer al cargar deudas y tarifas.",
            FontSize = 13,
            Foreground = UiKit.Suave,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var estado = UiKit.Boton(c.Activo ? "Desactivar concepto" : "Activar concepto", principal: !c.Activo, peligro: c.Activo);
        estado.HorizontalAlignment = HorizontalAlignment.Stretch;
        estado.Click += async (_, _) => await CambiarEstadoAsync(c);
        _ficha.Children.Add(estado);
    }

    private static TextBlock Seccion(string texto) => new()
    {
        Text = texto,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = UiKit.Titulo,
        Margin = new Thickness(0, 0, 0, 10)
    };

    private static StackPanel CampoAncho(string etiqueta, TextBox caja)
    {
        caja.HorizontalAlignment = HorizontalAlignment.Stretch;
        caja.Height = 38;
        var pila = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        pila.Children.Add(new TextBlock { Text = etiqueta, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Etiqueta, Margin = new Thickness(0, 0, 0, 5) });
        pila.Children.Add(caja);
        return pila;
    }

    private static Border Cifra(string titulo, int valor, Thickness margen)
    {
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock { Text = valor.ToString("N0", Bo), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = UiKit.Tinta });
        pila.Children.Add(new TextBlock { Text = titulo, FontSize = 12, Foreground = UiKit.Suave });
        return new Border
        {
            Background = ColoresPanel.Hex("#F3F6FA"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = margen,
            Child = pila
        };
    }

    // =====================================================================

    private async Task Ejecutar(Func<Task<Mensaje?>> accion, Guid seleccionar, string error)
    {
        try
        {
            var r = await accion();
            await CargarAsync(seleccionar);
            if (!string.IsNullOrWhiteSpace(r?.Texto))
                _resumen.Text = "✓ " + r!.Texto;
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, error, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Task GuardarAsync(ConceptoFila c, string nombre, string descripcion) =>
        Ejecutar(() => ApiJson.PutAsync<Mensaje>($"/api/conceptos-cobro/{c.Id}",
                new { nombre = nombre.Trim(), descripcion = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim() }),
            c.Id, "No se guardó el concepto");

    private async Task CambiarEstadoAsync(ConceptoFila c)
    {
        if (c.Activo)
        {
            var texto = $"Concepto: {c.Nombre}\n\nYa no aparecerá al cargar deudas nuevas ni al asignar tarifas." +
                        (c.Pendientes > 0 ? $"\nSus {c.Pendientes} deudas pendientes se pueden seguir cobrando." : "") +
                        "\nLos recibos y reportes anteriores lo siguen mostrando. Puedes volver a activarlo cuando quieras.\n\n¿Desactivar?";
            if (Dialogo.Mostrar(Window.GetWindow(this), texto, "Desactivar concepto", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        await Ejecutar(() => ApiJson.PostAsync<Mensaje>($"/api/conceptos-cobro/{c.Id}/{(c.Activo ? "desactivar" : "activar")}", new { }),
            c.Id, "No se cambió el estado");
    }

    private async Task NuevoAsync()
    {
        var ventana = new NuevoConceptoWindow { Owner = Window.GetWindow(this) };
        if (ventana.ShowDialog() == true)
            await CargarAsync(ventana.ConceptoCreado?.Id);
    }

    // =====================================================================

    private sealed class Mensaje
    {
        [System.Text.Json.Serialization.JsonPropertyName("mensaje")]
        public string? Texto { get; set; }
    }

    public sealed class ConceptoFila
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Descripcion { get; set; }
        public bool Activo { get; set; }
        public int Deudas { get; set; }
        public int Pendientes { get; set; }
        public int Tarifas { get; set; }

        public string EstadoTexto => Activo ? "En uso" : "Desactivado";

        public Brush FondoEstado => ColoresPanel.Hex(Activo ? "#E7F5E7" : "#EEF2F7");

        public Brush TintaEstado => ColoresPanel.Hex(Activo ? "#0B5A0B" : "#5A6B7D");
    }
}
