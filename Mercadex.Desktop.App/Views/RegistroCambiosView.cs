using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Registro de cambios (auditoría): quién hizo qué, cuándo y desde dónde.
/// Cobros, anulaciones, aprobaciones de pagos QR, cierres de caja,
/// cambios de deudas, socios, casetas y usuarios, e ingresos al sistema.
/// Solo lectura: administrador y consulta (directiva).
/// </summary>
public sealed class RegistroCambiosView : UserControl
{
    private const int Tamano = 50;

    private readonly DatePicker _desde = UiKit.Fecha(DateTime.Today.AddDays(-7));
    private readonly DatePicker _hasta = UiKit.Fecha(DateTime.Today);
    private readonly ComboBox _modulo = UiKit.Combo(190);
    private readonly ComboBox _usuario = UiKit.Combo(200);
    private readonly TextBox _texto = UiKit.Texto(220, 80);
    private readonly TextBlock _resumen = new() { Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly Button _anterior = UiKit.Boton("‹ Anterior");
    private readonly Button _siguiente = UiKit.Boton("Siguiente ›");
    private readonly TextBlock _pagina = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), Foreground = UiKit.Suave };

    private int _paginaActual = 1;
    private int _paginas = 1;
    private bool _llenando;

    public RegistroCambiosView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);
        arriba.Children.Add(UiKit.Encabezado("Registro de cambios"));
        arriba.Children.Add(UiKit.Ayuda(
            "Quién hizo qué y cuándo: cobros, anulaciones, pagos QR aprobados o rechazados, cierres de caja, " +
            "cambios en deudas, socios, casetas y usuarios, e ingresos al sistema. Las líneas no se pueden editar ni borrar.", 10));

        _modulo.ItemsSource = new List<UiKit.Item<string>> { new("", "Todos") };
        _modulo.SelectedIndex = 0;
        _usuario.ItemsSource = new List<UiKit.Item<string>> { new("", "Todos") };
        _usuario.SelectedIndex = 0;

        _modulo.SelectionChanged += async (_, _) => { if (!_llenando) await BuscarAsync(1); };
        _usuario.SelectionChanged += async (_, _) => { if (!_llenando) await BuscarAsync(1); };
        _texto.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await BuscarAsync(1); };

        var buscar = UiKit.Boton("Buscar", principal: true);
        buscar.Click += async (_, _) => await BuscarAsync(1);
        var exportar = UiKit.Boton("Exportar a Excel");
        exportar.Margin = new Thickness(10, 0, 0, 0);
        exportar.Click += async (_, _) => await ExportarAsync();

        var filtros = new WrapPanel();
        filtros.Children.Add(UiKit.Campo("Desde", _desde));
        filtros.Children.Add(UiKit.Campo("Hasta", _hasta));
        filtros.Children.Add(UiKit.Campo("Módulo", _modulo));
        filtros.Children.Add(UiKit.Campo("Usuario", _usuario));
        filtros.Children.Add(UiKit.Campo("Buscar (caseta, recibo, socio…)", _texto));
        filtros.Children.Add(buscar);
        filtros.Children.Add(exportar);
        arriba.Children.Add(UiKit.Tarjeta(filtros, 8));
        arriba.Children.Add(_resumen);
        raiz.Children.Add(arriba);

        var pie = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(pie, Dock.Bottom);
        _anterior.Click += async (_, _) => await BuscarAsync(_paginaActual - 1);
        _siguiente.Click += async (_, _) => await BuscarAsync(_paginaActual + 1);
        pie.Children.Add(_anterior);
        pie.Children.Add(_pagina);
        pie.Children.Add(_siguiente);
        raiz.Children.Add(pie);

        _tabla.Columns.Add(UiKit.Columna("Fecha y hora", nameof(CambioFila.FechaTexto), 1.15));
        _tabla.Columns.Add(UiKit.Columna("Usuario", nameof(CambioFila.UsuarioTexto), 1.3));
        _tabla.Columns.Add(UiKit.Columna("Desde", nameof(CambioFila.Origen), 0.85));
        _tabla.Columns.Add(UiKit.Columna("Módulo", nameof(CambioFila.Modulo), 1));
        _tabla.Columns.Add(UiKit.Columna("Acción", nameof(CambioFila.Accion), 1.3));

        var detalle = UiKit.Columna("Detalle", nameof(CambioFila.Detalle), 3.2);
        var envolver = new Style(typeof(TextBlock));
        envolver.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        envolver.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 4, 6, 4)));
        detalle.ElementStyle = envolver;
        _tabla.Columns.Add(detalle);

        _tabla.MouseDoubleClick += (_, _) => VerDetalle();
        _tabla.ToolTip = "Doble clic para ver la línea completa";
        raiz.Children.Add(_tabla);

        Content = raiz;
        Loaded += async (_, _) => await BuscarAsync(1);
    }

    // =====================================================================

    private string Filtros()
    {
        var q = new List<string>();
        if (_desde.SelectedDate is DateTime d) q.Add("desde=" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (_hasta.SelectedDate is DateTime h) q.Add("hasta=" + h.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if ((_modulo.SelectedItem as UiKit.Item<string>)?.Valor is { Length: > 0 } m) q.Add("modulo=" + Uri.EscapeDataString(m));
        if ((_usuario.SelectedItem as UiKit.Item<string>)?.Valor is { Length: > 0 } u) q.Add("usuario=" + Uri.EscapeDataString(u));
        if (_texto.Text.Trim() is { Length: > 0 } t) q.Add("texto=" + Uri.EscapeDataString(t));
        return string.Join("&", q);
    }

    private async Task BuscarAsync(int pagina)
    {
        if (_desde.SelectedDate is DateTime d && _hasta.SelectedDate is DateTime h && d > h)
        {
            _resumen.Text = "La fecha «Desde» no puede ser posterior a «Hasta».";
            return;
        }

        pagina = Math.Max(1, pagina);
        _resumen.Text = "Cargando…";

        try
        {
            var r = await ApiJson.GetAsync<RespuestaCambios>(
                $"/api/registro-cambios?{Filtros()}&pagina={pagina}&tamano={Tamano}");

            if (r is null)
            {
                _resumen.Text = "El servidor no devolvió datos.";
                return;
            }

            LlenarFiltros(r);

            _paginaActual = r.Pagina;
            _paginas = Math.Max(1, r.Paginas);
            _tabla.ItemsSource = r.Filas;

            _resumen.Text = r.Total == 0
                ? "No hay cambios registrados con esos filtros."
                : $"{r.Total:N0} {(r.Total == 1 ? "línea" : "líneas")} encontradas. Las más recientes primero.";

            _pagina.Text = $"Página {_paginaActual} de {_paginas}";
            _anterior.IsEnabled = _paginaActual > 1;
            _siguiente.IsEnabled = _paginaActual < _paginas;
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudo cargar el registro: " + ex.Message;
        }
    }

    private void LlenarFiltros(RespuestaCambios r)
    {
        _llenando = true;
        try
        {
            Llenar(_modulo, r.Modulos);
            Llenar(_usuario, r.Usuarios);
        }
        finally
        {
            _llenando = false;
        }
    }

    private static void Llenar(ComboBox combo, List<string>? valores)
    {
        var actual = (combo.SelectedItem as UiKit.Item<string>)?.Valor ?? "";
        var items = new List<UiKit.Item<string>> { new("", "Todos") };
        items.AddRange((valores ?? new()).Select(v => new UiKit.Item<string>(v, v)));

        if (actual.Length > 0 && items.All(i => i.Valor != actual))
            items.Add(new UiKit.Item<string>(actual, actual));

        combo.ItemsSource = items;
        combo.SelectedItem = items.First(i => i.Valor == actual);
    }

    private void VerDetalle()
    {
        if (_tabla.SelectedItem is not CambioFila f)
            return;

        Dialogo.Mostrar(
            $"{f.FechaTexto}\n{f.UsuarioTexto} · desde {f.Origen}\n\n{f.Modulo}: {f.Accion}\n\n{f.Detalle}",
            "Registro de cambios", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task ExportarAsync()
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar registro de cambios",
            Filter = "Archivo CSV para Excel (*.csv)|*.csv",
            FileName = $"Registro_de_cambios_{DateTime.Today:yyyy-MM-dd}.csv"
        };

        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            _resumen.Text = "Preparando el archivo…";
            var bytes = await ApiJson.BytesAsync($"/api/registro-cambios/csv?{Filtros()}");
            await File.WriteAllBytesAsync(dialogo.FileName, bytes);
            _resumen.Text = "Archivo guardado: " + dialogo.FileName;
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(ex.Message, "No se pudo exportar", MessageBoxButton.OK, MessageBoxImage.Warning);
            _resumen.Text = "";
        }
    }

    // =====================================================================

    private sealed class RespuestaCambios
    {
        public int Total { get; set; }
        public int Pagina { get; set; } = 1;
        public int Paginas { get; set; } = 1;
        public List<string>? Modulos { get; set; }
        public List<string>? Usuarios { get; set; }
        public List<CambioFila> Filas { get; set; } = new();
    }

    public sealed class CambioFila
    {
        public Guid Id { get; set; }
        public DateTimeOffset Fecha { get; set; }
        public string Usuario { get; set; } = "";
        public string? Rol { get; set; }
        public string Origen { get; set; } = "";
        public string Modulo { get; set; } = "";
        public string Accion { get; set; } = "";
        public string? Detalle { get; set; }

        /// <summary>La API envía la hora de Bolivia (…-04:00).</summary>
        public string FechaTexto => Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        public string UsuarioTexto => Rol switch
        {
            "Administrador" => $"{Usuario} (Administrador)",
            "Cajero" => $"{Usuario} (Caja)",
            "Consulta" => $"{Usuario} (Consulta)",
            "Socio" => $"{Usuario} (Socio)",
            _ => Usuario
        };
    }
}
