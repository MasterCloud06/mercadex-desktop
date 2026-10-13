using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

using Mercadex.Desktop.App.Dtos;

namespace Mercadex.Desktop.App.Views;

public sealed class VistaPreviaReciboWindow : Window
{
    private readonly ReciboDto _recibo;
    private readonly PagoDto _pago;
    private readonly string _caseta;
    private readonly string _bloque;
    private readonly string _moneda;
    private string _titular = "Consultando titular...";

    public VistaPreviaReciboWindow(
        ReciboDto recibo,
        PagoDto pago,
        string caseta,
        string bloque,
        string moneda)
    {
        ArgumentNullException.ThrowIfNull(recibo);
        ArgumentNullException.ThrowIfNull(pago);

        if (recibo.Id == Guid.Empty ||
            recibo.PagoId != pago.Id)
        {
            throw new InvalidOperationException(
                "El recibo no corresponde al pago recibido.");
        }

        if (recibo.Detalles == null ||
            recibo.Detalles.Count == 0)
        {
            throw new InvalidOperationException(
                "El recibo no contiene conceptos.");
        }

        if (recibo.Detalles.Sum(x => x.Monto) != recibo.Total)
        {
            throw new InvalidOperationException(
                "El total del recibo no coincide con sus conceptos.");
        }

        _recibo = recibo;
        _pago = pago;
        _caseta = caseta;
        _bloque = bloque;
        _moneda = moneda;

        Title = $"Vista previa — Recibo N.º {recibo.Numero}";

        Width = Math.Min(900, SystemParameters.WorkArea.Width);
        Height = Math.Min(780, SystemParameters.WorkArea.Height);

        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;

        var contenedor = new DockPanel
        {
            Margin = new Thickness(12)
        };

        var botones = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var imprimir = new Button
        {
            Content = "Imprimir recibo",
            Padding = new Thickness(18, 9, 18, 9),
            Margin = new Thickness(0, 0, 12, 0)
        };

        imprimir.Click += Imprimir_Click;
        imprimir.IsEnabled = false;

        var cerrar = new Button
        {
            Content = "Cerrar",
            Padding = new Thickness(18, 9, 18, 9),
            IsCancel = true
        };

        cerrar.Click += (_, _) => Close();

        botones.Children.Add(imprimir);
        var guardarPdf = new Button
        {
            Content = "Guardar PDF",
            Padding = new Thickness(18, 9, 18, 9),
            Margin = new Thickness(0, 0, 12, 0),
            Background = new SolidColorBrush(
                Color.FromRgb(40, 122, 66)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold
        };

        guardarPdf.Click += GuardarPdf_Click;
        guardarPdf.IsEnabled = false;

        botones.Children.Add(guardarPdf);
        botones.Children.Add(cerrar);

        DockPanel.SetDock(botones, Dock.Top);
        contenedor.Children.Add(botones);

        var visor = new FlowDocumentReader
        {
            Document = CrearDocumento(),
            ViewingMode = FlowDocumentReaderViewingMode.Scroll,
            Zoom = 125,
            MinZoom = 70,
            MaxZoom = 300,
            IsPrintEnabled = false,
            IsFindEnabled = true
        };

        contenedor.Children.Add(visor);

        Content = contenedor;

        Loaded += async (_, _) =>
        {
            try
            {
                var servicio =
                    new Mercadex.Desktop.App.Services.TitularCasetaService();

                _titular = await servicio.ObtenerAsync(
                    _recibo.UnidadComercialId);
            }
            catch (Exception)
            {
                _titular = "No disponible: error de consulta";
            }
            finally
            {
                visor.Document = CrearDocumento();

                imprimir.IsEnabled = true;
                guardarPdf.IsEnabled = true;
            }
        };
    }

    private FlowDocument CrearDocumento()
    {
        var documento = new FlowDocument
        {
            PageWidth = 793.7,
            PageHeight = 1122.5,
            PagePadding = new Thickness(45),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Arial"),
            FontSize = 12,
            Foreground = Brushes.Black,
            Background = Brushes.White
        };

        AgregarLinea(
            documento,
            "ASOCIACIÓN DE COMERCIANTES 13 DE JULIO",
            negrita: true,
            tamano: 18,
            centrado: true);

        var anulado = string.Equals(
            _recibo.Estado,
            "Anulado",
            StringComparison.OrdinalIgnoreCase);

        AgregarLinea(
            documento,
            anulado ? "RECIBO ANULADO" : "RECIBO DE CAJA",
            negrita: true,
            tamano: 16,
            centrado: true);

        AgregarLinea(
            documento,
            $"Recibo N.º: {_recibo.Numero}");

        AgregarLinea(
            documento,
            $"Fecha: {_recibo.FechaEmision.ToLocalTime():dd/MM/yyyy HH:mm}");

        AgregarLinea(documento, $"Caseta/local: {_caseta}");

        if (!string.IsNullOrWhiteSpace(_bloque))
        {
            AgregarLinea(documento, $"Bloque: {_bloque}");
        }

        AgregarLinea(
            documento,
            $"Titular actual registrado: {_titular}");

        AgregarLinea(
            documento,
            $"Persona que realizó el pago: {_pago.NombrePagador}");

        AgregarLinea(
            documento,
            $"Forma de pago: {_pago.FormaPago}");

        if (Services.TextoRecibo.Referencia(_pago) is { } referencia)
            AgregarLinea(documento, referencia);

        AgregarLinea(documento, $"Moneda: {_moneda}");

        if (Services.TextoRecibo.TipoCambio(_pago) is { } tipoCambio)
            AgregarLinea(documento, tipoCambio);

        AgregarLinea(documento, Services.TextoRecibo.TipoIngreso(_recibo));
        AgregarLinea(documento, Services.TextoRecibo.Cantidad(_recibo));

        AgregarLinea(
            documento,
            "CONCEPTOS COBRADOS",
            negrita: true);

        foreach (var detalle in _recibo.Detalles)
        {
            AgregarLinea(
                documento,
                Services.TextoRecibo.Concepto(detalle, _moneda));
        }

        AgregarLinea(
            documento,
            $"TOTAL: {_recibo.Total:N2} {_moneda}",
            negrita: true,
            tamano: 18);

        if (Services.TextoRecibo.CobradoPor(_recibo) is { } cobradoPor)
            AgregarLinea(documento, cobradoPor);

        if (anulado)
        {
            AgregarLinea(
                documento,
                "ANULADO — SIN VALIDEZ COMO COMPROBANTE DE PAGO",
                negrita: true);
        }

        return documento;
    }

    private static void AgregarLinea(
        FlowDocument documento,
        string texto,
        bool negrita = false,
        double tamano = 12,
        bool centrado = false)
    {
        documento.Blocks.Add(
            new Paragraph(new Run(texto))
            {
                FontWeight = negrita
                    ? FontWeights.Bold
                    : FontWeights.Normal,

                FontSize = tamano,

                TextAlignment = centrado
                    ? TextAlignment.Center
                    : TextAlignment.Left,

                Margin = new Thickness(0, 5, 0, 5)
            });
    }

    private void GuardarPdf_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var dialogo = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Guardar recibo en PDF",
                Filter = "Documento PDF (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = $"Recibo_{_recibo.Numero}.pdf",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (dialogo.ShowDialog(this) != true)
                return;

            Mercadex.Desktop.App.Services.ReciboPdfService.Guardar(
                dialogo.FileName,
                _recibo,
                _pago,
                _caseta,
                _bloque,
                _moneda,
                _titular);

            var respuesta = MessageBox.Show(
                this,
                "El recibo PDF se guardó correctamente.\n\n" +
                "¿Deseas abrirlo ahora?",
                "Mercadex - Recibo PDF",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (respuesta == MessageBoxResult.Yes)
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dialogo.FileName,
                            UseShellExecute = true
                        });
                }
                catch (Exception exAbrir)
                {
                    MessageBox.Show(
                        this,
                        "El PDF está guardado, pero no se pudo abrir.\n\n" +
                        exAbrir.Message,
                        "Aviso",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "No se pudo guardar el PDF.\n\n" +
                "Si el cobro ya fue registrado, no lo repitas.\n\n" +
                ex.Message,
                "Error al generar PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    private void Imprimir_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var dialogo = new PrintDialog();

            if (dialogo.ShowDialog() != true)
                return;

            // Se crea otra copia para conservar la vista previa.
            var documento = CrearDocumento();

            documento.PageWidth = dialogo.PrintableAreaWidth;
            documento.PageHeight = dialogo.PrintableAreaHeight;

            dialogo.PrintDocument(
                ((IDocumentPaginatorSource)documento)
                    .DocumentPaginator,
                $"Recibo {_recibo.Numero}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "El cobro ya está registrado. No vuelva a cobrar.\n\n" +
                "No se pudo imprimir el recibo. " +
                "Puede intentar imprimir nuevamente.\n\n" +
                ex.Message,
                "Error de impresión",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}