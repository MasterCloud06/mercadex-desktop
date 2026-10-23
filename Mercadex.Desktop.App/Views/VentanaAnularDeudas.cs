using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Anular una deuda o varias: primero la vista previa (cuántas, cuánto y
/// cuáles no se pueden anular) y el botón para confirmar. Cuando son varias,
/// todas vienen marcadas y, como en el filtro de Excel, se busca la caseta y se
/// desmarcan las que no se anulan o se usa «(Seleccionar todo)». El motivo
/// es opcional. Nada cambia hasta pulsar «Anular». La API registra quién
/// anuló, cuándo y, si se escribió, por qué.
/// </summary>
internal sealed class VentanaAnularDeudas : Window
{
    public const string Ruta = "/api/obligaciones-anulaciones";

    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly Dictionary<string, object?> _pedido;

    // Una clave por confirmación: si el envío se repite, la API no anula dos veces.
    private readonly Guid _clave = Guid.NewGuid();

    private readonly TextBlock _resumen = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _filtros = new() { Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly TextBlock _bloqueadas = new() { TextWrapping = TextWrapping.Wrap, Foreground = UiKit.Suave, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBox _motivo = UiKit.Texto(560, 500);
    private readonly TextBlock _error = new() { Foreground = ColoresPanel.Hex("#A12A2A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button _anular = UiKit.Boton("Anular", peligro: true);
    private readonly StackPanel _confirmacion = new();
    private readonly StackPanel _marcar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    private readonly CheckBox _todas = new() { Content = "(Seleccionar todo)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    private readonly TextBox _buscar = UiKit.Texto(220, 40);
    private ListCollectionView? _vista;
    private readonly DataGridTemplateColumn _columnaAnular;

    private VistaPrevia? _previa;

    public string Mensaje { get; private set; } = "";

    /// <param name="pedido">obligacionIds, o los filtros (conceptoCobroId, anio, mes, caseta, bloque).</param>
    public VentanaAnularDeudas(Dictionary<string, object?> pedido)
    {
        _pedido = pedido;

        Title = "Anular deudas";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var pila = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        pila.Children.Add(new TextBlock { Text = "Anular deudas", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta });
        pila.Children.Add(UiKit.Ayuda(
            "La deuda no se borra: queda «Anulada», deja de sumar en los saldos y no se vuelve a generar. " +
            "Nada cambia hasta que pulses «Anular».", 12));

        pila.Children.Add(_resumen);
        pila.Children.Add(_filtros);

        // Casilla «Anular»: con varias deudas, se desmarcan las que no se anulan.
        var casilla = new FrameworkElementFactory(typeof(CheckBox));
        casilla.SetBinding(ToggleButton.IsCheckedProperty,
            new Binding(nameof(FilaPrevia.Incluir)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        casilla.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        casilla.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        casilla.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => Actualizar()));
        _columnaAnular = new DataGridTemplateColumn
        {
            Header = "Anular",
            CellTemplate = new DataTemplate { VisualTree = casilla },
            Width = new DataGridLength(0.6, DataGridLengthUnitType.Star),
            Visibility = Visibility.Collapsed
        };
        _tabla.Columns.Add(_columnaAnular);
        _tabla.Columns.Add(UiKit.Columna("Caseta", nameof(FilaPrevia.Caseta), 0.8));
        _tabla.Columns.Add(UiKit.Columna("Concepto", nameof(FilaPrevia.Concepto), 1.8));
        _tabla.Columns.Add(UiKit.Columna("Período", nameof(FilaPrevia.Periodo), 0.9));
        _tabla.Columns.Add(UiKit.Columna("Monto", nameof(FilaPrevia.Monto), 0.9, "N2", derecha: true));
        _tabla.Columns.Add(UiKit.Columna("Moneda", nameof(FilaPrevia.Moneda), 0.7));
        _tabla.MaxHeight = 300;

        // Como el filtro de Excel: buscar y «(Seleccionar todo)» sobre lo que se ve.
        // Buscar solo oculta filas: las marcas de las ocultas se conservan.
        _buscar.ToolTip = "Buscar caseta, concepto o período";
        _buscar.TextChanged += (_, _) =>
        {
            _vista?.Refresh();
            Actualizar();
        };
        _todas.Click += (_, _) => MarcarVisibles(_todas.IsChecked == true);
        _marcar.Children.Add(new TextBlock { Text = "Buscar:", Foreground = UiKit.Etiqueta, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _marcar.Children.Add(_buscar);
        _marcar.Children.Add(_todas);
        pila.Children.Add(_marcar);
        pila.Children.Add(_tabla);
        pila.Children.Add(_bloqueadas);

        _motivo.Height = 54;
        _motivo.TextWrapping = TextWrapping.Wrap;
        _motivo.AcceptsReturn = true;
        _motivo.VerticalContentAlignment = VerticalAlignment.Top;

        _confirmacion.Margin = new Thickness(0, 14, 0, 0);
        _confirmacion.Children.Add(UiKit.Campo("Motivo (opcional)", _motivo));
        _confirmacion.Visibility = Visibility.Collapsed;
        pila.Children.Add(_confirmacion);

        pila.Children.Add(_error);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelar = UiKit.Boton("Cancelar");
        cancelar.IsCancel = true;
        cancelar.Margin = new Thickness(0, 0, 10, 0);
        _anular.IsEnabled = false;
        _anular.Click += async (_, _) => await AnularAsync();
        botones.Children.Add(cancelar);
        botones.Children.Add(_anular);
        pila.Children.Add(botones);

        Content = pila;
        Loaded += async (_, _) => await CargarAsync();
    }

    private async Task CargarAsync()
    {
        _error.Text = "";
        _resumen.Text = "Preparando la vista previa…";
        _previa = null;
        Actualizar();

        try
        {
            _previa = await ApiJson.PostAsync<VistaPrevia>($"{Ruta}/vista-previa", _pedido);
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudo preparar la vista previa.";
            _error.Text = ex.Message;
            return;
        }

        if (_previa is null)
            return;

        foreach (var d in _previa.Deudas)
            d.Incluir = true;

        var elegir = _previa.Deudas.Count > 1;
        _columnaAnular.Visibility = elegir ? Visibility.Visible : Visibility.Collapsed;
        _marcar.Visibility = elegir ? Visibility.Visible : Visibility.Collapsed;

        _filtros.Text = _previa.Filtros;
        _buscar.Text = "";
        _vista = new ListCollectionView(_previa.Deudas) { Filter = o => o is FilaPrevia f && Coincide(f, _buscar.Text) };
        _tabla.ItemsSource = _vista;
        _tabla.Visibility = _previa.Deudas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _bloqueadas.Text = _previa.Bloqueadas.Count == 0
            ? ""
            : $"No se anularán {_previa.Bloqueadas.Count}:\n" + string.Join("\n", _previa.Bloqueadas.Take(15)
                .Select(b => $"• {b.Caseta} · {b.Concepto} {b.Periodo} · {b.Moneda} {b.Monto.ToString("N2", Bo)}: {b.Motivo}")) +
              (_previa.Bloqueadas.Count > 15 ? $"\n… y {_previa.Bloqueadas.Count - 15} más." : "");

        _confirmacion.Visibility = _previa.Cantidad > 0 ? Visibility.Visible : Visibility.Collapsed;
        Actualizar();
    }

    internal static bool Coincide(FilaPrevia fila, string texto)
    {
        texto = texto.Trim();
        return texto.Length == 0
            || fila.Caseta.Contains(texto, StringComparison.OrdinalIgnoreCase)
            || fila.Concepto.Contains(texto, StringComparison.OrdinalIgnoreCase)
            || fila.Periodo.Contains(texto, StringComparison.OrdinalIgnoreCase);
    }

    private List<FilaPrevia> Visibles() =>
        _previa?.Deudas.Where(d => Coincide(d, _buscar.Text)).ToList() ?? new();

    private List<FilaPrevia> Marcadas() =>
        _previa?.Deudas.Where(d => d.Incluir).ToList() ?? new();

    /// <summary>Resumen, total y botón según las deudas marcadas.</summary>
    private void Actualizar()
    {
        var marcadas = Marcadas();

        if (_previa is { Cantidad: > 0 })
        {
            var total = string.Join(" + ", marcadas
                .GroupBy(d => d.Moneda)
                .Select(g => $"{g.Key} {g.Sum(d => d.Monto).ToString("N2", Bo)}"));
            var desmarcadas = _previa.Deudas.Count - marcadas.Count;

            _resumen.Text = marcadas.Count switch
            {
                0 => "Desmarcaste todas: no se anulará ninguna.",
                1 => $"Se anulará 1 deuda · {total}",
                _ => $"Se anularán {marcadas.Count} deudas · {total}"
            } + (desmarcadas > 0 ? $"  ({desmarcadas} desmarcada{(desmarcadas == 1 ? "" : "s")})" : "");
        }
        else if (_previa is not null)
        {
            _resumen.Text = "No hay deudas que se puedan anular.";
        }

        _anular.Content = marcadas.Count == 1 ? "Anular 1 deuda" : $"Anular {marcadas.Count} deudas";
        _anular.IsEnabled = marcadas.Count > 0;

        // «(Seleccionar todo)»: marcada, vacía o a medias según las filas visibles.
        var visibles = Visibles();
        _todas.IsEnabled = visibles.Count > 0;
        _todas.IsChecked = visibles.Count > 0 && visibles.All(d => d.Incluir) ? true
            : visibles.Any(d => d.Incluir) ? null
            : false;
    }

    private void MarcarVisibles(bool marcar)
    {
        foreach (var d in Visibles())
            d.Incluir = marcar;

        _tabla.Items.Refresh();
        Actualizar();
    }

    /// <summary>
    /// Lo que se confirma: el pedido original o, si hay desmarcadas, el mismo
    /// pedido sin ellas (las elegidas una por una, o los filtros con exclusiones).
    /// </summary>
    private Dictionary<string, object?> PedidoFinal(List<FilaPrevia> marcadas)
    {
        var pedido = new Dictionary<string, object?>(_pedido);
        var desmarcadas = _previa!.Deudas.Where(d => !d.Incluir).Select(d => d.Id).ToList();

        if (desmarcadas.Count == 0)
            return pedido;

        if (pedido.ContainsKey("obligacionIds"))
            pedido["obligacionIds"] = marcadas.Select(d => d.Id).ToList();
        else
            pedido["excluirObligacionIds"] = desmarcadas;

        return pedido;
    }

    private async Task AnularAsync()
    {
        if (_previa is null || !_anular.IsEnabled)
            return;

        _anular.IsEnabled = false;
        _error.Text = "";

        var marcadas = Marcadas();
        var pedido = PedidoFinal(marcadas);

        try
        {
            // Con desmarcadas, la API calcula la vista previa de lo que de verdad se
            // anula; si no coincide con lo marcado (algo cambió), no se anula nada.
            var previa = _previa;
            if (marcadas.Count != _previa.Deudas.Count)
            {
                previa = await ApiJson.PostAsync<VistaPrevia>($"{Ruta}/vista-previa", pedido)
                         ?? throw new InvalidOperationException("No se recibió la vista previa.");

                if (previa.Cantidad != marcadas.Count ||
                    !previa.Deudas.Select(d => d.Id).ToHashSet().SetEquals(marcadas.Select(d => d.Id)))
                    throw new InvalidOperationException("Las deudas cambiaron desde la vista previa. No se anuló nada.");
            }

            var cuerpo = new Dictionary<string, object?>(pedido)
            {
                ["motivo"] = _motivo.Text.Trim(),
                ["confirmo"] = true,
                ["claveOperacion"] = _clave,
                ["huella"] = previa.Huella,
                ["cantidadEsperada"] = previa.Cantidad
            };

            var r = await ApiJson.PostAsync<Respuesta>(Ruta, cuerpo);
            Mensaje = r?.Mensaje ?? "Deudas anuladas.";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            // Si cambió algo desde la vista previa, la API no anula nada: se vuelve a cargar.
            _error.Text = ex.Message + "\nSe actualizó la vista previa: revísala otra vez.";
            await CargarAsync();
            _error.Text = ex.Message + "\nSe actualizó la vista previa: revísala otra vez.";
        }
    }

    private sealed class VistaPrevia
    {
        public string Filtros { get; set; } = "";
        public int Cantidad { get; set; }
        public string Huella { get; set; } = "";
        public List<TotalPrevia> Totales { get; set; } = new();
        public List<FilaPrevia> Deudas { get; set; } = new();
        public List<FilaBloqueada> Bloqueadas { get; set; } = new();
    }

    private sealed class TotalPrevia
    {
        public string Moneda { get; set; } = "";
        public decimal Total { get; set; }
    }

    public sealed class FilaPrevia
    {
        public Guid Id { get; set; }
        public string Caseta { get; set; } = "";
        public string Concepto { get; set; } = "";
        public string Periodo { get; set; } = "";
        public decimal Monto { get; set; }
        public string Moneda { get; set; } = "";

        /// <summary>Marcada: se anula.</summary>
        public bool Incluir { get; set; } = true;
    }

    private sealed class FilaBloqueada
    {
        public string Caseta { get; set; } = "";
        public string Concepto { get; set; } = "";
        public string Periodo { get; set; } = "";
        public decimal Monto { get; set; }
        public string Moneda { get; set; } = "";
        public string Motivo { get; set; } = "";
    }

    private sealed class Respuesta
    {
        public string? Mensaje { get; set; }
    }
}
