using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views.Reportes;

/// <summary>
/// Base de los reportes: filtros arriba, botón Generar, resumen, tabla y
/// Guardar PDF / Excel. La tabla en pantalla, el PDF y el Excel salen del
/// mismo <see cref="Reporte"/>, así siempre coinciden.
/// </summary>
public abstract class ReporteTablaView : UserControl
{
    protected static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    protected readonly WrapPanel Filtros = new();
    private readonly TextBlock _estado = new() { Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _resumen = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly Button _generar = UiKit.Boton("Generar", principal: true);
    private readonly Button _pdf = UiKit.Boton("Guardar PDF");
    private readonly Button _excel = UiKit.Boton("Excel");
    private Reporte? _actual;

    protected ReporteTablaView(string titulo, string ayuda)
    {
        var raiz = new DockPanel();

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);

        var cabeza = new DockPanel();
        var acciones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _pdf.Margin = new Thickness(0, 0, 8, 0);
        _pdf.IsEnabled = _excel.IsEnabled = false;
        _pdf.Click += (_, _) => { if (_actual is not null) ReportePdfService.GuardarPdf(Window.GetWindow(this), _actual); };
        _excel.Click += (_, _) => { if (_actual is not null) ReportePdfService.GuardarExcel(Window.GetWindow(this), _actual); };
        acciones.Children.Add(_pdf);
        acciones.Children.Add(_excel);
        DockPanel.SetDock(acciones, Dock.Right);
        cabeza.Children.Add(acciones);
        cabeza.Children.Add(new TextBlock { Text = titulo, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo, VerticalAlignment = VerticalAlignment.Center });
        arriba.Children.Add(cabeza);
        arriba.Children.Add(UiKit.Ayuda(ayuda, 10));

        _generar.Margin = new Thickness(0, 0, 0, 10);
        _generar.VerticalAlignment = VerticalAlignment.Bottom;
        _generar.Click += async (_, _) => await GenerarAsync();

        var barra = new DockPanel();
        DockPanel.SetDock(_generar, Dock.Right);
        barra.Children.Add(_generar);
        barra.Children.Add(Filtros);
        arriba.Children.Add(UiKit.Tarjeta(barra, 10));
        arriba.Children.Add(_estado);
        arriba.Children.Add(_resumen);
        raiz.Children.Add(arriba);

        _tabla.CanUserSortColumns = false;
        _tabla.ItemsSource = new List<FilaReporte>();
        var estilo = new Style(typeof(DataGridRow), UiKit.Estilo("Mx.Row"));
        var grupo = new DataTrigger { Binding = new Binding(nameof(FilaReporte.Grupo)), Value = true };
        grupo.Setters.Add(new Setter(BackgroundProperty, ColoresPanel.Hex("#EEF3F8")));
        grupo.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
        estilo.Triggers.Add(grupo);
        var total = new DataTrigger { Binding = new Binding(nameof(FilaReporte.Total)), Value = true };
        total.Setters.Add(new Setter(FontWeightProperty, FontWeights.Bold));
        total.Setters.Add(new Setter(ForegroundProperty, UiKit.Titulo));
        estilo.Triggers.Add(total);
        _tabla.RowStyle = estilo;
        raiz.Children.Add(_tabla);

        Content = raiz;
        _estado.Text = "Elige los filtros y pulsa Generar.";
    }

    /// <summary>Arma el reporte con los filtros actuales. Devuelve null si falta algún dato (y ya avisó).</summary>
    protected abstract Task<Reporte?> ConstruirAsync();

    /// <summary>Texto del botón Generar.</summary>
    protected string TextoGenerar
    {
        set => _generar.Content = value;
    }

    protected void Avisar(string texto) => _estado.Text = texto;

    public async Task GenerarAsync()
    {
        _generar.IsEnabled = false;
        _estado.Text = "Generando…";
        try
        {
            var r = await ConstruirAsync();
            if (r is null)
                return;

            Mostrar(r);
            _estado.Text = r.Filas.Count == 0
                ? "No hay datos con esos filtros."
                : $"Generado a las {DateTime.Now:HH:mm}. Puedes guardarlo en PDF o Excel.";
        }
        catch (Exception ex)
        {
            _estado.Text = "No se pudo generar: " + ex.Message;
        }
        finally
        {
            _generar.IsEnabled = true;
        }
    }

    private void Mostrar(Reporte r)
    {
        _actual = r;
        _pdf.IsEnabled = _excel.IsEnabled = r.Filas.Count > 0;

        _resumen.Children.Clear();
        foreach (var linea in r.Resumen)
            _resumen.Children.Add(new TextBlock { Text = linea, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(r.Nota))
            _resumen.Children.Add(new TextBlock { Text = r.Nota, Foreground = UiKit.Suave, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });

        _tabla.Columns.Clear();
        for (var i = 0; i < r.Columnas.Count; i++)
        {
            var c = r.Columnas[i];
            var texto = new Style(typeof(TextBlock));
            texto.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            texto.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
            if (c.Derecha)
                texto.Setters.Add(new Setter(HorizontalAlignmentProperty, HorizontalAlignment.Right));

            _tabla.Columns.Add(new DataGridTextColumn
            {
                Header = c.Titulo,
                Binding = new Binding($"{nameof(FilaReporte.Celdas)}[{i}]"),
                Width = new DataGridLength(c.Peso, DataGridLengthUnitType.Star),
                ElementStyle = texto
            });
        }

        _tabla.ItemsSource = r.Filas;
    }

    // =====================================================================
    // AYUDANTES PARA LOS REPORTES
    // =====================================================================

    protected static string Monto(decimal v) => v.ToString("N2", Bo);

    protected static string Fecha(DateTime d) => d.ToString("dd/MM/yyyy", Bo);

    protected static string Rango(DateTime desde, DateTime hasta) =>
        desde.Date == hasta.Date ? $"Del día {Fecha(desde)}" : $"Del {Fecha(desde)} al {Fecha(hasta)}";

    /// <summary>Selector de fechas con atajos: Hoy, Este mes, Mes anterior.</summary>
    protected (DatePicker Desde, DatePicker Hasta) AgregarRango(DateTime desde, DateTime hasta)
    {
        var d = UiKit.Fecha(desde);
        var h = UiKit.Fecha(hasta);
        Filtros.Children.Add(UiKit.Campo("Desde", d));
        Filtros.Children.Add(UiKit.Campo("Hasta", h));

        var atajos = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 14, 10) };
        void Atajo(string texto, Func<(DateTime, DateTime)> rango)
        {
            var b = new Button { Content = texto, Style = UiKit.Estilo("Mx.NavLink"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            b.Click += (_, _) =>
            {
                var (a, z) = rango();
                d.SelectedDate = a;
                h.SelectedDate = z;
            };
            atajos.Children.Add(b);
        }

        var hoy = DateTime.Today;
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        Atajo("Hoy", () => (hoy, hoy));
        Atajo("Este mes", () => (inicioMes, hoy));
        Atajo("Mes anterior", () => (inicioMes.AddMonths(-1), inicioMes.AddDays(-1)));
        Filtros.Children.Add(atajos);
        return (d, h);
    }

    /// <summary>Lee y valida el rango. Devuelve null si está mal (y avisa).</summary>
    protected (DateTime Desde, DateTime Hasta)? LeerRango(DatePicker d, DatePicker h)
    {
        if (d.SelectedDate is not { } desde || h.SelectedDate is not { } hasta)
        {
            Avisar("Elige las dos fechas.");
            return null;
        }

        if (desde > hasta)
        {
            Avisar("La fecha inicial no puede ser posterior a la final.");
            return null;
        }

        return (desde.Date, hasta.Date);
    }

    // ---------- Catálogos compartidos ----------

    protected sealed class Catalogos
    {
        public List<UiKit.Item<Guid?>> Conceptos { get; init; } = new();
        public List<string> Bloques { get; init; } = new();
        public Dictionary<Guid, string> Monedas { get; init; } = new();
        public Dictionary<Guid, (string Codigo, string? Bloque)> Unidades { get; init; } = new();
    }

    private static Catalogos? _cache;
    private static DateTime _cacheHora;

    /// <summary>Conceptos, bloques, monedas y unidades (se guardan 2 minutos).</summary>
    protected static async Task<Catalogos> CatalogosAsync()
    {
        if (_cache is not null && DateTime.Now - _cacheHora < TimeSpan.FromMinutes(2))
            return _cache;

        var conceptos = await ApiJson.GetAsync<CatalogoConceptos>("/api/ingresos/catalogos");
        var monedas = await ApiJson.GetAsync<List<MonedaDto>>("/api/monedas") ?? new();
        var unidades = await ApiJson.GetAsync<List<UnidadDto>>("/api/unidades-comerciales") ?? new();

        _cache = new Catalogos
        {
            Conceptos = (conceptos?.Conceptos ?? new())
                .Where(c => c.Id != Guid.Empty)
                .OrderBy(c => c.Nombre)
                .Select(c => new UiKit.Item<Guid?>(c.Id, c.Nombre))
                .ToList(),
            Bloques = unidades.Select(u => u.Bloque?.Trim()).OfType<string>().Where(b => b.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(b => b.Length).ThenBy(b => b, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Monedas = monedas.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Codigo),
            Unidades = unidades.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => (g.First().Codigo, g.First().Bloque))
        };
        _cacheHora = DateTime.Now;
        return _cache;
    }

    protected static ComboBox ComboBloques(Catalogos c)
    {
        var combo = UiKit.Combo(130);
        combo.ItemsSource = new[] { new UiKit.Item<string?>(null, "Todos") }
            .Concat(c.Bloques.Select(b => new UiKit.Item<string?>(b, b))).ToList();
        combo.SelectedIndex = 0;
        return combo;
    }

    private sealed class CatalogoConceptos
    {
        public List<ConceptoDto> Conceptos { get; set; } = new();
    }

    private sealed class ConceptoDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = "";
    }

    private sealed class MonedaDto
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
    }

    private sealed class UnidadDto
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public string? Bloque { get; set; }
    }
}
