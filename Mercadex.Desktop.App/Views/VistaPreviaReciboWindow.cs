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

    private readonly bool _reimpresion;

    public VistaPreviaReciboWindow(
        ReciboDto recibo,
        PagoDto pago,
        string caseta,
        string bloque,
        string moneda,
        bool reimpresion = false)
    {
        _reimpresion = reimpresion;

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

        imprimir.IsEnabled = true;
        guardarPdf.IsEnabled = true;
    }

    /// <summary>Recibo oficial de caja, como el del sistema anterior (ver <see cref="Services.FormatoRecibo"/>).</summary>
    private FlowDocument CrearDocumento()
    {
        var r = Services.FormatoRecibo.Crear(_recibo, _pago, _caseta, _bloque, _moneda, DateTime.Now, _reimpresion);
        var rojo = new SolidColorBrush(Color.FromRgb(165, 32, 32));

        var documento = new FlowDocument
        {
            PageWidth = 816,
            PageHeight = 1056,
            PagePadding = new Thickness(40),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Arial"),
            FontSize = 12,
            Foreground = Brushes.Black,
            Background = Brushes.White
        };

        // Cabecera: asociación a la izquierda, fecha y hora de impresión a la derecha.
        var cabecera = Tabla(documento, 3, 1);
        Fila(cabecera,
            Celda(Parrafo(Negrita(Services.FormatoRecibo.Asociacion))),
            Celda(Parrafo(new Run($"Fecha:  {r.ImpresoFecha}"), new LineBreak(), new Run($"Hora:    {r.ImpresoHora}")), alineacion: TextAlignment.Right, tamano: 10));

        var titulo = Parrafo(new Run(r.Titulo) { FontWeight = FontWeights.Bold, FontSize = 16 });
        titulo.TextAlignment = TextAlignment.Center;
        titulo.Margin = new Thickness(0, 0, 0, 8);
        if (r.Anulado)
            titulo.Foreground = rojo;
        documento.Blocks.Add(titulo);

        if (r.Reimpresion is { } reimp)
            documento.Blocks.Add(new Paragraph(new Run(reimp)) { FontSize = 10, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });

        // Datos del pago.
        var datos = Tabla(documento, 1.4, 1.05, 0.95);
        Fila(datos,
            Celda(Dato("NRO RECIBO:", r.Numero.ToString())),
            Celda(r.Bloque is null ? new Paragraph() : Dato("BLOQUE:", r.Bloque)),
            Celda(Dato("FECHA:", r.Fecha)));
        Fila(datos,
            Celda(Dato("RECIBIMOS DE:", r.RecibimosDe), columnas: 2),
            Celda(Dato("CASETA:", r.Caseta)));
        if (r.FormaPago is not null || r.TipoCambio is not null)
            Fila(datos,
                Celda(r.FormaPago is null ? new Paragraph() : Dato("FORMA DE PAGO:", r.FormaPago), columnas: 2),
                Celda(r.TipoCambio is null ? new Paragraph() : Dato("T/C:", r.TipoCambio)));
        Fila(datos, Celda(Dato("OBSERVACIÓN:", r.Observacion), columnas: 3));

        // Detalle y total en letras.
        var detalle = Tabla(documento, 5, 1);
        detalle.Margin = new Thickness(0, 8, 0, 0);
        Fila(detalle,
            Celda(Parrafo(Negrita("DETALLE")), alineacion: TextAlignment.Center, borde: new Thickness(0, 1, 0, 1)),
            Celda(Parrafo(Negrita("MONTO")), alineacion: TextAlignment.Right, borde: new Thickness(0, 1, 0, 1)));
        foreach (var (texto, monto) in r.Lineas)
            Fila(detalle, Celda(Parrafo(new Run(texto))), Celda(Parrafo(new Run(monto)), alineacion: TextAlignment.Right));
        Fila(detalle,
            Celda(Dato("SON:", r.Son), borde: new Thickness(0, 1, 0, 0)),
            Celda(Parrafo(Negrita(r.Total)), alineacion: TextAlignment.Right, borde: new Thickness(0, 1, 0, 0)));

        if (r.Anulacion is { } anulacion)
        {
            documento.Blocks.Add(new Paragraph(new Run(anulacion)) { FontWeight = FontWeights.Bold, Foreground = rojo, Margin = new Thickness(0, 10, 0, 0) });
            documento.Blocks.Add(new Paragraph(new Run("SIN VALIDEZ COMO COMPROBANTE DE PAGO")) { FontWeight = FontWeights.Bold, Foreground = rojo, Margin = new Thickness(0) });
        }

        // Firmas: quien elaboró y quien entrega conforme.
        var firmas = Tabla(documento, 1, 0.15, 1);
        firmas.Margin = new Thickness(0, 60, 0, 0);
        var emisor = Parrafo(new Run(r.Emisor ?? ""));
        if (r.CargoEmisor is { } cargo)
        {
            emisor.Inlines.Add(new LineBreak());
            emisor.Inlines.Add(new Run(cargo) { FontSize = 10 });
        }
        Fila(firmas,
            Celda(emisor, alineacion: TextAlignment.Center, borde: new Thickness(0, 1, 0, 0)),
            Celda(new Paragraph()),
            Celda(Parrafo(new Run(Services.FormatoRecibo.EntregueConforme)), alineacion: TextAlignment.Center, borde: new Thickness(0, 1, 0, 0)));

        return documento;
    }

    private static Table Tabla(FlowDocument documento, params double[] anchos)
    {
        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0) };
        foreach (var a in anchos)
            tabla.Columns.Add(new TableColumn { Width = new GridLength(a, GridUnitType.Star) });
        tabla.RowGroups.Add(new TableRowGroup());
        documento.Blocks.Add(tabla);
        return tabla;
    }

    private static void Fila(Table tabla, params TableCell[] celdas)
    {
        var fila = new TableRow();
        foreach (var c in celdas)
            fila.Cells.Add(c);
        tabla.RowGroups[0].Rows.Add(fila);
    }

    private static TableCell Celda(
        Paragraph parrafo,
        int columnas = 1,
        TextAlignment alineacion = TextAlignment.Left,
        Thickness? borde = null,
        double? tamano = null)
    {
        parrafo.TextAlignment = alineacion;
        parrafo.Margin = new Thickness(0);
        if (tamano is { } t)
            parrafo.FontSize = t;
        return new TableCell(parrafo)
        {
            ColumnSpan = columnas,
            BorderBrush = Brushes.Black,
            BorderThickness = borde ?? new Thickness(0),
            Padding = new Thickness(2, 3, 2, 3)
        };
    }

    private static Paragraph Parrafo(params Inline[] partes)
    {
        var p = new Paragraph();
        p.Inlines.AddRange(partes);
        return p;
    }

    private static Run Negrita(string texto) => new(texto) { FontWeight = FontWeights.Bold };

    private static Paragraph Dato(string etiqueta, string valor) => Parrafo(Negrita(etiqueta + " "), new Run(valor));

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
                _reimpresion);

            var respuesta = Dialogo.Mostrar(
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
                    Dialogo.Mostrar(
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
            Dialogo.Mostrar(
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
            Dialogo.Mostrar(
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