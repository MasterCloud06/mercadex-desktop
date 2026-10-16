using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

    private readonly ComboBox _mostrar = UiKit.Combo(170);
    private readonly TextBlock _resumen = new() { Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly StackPanel _ficha = new();

    private List<ConceptoFila> _todos = new();

    public ConceptosView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        var arriba = new StackPanel();
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
            "Los que ya no se usan se desactivan: dejan de aparecer al cargar deudas, pero los recibos y reportes anteriores los conservan.", 10));

        _mostrar.ItemsSource = new List<UiKit.Item<string>>
        {
            new("activos", "En uso"),
            new("inactivos", "Desactivados"),
            new("todos", "Todos")
        };
        _mostrar.SelectedIndex = 0;
        _mostrar.SelectionChanged += (_, _) => Filtrar();

        var filtros = new WrapPanel();
        filtros.Children.Add(UiKit.Campo("Mostrar", _mostrar));
        arriba.Children.Add(UiKit.Tarjeta(filtros, 8));
        arriba.Children.Add(_resumen);
        raiz.Children.Add(arriba);

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });

        _tabla.Columns.Add(UiKit.Columna("Concepto", nameof(ConceptoFila.Nombre), 2.2));
        _tabla.Columns.Add(UiKit.Columna("Código", nameof(ConceptoFila.Codigo), 1.1));
        _tabla.Columns.Add(UiKit.Columna("Deudas", nameof(ConceptoFila.Deudas), 0.7, derecha: true));
        _tabla.Columns.Add(UiKit.Columna("Pendientes", nameof(ConceptoFila.Pendientes), 0.8, derecha: true));
        _tabla.Columns.Add(UiKit.Columna("Tarifas", nameof(ConceptoFila.Tarifas), 0.7, derecha: true));
        _tabla.Columns.Add(UiKit.Columna("Estado", nameof(ConceptoFila.EstadoTexto), 0.9));

        var estilo = new Style(typeof(DataGridRow), UiKit.Estilo("Mx.Row"));
        var inactivo = new DataTrigger { Binding = new Binding(nameof(ConceptoFila.Activo)), Value = false };
        inactivo.Setters.Add(new Setter(ForegroundProperty, UiKit.Suave));
        estilo.Triggers.Add(inactivo);
        _tabla.RowStyle = estilo;
        _tabla.SelectionChanged += (_, _) => MostrarFicha();
        Grid.SetColumn(_tabla, 0);
        cuerpo.Children.Add(_tabla);

        var panel = new Border
        {
            Style = UiKit.Estilo("Mx.Card"),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _ficha }
        };
        Grid.SetColumn(panel, 2);
        cuerpo.Children.Add(panel);

        raiz.Children.Add(cuerpo);
        Content = raiz;

        MostrarVacio();
        Loaded += async (_, _) => await CargarAsync();
    }

    // =====================================================================

    private async Task CargarAsync(Guid? seleccionar = null)
    {
        seleccionar ??= (_tabla.SelectedItem as ConceptoFila)?.Id;
        try
        {
            _resumen.Text = "Cargando…";
            _todos = await ApiJson.GetAsync<List<ConceptoFila>>("/api/conceptos-cobro") ?? new();
            _resumen.Text = $"{_todos.Count(c => c.Activo)} en uso   ·   {_todos.Count(c => !c.Activo)} desactivados";
            Filtrar(seleccionar);
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudieron cargar los conceptos: " + ex.Message;
        }
    }

    private void Filtrar(Guid? seleccionar = null)
    {
        seleccionar ??= (_tabla.SelectedItem as ConceptoFila)?.Id;
        var filtro = (_mostrar.SelectedItem as UiKit.Item<string>)?.Valor ?? "activos";
        var filas = _todos.Where(c => filtro switch
        {
            "inactivos" => !c.Activo,
            "todos" => true,
            _ => c.Activo
        }).ToList();

        _tabla.ItemsSource = filas;
        if (seleccionar is { } id && filas.FirstOrDefault(f => f.Id == id) is { } fila)
            _tabla.SelectedItem = fila;
        else if (_tabla.SelectedItem is null)
            MostrarVacio();
    }

    private void MostrarVacio()
    {
        _ficha.Children.Clear();
        _ficha.Children.Add(new TextBlock
        {
            Text = "Elige un concepto para corregir su nombre o desactivarlo.",
            Foreground = UiKit.Suave,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
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

        var cabeza = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = ColoresPanel.Hex(c.Activo ? "#E7F5E7" : "#EEF2F7"),
            Child = new TextBlock { Text = c.EstadoTexto, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex(c.Activo ? "#0B5A0B" : "#5A6B7D") }
        };
        DockPanel.SetDock(chip, Dock.Right);
        cabeza.Children.Add(chip);
        cabeza.Children.Add(new TextBlock { Text = c.Nombre, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo, TextWrapping = TextWrapping.Wrap });
        _ficha.Children.Add(cabeza);
        _ficha.Children.Add(UiKit.Ayuda($"Código {c.Codigo}", 10));

        var uso = UiKit.Aviso(out var usoTexto, "info");
        usoTexto.Text = c.Deudas == 0 && c.Tarifas == 0
            ? "Todavía no se usó en ninguna deuda ni tarifa."
            : $"Se usó en {c.Deudas} {(c.Deudas == 1 ? "deuda" : "deudas")} ({c.Pendientes} pendientes) y tiene {c.Tarifas} {(c.Tarifas == 1 ? "tarifa" : "tarifas")}.";
        _ficha.Children.Add(uso);

        // ---------- Corregir ----------

        _ficha.Children.Add(new TextBlock { Text = "Corregir", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Titulo, Margin = new Thickness(0, 10, 0, 6) });

        var nombre = UiKit.Texto(340, 150);
        nombre.Text = c.Nombre;
        var descripcion = UiKit.Texto(340, 300);
        descripcion.Text = c.Descripcion ?? "";
        _ficha.Children.Add(UiKit.Campo("Nombre (como sale en el recibo)", nombre, 0));
        _ficha.Children.Add(UiKit.Campo("Descripción (opcional)", descripcion, 0));

        var guardar = UiKit.Boton("Guardar cambios", principal: true);
        guardar.HorizontalAlignment = HorizontalAlignment.Left;
        guardar.Click += async (_, _) => await GuardarAsync(c, nombre.Text, descripcion.Text);
        _ficha.Children.Add(guardar);
        _ficha.Children.Add(UiKit.Ayuda("Los recibos ya emitidos conservan el nombre con el que se cobraron.", 0));

        // ---------- Estado ----------

        _ficha.Children.Add(new TextBlock { Text = c.Activo ? "Dejar de usarlo" : "Volver a usarlo", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Titulo, Margin = new Thickness(0, 16, 0, 6) });
        _ficha.Children.Add(UiKit.Ayuda(c.Activo
            ? "Ya no aparecerá al cargar deudas ni al asignar tarifas. Sus deudas pendientes se pueden seguir cobrando y todo su historial se conserva."
            : "Vuelve a aparecer al cargar deudas y tarifas.", 8));

        var estado = UiKit.Boton(c.Activo ? "Desactivar concepto" : "Activar concepto", peligro: c.Activo);
        estado.HorizontalAlignment = HorizontalAlignment.Left;
        estado.Click += async (_, _) => await CambiarEstadoAsync(c);
        _ficha.Children.Add(estado);
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
    }
}
