using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Cobrar (manual, 1. COBRANZA): se escribe el número de caseta y Enter;
/// el sistema identifica la caseta y muestra todo lo pendiente; se marcan
/// los conceptos que paga el socio; el importe se calcula solo; se registra
/// el cobro y se emite el recibo. Con QR, el número de transacción es obligatorio.
/// </summary>
public sealed class IngresosView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly IngresosService _servicio = new();

    private List<UnidadIngresoModel> _unidades = new();
    private List<MonedaIngresoModel> _monedas = new();
    private Dictionary<Guid, string> _conceptos = new();
    private UnidadIngresoModel? _unidad;
    private FichaCaseta? _ficha;
    private (Guid? Id, string Codigo)? _precarga;
    private bool _listo;
    private bool _cobrando;

    // Paso 1: caseta
    private readonly TextBox _caseta = new()
    {
        Width = 170,
        Height = 50,
        FontSize = 24,
        FontWeight = FontWeights.SemiBold,
        MaxLength = 20,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _identidad = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 22, VerticalAlignment = VerticalAlignment.Center };

    // Paso 2: deudas
    private readonly DataGrid _tabla = UiKit.Tabla();
    private DataGridTextColumn? _columnaAPagar;
    private readonly TextBlock _sinDeudas = new() { Foreground = UiKit.Suave, Margin = new Thickness(2, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly Button _marcarTodas = new() { Content = "Marcar todas" };
    private List<FilaCobro> _filas = new();

    // Aviso: la caseta tiene un pago QR de la app en revisión (evita cobrar dos veces).
    private readonly Border _avisoQr;
    private readonly TextBlock _avisoQrTexto;

    // Resumen
    private readonly TextBlock _total = new() { FontSize = 34, FontWeight = FontWeights.Bold, Foreground = UiKit.Tinta };
    private readonly TextBlock _detalleTotal = new() { Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly ComboBox _forma = UiKit.Combo(300);
    private readonly TextBox _referencia = UiKit.Texto(300, 80);
    private readonly StackPanel _panelReferencia;
    private readonly TextBlock _etiquetaReferencia = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Etiqueta, Margin = new Thickness(0, 0, 0, 5) };
    private readonly TextBox _tipoCambio = UiKit.Texto(120, 10);
    private readonly StackPanel _panelTipoCambio;
    private readonly TextBox _pagador = UiKit.Texto(300, 150);
    private readonly TextBox _observaciones = UiKit.Texto(300, 500);
    private readonly Button _cobrar;
    private readonly Border _avisoResumen;
    private readonly TextBlock _avisoResumenTexto;

    public IngresosView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);
        arriba.Children.Add(UiKit.Encabezado("Cobrar"));
        arriba.Children.Add(UiKit.Ayuda(
            "Escribe el número de caseta y presiona Enter. Marca lo que paga el socio y pulsa «Cobrar y emitir recibo».", 12));
        raiz.Children.Add(arriba);

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        // ---------- Izquierda: caseta y deudas ----------

        var izquierda = new DockPanel();

        var paso1 = new DockPanel();
        var buscarPanel = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        DockPanel.SetDock(buscarPanel, Dock.Left);
        buscarPanel.Children.Add(new TextBlock { Text = "Número de caseta", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Etiqueta, Margin = new Thickness(0, 0, 0, 5) });
        _caseta.Style = UiKit.Estilo("Mx.TextBox");
        _caseta.FontSize = 24;
        _caseta.Height = 50;
        buscarPanel.Children.Add(_caseta);
        paso1.Children.Add(buscarPanel);
        paso1.Children.Add(_identidad);

        var tarjeta1 = UiKit.Tarjeta(paso1);
        DockPanel.SetDock(tarjeta1, Dock.Top);
        izquierda.Children.Add(tarjeta1);

        var cabeceraDeudas = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        _marcarTodas.Style = UiKit.Estilo("Mx.NavLink");
        _marcarTodas.Foreground = ColoresPanel.Hex("#1F4E79");
        _marcarTodas.FontSize = 13;
        _marcarTodas.Visibility = Visibility.Collapsed;
        _marcarTodas.Click += (_, _) => MarcarTodas();
        DockPanel.SetDock(_marcarTodas, Dock.Right);
        cabeceraDeudas.Children.Add(_marcarTodas);
        cabeceraDeudas.Children.Add(UiKit.Subtitulo("Deudas pendientes"));

        var paso2 = new DockPanel();
        DockPanel.SetDock(cabeceraDeudas, Dock.Top);
        paso2.Children.Add(cabeceraDeudas);

        _avisoQr = UiKit.Aviso(out _avisoQrTexto, "aviso");
        var irQr = new Button { Content = "Ir a Pagos QR", Style = UiKit.Estilo("Mx.NavLink"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        irQr.Click += (_, _) => Navegacion.AbrirPagosQr();
        var contenidoQr = new StackPanel();
        contenidoQr.Children.Add(new TextBlock { Text = "Esta caseta tiene un pago QR de la app en revisión", FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex("#7A5200") });
        _avisoQr.Child = null;
        contenidoQr.Children.Add(_avisoQrTexto);
        contenidoQr.Children.Add(irQr);
        _avisoQr.Child = contenidoQr;
        _avisoQr.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(_avisoQr, Dock.Top);
        paso2.Children.Add(_avisoQr);
        DockPanel.SetDock(_sinDeudas, Dock.Bottom);
        paso2.Children.Add(_sinDeudas);

        ConfigurarTabla();
        paso2.Children.Add(_tabla);

        izquierda.Children.Add(new Border
        {
            Style = UiKit.Estilo("Mx.Card"),
            Child = paso2
        });

        Grid.SetColumn(izquierda, 0);
        cuerpo.Children.Add(izquierda);

        // ---------- Derecha: resumen y datos del cobro ----------

        var resumen = new StackPanel();
        resumen.Children.Add(new TextBlock { Text = "Total a cobrar", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Etiqueta });
        resumen.Children.Add(_total);
        resumen.Children.Add(_detalleTotal);

        _avisoResumen = UiKit.Aviso(out _avisoResumenTexto, "aviso");
        _avisoResumen.Visibility = Visibility.Collapsed;
        resumen.Children.Add(_avisoResumen);

        resumen.Children.Add(UiKit.Campo("Forma de pago", _forma, 0));

        _panelReferencia = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        _panelReferencia.Children.Add(_etiquetaReferencia);
        _panelReferencia.Children.Add(_referencia);
        resumen.Children.Add(_panelReferencia);

        _panelTipoCambio = UiKit.Campo("Tipo de cambio (Bs por 1 USD)", _tipoCambio, 0);
        _tipoCambio.Text = "6,96";
        resumen.Children.Add(_panelTipoCambio);

        resumen.Children.Add(UiKit.Campo("Persona que paga", _pagador, 0));

        // Observaciones del recibo: opcionales, se imprimen en el recibo.
        _observaciones.Height = 56;
        _observaciones.TextWrapping = TextWrapping.Wrap;
        _observaciones.AcceptsReturn = true;
        _observaciones.VerticalContentAlignment = VerticalAlignment.Top;
        _observaciones.ToolTip = "Opcional. Se imprime en el recibo.";
        resumen.Children.Add(UiKit.Campo("Observaciones (opcional)", _observaciones, 0));

        _cobrar = new Button
        {
            Content = "Cobrar y emitir recibo",
            Height = 48,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 6),
            Background = ColoresPanel.Hex("#287A42"),
            BorderBrush = ColoresPanel.Hex("#287A42"),
            Foreground = Brushes.White,
            Style = UiKit.Estilo("Mx.PrimaryButton")
        };
        _cobrar.Click += async (_, _) => await CobrarAsync();
        resumen.Children.Add(_cobrar);
        resumen.Children.Add(new TextBlock
        {
            Text = "El número de recibo lo asigna el sistema. Deudas en bolivianos y en dólares se cobran por separado.",
            FontSize = 12,
            Foreground = UiKit.Suave,
            TextWrapping = TextWrapping.Wrap
        });

        var derecha = new Border
        {
            Style = UiKit.Estilo("Mx.Card"),
            VerticalAlignment = VerticalAlignment.Top,
            Child = resumen
        };
        Grid.SetColumn(derecha, 2);
        cuerpo.Children.Add(derecha);

        raiz.Children.Add(cuerpo);
        Content = raiz;

        // ---------- Eventos ----------

        _caseta.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await BuscarAsync(_caseta.Text);
            }
        };

        _forma.SelectionChanged += (_, _) => ActualizarResumen();
        _pagador.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && _cobrar.IsEnabled)
                await CobrarAsync();
        };

        if (SesionActual.Iniciada && !SesionActual.PuedeCobrar)
        {
            _cobrar.IsEnabled = false;
            _cobrar.Content = "Tu usuario es de solo consulta";
        }

        MostrarIdentidad(null);
        ActualizarResumen();

        Loaded += async (_, _) =>
        {
            if (!_listo)
                await CargarCatalogosAsync();

            Keyboard.Focus(_caseta);
        };
    }

    /// <summary>Abre la pantalla con una caseta ya buscada (Cobro rápido, Mapa, Casetas).</summary>
    public void PrecargarUnidad(Guid? unidadId, string codigo)
    {
        _precarga = (unidadId, codigo);
        _caseta.Text = codigo;

        if (_listo)
            _ = BuscarAsync(codigo, unidadId);
    }

    // =====================================================================
    // DATOS
    // =====================================================================

    private async Task CargarCatalogosAsync()
    {
        try
        {
            _unidades = await _servicio.ObtenerUnidadesAsync();
            _monedas = await _servicio.ObtenerMonedasAsync();

            var catalogos = await _servicio.ObtenerCatalogosAsync();
            _conceptos = (catalogos?.Conceptos ?? new()).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().Nombre);

            var formas = (catalogos?.FormasPago ?? new()).ToList();
            if (formas.Count == 0)
            {
                formas = new List<FormaPagoIngresoModel>
                {
                    new() { Id = 1, Nombre = "Efectivo" },
                    new() { Id = 3, Nombre = "QR" },
                    new() { Id = 2, Nombre = "Transferencia" }
                };
            }

            _forma.ItemsSource = formas.Select(f => new UiKit.Item<int>(f.Id, f.Nombre)).ToList();
            _forma.SelectedIndex = 0;

            _listo = true;

            if (_precarga is { } p)
                await BuscarAsync(p.Codigo, p.Id);
        }
        catch (Exception ex)
        {
            _identidad.Text = "No se pudieron cargar los datos: " + ex.Message;
            _identidad.Foreground = ColoresPanel.Hex("#A12A2A");
        }
    }

    private async Task BuscarAsync(string codigo, Guid? unidadId = null)
    {
        _precarga = null;
        codigo = (codigo ?? "").Trim();

        if (codigo.Length == 0 && unidadId is null)
        {
            MostrarIdentidad(null);
            return;
        }

        var unidad = unidadId is { } id
            ? _unidades.FirstOrDefault(u => u.Id == id)
            : null;

        unidad ??= _unidades.FirstOrDefault(u => string.Equals(u.Codigo.Trim(), codigo, StringComparison.OrdinalIgnoreCase));

        if (unidad is null)
        {
            // Puede ser una unidad registrada hace un momento.
            try
            {
                _unidades = await _servicio.ObtenerUnidadesAsync();
                unidad = _unidades.FirstOrDefault(u => string.Equals(u.Codigo.Trim(), codigo, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // Sin conexión: se informa abajo.
            }
        }

        _unidad = unidad;
        _filas = new();
        _tabla.ItemsSource = null;
        _avisoQr.Visibility = Visibility.Collapsed;

        if (unidad is null)
        {
            _ficha = null;
            _identidad.Text = $"No existe la caseta «{codigo}». Revisa el número.";
            _identidad.Foreground = ColoresPanel.Hex("#A12A2A");
            _sinDeudas.Text = "";
            ActualizarResumen();
            _caseta.SelectAll();
            return;
        }

        _caseta.Text = unidad.Codigo;
        _identidad.Foreground = UiKit.Tinta;
        _identidad.Text = $"Buscando la caseta {unidad.Codigo}…";

        try
        {
            _ficha = await ApiJson.GetAsync<FichaCaseta>($"/api/casetas/{unidad.Id}");
        }
        catch
        {
            _ficha = null;
        }

        MostrarIdentidad(unidad);
        await CargarDeudasAsync(unidad);
    }

    private async Task CargarDeudasAsync(UnidadIngresoModel unidad)
    {
        try
        {
            var pendientes = await _servicio.ObtenerPendientesAsync(unidad.Id);
            if (_unidad?.Id != unidad.Id)
                return;

            var monedas = _monedas.ToDictionary(m => m.Id, m => m.Codigo);

            _filas = pendientes
                .OrderBy(o => o.PeriodoAnio ?? 0).ThenBy(o => o.PeriodoMes ?? 0).ThenBy(o => o.FechaEmision)
                .Select(o => new FilaCobro(o,
                    _conceptos.GetValueOrDefault(o.ConceptoCobroId, "Concepto"),
                    monedas.GetValueOrDefault(o.MonedaId, "?")))
                .ToList();

            foreach (var f in _filas)
                f.PropertyChanged += (_, _) => ActualizarResumen();

            await MarcarPagosQrEnRevisionAsync(unidad);

            // Si todo es de una sola moneda se marca todo: el socio suele pagar lo que debe.
            // Las deudas con un pago QR en revisión no se marcan solas.
            if (_filas.Select(f => f.MonedaId).Distinct().Count() == 1)
                foreach (var f in _filas.Where(f => !f.EnRevisionQr))
                    f.Seleccionada = true;

            _tabla.ItemsSource = _filas;
            _sinDeudas.Text = _filas.Count == 0 ? "Esta caseta no tiene deudas pendientes. Está al día." : "";
            _marcarTodas.Visibility = _filas.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            _pagador.Text = _ficha?.Titular?.Nombre ?? unidad.Titular ?? "";
            ActualizarResumen();
        }
        catch (Exception ex)
        {
            _sinDeudas.Text = "No se pudieron consultar las deudas: " + ex.Message;
        }
    }

    // =====================================================================
    // PANTALLA
    // =====================================================================

    private void MostrarIdentidad(UnidadIngresoModel? unidad)
    {
        _identidad.Inlines.Clear();

        if (unidad is null)
        {
            _identidad.Foreground = UiKit.Suave;
            _identidad.Text = "Escribe el número y presiona Enter.";
            return;
        }

        var tipo = unidad.Tipo switch { 2 => "Comedor", 3 => "Ambulante", _ => "Caseta" };
        var titular = _ficha?.Titular?.Nombre ?? unidad.Titular;

        _identidad.Foreground = UiKit.Tinta;
        _identidad.Inlines.Add(new System.Windows.Documents.Run($"{tipo} {unidad.Codigo}") { FontSize = 20, FontWeight = FontWeights.Bold });
        _identidad.Inlines.Add(new System.Windows.Documents.Run($"   Bloque {(string.IsNullOrWhiteSpace(unidad.Bloque) ? "—" : unidad.Bloque)}") { Foreground = UiKit.Suave });
        _identidad.Inlines.Add(new System.Windows.Documents.LineBreak());

        if (string.IsNullOrWhiteSpace(titular))
        {
            _identidad.Inlines.Add(new System.Windows.Documents.Run("Sin titular registrado") { Foreground = ColoresPanel.Hex("#7A5200") });
        }
        else
        {
            _identidad.Inlines.Add(new System.Windows.Documents.Run("Titular: ") { Foreground = UiKit.Suave });
            _identidad.Inlines.Add(new System.Windows.Documents.Run(titular) { FontWeight = FontWeights.SemiBold });

            var ci = _ficha?.Titular?.Documento;
            if (!string.IsNullOrWhiteSpace(ci) && ci != "SIN_DOCUMENTO")
                _identidad.Inlines.Add(new System.Windows.Documents.Run($"  ·  CI {ci}") { Foreground = UiKit.Suave });
        }

        if (!unidad.Activa)
        {
            _identidad.Inlines.Add(new System.Windows.Documents.LineBreak());
            _identidad.Inlines.Add(new System.Windows.Documents.Run("Unidad inactiva") { Foreground = ColoresPanel.Hex("#A12A2A") });
        }
    }

    private void ConfigurarTabla()
    {
        _tabla.IsReadOnly = false;
        _tabla.SelectionMode = DataGridSelectionMode.Single;
        _tabla.MinHeight = 160;

        var marca = new DataGridTemplateColumn { Header = "Cobrar", Width = new DataGridLength(70) };
        var plantilla = new FrameworkElementFactory(typeof(CheckBox));
        plantilla.SetBinding(ToggleButton_IsCheckedProperty(), new Binding(nameof(FilaCobro.Seleccionada)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        plantilla.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        plantilla.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        marca.CellTemplate = new DataTemplate { VisualTree = plantilla };
        _tabla.Columns.Add(marca);

        foreach (var c in new[]
                 {
                     UiKit.Columna("Concepto", nameof(FilaCobro.Concepto), 2.2),
                     UiKit.Columna("Período", nameof(FilaCobro.Periodo), 1),
                     UiKit.Columna("Saldo", nameof(FilaCobro.Saldo), 1, "N2", derecha: true)
                 })
        {
            c.IsReadOnly = true;
            _tabla.Columns.Add(c);
        }

        // Pago parcial: se puede cobrar menos que el saldo (lo demás queda pendiente).
        var estiloAPagar = new Style(typeof(TextBox));
        estiloAPagar.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Right));
        estiloAPagar.Setters.Add(new Setter(PaddingProperty, new Thickness(2, 0, 2, 0)));
        var estiloAPagarVer = new Style(typeof(TextBlock));
        estiloAPagarVer.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        estiloAPagarVer.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        estiloAPagarVer.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
        _columnaAPagar = new DataGridTextColumn
        {
            Header = "A pagar",
            Binding = new Binding(nameof(FilaCobro.APagarTexto)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            EditingElementStyle = estiloAPagar,
            ElementStyle = estiloAPagarVer
        };
        _tabla.Columns.Add(_columnaAPagar);

        var moneda = UiKit.Columna("Moneda", nameof(FilaCobro.Moneda), 0.7);
        moneda.IsReadOnly = true;
        _tabla.Columns.Add(moneda);

        var nota = UiKit.Columna("Nota", nameof(FilaCobro.Nota), 1.1);
        nota.IsReadOnly = true;
        var estiloNota = new Style(typeof(TextBlock));
        estiloNota.Setters.Add(new Setter(TextBlock.ForegroundProperty, ColoresPanel.Hex("#7A5200")));
        estiloNota.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        estiloNota.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(FilaCobro.NotaDetalle))));
        nota.ElementStyle = estiloNota;
        _tabla.Columns.Add(nota);

        // Clic en cualquier parte de la fila marca o desmarca.
        _tabla.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d &&
                ItemsControl.ContainerFromElement(_tabla, d) is DataGridRow fila &&
                fila.Item is FilaCobro f &&
                FindParent<CheckBox>(d) is null &&
                FindParent<DataGridCell>(d)?.Column != _columnaAPagar)
            {
                f.Seleccionada = !f.Seleccionada;
            }
        };
        _tabla.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && _tabla.SelectedItem is FilaCobro f)
            {
                f.Seleccionada = !f.Seleccionada;
                e.Handled = true;
            }
        };
    }

    private static DependencyProperty ToggleButton_IsCheckedProperty() =>
        System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private void MarcarTodas()
    {
        var marcar = _filas.Any(f => !f.Seleccionada);
        foreach (var f in _filas)
            f.Seleccionada = marcar;
    }

    private List<FilaCobro> Seleccionadas => _filas.Where(f => f.Seleccionada).ToList();

    private void ActualizarResumen()
    {
        var sel = Seleccionadas;
        var monedas = sel.Select(f => f.Moneda).Distinct().ToList();

        _marcarTodas.Content = _filas.Count > 0 && _filas.All(f => f.Seleccionada) ? "Quitar todas" : "Marcar todas";

        if (sel.Count == 0)
        {
            _total.Text = "0,00";
            _detalleTotal.Text = _unidad is null ? "Busca una caseta para empezar." : "Marca las deudas que paga.";
        }
        else if (monedas.Count > 1)
        {
            _total.Text = "—";
            _detalleTotal.Text = "";
        }
        else
        {
            _total.Text = $"{monedas[0]} {sel.Sum(f => f.APagar).ToString("N2", Bo)}";
            _detalleTotal.Text = sel.Count == 1 ? "1 concepto" : $"{sel.Count} conceptos";
        }

        _avisoResumenTexto.Text = monedas.Count > 1
            ? "Marcaste deudas en bolivianos y en dólares. Cóbralas por separado: primero una moneda y después la otra."
            : "";
        _avisoResumen.Visibility = monedas.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        // Número de transacción: obligatorio con QR, opcional con transferencia u otro.
        var forma = (_forma.SelectedItem as UiKit.Item<int>)?.Valor ?? 1;
        _panelReferencia.Visibility = forma == 1 ? Visibility.Collapsed : Visibility.Visible;
        _etiquetaReferencia.Text = forma == 3 ? "N.º de transacción QR *" : "N.º de transacción o referencia";

        // Tipo de cambio: solo si se cobra en una moneda distinta del boliviano.
        _panelTipoCambio.Visibility = monedas.Count == 1 && monedas[0] != "BOB" ? Visibility.Visible : Visibility.Collapsed;

        _cobrar.IsEnabled = !_cobrando &&
                            (!SesionActual.Iniciada || SesionActual.PuedeCobrar) &&
                            sel.Count > 0 && monedas.Count == 1;

        if (!_cobrando && (!SesionActual.Iniciada || SesionActual.PuedeCobrar))
            _cobrar.Content = sel.Count > 0 && monedas.Count == 1
                ? $"Cobrar {_total.Text} y emitir recibo"
                : "Cobrar y emitir recibo";
    }

    // =====================================================================
    // COBRO
    // =====================================================================

    private async Task MarcarPagosQrEnRevisionAsync(UnidadIngresoModel unidad)
    {
        _avisoQr.Visibility = Visibility.Collapsed;
        if (!(Sesion.SesionActual.PuedeCobrar || !Sesion.SesionActual.Iniciada))
            return;

        List<PagoQrEnRevision>? pagos;
        try
        {
            pagos = await ApiJson.GetAsync<List<PagoQrEnRevision>>($"/api/panel/pagos/en-revision?unidadId={unidad.Id}");
        }
        catch
        {
            return; // Si no se puede consultar, se cobra como siempre.
        }

        if (pagos is null || pagos.Count == 0 || _unidad?.Id != unidad.Id)
            return;

        foreach (var p in pagos)
        {
            var detalle = $"Enviado desde la app el {p.Fecha.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy HH:mm", Bo)} · " +
                          $"{p.Moneda} {p.Monto.ToString("N2", Bo)} · transacción {p.Referencia}";
            foreach (var f in _filas.Where(f => p.ObligacionIds.Contains(f.Id)))
                f.MarcarQrEnRevision(detalle);
        }

        var primero = pagos[0];
        _avisoQrTexto.Text = (pagos.Count == 1
                ? $"El socio envió un pago de {primero.Moneda} {primero.Monto.ToString("N2", Bo)} el {primero.Fecha.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy HH:mm", Bo)} (transacción {primero.Referencia})."
                : $"El socio envió {pagos.Count} pagos que esperan revisión.") +
            " Las deudas que cubre están marcadas como «Pago QR en revisión». No las cobres en ventanilla: revisa primero el pago.";
        _avisoQr.Visibility = Visibility.Visible;
    }

    private sealed class PagoQrEnRevision
    {
        public Guid Id { get; set; }
        public string Moneda { get; set; } = "";
        public decimal Monto { get; set; }
        public string? Referencia { get; set; }
        public DateTimeOffset Fecha { get; set; }
        public List<Guid> ObligacionIds { get; set; } = new();
    }

    private async Task CobrarAsync()
    {
        var sel = Seleccionadas;
        if (_unidad is null || sel.Count == 0 || sel.Select(f => f.MonedaId).Distinct().Count() != 1 || _cobrando)
            return;

        var owner = Window.GetWindow(this);
        var forma = _forma.SelectedItem as UiKit.Item<int>;
        var referencia = _referencia.Text.Trim();
        var pagador = _pagador.Text.Trim();
        var monedaId = sel[0].MonedaId;
        var moneda = sel[0].Moneda;
        var total = sel.Sum(f => f.APagar);

        if (sel.Any(f => f.APagar <= 0 || f.APagar > f.Saldo))
        {
            Dialogo.Mostrar(owner, "Revisa la columna «A pagar»: cada monto debe ser mayor a cero y no puede superar el saldo de la deuda.",
                "Monto a pagar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (forma is null)
        {
            Dialogo.Mostrar(owner, "Elige la forma de pago.", "Falta un dato", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (forma.Valor == 3 && referencia.Length == 0)
        {
            Dialogo.Mostrar(owner, "Para un pago con QR escribe el número de transacción que figura en el comprobante del banco.",
                "Falta el número de transacción", MessageBoxButton.OK, MessageBoxImage.Warning);
            _referencia.Focus();
            return;
        }

        if (pagador.Length == 0)
        {
            Dialogo.Mostrar(owner, "Escribe el nombre de la persona que paga.", "Falta un dato", MessageBoxButton.OK, MessageBoxImage.Warning);
            _pagador.Focus();
            return;
        }

        var tipoCambio = 1m;
        if (moneda != "BOB")
        {
            if (ApiJson.LeerMonto(_tipoCambio.Text) is not { } tc)
            {
                Dialogo.Mostrar(owner, "Escribe el tipo de cambio (por ejemplo 6,96).", "Falta un dato", MessageBoxButton.OK, MessageBoxImage.Warning);
                _tipoCambio.Focus();
                return;
            }

            tipoCambio = tc;
        }

        if (sel.Any(f => f.EnRevisionQr))
        {
            var seguir = Dialogo.Mostrar(owner,
                "Marcaste deudas que el socio ya pagó con QR desde la app y que están esperando revisión.\n\n" +
                "Si las cobras ahora en ventanilla, el pago QR tendrá que rechazarse y devolverle el dinero al socio.\n\n" +
                "Lo correcto es revisar primero ese pago en «Pagos QR».\n\n¿Cobrar igual en ventanilla?",
                "Pago QR en revisión", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (seguir != MessageBoxResult.Yes)
                return;
        }

        var lineas = string.Join("\n", sel.Select(f => f.EsParcial
            ? $"{f.Concepto} {f.Periodo}: {f.APagar.ToString("N2", Bo)} (pago parcial; queda {(f.Saldo - f.APagar).ToString("N2", Bo)})"
            : $"{f.Concepto} {f.Periodo}: {f.APagar.ToString("N2", Bo)}"));

        // Los pagos parciales quedan anotados en el recibo (se imprimen y se ven al reimprimir).
        var parciales = sel.Where(f => f.EsParcial || f.Pagado > 0).Select(f =>
            $"{f.Concepto} {f.Periodo}: {(f.EsParcial ? "pago parcial" : "pago del saldo")}, " +
            $"total {f.Monto.ToString("N2", Bo)}, " +
            $"pagado {(f.Pagado + f.APagar).ToString("N2", Bo)}, " +
            $"saldo {(f.Saldo - f.APagar).ToString("N2", Bo)}.").ToList();
        var observaciones = string.Join(" ", new[] { _observaciones.Text.Trim() }.Concat(parciales).Where(t => t.Length > 0));
        if (observaciones.Length > 500)
        {
            Dialogo.Mostrar(owner, "Las observaciones del recibo, junto con el detalle de los pagos parciales, superan los 500 caracteres. Acorta las observaciones.",
                "Observaciones demasiado largas", MessageBoxButton.OK, MessageBoxImage.Warning);
            _observaciones.Focus();
            return;
        }

        var confirmar = Dialogo.Mostrar(owner,
            $"Caseta: {_unidad.Codigo}\nPaga: {pagador}\nForma de pago: {forma.Texto}" +
            (referencia.Length > 0 && forma.Valor != 1 ? $"\nN.º de transacción: {referencia}" : "") +
            (moneda != "BOB" ? $"\nTipo de cambio: {tipoCambio.ToString("N2", Bo)}" : "") +
            $"\n\n{lineas}\n\nTotal: {moneda} {total.ToString("N2", Bo)}\n\n¿Registrar el cobro y emitir el recibo?",
            "Confirmar cobro", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);

        if (confirmar != MessageBoxResult.Yes)
            return;

        _cobrando = true;
        _cobrar.IsEnabled = false;
        _cobrar.Content = "Registrando…";

        var unidad = _unidad;
        ResultadoCobroIngresoModel? resultado = null;

        try
        {
            resultado = await _servicio.CobrarAsync(new ProcesarCobroIngresoModel
            {
                NumeroRecibo = 0,
                UnidadComercialId = unidad.Id,
                MonedaId = monedaId,
                Monto = total,
                FormaPago = forma.Valor,
                NombrePagador = pagador,
                TipoCambio = tipoCambio,
                Referencia = forma.Valor == 1 || referencia.Length == 0 ? null : referencia,
                Items = sel.Select(f => new ItemCobroIngresoModel
                {
                    ObligacionId = f.Id,
                    NombreConcepto = f.Concepto,
                    Monto = f.EsParcial ? f.APagar : null
                }).ToList(),
                Observaciones = observaciones.Length == 0 ? null : observaciones
            });

            if (resultado?.Recibo is null)
                throw new InvalidOperationException("El servidor no devolvió el recibo.");
        }
        catch (Exception ex)
        {
            _cobrando = false;
            ActualizarResumen();
            Dialogo.Mostrar(owner, ex.Message + "\n\nNo se registró ningún cobro.", "No se pudo cobrar", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // A partir de aquí el cobro está registrado: nunca se debe repetir.
        _referencia.Clear();
        _observaciones.Clear();

        try
        {
            var api = new Api.ApiClient();
            var recibo = await api.GetAsync<Dtos.ReciboDto>($"/api/Caja/recibos/{resultado.Recibo.Id}")
                         ?? throw new InvalidOperationException("No se pudo consultar el recibo registrado.");
            var pago = await api.GetAsync<Dtos.PagoDto>($"/api/Caja/pagos/{recibo.PagoId}")
                       ?? throw new InvalidOperationException("No se pudieron consultar los datos del pago.");

            new VistaPreviaReciboWindow(recibo, pago, unidad.Codigo, unidad.Bloque ?? "", moneda) { Owner = owner }.ShowDialog();
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(owner,
                $"Recibo N.º: {resultado.Recibo.Numero}\n\nEl cobro YA está registrado: no lo repitas.\n\n" +
                "No se pudo abrir la vista previa; puedes imprimirlo desde Recibos.\n\n" + ex.Message,
                "Cobro registrado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _cobrando = false;
        }

        // Lista para la siguiente caseta: se actualizan las deudas de esta.
        await CargarDeudasAsync(unidad);
        _caseta.SelectAll();
        Keyboard.Focus(_caseta);
    }

    // =====================================================================
    // MODELOS
    // =====================================================================

    public sealed class FilaCobro : INotifyPropertyChanged
    {
        private bool _seleccionada;

        public FilaCobro(ObligacionIngresoModel o, string concepto, string moneda)
        {
            Id = o.Id;
            MonedaId = o.MonedaId;
            Monto = o.Monto;
            Pagado = o.MontoPagado;
            Saldo = o.Saldo ?? o.Monto - o.MontoPagado;
            _aPagar = Saldo;
            Concepto = concepto;
            Moneda = moneda;
            Periodo = o.PeriodoAnio is { } a && o.PeriodoMes is { } m ? $"{m:00}/{a}" : o.FechaEmision.ToString("dd/MM/yyyy");
            Nota = Pagado > 0
                ? $"Pagó {Pagado.ToString("N2", CultureInfo.GetCultureInfo("es-BO"))} de {Monto.ToString("N2", CultureInfo.GetCultureInfo("es-BO"))}"
                : o.Modificaciones > 0 ? "✎ Modificada" : "";
            NotaDetalle = Pagado > 0 ? "Tiene pagos parciales." : o.UltimaModificacion;
        }

        private decimal _aPagar;

        public Guid Id { get; }
        public Guid MonedaId { get; }

        /// <summary>Monto total de la deuda.</summary>
        public decimal Monto { get; }

        /// <summary>Lo pagado antes (pagos parciales).</summary>
        public decimal Pagado { get; }

        /// <summary>Lo que falta pagar.</summary>
        public decimal Saldo { get; }

        /// <summary>Lo que se cobra ahora: el saldo, o menos (pago parcial).</summary>
        public decimal APagar
        {
            get => _aPagar;
            set
            {
                if (_aPagar == value)
                    return;
                _aPagar = value;
                Avisar();
                Avisar(nameof(APagarTexto));
                Avisar(nameof(EsParcial));
            }
        }

        public bool EsParcial => APagar < Saldo;

        /// <summary>Lo que se escribe en la columna «A pagar». Un valor inválido se descarta.</summary>
        public string APagarTexto
        {
            get => APagar.ToString("N2", CultureInfo.GetCultureInfo("es-BO"));
            set
            {
                if (ApiJson.LeerMonto(value) is { } monto && monto > 0 && monto <= Saldo && decimal.Round(monto, 2) == monto)
                {
                    APagar = monto;
                    Seleccionada = true;
                }
                else
                {
                    Avisar();
                }
            }
        }
        public string Concepto { get; }
        public string Moneda { get; }
        public string Periodo { get; }
        public string Nota { get; private set; }
        public string? NotaDetalle { get; private set; }

        /// <summary>La deuda está incluida en un pago QR de la app que espera revisión.</summary>
        public bool EnRevisionQr { get; private set; }

        public void MarcarQrEnRevision(string detalle)
        {
            EnRevisionQr = true;
            Nota = "Pago QR en revisión";
            NotaDetalle = detalle;
            Avisar(nameof(Nota));
            Avisar(nameof(NotaDetalle));
        }

        public bool Seleccionada
        {
            get => _seleccionada;
            set
            {
                if (_seleccionada == value)
                    return;
                _seleccionada = value;
                Avisar();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Avisar([CallerMemberName] string? propiedad = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
    }

    private sealed class FichaCaseta
    {
        public TitularCaseta? Titular { get; set; }
    }

    private sealed class TitularCaseta
    {
        public string Nombre { get; set; } = "";
        public string? Documento { get; set; }
    }
}
