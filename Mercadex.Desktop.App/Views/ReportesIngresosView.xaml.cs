
using Mercadex.Desktop.App.Dtos;
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

    private async Task ConsultarAsync()
    {
        if (_consultando)
            return;

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

        TxtEstado.Text = "Consultando ingresos en Render...";

        try
        {
            var recibos =
                await Api.GetFromJsonAsync<List<ReciboDto>>(
                    "/api/Caja/recibos")
                ?? new();

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

            // Solo recibos emitidos.
            // Los anulados no cuentan como recaudación.

            var emitidos = recibos
                .Where(r => string.Equals(
                    r.Estado,
                    "Emitido",
                    StringComparison.OrdinalIgnoreCase))
                .Where(r =>
                {
                    var fecha =
                        r.FechaEmision.LocalDateTime.Date;

                    return fecha >= desde && fecha <= hasta;
                })
                .ToList();

            var filas = new List<FilaDetalleIngreso>();

            foreach (var recibo in emitidos)
            {
                // Es una consulta GET.
                // No registra ningún pago nuevo.

                var pago =
                    await Api.GetFromJsonAsync<PagoDto>(
                        $"/api/Caja/pagos/{recibo.PagoId}");

                if (pago is null)
                {
                    throw new InvalidOperationException(
                        $"Faltan los datos del pago del " +
                        $"recibo {recibo.Numero}.");
                }

                if (!monedasPorId.TryGetValue(
                    pago.MonedaId,
                    out var moneda))
                {
                    throw new InvalidOperationException(
                        $"Moneda desconocida en el " +
                        $"recibo {recibo.Numero}.");
                }

                unidadesPorId.TryGetValue(
                    recibo.UnidadComercialId,
                    out var unidad);

                if (recibo.Detalles is null ||
                    recibo.Detalles.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"El recibo {recibo.Numero} " +
                        "no tiene conceptos registrados.");
                }

                foreach (var detalle in recibo.Detalles)
                {
                    filas.Add(new FilaDetalleIngreso
                    {
                        Fecha =
                            recibo.FechaEmision.LocalDateTime
                                .ToString("dd/MM/yyyy HH:mm"),

                        FechaOrden =
                            recibo.FechaEmision,

                        NumeroRecibo = recibo.Numero,

                        Caseta =
                            unidad?.Codigo ?? "Sin identificar",

                        Bloque =
                            unidad?.Bloque ?? "",

                        Pagador = pago.NombrePagador,

                        Concepto = detalle.Concepto,

                        FormaPago = pago.FormaPago,

                        Moneda = moneda,

                        Monto = detalle.Monto,

                        Referencia = pago.Referencia ?? ""
                    });
                }

                // Evitar publicar totales contradictorios.

                var sumaDetalles =
                    recibo.Detalles.Sum(d => d.Monto);

                if (sumaDetalles != recibo.Total)
                {
                    throw new InvalidOperationException(
                        $"El total del recibo {recibo.Numero} " +
                        "no coincide con sus conceptos.");
                }
            }

            // A. TOTAL POR MONEDA

            _totales = filas
                .GroupBy(f => f.Moneda)
                .Select(g => new FilaTotalIngreso
                {
                    Moneda = g.Key,

                    Recibos = g
                        .Select(f => f.NumeroRecibo)
                        .Distinct()
                        .Count(),

                    Total = g.Sum(f => f.Monto)
                })
                .OrderBy(f => f.Moneda)
                .ToList();

            // B. AGRUPACIÃ“N POR CONCEPTO Y MONEDA

            _conceptos = filas
                .GroupBy(f => new
                {
                    f.Concepto,
                    f.Moneda
                })
                .Select(g => new FilaConceptoIngreso
                {
                    Concepto = g.Key.Concepto,

                    Moneda = g.Key.Moneda,

                    CantidadPagos = g
                        .Select(f => f.NumeroRecibo)
                        .Distinct()
                        .Count(),

                    Total = g.Sum(f => f.Monto)
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

            var cantidadRecibos =
                emitidos.Count;

            TxtResumen.Text =
                $"Del {desde:dd/MM/yyyy} al " +
                $"{hasta:dd/MM/yyyy} | " +
                $"Recibos: {cantidadRecibos} | " +
                $"Conceptos cobrados: {filas.Count}";

            TxtTotales.Text = _totales.Count == 0
                ? "Sin recaudación en el período."
                : string.Join(
                    "     |     ",
                    _totales.Select(t =>
                        $"{t.Moneda}: " +
                        t.Total.ToString("N2", _cultura)));

            TxtEstado.Text =
                "Reportes generados correctamente.";
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
                    "Referencia"
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
                    x.Referencia
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
        // como fÃ³rmulas al abrir el documento.

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
}
