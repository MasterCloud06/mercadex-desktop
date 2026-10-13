
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Views;

public partial class ReportesIngresosView : UserControl
{
    private static readonly HttpClient Api = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(60));

    private readonly CultureInfo _cultura =
        CultureInfo.GetCultureInfo("es-BO");

    private List<FilaTotalIngreso> _totales = new();
    private List<FilaConceptoIngreso> _conceptos = new();
    private List<FilaDetalleIngreso> _detalles = new();

    private bool _consultando;

    public ReportesIngresosView()
    {
        InitializeComponent();

        FechaDesde.SelectedDate =
            new DateTime(DateTime.Today.Year,
                         DateTime.Today.Month, 1);

        FechaHasta.SelectedDate = DateTime.Today;
    }

private async void Hoy_Click(
    object sender,
    RoutedEventArgs e)
{
    FechaDesde.SelectedDate = DateTime.Today;
    FechaHasta.SelectedDate = DateTime.Today;

    await ConsultarAsync();

    // Abrir directamente el detalle diario.
    Pestanas.SelectedIndex = 2;
}

    private async void Consultar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ConsultarAsync();
    }

    // ---------------- Filtros: concepto y bloque ----------------

    private const string TodosLosBloques = "Todos los bloques";

    private bool _filtrosCargados;

    private async Task CargarFiltrosAsync()
    {
        if (_filtrosCargados)
            return;

        var conceptos = new List<FiltroConcepto> { new() { Id = Guid.Empty, Nombre = "Todos los conceptos" } };
        var bloques = new List<string> { TodosLosBloques };

        try
        {
            var catalogos = await Api.GetFromJsonAsync<CatalogosFiltro>("/api/ingresos/catalogos");
            conceptos.AddRange((catalogos?.Conceptos ?? new())
                .Where(c => c.Id != Guid.Empty)
                .OrderBy(c => c.Nombre));

            var unidades = await Api.GetFromJsonAsync<List<UnidadReporte>>("/api/unidades-comerciales") ?? new();
            bloques.AddRange(unidades
                .Select(u => u.Bloque?.Trim())
                .OfType<string>()
                .Where(b => b.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(b => b.Length)
                .ThenBy(b => b));

            _filtrosCargados = true;
        }
        catch
        {
            // Sin catálogos el reporte funciona igual, sin filtros.
        }

        CmbConcepto.ItemsSource = conceptos;
        CmbConcepto.SelectedIndex = 0;
        CmbBloque.ItemsSource = bloques;
        CmbBloque.SelectedIndex = 0;
    }

    private Guid? ConceptoFiltro =>
        CmbConcepto.SelectedItem is FiltroConcepto { Id: var id } && id != Guid.Empty ? id : null;

    private string? BloqueFiltro =>
        CmbBloque.SelectedItem is string b && b != TodosLosBloques ? b : null;

    // ---------------- Consulta ----------------

    private async Task ConsultarAsync()
    {
        if (_consultando)
            return;

        await CargarFiltrosAsync();

        if (FechaDesde.SelectedDate is not DateTime desde ||
            FechaHasta.SelectedDate is not DateTime hasta)
        {
            TxtEstado.Text =
                "Selecciona la fecha inicial y final.";
            return;
        }

        desde = desde.Date;
        hasta = hasta.Date;

        if (hasta < desde)
        {
            TxtEstado.Text =
                "La fecha final no puede ser anterior a la inicial.";
            return;
        }

        _consultando = true;
        BtnConsultar.IsEnabled = false;

        TxtEstado.Text = "Consultando ingresos...";

        try
        {
            // Un solo reporte calculado en el servidor (fechas de Bolivia,
            // solo recibos válidos). Con concepto, cada recibo cuenta solo
            // el importe de ese concepto; con bloque, solo sus casetas.

            var ruta =
                "/api/reportes-caja" +
                $"?desde={desde:yyyy-MM-dd}" +
                $"&hasta={hasta:yyyy-MM-dd}" +
                (ConceptoFiltro is { } conceptoId ? $"&conceptoId={conceptoId}" : "") +
                (BloqueFiltro is { } bloqueElegido ? $"&bloque={Uri.EscapeDataString(bloqueElegido)}" : "");

            using var respuesta = await Api.GetAsync(ruta);

            if (!respuesta.IsSuccessStatusCode)
            {
                string? mensaje = null;

                try
                {
                    mensaje = (await respuesta.Content.ReadFromJsonAsync<RespuestaError>())?.Mensaje;
                }
                catch
                {
                    // Respuesta sin JSON.
                }

                throw new InvalidOperationException(
                    mensaje ?? $"El servidor respondió con el error {(int)respuesta.StatusCode}.");
            }

            var reporte = await respuesta.Content.ReadFromJsonAsync<ReporteServidor>()
                ?? throw new InvalidOperationException("No se recibieron los datos del reporte.");

            var unidades =
                await Api.GetFromJsonAsync<List<UnidadReporte>>(
                    "/api/unidades-comerciales")
                ?? new();

            var monedas =
                await Api.GetFromJsonAsync<List<MonedaReporte>>(
                    "/api/monedas")
                ?? new();

            var unidadesPorId = unidades
                .GroupBy(x => x.Id)
                .ToDictionary(
                    x => x.Key,
                    x => x.First());

            var monedasPorId = monedas
                .GroupBy(x => x.Id)
                .ToDictionary(
                    x => x.Key,
                    x => x.First().Codigo);

            string Moneda(Guid id) =>
                monedasPorId.TryGetValue(id, out var codigo) ? codigo : "?";

            var filas = new List<FilaDetalleIngreso>();

            foreach (var cobro in reporte.Cobros)
            {
                unidadesPorId.TryGetValue(
                    cobro.UnidadComercialId,
                    out var unidad);

                foreach (var detalle in cobro.Conceptos)
                {
                    filas.Add(new FilaDetalleIngreso
                    {
                        Fecha = cobro.FechaBolivia,
                        FechaOrden = cobro.FechaEmisionUtc,
                        NumeroRecibo = cobro.NumeroRecibo,
                        Caseta = unidad?.Codigo ?? "Sin identificar",
                        Bloque = unidad?.Bloque ?? "",
                        Pagador = cobro.Pagador,
                        Concepto = detalle.Concepto,
                        FormaPago = cobro.FormaPago,
                        Moneda = Moneda(cobro.MonedaId),
                        Monto = detalle.Monto,
                        Referencia = cobro.Referencia ?? "",
                        CobradoPor = string.IsNullOrWhiteSpace(cobro.CobradoPor) ? "—" : cobro.CobradoPor!
                    });
                }
            }

            // A. TOTAL POR MONEDA

            _totales = reporte.Resumen
                .Select(r => new FilaTotalIngreso
                {
                    Moneda = Moneda(r.MonedaId),
                    Recibos = r.CantidadRecibos,
                    Total = r.Total
                })
                .OrderBy(f => f.Moneda)
                .ToList();

            // B. AGRUPACIÓN POR CONCEPTO Y MONEDA

            _conceptos = reporte.Conceptos
                .Select(c => new FilaConceptoIngreso
                {
                    Concepto = c.Concepto,
                    Moneda = Moneda(c.MonedaId),
                    CantidadPagos = c.Cantidad,
                    Total = c.Total
                })
                .OrderBy(f => f.Concepto)
                .ThenBy(f => f.Moneda)
                .ToList();

            // C. DETALLE DE CADA CONCEPTO COBRADO

            _detalles = filas
                .OrderBy(f => f.FechaOrden)
                .ThenBy(f => f.NumeroRecibo)
                .ThenBy(f => f.Concepto)
                .ToList();

            TablaTotales.ItemsSource = _totales;
            TablaConceptos.ItemsSource = _conceptos;
            TablaDetalle.ItemsSource = _detalles;

            var filtros = new List<string>();
            if (ConceptoFiltro.HasValue && CmbConcepto.SelectedItem is FiltroConcepto c)
                filtros.Add($"Concepto: {c.Nombre}");
            if (BloqueFiltro is { } b)
                filtros.Add($"Bloque: {b}");

            TxtResumen.Text =
                $"Del {desde:dd/MM/yyyy} al " +
                $"{hasta:dd/MM/yyyy} | " +
                $"Recibos: {reporte.CantidadRecibosValidos} | " +
                $"Conceptos cobrados: {filas.Count}" +
                (filtros.Count > 0 ? " | " + string.Join(" · ", filtros) : "");

            TxtTotales.Text = _totales.Count == 0
                ? "Sin recaudación en el período."
                : string.Join(
                    "     |     ",
                    _totales.Select(t =>
                        $"{t.Moneda}: " +
                        t.Total.ToString("N2", _cultura)));

            TxtEstado.Text = reporte.Advertencias.Count == 0
                ? "Reportes generados correctamente."
                : "Reportes generados con advertencias: " + string.Join(" ", reporte.Advertencias);
        }
        catch (Exception ex)
        {
            _totales.Clear();
            _conceptos.Clear();
            _detalles.Clear();

            TablaTotales.ItemsSource = null;
            TablaConceptos.ItemsSource = null;
            TablaDetalle.ItemsSource = null;

            TxtResumen.Text = "No se pudo generar el reporte.";
            TxtTotales.Text = "";

            TxtEstado.Text =
                "Error al consultar los ingresos: " +
                ex.Message;
        }
        finally
        {
            _consultando = false;
            BtnConsultar.IsEnabled = true;
        }
    }

    private void Exportar_Click(
        object sender,
        RoutedEventArgs e)
    {
        string nombre;
        string[] encabezados;
        IEnumerable<string[]> registros;

        switch (Pestanas.SelectedIndex)
        {
            case 0:
                nombre = "RecaudacionTotal.csv";

                encabezados = new[]
                {
                    "Moneda",
                    "Cantidad de recibos",
                    "Total"
                };

                registros = _totales.Select(x => new[]
                {
                    x.Moneda,
                    x.Recibos.ToString(),
                    NumeroCsv(x.Total)
                });

                break;

            case 1:
                nombre = "RecaudacionPorServicio.csv";

                encabezados = new[]
                {
                    "Servicio",
                    "Moneda",
                    "Cantidad de pagos",
                    "Total"
                };

                registros = _conceptos.Select(x => new[]
                {
                    x.Concepto,
                    x.Moneda,
                    x.CantidadPagos.ToString(),
                    NumeroCsv(x.Total)
                });

                break;

            default:
                nombre = "DetalleCobros.csv";

                encabezados = new[]
                {
                    "Fecha",
                    "Recibo",
                    "Caseta",
                    "Bloque",
                    "Pagador",
                    "Concepto",
                    "Forma de pago",
                    "Moneda",
                    "Importe",
                    "Referencia",
                    "Cobrado por"
                };

                registros = _detalles.Select(x => new[]
                {
                    x.Fecha,
                    x.NumeroRecibo.ToString(),
                    x.Caseta,
                    x.Bloque,
                    x.Pagador,
                    x.Concepto,
                    x.FormaPago,
                    x.Moneda,
                    NumeroCsv(x.Monto),
                    x.Referencia,
                    x.CobradoPor
                });

                break;
        }

        var dialogo = new SaveFileDialog
        {
            Title = "Exportar reporte de ingresos",
            FileName = nombre,
            DefaultExt = ".csv",
            Filter = "Archivos CSV (*.csv)|*.csv"
        };

        if (dialogo.ShowDialog() != true)
            return;

        try
        {
            var csv = new StringBuilder();

            csv.AppendLine(
                string.Join(";", encabezados.Select(CeldaCsv)));

            foreach (var fila in registros)
            {
                csv.AppendLine(
                    string.Join(";", fila.Select(CeldaCsv)));
            }

            File.WriteAllText(
                dialogo.FileName,
                csv.ToString(),
                new UTF8Encoding(true));

            MessageBox.Show(
                "Reporte exportado correctamente.",
                "Mercadex",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Error de exportación",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string NumeroCsv(decimal valor)
    {
        return valor.ToString(
            "0.00",
            CultureInfo.InvariantCulture);
    }

    private static string CeldaCsv(string? valor)
    {
        var texto = valor ?? "";

        // Evitar que Excel interprete textos del CSV
        // como fórmulas al abrir el documento.

        if (texto.Length > 0 &&
            "=+-@".Contains(texto[0]) &&
            !decimal.TryParse(
                texto,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out _))
        {
            texto = "'" + texto;
        }

        return "\"" + texto.Replace("\"", "\"\"") + "\"";
    }

    private sealed class UnidadReporte
    {
        public Guid Id { get; set; }

        public string Codigo { get; set; } = "";

        public string? Bloque { get; set; }
    }

    private sealed class MonedaReporte
    {
        public Guid Id { get; set; }

        public string Codigo { get; set; } = "";
    }

    public sealed class FiltroConcepto
    {
        public Guid Id { get; set; }

        public string Nombre { get; set; } = "";
    }

    private sealed class CatalogosFiltro
    {
        public List<FiltroConcepto> Conceptos { get; set; } = new();
    }

    private sealed class RespuestaError
    {
        public string? Mensaje { get; set; }
    }

    private sealed class ReporteServidor
    {
        public int CantidadRecibosValidos { get; set; }

        public List<ResumenServidor> Resumen { get; set; } = new();

        public List<ConceptoServidor> Conceptos { get; set; } = new();

        public List<CobroServidor> Cobros { get; set; } = new();

        public List<string> Advertencias { get; set; } = new();
    }

    private sealed class ResumenServidor
    {
        public Guid MonedaId { get; set; }

        public int CantidadRecibos { get; set; }

        public decimal Total { get; set; }
    }

    private sealed class ConceptoServidor
    {
        public Guid MonedaId { get; set; }

        public string Concepto { get; set; } = "";

        public int Cantidad { get; set; }

        public decimal Total { get; set; }
    }

    private sealed class CobroServidor
    {
        public long NumeroRecibo { get; set; }

        public Guid UnidadComercialId { get; set; }

        public Guid MonedaId { get; set; }

        public string FormaPago { get; set; } = "";

        public string Pagador { get; set; } = "";

        public string? Referencia { get; set; }

        public string? CobradoPor { get; set; }

        public DateTimeOffset FechaEmisionUtc { get; set; }

        public string FechaBolivia { get; set; } = "";

        public List<DetalleServidor> Conceptos { get; set; } = new();
    }

    private sealed class DetalleServidor
    {
        public string Concepto { get; set; } = "";

        public decimal Monto { get; set; }
    }
}

public sealed class FilaTotalIngreso
{
    public string Moneda { get; set; } = "";

    public int Recibos { get; set; }

    public decimal Total { get; set; }
}

public sealed class FilaConceptoIngreso
{
    public string Concepto { get; set; } = "";

    public string Moneda { get; set; } = "";

    public int CantidadPagos { get; set; }

    public decimal Total { get; set; }
}

public sealed class FilaDetalleIngreso
{
    public DateTimeOffset FechaOrden { get; set; }

    public string Fecha { get; set; } = "";

    public long NumeroRecibo { get; set; }

    public string Caseta { get; set; } = "";

    public string Bloque { get; set; } = "";

    public string Pagador { get; set; } = "";

    public string Concepto { get; set; } = "";

    public string FormaPago { get; set; } = "";

    public string Moneda { get; set; } = "";

    public decimal Monto { get; set; }

    public string Referencia { get; set; } = "";

    public string CobradoPor { get; set; } = "";
}
