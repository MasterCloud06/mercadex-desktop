using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Dtos;
using Mercadex.Desktop.App.Services;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Recibos (manual, 2. REIMPRESIÓN DE RECIBOS): buscar por número de recibo
/// o por fecha, ver el recibo y reimprimirlo. Reimprimir no genera otro cobro
/// ni modifica deudas. Un recibo anulado aparece siempre como ANULADO.
/// </summary>
public sealed class RecibosView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly TextBox _numero = UiKit.Texto(120, 12);
    private readonly DatePicker _desde = UiKit.Fecha(DateTime.Today.AddDays(-6));
    private readonly DatePicker _hasta = UiKit.Fecha(DateTime.Today);
    private readonly TextBox _caseta = UiKit.Texto(110, 20);
    private readonly ComboBox _estado = UiKit.Combo(130);
    private readonly TextBlock _resumen = new() { Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly StackPanel _detalle = new();

    private ReciboDto? _recibo;
    private PagoDto? _pago;
    private FilaRecibo? _fila;

    public RecibosView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);
        arriba.Children.Add(UiKit.Encabezado("Recibos"));
        arriba.Children.Add(UiKit.Ayuda(
            "Busca un recibo por su número o por fecha para verlo y reimprimirlo. Reimprimir no genera otro cobro ni cambia las deudas.", 10));

        // ---------- Filtros ----------

        _estado.ItemsSource = new List<UiKit.Item<string?>>
        {
            new(null, "Todos"), new("Emitido", "Válidos"), new("Anulado", "Anulados")
        };
        _estado.SelectedIndex = 0;

        var buscar = UiKit.Boton("Buscar", principal: true);
        buscar.Margin = new Thickness(0, 0, 8, 10);
        buscar.Click += async (_, _) => await BuscarAsync();

        var hoy = UiKit.Boton("Hoy");
        hoy.Margin = new Thickness(0, 0, 0, 10);
        hoy.Click += async (_, _) =>
        {
            _numero.Clear();
            _desde.SelectedDate = _hasta.SelectedDate = DateTime.Today;
            await BuscarAsync();
        };

        var filtros = new WrapPanel();
        filtros.Children.Add(UiKit.Campo("N.º de recibo", _numero));
        var separador = new TextBlock { Text = "o", Foreground = UiKit.Suave, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 18) };
        filtros.Children.Add(separador);
        filtros.Children.Add(UiKit.Campo("Desde", _desde));
        filtros.Children.Add(UiKit.Campo("Hasta", _hasta));
        filtros.Children.Add(UiKit.Campo("Caseta", _caseta));
        filtros.Children.Add(UiKit.Campo("Estado", _estado));
        filtros.Children.Add(buscar);
        filtros.Children.Add(hoy);
        arriba.Children.Add(UiKit.Tarjeta(filtros, 8));
        arriba.Children.Add(_resumen);
        raiz.Children.Add(arriba);

        foreach (var caja in new[] { _numero, _caseta })
        {
            caja.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter)
                    await BuscarAsync();
            };
        }

        // ---------- Lista y detalle ----------

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

        _tabla.Columns.Add(UiKit.Columna("N.º", nameof(FilaRecibo.Numero), 0.6));
        _tabla.Columns.Add(UiKit.Columna("Fecha", nameof(FilaRecibo.FechaTexto), 1.1));
        _tabla.Columns.Add(UiKit.Columna("Caseta", nameof(FilaRecibo.Caseta), 0.7));
        _tabla.Columns.Add(UiKit.Columna("Pagó", nameof(FilaRecibo.Pagador), 1.6));
        _tabla.Columns.Add(UiKit.Columna("Forma de pago", nameof(FilaRecibo.FormaPago), 0.9));
        _tabla.Columns.Add(UiKit.Columna("Total", nameof(FilaRecibo.TotalTexto), 1.1, derecha: true));
        _tabla.Columns.Add(UiKit.Columna("Estado", nameof(FilaRecibo.EstadoTexto), 0.8));
        _tabla.Columns.Add(UiKit.Columna("Cobró", nameof(FilaRecibo.CobradoPor), 1.2));

        // Los anulados se ven en rojo: nunca desaparecen de la lista.
        var estiloFila = new Style(typeof(DataGridRow), (Style)UiKit.Estilo("Mx.Row"));
        var anulado = new DataTrigger { Binding = new Binding(nameof(FilaRecibo.Anulado)), Value = true };
        anulado.Setters.Add(new Setter(ForegroundProperty, ColoresPanel.Hex("#A12A2A")));
        estiloFila.Triggers.Add(anulado);
        _tabla.RowStyle = estiloFila;

        _tabla.SelectionChanged += async (_, _) => await MostrarDetalleAsync();
        _tabla.MouseDoubleClick += (_, _) => VerEImprimir();
        Grid.SetColumn(_tabla, 0);
        cuerpo.Children.Add(_tabla);

        var panel = new Border
        {
            Style = UiKit.Estilo("Mx.Card"),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _detalle }
        };
        Grid.SetColumn(panel, 2);
        cuerpo.Children.Add(panel);

        raiz.Children.Add(cuerpo);
        Content = raiz;

        MostrarVacio();
        Loaded += async (_, _) => await BuscarAsync();
    }

    // =====================================================================
    // BÚSQUEDA
    // =====================================================================

    private async Task BuscarAsync()
    {
        var parametros = new List<string>();
        var numeroTexto = _numero.Text.Trim().TrimStart('0');

        if (_numero.Text.Trim().Length > 0)
        {
            if (!long.TryParse(_numero.Text.Trim(), out var numero) || numero <= 0)
            {
                _resumen.Text = "El número de recibo solo lleva números.";
                return;
            }

            parametros.Add($"numero={numero}");
        }
        else
        {
            if (_desde.SelectedDate is { } d)
                parametros.Add($"desde={d:yyyy-MM-dd}");
            if (_hasta.SelectedDate is { } h)
                parametros.Add($"hasta={h:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(_caseta.Text))
                parametros.Add("caseta=" + Uri.EscapeDataString(_caseta.Text.Trim()));
            if (_estado.SelectedItem is UiKit.Item<string?> { Valor: { } estado })
                parametros.Add("estado=" + estado);
        }

        try
        {
            _resumen.Text = "Buscando…";
            var r = await ApiJson.GetAsync<BusquedaRespuesta>("/api/recibos/buscar?" + string.Join("&", parametros));
            var filas = r?.Recibos ?? new();

            _tabla.ItemsSource = filas;

            if (filas.Count == 0)
            {
                _resumen.Text = numeroTexto.Length > 0
                    ? $"No existe el recibo N.º {_numero.Text.Trim()}."
                    : "No hay recibos con esos filtros.";
                MostrarVacio();
                return;
            }

            var validos = filas.Where(f => !f.Anulado).ToList();
            var totales = validos.GroupBy(f => f.Moneda)
                .Select(g => $"{g.Key} {g.Sum(f => f.Total).ToString("N2", Bo)}");

            _resumen.Text =
                $"{r!.Total} {(r.Total == 1 ? "recibo" : "recibos")}" +
                (validos.Count > 0 ? $"   ·   válidos: {validos.Count} por {string.Join(" + ", totales)}" : "") +
                (filas.Count(f => f.Anulado) is var anulados and > 0 ? $"   ·   anulados: {anulados}" : "") +
                (r.Mostrados < r.Total ? $"   ·   se muestran los {r.Mostrados} más recientes" : "");

            // Con un solo resultado (búsqueda por número) se abre su detalle.
            if (filas.Count == 1)
                _tabla.SelectedItem = filas[0];
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudo buscar: " + ex.Message;
        }
    }

    // =====================================================================
    // DETALLE
    // =====================================================================

    private void MostrarVacio()
    {
        _recibo = null;
        _pago = null;
        _fila = null;
        _detalle.Children.Clear();
        _detalle.Children.Add(new TextBlock
        {
            Text = "Elige un recibo de la lista para ver su detalle y reimprimirlo.",
            Foreground = UiKit.Suave,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
        });
    }

    private async Task MostrarDetalleAsync()
    {
        if (_tabla.SelectedItem is not FilaRecibo fila)
        {
            MostrarVacio();
            return;
        }

        try
        {
            var api = new Api.ApiClient();
            var recibo = await api.GetAsync<ReciboDto>($"/api/Caja/recibos/{fila.Id}");
            var pago = recibo is null ? null : await api.GetAsync<PagoDto>($"/api/Caja/pagos/{recibo.PagoId}");

            if (recibo is null || pago is null || (_tabla.SelectedItem as FilaRecibo)?.Id != fila.Id)
                return;

            _recibo = recibo;
            _pago = pago;
            _fila = fila;
            ConstruirDetalle(fila, recibo, pago);
        }
        catch (Exception ex)
        {
            _detalle.Children.Clear();
            _detalle.Children.Add(new TextBlock { Text = "No se pudo abrir el recibo: " + ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = ColoresPanel.Hex("#A12A2A") });
        }
    }

    private void ConstruirDetalle(FilaRecibo f, ReciboDto recibo, PagoDto pago)
    {
        _detalle.Children.Clear();

        var cabeza = new DockPanel();
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = ColoresPanel.Hex(f.Anulado ? "#FDECEC" : "#E7F5E7"),
            Child = new TextBlock
            {
                Text = f.Anulado ? "ANULADO" : "Válido",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = ColoresPanel.Hex(f.Anulado ? "#A12A2A" : "#0B5A0B")
            }
        };
        DockPanel.SetDock(chip, Dock.Right);
        cabeza.Children.Add(chip);
        cabeza.Children.Add(new TextBlock { Text = $"Recibo N.º {f.Numero}", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo });
        _detalle.Children.Add(cabeza);
        _detalle.Children.Add(UiKit.Ayuda(f.FechaTexto, 10));

        if (f.Anulado)
        {
            var aviso = UiKit.Aviso(out var texto, "error");
            texto.Text = TextoRecibo.Anulacion(recibo) ?? "ANULADO";
            _detalle.Children.Add(aviso);
        }

        var datos = new List<(string, string)>
        {
            ("Caseta", $"{f.Caseta}{(string.IsNullOrWhiteSpace(f.Bloque) ? "" : $" · Bloque {f.Bloque}")}"),
            ("Pagó", f.Pagador),
            ("Forma de pago", f.FormaPago)
        };

        if (TextoRecibo.Referencia(pago) is { } referencia)
            datos.Add(("Transacción", referencia.Split(": ", 2).Last()));
        if (TextoRecibo.TipoCambio(pago) is { } tipoCambio)
            datos.Add(("Tipo de cambio", tipoCambio.Split(": ", 2).Last()));
        datos.Add(("Cobró", f.CobradoPor));

        _detalle.Children.Add(Ficha(datos));

        _detalle.Children.Add(new TextBlock { Text = "Conceptos cobrados", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Titulo, Margin = new Thickness(0, 4, 0, 6) });

        foreach (var d in recibo.Detalles)
        {
            var linea = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var monto = new TextBlock { Text = d.Monto.ToString("N2", Bo), FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta };
            DockPanel.SetDock(monto, Dock.Right);
            linea.Children.Add(monto);
            linea.Children.Add(new TextBlock { Text = $"{d.Concepto} · {d.Periodo}", TextWrapping = TextWrapping.Wrap, Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 10, 0) });
            _detalle.Children.Add(linea);
        }

        var total = new DockPanel { Margin = new Thickness(0, 6, 0, 14) };
        var totalMonto = new TextBlock { Text = f.TotalTexto, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = f.Anulado ? ColoresPanel.Hex("#A12A2A") : UiKit.Tinta };
        DockPanel.SetDock(totalMonto, Dock.Right);
        total.Children.Add(totalMonto);
        total.Children.Add(new TextBlock { Text = "Total", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta });
        _detalle.Children.Add(new Border { BorderBrush = ColoresPanel.Hex("#E1E7EF"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0), Child = total });

        var reimprimir = UiKit.Boton(f.Anulado ? "Ver el recibo anulado" : "Ver e imprimir de nuevo", principal: true);
        reimprimir.HorizontalAlignment = HorizontalAlignment.Stretch;
        reimprimir.Click += (_, _) => VerEImprimir();
        _detalle.Children.Add(reimprimir);

        _detalle.Children.Add(UiKit.Ayuda(
            "Se imprime marcado como REIMPRESIÓN. No crea un cobro nuevo ni cambia las deudas." +
            (!f.Anulado && (SesionActual.EsAdministrador || !SesionActual.Iniciada)
                ? "\nPara anular este cobro: Reportes → Anular cobro."
                : ""), 0));
    }

    private static Border Ficha(IEnumerable<(string Dato, string Valor)> filas)
    {
        var grilla = new Grid();
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var i = 0;
        foreach (var (dato, valor) in filas)
        {
            grilla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var e = new TextBlock { Text = dato, Foreground = UiKit.Suave, Margin = new Thickness(0, 2, 14, 2) };
            var v = new TextBlock { Text = valor, Foreground = UiKit.Tinta, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(e, i);
            Grid.SetRow(v, i);
            Grid.SetColumn(v, 1);
            grilla.Children.Add(e);
            grilla.Children.Add(v);
            i++;
        }

        return new Border
        {
            Background = UiKit.Fondo,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12),
            Child = grilla
        };
    }

    private void VerEImprimir()
    {
        if (_recibo is null || _pago is null || _fila is null)
            return;

        try
        {
            new VistaPreviaReciboWindow(_recibo, _pago, _fila.Caseta, _fila.Bloque ?? "", _fila.Moneda, reimpresion: true)
            {
                Owner = Window.GetWindow(this)
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se pudo abrir el recibo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    // MODELOS
    // =====================================================================

    private sealed class BusquedaRespuesta
    {
        public int Total { get; set; }
        public int Mostrados { get; set; }
        public List<FilaRecibo> Recibos { get; set; } = new();
    }

    public sealed class FilaRecibo
    {
        public Guid Id { get; set; }
        public long Numero { get; set; }
        public DateTimeOffset FechaEmision { get; set; }
        public string Caseta { get; set; } = "";
        public string? Bloque { get; set; }
        public string Pagador { get; set; } = "";
        public string FormaPago { get; set; } = "";
        public string Moneda { get; set; } = "";
        public decimal Total { get; set; }
        public string Estado { get; set; } = "";
        public string? RegistradoPor { get; set; }

        public bool Anulado => string.Equals(Estado, "Anulado", StringComparison.OrdinalIgnoreCase);
        public string EstadoTexto => Anulado ? "ANULADO" : "Válido";
        public string FechaTexto => FechaEmision.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy HH:mm", Bo);
        public string TotalTexto => $"{Moneda} {Total.ToString("N2", Bo)}";
        public string CobradoPor => string.IsNullOrWhiteSpace(RegistradoPor) ? "—" : RegistradoPor!;
    }
}
