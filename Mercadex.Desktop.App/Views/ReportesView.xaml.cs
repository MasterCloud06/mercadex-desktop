
using Mercadex.Desktop.App.Models;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Views;

public partial class ReportesView : UserControl
{
    private static readonly HttpClient Http = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(40));

    private readonly CultureInfo _cultura =
        CultureInfo.GetCultureInfo("es-BO");

    private List<ObligacionIngresoModel> _obligaciones = new();
    private List<UnidadIngresoModel> _unidades = new();
    private List<MonedaIngresoModel> _monedas = new();

    private CatalogosIngresosModel? _catalogos;

    private List<FilaCorte> _corte = new();
    private List<FilaContabilidad> _contabilidad = new();

    private bool _cargando;
    private bool _inicializado;
    private bool _actualizandoFiltros;

    public ReportesView()
    {
        InitializeComponent();

        // Anular cobros: solo administradores.
        if (!Sesion.SesionActual.EsAdministrador)
            TabAnularCobro.Visibility = System.Windows.Visibility.Collapsed;

        Loaded += ReportesView_Loaded;
    }

    // ---------- Reportes en ventana propia ----------

    private static readonly System.Net.Http.HttpClient ApiVentanas =
        ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(90));

    private void ReportesCaja_Click(object sender, RoutedEventArgs e) =>
        new ReportesCajaWindow(ApiVentanas) { Owner = Window.GetWindow(this) }.ShowDialog();

    private void DeudasConsolidadas_Click(object sender, RoutedEventArgs e) =>
        new DeudoresConsolidadosWindow(ApiVentanas) { Owner = Window.GetWindow(this) }.ShowDialog();

    private void ComprobantesAnulacion_Click(object sender, RoutedEventArgs e) =>
        new ComprobantesAnulacionWindow(ApiVentanas) { Owner = Window.GetWindow(this) }.ShowDialog();

    private async void ReportesView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_inicializado)
            return;

        _inicializado = true;
        await CargarAsync();
    }

    private async void Actualizar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CargarAsync();
    }

    private async Task CargarAsync()
    {
        if (_cargando)
            return;

        _cargando = true;
        TxtEstado.Text = "Consultando obligaciones y casetas...";

        try
        {
            _obligaciones =
                await Http.GetFromJsonAsync<
                    List<ObligacionIngresoModel>>(
                        "/api/Obligaciones")
                ?? new();

            _unidades =
                await Http.GetFromJsonAsync<
                    List<UnidadIngresoModel>>(
                        "/api/unidades-comerciales")
                ?? new();

            _monedas =
                await Http.GetFromJsonAsync<
                    List<MonedaIngresoModel>>(
                        "/api/monedas")
                ?? new();

            _catalogos =
                await Http.GetFromJsonAsync<
                    CatalogosIngresosModel>(
                        "/api/ingresos/catalogos");

            PrepararFiltros();
            GenerarInformes();

            TxtEstado.Text =
                "Datos actualizados: " +
                DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        }
        catch (OperationCanceledException)
        {
            TxtEstado.Text =
                "La API tardó demasiado en responder. " +
                "Pulsa Actualizar para volver a consultar.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No fue posible cargar los informes: " +
                ex.Message;
        }
        finally
        {
            _cargando = false;
        }
    }

    private void PrepararFiltros()
    {
        _actualizandoFiltros = true;

        var bloqueAnterior =
            FiltroBloque.SelectedItem as string;

        var monedaAnterior =
            FiltroMoneda.SelectedItem as string;

        FiltroBloque.Items.Clear();
        FiltroBloque.Items.Add("Todos");

        foreach (var bloque in _unidades
            .Select(u => u.Bloque)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b))
        {
            FiltroBloque.Items.Add(bloque);
        }

        FiltroBloque.SelectedItem =
            bloqueAnterior != null &&
            FiltroBloque.Items.Contains(bloqueAnterior)
                ? bloqueAnterior
                : "Todos";

        FiltroConcepto.ItemsSource = null;

        var conceptos = new List<ConceptoFiltro>
        {
            new() { Id = null, Nombre = "Todos" }
        };

        conceptos.AddRange(
            (_catalogos?.Conceptos ?? new())
            .Select(c => new ConceptoFiltro
            {
                Id = c.Id,
                Nombre = c.Nombre
            })
            .OrderBy(c => c.Nombre));

        FiltroConcepto.ItemsSource = conceptos;
        FiltroConcepto.SelectedIndex = 0;

        FiltroMoneda.Items.Clear();
        FiltroMoneda.Items.Add("Todas");

        foreach (var moneda in _monedas.OrderBy(m => m.Codigo))
            FiltroMoneda.Items.Add(moneda.Codigo);

        FiltroMoneda.SelectedItem =
            monedaAnterior != null &&
            FiltroMoneda.Items.Contains(monedaAnterior)
                ? monedaAnterior
                : "Todas";

        _actualizandoFiltros = false;
    }

    private void Filtros_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_actualizandoFiltros)
            GenerarInformes();
    }

    private void Caseta_Changed(
        object sender,
        TextChangedEventArgs e)
    {
        GenerarInformes();
    }

    private void Fechas_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        GenerarInformes();
    }

    private void GenerarInformes()
    {
        if (_actualizandoFiltros ||
            TablaCorte == null ||
            TablaContabilidad == null ||
            FiltroBloque == null ||
            FiltroConcepto == null ||
            FiltroMoneda == null ||
            FiltroCaseta == null ||
            FechaDesde == null ||
            FechaHasta == null)
        {
            return;
        }

        var unidadesPorId = _unidades
            .GroupBy(u => u.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var monedasPorId = _monedas
            .GroupBy(m => m.Id)
            .ToDictionary(g => g.Key, g => g.First().Codigo);

        var conceptosPorId =
            (_catalogos?.Conceptos ?? new())
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().Nombre);

        var bloque =
            FiltroBloque.SelectedItem as string ?? "Todos";

        var moneda =
            FiltroMoneda.SelectedItem as string ?? "Todas";

        var concepto =
            FiltroConcepto.SelectedItem as ConceptoFiltro;

        var caseta = FiltroCaseta.Text.Trim();

        var desde = FechaDesde.SelectedDate;
        var hasta = FechaHasta.SelectedDate;

        var pendientes = _obligaciones
            .Where(o => string.Equals(
                o.Estado,
                "Pendiente",
                StringComparison.OrdinalIgnoreCase))
            .Where(o => unidadesPorId.ContainsKey(
                o.UnidadComercialId))
            .ToList();

        if (bloque != "Todos")
        {
            pendientes = pendientes
                .Where(o => string.Equals(
                    unidadesPorId[o.UnidadComercialId].Bloque,
                    bloque,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (concepto?.Id is Guid conceptoId)
        {
            pendientes = pendientes
                .Where(o => o.ConceptoCobroId == conceptoId)
                .ToList();
        }

        if (moneda != "Todas")
        {
            pendientes = pendientes
                .Where(o =>
                    monedasPorId.TryGetValue(
                        o.MonedaId,
                        out var codigo) &&
                    string.Equals(
                        codigo,
                        moneda,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(caseta))
        {
            pendientes = pendientes
                .Where(o =>
                    unidadesPorId[o.UnidadComercialId]
                        .Codigo.Contains(
                            caseta,
                            StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (desde.HasValue)
        {
            pendientes = pendientes
                .Where(o =>
                    o.FechaEmision >=
                    DateOnly.FromDateTime(desde.Value))
                .ToList();
        }

        if (hasta.HasValue)
        {
            pendientes = pendientes
                .Where(o =>
                    o.FechaEmision <=
                    DateOnly.FromDateTime(hasta.Value))
                .ToList();
        }

        // INFORME PARA CORTE:
        // Una fila por caseta y moneda.
        // No se mezclan importes de BOB y USD.

        _corte = pendientes
            .GroupBy(o => new
            {
                o.UnidadComercialId,
                o.MonedaId
            })
            .Select(grupo =>
            {
                var unidad =
                    unidadesPorId[grupo.Key.UnidadComercialId];

                var nombres = grupo
                    .Select(o =>
                        conceptosPorId.TryGetValue(
                            o.ConceptoCobroId,
                            out var nombre)
                            ? nombre
                            : "Concepto sin identificar")
                    .Distinct()
                    .OrderBy(x => x);

                return new FilaCorte
                {
                    Codigo = unidad.Codigo,
                    Bloque = unidad.Bloque ?? "",
                    Servicios = string.Join(", ", nombres),
                    Cantidad = grupo.Count(),
                    Moneda = monedasPorId.TryGetValue(
                        grupo.Key.MonedaId,
                        out var codigo)
                        ? codigo
                        : "Sin identificar",
                    Total = grupo.Sum(o => o.Monto)
                };
            })
            .OrderBy(f => f.Bloque)
            .ThenBy(f => NumeroOrden(f.Codigo))
            .ThenBy(f => f.Codigo)
            .ThenBy(f => f.Moneda)
            .ToList();

        // INFORME PARA CONTABILIDAD:
        // Una fila por obligación pendiente.
        // Total calculado por caseta y moneda.

        _contabilidad = pendientes
            .GroupBy(o => new
            {
                o.UnidadComercialId,
                o.MonedaId
            })
            .SelectMany(grupo =>
            {
                var totalCaseta = grupo.Sum(o => o.Monto);

                return grupo.Select(o =>
                {
                    var unidad =
                        unidadesPorId[o.UnidadComercialId];

                    var nombreConcepto =
                        conceptosPorId.TryGetValue(
                            o.ConceptoCobroId,
                            out var nombre)
                            ? nombre
                            : "Sin identificar";

                    var codigoMoneda =
                        monedasPorId.TryGetValue(
                            o.MonedaId,
                            out var codigo)
                            ? codigo
                            : "Sin identificar";

                    return new FilaContabilidad
                    {
                        Codigo = unidad.Codigo,
                        Bloque = unidad.Bloque ?? "",
                        Fecha = o.FechaEmision
                            .ToString("dd/MM/yyyy"),
                        Concepto = nombreConcepto,
                        Periodo =
                            o.PeriodoMes.HasValue &&
                            o.PeriodoAnio.HasValue
                            ? $"{o.PeriodoMes.Value:00}/" +
                              $"{o.PeriodoAnio.Value}"
                            : "Sin período",
                        Moneda = codigoMoneda,
                        Monto = o.Monto,
                        TotalCaseta = totalCaseta
                    };
                });
            })
            .OrderBy(f => f.Bloque)
            .ThenBy(f => NumeroOrden(f.Codigo))
            .ThenBy(f => f.Codigo)
            .ThenBy(f => f.Moneda)
            .ThenBy(f => f.Fecha)
            .ToList();

        TablaCorte.ItemsSource = _corte;
        TablaContabilidad.ItemsSource = _contabilidad;

        var casetasDeudoras = pendientes
            .Select(o => o.UnidadComercialId)
            .Distinct()
            .Count();

        TxtResumen.Text =
            $"Casetas deudoras: {casetasDeudoras}  |  " +
            $"Obligaciones pendientes: {pendientes.Count}";

        var totales = pendientes
            .GroupBy(o =>
                monedasPorId.TryGetValue(
                    o.MonedaId,
                    out var codigo)
                    ? codigo
                    : "Sin identificar")
            .OrderBy(g => g.Key)
            .Select(g =>
                $"{g.Key}: {g.Sum(o => o.Monto).ToString("N2", _cultura)}");

        TxtTotales.Text = string.Join("    |    ", totales);

        if (pendientes.Count == 0)
            TxtTotales.Text = "Sin deudas para los filtros seleccionados.";
    }

    private static int NumeroOrden(string codigo)
    {
        return int.TryParse(codigo, out var numero)
            ? numero
            : int.MaxValue;
    }

    private void ExportarCorte_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filas = _corte.Select(f => new[]
        {
            f.Codigo,
            f.Bloque,
            f.Servicios,
            f.Cantidad.ToString(),
            f.Moneda,
            f.Total.ToString("0.00", CultureInfo.InvariantCulture)
        });

        ExportarCsv(
            "ListaDeudoresCorte.csv",
            new[]
            {
                "Caseta",
                "Bloque",
                "Servicios",
                "Cantidad",
                "Moneda",
                "Total"
            },
            filas);
    }

    private void ExportarContabilidad_Click(
        object sender,
        RoutedEventArgs e)
    {
        var filas = _contabilidad.Select(f => new[]
        {
            f.Codigo,
            f.Bloque,
            f.Fecha,
            f.Concepto,
            f.Periodo,
            f.Moneda,
            f.Monto.ToString("0.00", CultureInfo.InvariantCulture),
            f.TotalCaseta.ToString("0.00", CultureInfo.InvariantCulture)
        });

        ExportarCsv(
            "ListaDeudoresContabilidad.csv",
            new[]
            {
                "Caseta",
                "Bloque",
                "Fecha",
                "Concepto",
                "Periodo",
                "Moneda",
                "Importe",
                "TotalCaseta"
            },
            filas);
    }

    private void ExportarCsv(
        string nombreArchivo,
        string[] encabezados,
        IEnumerable<string[]> filas)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Exportar informe",
            FileName = nombreArchivo,
            DefaultExt = ".csv",
            Filter = "Archivo CSV (*.csv)|*.csv"
        };

        if (dialogo.ShowDialog() != true)
            return;

        try
        {
            var contenido = new StringBuilder();

            contenido.AppendLine(
                string.Join(";", encabezados.Select(EscaparCsv)));

            foreach (var fila in filas)
            {
                contenido.AppendLine(
                    string.Join(";", fila.Select(EscaparCsv)));
            }

            File.WriteAllText(
                dialogo.FileName,
                contenido.ToString(),
                new UTF8Encoding(true));

            Dialogo.Mostrar(
                "Informe exportado correctamente.",
                "Mercadex",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(
                ex.Message,
                "Error al exportar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string EscaparCsv(string? valor)
    {
        var texto = valor ?? "";

        return "\"" + texto.Replace("\"", "\"\"") + "\"";
    }
}

public sealed class ConceptoFiltro
{
    public Guid? Id { get; set; }

    public string Nombre { get; set; } = string.Empty;
}

public sealed class FilaCorte
{
    public string Codigo { get; set; } = string.Empty;

    public string Bloque { get; set; } = string.Empty;

    public string Servicios { get; set; } = string.Empty;

    public int Cantidad { get; set; }

    public string Moneda { get; set; } = string.Empty;

    public decimal Total { get; set; }
}

public sealed class FilaContabilidad
{
    public string Codigo { get; set; } = string.Empty;

    public string Bloque { get; set; } = string.Empty;

    public string Fecha { get; set; } = string.Empty;

    public string Concepto { get; set; } = string.Empty;

    public string Periodo { get; set; } = string.Empty;

    public string Moneda { get; set; } = string.Empty;

    public decimal Monto { get; set; }

    public decimal TotalCaseta { get; set; }
}
