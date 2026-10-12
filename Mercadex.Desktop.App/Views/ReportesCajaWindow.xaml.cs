using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mercadex.Desktop.App.Views;

public partial class ReportesCajaWindow : Window
{
    // MERCADEX_REPORTES_CAJA_WPF_V1

    private static readonly Guid MonedaBolivianos =
        Guid.Parse("c1922a82-4f86-4210-9a4b-15719a6b3f3a");

    private static readonly TimeSpan HoraBolivia =
        TimeSpan.FromHours(-4);

    private readonly HttpClient _api;

    private ReporteRespuesta? _reporte;
    private bool _ocupado;

    public ReportesCajaWindow(HttpClient api)
    {
        InitializeComponent();

        _api = api;

        FechaDesde.SelectedDate = DateTime.Today;
        FechaHasta.SelectedDate = DateTime.Today;

        Loaded += async (_, _) => await ConsultarAsync();
    }

    private static string EtiquetaMoneda(Guid monedaId)
    {
        return monedaId == MonedaBolivianos
            ? "BOB"
            : "ID " + monedaId.ToString("N")[..8];
    }

    private bool EntornoPermitido()
    {
        var uri = _api.BaseAddress;

        return uri is not null &&
               uri.Scheme == Uri.UriSchemeHttp &&
               uri.Host == "127.0.0.1" &&
               uri.Port == 5189 &&
               Environment.GetEnvironmentVariable(
                   "MERCADEX_TARIFAS_DEV_MODE") == "SI" &&
               _api.DefaultRequestHeaders.Authorization is not null;
    }

    private async void Hoy_Click(
        object sender,
        RoutedEventArgs e)
    {
        FechaDesde.SelectedDate = DateTime.Today;
        FechaHasta.SelectedDate = DateTime.Today;

        await ConsultarAsync();
    }

    private async void Mes_Click(
        object sender,
        RoutedEventArgs e)
    {
        var hoy = DateTime.Today;

        FechaDesde.SelectedDate =
            new DateTime(hoy.Year, hoy.Month, 1);

        FechaHasta.SelectedDate = hoy;

        await ConsultarAsync();
    }

    private async void Consultar_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ConsultarAsync();
    }

    private async Task ConsultarAsync()
    {
        if (_ocupado)
            return;

        if (!EntornoPermitido())
        {
            TxtEstado.Text =
                "Los reportes requieren la API local de Mercadex 1.5.0.";
            return;
        }

        if (
            !FechaDesde.SelectedDate.HasValue ||
            !FechaHasta.SelectedDate.HasValue
        )
        {
            TxtEstado.Text =
                "Selecciona las dos fechas del reporte.";
            return;
        }

        var desde = FechaDesde.SelectedDate.Value.Date;
        var hasta = FechaHasta.SelectedDate.Value.Date;

        if (desde > hasta)
        {
            TxtEstado.Text =
                "La fecha inicial no puede superar la final.";
            return;
        }

        if ((hasta - desde).TotalDays > 366)
        {
            TxtEstado.Text =
                "El rango máximo permitido es de 367 días.";
            return;
        }

        _ocupado = true;
        BtnConsultar.IsEnabled = false;
        BtnImprimir.IsEnabled = false;
        _reporte = null;

        try
        {
            TxtEstado.Text = "Consultando recaudación...";

            var ruta =
                "/api/reportes-caja" +
                $"?desde={desde:yyyy-MM-dd}" +
                $"&hasta={hasta:yyyy-MM-dd}";

            var reporte = await _api
                .GetFromJsonAsync<ReporteRespuesta>(ruta);

            if (reporte is null)
            {
                throw new InvalidOperationException(
                    "No se recibieron los datos del reporte.");
            }

            // Obtener códigos de caseta solamente para mostrar
            // etiquetas legibles. No se modifica información.

            var codigos = new Dictionary<Guid, string>();

            try
            {
                var unidades = await _api
                    .GetFromJsonAsync<List<UnidadConsulta>>(
                        "/api/unidades-comerciales");

                foreach (var unidad in unidades ?? new())
                {
                    if (
                        unidad.Id != Guid.Empty &&
                        !string.IsNullOrWhiteSpace(unidad.Codigo)
                    )
                    {
                        codigos[unidad.Id] = unidad.Codigo;
                    }
                }
            }
            catch
            {
                // El reporte continúa aunque no puedan
                // recuperarse los nombres de las casetas.
            }

            foreach (var cobro in reporte.Cobros)
            {
                cobro.Caseta = codigos.TryGetValue(
                    cobro.UnidadComercialId, out var codigo)
                    ? codigo
                    : cobro.UnidadComercialId.ToString("N")[..8];
            }

            _reporte = reporte;

            TablaDias.ItemsSource = reporte.Dias;
            TablaFormas.ItemsSource = reporte.FormasPago;
            TablaConceptos.ItemsSource = reporte.Conceptos;
            TablaCobros.ItemsSource = reporte.Cobros;
            TablaAnulaciones.ItemsSource = reporte.Anulaciones;

            TxtValidos.Text =
                reporte.CantidadRecibosValidos.ToString(
                    CultureInfo.CurrentCulture);

            TxtAnulaciones.Text =
                reporte.CantidadAnulaciones.ToString(
                    CultureInfo.CurrentCulture);

            if (reporte.Resumen.Count == 0)
            {
                TxtTotal.Text = "0,00 — sin ingresos válidos";
            }
            else
            {
                TxtTotal.Text = string.Join(
                    "  |  ",
                    reporte.Resumen.Select(x =>
                        $"{x.Moneda}: {x.Total:N2}"));
            }

            var advertencias = reporte.Advertencias.Count;

            TxtEstado.Text =
                $"Período: {desde:dd/MM/yyyy} al " +
                $"{hasta:dd/MM/yyyy}. " +
                $"Cobros válidos: {reporte.Cobros.Count}. " +
                $"Advertencias: {advertencias}.";

            BtnImprimir.IsEnabled = true;
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo consultar el reporte: " + ex.Message;

            TablaDias.ItemsSource = null;
            TablaFormas.ItemsSource = null;
            TablaConceptos.ItemsSource = null;
            TablaCobros.ItemsSource = null;
            TablaAnulaciones.ItemsSource = null;
            TxtTotal.Text = "Sin resultados";
            TxtValidos.Text = "—";
            TxtAnulaciones.Text = "—";
        }
        finally
        {
            _ocupado = false;
            BtnConsultar.IsEnabled = true;
        }
    }

    private void Imprimir_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_reporte is null || !EntornoPermitido())
            return;

        var r = _reporte;

        var documento = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            PageWidth = 793,
            PageHeight = 1122,
            PagePadding = new Thickness(50),
            ColumnWidth = 690
        };

        documento.Blocks.Add(new Paragraph(
            new Run("MERCADEX"))
        {
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });

        documento.Blocks.Add(new Paragraph(
            new Run("REPORTE DE CAJA"))
        {
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });

        documento.Blocks.Add(new Paragraph(
            new Run(
                $"Período: {r.Desde} al {r.Hasta}\n" +
                "Zona horaria: Bolivia (UTC-04:00)\n" +
                "Consulta de recaudación — no es un cierre definitivo"))
        {
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18)
        });

        AgregarLinea(
            documento,
            $"Recibos válidos: {r.CantidadRecibosValidos}");

        AgregarLinea(
            documento,
            $"Anulaciones en el período: {r.CantidadAnulaciones}");

        AgregarSeccion(
            documento,
            "TOTAL RECAUDADO POR MONEDA",
            r.Resumen.Select(x =>
                $"{x.Moneda}: {x.Total:N2}  |  " +
                $"Recibos: {x.CantidadRecibos}"),
            "Sin ingresos válidos.");

        AgregarSeccion(
            documento,
            "RECAUDACIÓN DIARIA Y ACUMULADOS",
            r.Dias.Select(x =>
                $"{x.Fecha}  |  {x.Moneda}  |  " +
                $"Recibos: {x.CantidadRecibos}  |  " +
                $"Día: {x.Total:N2}  |  " +
                $"Acumulado: {x.Acumulado:N2}"),
            "Sin recaudación diaria.");

        AgregarSeccion(
            documento,
            "FORMAS DE PAGO",
            r.FormasPago.Select(x =>
                $"{x.FormaPago}  |  {x.Moneda}  |  " +
                $"Cantidad: {x.Cantidad}  |  " +
                $"Total: {x.Total:N2}"),
            "Sin pagos válidos.");

        AgregarSeccion(
            documento,
            "INGRESOS POR CONCEPTO",
            r.Conceptos.Select(x =>
                $"{x.Concepto}  |  {x.Moneda}  |  " +
                $"Cantidad: {x.Cantidad}  |  " +
                $"Total: {x.Total:N2}"),
            "Sin ingresos por concepto.");

        AgregarSeccion(
            documento,
            "DETALLE DE COBROS VÁLIDOS",
            r.Cobros.Select(x =>
                $"Recibo {x.NumeroRecibo}  |  " +
                $"{x.FechaBolivia}  |  " +
                $"Caseta {x.Caseta}  |  " +
                $"{x.FormaPago}  |  " +
                $"{x.Moneda} {x.Monto:N2}  |  " +
                $"{x.Pagador}"),
            "Sin cobros válidos.");

        AgregarSeccion(
            documento,
            "RECIBOS ANULADOS (NO SUMADOS A INGRESOS)",
            r.Anulaciones.Select(x =>
                $"Recibo {x.NumeroRecibo}  |  " +
                $"{x.FechaLocal}  |  " +
                $"{x.Moneda} {x.MontoExcluido:N2}\n" +
                $"Motivo: {x.Motivo}\n" +
                $"Responsable: {x.Responsable}"),
            "Sin anulaciones en el período.");

        if (r.Advertencias.Count > 0)
        {
            AgregarSeccion(
                documento,
                "ADVERTENCIAS DE VALIDACIÓN",
                r.Advertencias,
                "");
        }

        documento.Blocks.Add(new Paragraph(
            new Run(
                "Este reporte excluye los recibos anulados " +
                "de la recaudación válida y no representa " +
                "un cierre definitivo de caja."))
        {
            FontSize = 10,
            Margin = new Thickness(0, 22, 0, 0)
        });

        try
        {
            var dialogo = new PrintDialog();

            if (dialogo.ShowDialog() != true)
                return;

            dialogo.PrintDocument(
                ((IDocumentPaginatorSource)documento)
                    .DocumentPaginator,
                "Mercadex - Reporte de caja");

            TxtEstado.Text =
                "Reporte enviado al sistema de impresión.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo imprimir: " + ex.Message;
        }
    }

    private static void AgregarLinea(
        FlowDocument documento,
        string texto)
    {
        documento.Blocks.Add(new Paragraph(
            new Run(texto))
        {
            Margin = new Thickness(0, 5, 0, 5)
        });
    }

    private static void AgregarSeccion(
        FlowDocument documento,
        string titulo,
        IEnumerable<string> lineas,
        string mensajeVacio)
    {
        documento.Blocks.Add(new Paragraph(
            new Run(titulo))
        {
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 20, 0, 7)
        });

        var items = lineas.ToList();

        if (items.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(mensajeVacio))
                AgregarLinea(documento, mensajeVacio);

            return;
        }

        foreach (var item in items)
        {
            documento.Blocks.Add(new Paragraph(
                new Run(item))
            {
                Margin = new Thickness(0, 2, 0, 5)
            });
        }
    }

    private void Cerrar_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    public sealed class UnidadConsulta
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
    }

    public sealed class ReporteRespuesta
    {
        public string Desde { get; set; } = "";
        public string Hasta { get; set; } = "";
        public int CantidadRecibosValidos { get; set; }
        public int CantidadAnulaciones { get; set; }

        public List<ResumenMoneda> Resumen { get; set; } = new();
        public List<DiaReporte> Dias { get; set; } = new();
        public List<FormaPagoReporte> FormasPago { get; set; } = new();
        public List<ConceptoReporte> Conceptos { get; set; } = new();
        public List<CobroReporte> Cobros { get; set; } = new();
        public List<AnulacionReporte> Anulaciones { get; set; } = new();
        public List<string> Advertencias { get; set; } = new();
    }

    public sealed class ResumenMoneda
    {
        public Guid MonedaId { get; set; }
        public int CantidadRecibos { get; set; }
        public decimal Total { get; set; }

        public string Moneda =>
            EtiquetaMoneda(MonedaId);
    }

    public sealed class DiaReporte
    {
        public string Fecha { get; set; } = "";
        public Guid MonedaId { get; set; }
        public int CantidadRecibos { get; set; }
        public decimal Total { get; set; }
        public decimal Acumulado { get; set; }

        public string Moneda =>
            EtiquetaMoneda(MonedaId);
    }

    public sealed class FormaPagoReporte
    {
        public Guid MonedaId { get; set; }
        public string FormaPago { get; set; } = "";
        public int Cantidad { get; set; }
        public decimal Total { get; set; }

        public string Moneda =>
            EtiquetaMoneda(MonedaId);
    }

    public sealed class ConceptoReporte
    {
        public Guid MonedaId { get; set; }
        public Guid ConceptoCobroId { get; set; }
        public string Concepto { get; set; } = "";
        public int Cantidad { get; set; }
        public decimal Total { get; set; }

        public string Moneda =>
            EtiquetaMoneda(MonedaId);
    }

    public sealed class CobroReporte
    {
        public Guid ReciboId { get; set; }
        public long NumeroRecibo { get; set; }
        public Guid UnidadComercialId { get; set; }
        public Guid MonedaId { get; set; }
        public decimal Monto { get; set; }
        public string FormaPago { get; set; } = "";
        public string Pagador { get; set; } = "";
        public string? Referencia { get; set; }
        public string FechaBolivia { get; set; } = "";
        public string Caseta { get; set; } = "";

        public string Moneda =>
            EtiquetaMoneda(MonedaId);
    }

    public sealed class AnulacionReporte
    {
        public Guid ReciboId { get; set; }
        public long NumeroRecibo { get; set; }
        public DateTimeOffset? FechaAnulacionUtc { get; set; }
        public decimal MontoExcluido { get; set; }
        public Guid? MonedaId { get; set; }
        public string? Motivo { get; set; }
        public string? Responsable { get; set; }

        public string FechaLocal =>
            FechaAnulacionUtc.HasValue
                ? FechaAnulacionUtc.Value
                    .ToOffset(HoraBolivia)
                    .ToString("dd/MM/yyyy HH:mm")
                : "";

        public string Moneda =>
            MonedaId.HasValue
                ? EtiquetaMoneda(MonedaId.Value)
                : "No identificada";
    }
}