using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Mercadex.Desktop.App.Views;

public partial class ComprobantesAnulacionWindow : Window
{
    // MERCADEX_COMPROBANTES_ANULACION_WPF_V1

    private readonly HttpClient _api;
    private bool _cargando;
    private int _versionSeleccion;
    private ComprobanteDetalle? _detalleActual;

    public ComprobantesAnulacionWindow(HttpClient api)
    {
        InitializeComponent();

        _api = api;

        Loaded += async (_, _) => await CargarAsync();
    }

    private bool EntornoLocal()
    {
        var uri = _api.BaseAddress;

        return uri is not null &&
               uri.Scheme == Uri.UriSchemeHttp &&
               uri.Host == "127.0.0.1" &&
               uri.Port == 5189 &&
               Environment.GetEnvironmentVariable(
                   "MERCADEX_TARIFAS_DEV_MODE") == "SI";
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

        if (!EntornoLocal())
        {
            TxtEstado.Text =
                "Esta ventana solo funciona con la API local.";
            return;
        }

        _cargando = true;
        _versionSeleccion++;
        _detalleActual = null;
        BtnImprimir.IsEnabled = false;

        try
        {
            TxtEstado.Text = "Consultando comprobantes...";

            var respuesta = await _api
                .GetFromJsonAsync<ListadoRespuesta>(
                    "/api/comprobantes-anulacion");

            if (respuesta is null)
            {
                throw new InvalidOperationException(
                    "La API no devolvio un listado.");
            }

            var items = respuesta.Comprobantes ?? new();

            TablaComprobantes.ItemsSource = items;

            TxtResumen.Text =
                $"Comprobantes registrados: {respuesta.Cantidad} | " +
                $"Mostrados: {respuesta.Mostrados}";

            if (items.Count == 0)
            {
                TxtDetalle.Text =
                    "Todavía no existen comprobantes de anulación.\n\n" +
                    "Cuando se anule un recibo válidamente, " +
                    "su comprobante aparecerá aquí.";

                TxtEstado.Text = "Sin comprobantes registrados.";
            }
            else
            {
                TablaComprobantes.SelectedItem = items.First();
                TxtEstado.Text =
                    "Selecciona un comprobante para consultar e imprimir.";
            }
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo consultar la API: " + ex.Message;
        }
        finally
        {
            _cargando = false;
        }
    }

    private async void Comprobante_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        var version = ++_versionSeleccion;

        _detalleActual = null;
        BtnImprimir.IsEnabled = false;

        if (TablaComprobantes.SelectedItem is not ComprobanteFila fila)
            return;

        try
        {
            TxtDetalle.Text = "Cargando detalle...";

            var detalle = await _api
                .GetFromJsonAsync<ComprobanteDetalle>(
                    $"/api/comprobantes-anulacion/{fila.Id}");

            if (version != _versionSeleccion)
                return;

            if (detalle is null)
            {
                throw new InvalidOperationException(
                    "No se encontro el comprobante.");
            }

            _detalleActual = detalle;

            TxtDetalle.Text = FormatearDetalle(detalle);

            BtnImprimir.IsEnabled = true;
        }
        catch (Exception ex)
        {
            if (version == _versionSeleccion)
            {
                TxtDetalle.Text =
                    "No se pudo consultar el comprobante: " +
                    ex.Message;
            }
        }
    }

    private static string FormatearDetalle(
        ComprobanteDetalle c)
    {
        return
            $"Referencia: {c.Referencia}\n" +
            $"Número de recibo: {c.NumeroRecibo}\n" +
            $"Importe original: {c.MontoTotal:N2}\n" +
            $"Fecha de emisión: " +
                $"{c.FechaEmisionRecibo.ToLocalTime():dd/MM/yyyy HH:mm}\n" +
            $"Fecha de anulación: " +
                $"{c.FechaAnulacionUtc.ToLocalTime():dd/MM/yyyy HH:mm}\n" +
            $"Responsable: {c.Responsable}\n" +
            $"Motivo: {c.Motivo}\n" +
            $"Identificador del recibo: {c.ReciboId}";
    }

    private void Imprimir_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_detalleActual is null)
            return;

        if (!EntornoLocal())
            return;

        var c = _detalleActual;

        var documento = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            PageWidth = 793,
            PageHeight = 1122,
            PagePadding = new Thickness(55),
            ColumnWidth = 680
        };

        var titulo = new Paragraph(
            new Run("MERCADEX"))
        {
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            TextAlignment = TextAlignment.Center
        };

        documento.Blocks.Add(titulo);

        documento.Blocks.Add(new Paragraph(
            new Run("COMPROBANTE DE ANULACIÓN DE RECIBO"))
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });

        documento.Blocks.Add(new Paragraph(
            new Run("Documento de auditoría de caja"))
        {
            FontSize = 11,
            TextAlignment = TextAlignment.Center
        });

        AgregarLinea(documento, "Referencia", c.Referencia);
        AgregarLinea(documento, "Número de recibo", c.NumeroRecibo.ToString());
        AgregarLinea(documento, "Importe del recibo", $"{c.MontoTotal:N2}");
        AgregarLinea(
            documento,
            "Fecha de emisión",
            c.FechaEmisionRecibo.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm"));
        AgregarLinea(
            documento,
            "Fecha de anulación",
            c.FechaAnulacionUtc.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm"));
        AgregarLinea(documento, "Responsable", c.Responsable);

        documento.Blocks.Add(new Paragraph(
            new Run("MOTIVO DE LA ANULACIÓN"))
        {
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 5)
        });

        documento.Blocks.Add(new Paragraph(
            new Run(c.Motivo))
        {
            TextAlignment = TextAlignment.Left
        });

        documento.Blocks.Add(new Paragraph(
            new Run("Recibo ID: " + c.ReciboId))
        {
            FontSize = 9,
            Margin = new Thickness(0, 18, 0, 5)
        });

        documento.Blocks.Add(new Paragraph(
            new Run(
                "Este comprobante deja constancia de la anulación " +
                "registrada en Mercadex. No constituye un nuevo pago."))
        {
            FontSize = 10
        });

        try
        {
            var dialogo = new PrintDialog();

            if (dialogo.ShowDialog() != true)
                return;

            dialogo.PrintDocument(
                ((IDocumentPaginatorSource)documento).DocumentPaginator,
                "Mercadex - Anulación " + c.Referencia);

            TxtEstado.Text =
                "Documento enviado al sistema de impresión.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo imprimir: " + ex.Message;
        }
    }

    private static void AgregarLinea(
        FlowDocument documento,
        string nombre,
        string valor)
    {
        var parrafo = new Paragraph
        {
            Margin = new Thickness(0, 6, 0, 6)
        };

        parrafo.Inlines.Add(new Bold(new Run(nombre + ": ")));
        parrafo.Inlines.Add(new Run(valor));

        documento.Blocks.Add(parrafo);
    }

    private void Cerrar_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    public sealed class ListadoRespuesta
    {
        public int Cantidad { get; set; }
        public int Mostrados { get; set; }

        public List<ComprobanteFila> Comprobantes { get; set; } = new();
    }

    public sealed class ComprobanteFila
    {
        public Guid Id { get; set; }
        public Guid ReciboId { get; set; }
        public long NumeroRecibo { get; set; }
        public decimal MontoTotal { get; set; }
        public string Referencia { get; set; } = "";
        public string Responsable { get; set; } = "";
        public DateTimeOffset FechaAnulacionUtc { get; set; }

        public string FechaLocal =>
            FechaAnulacionUtc.ToLocalTime()
                .ToString("dd/MM/yyyy HH:mm");
    }

    public sealed class ComprobanteDetalle
    {
        public Guid Id { get; set; }
        public Guid ReciboId { get; set; }
        public long NumeroRecibo { get; set; }
        public Guid UnidadComercialId { get; set; }
        public Guid PagoId { get; set; }
        public Guid MonedaId { get; set; }
        public decimal MontoTotal { get; set; }
        public DateTimeOffset FechaEmisionRecibo { get; set; }
        public DateTimeOffset FechaAnulacionUtc { get; set; }
        public string Motivo { get; set; } = "";
        public string Responsable { get; set; } = "";
        public string Referencia { get; set; } = "";
    }
}