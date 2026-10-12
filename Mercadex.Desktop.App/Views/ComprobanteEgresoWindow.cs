using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Vista previa del comprobante de egreso: imprimir o guardar en PDF.
/// </summary>
public sealed class ComprobanteEgresoWindow : Window
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private static readonly Brush Azul = ColoresPanel.Hex("#173B5C");
    private static readonly Brush Gris = ColoresPanel.Hex("#5A6B7D");
    private static readonly Brush Rojo = ColoresPanel.Hex("#B42318");

    private readonly EgresoModel _e;

    public ComprobanteEgresoWindow(EgresoModel egreso)
    {
        _e = egreso;

        Title = $"Vista previa — Comprobante de egreso N.º {egreso.Numero}";
        Width = 760;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ColoresPanel.Hex("#F3F6FA");

        var raiz = new DockPanel { Margin = new Thickness(14) };
        var botones = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(botones, Dock.Top);

        botones.Children.Add(Boton("Imprimir", "#1F4E79", Imprimir));
        botones.Children.Add(Boton("Guardar PDF", "#287A42", GuardarPdf));
        botones.Children.Add(Boton("Cerrar", "#5A6B7D", Close));
        raiz.Children.Add(botones);

        raiz.Children.Add(new FlowDocumentScrollViewer
        {
            Document = CrearDocumento(),
            Background = Brushes.White,
            BorderBrush = ColoresPanel.Hex("#DFE6EE"),
            BorderThickness = new Thickness(1)
        });

        Content = raiz;
    }

    private static Button Boton(string texto, string color, Action accion)
    {
        var b = new Button
        {
            Content = texto,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = ColoresPanel.Hex(color),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        b.Click += (_, _) => accion();
        return b;
    }

    private FlowDocument CrearDocumento()
    {
        var e = _e;
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            PagePadding = new Thickness(48),
            ColumnWidth = double.PositiveInfinity
        };

        doc.Blocks.Add(new Paragraph(new Bold(new Run("ASOCIACIÓN DE COMERCIANTES 13 DE JULIO")))
        { FontSize = 18, Foreground = Azul, TextAlignment = TextAlignment.Center, Margin = new Thickness(0) });

        var encabezado = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 14) };
        encabezado.Columns.Add(new TableColumn());
        encabezado.Columns.Add(new TableColumn());
        var ge = new TableRowGroup();
        var re = new TableRow();
        re.Cells.Add(new TableCell(new Paragraph(new Bold(new Run("COMPROBANTE DE EGRESO"))) { FontSize = 14 }));
        re.Cells.Add(new TableCell(new Paragraph(new Bold(new Run($"N.º {e.Numero:000000}")))
        { FontSize = 14, Foreground = Rojo, TextAlignment = TextAlignment.Right }));
        ge.Rows.Add(re);
        encabezado.RowGroups.Add(ge);
        doc.Blocks.Add(encabezado);

        var datos = new Table { CellSpacing = 0 };
        datos.Columns.Add(new TableColumn { Width = new GridLength(170) });
        datos.Columns.Add(new TableColumn());
        var gd = new TableRowGroup();

        void Par(string etiqueta, string? valor)
        {
            var r = new TableRow();
            r.Cells.Add(new TableCell(new Paragraph(new Run(etiqueta)) { Foreground = Gris, Margin = new Thickness(0, 3, 0, 3) }));
            r.Cells.Add(new TableCell(new Paragraph(new Bold(new Run(string.IsNullOrWhiteSpace(valor) ? "—" : valor)))
            { Margin = new Thickness(0, 3, 0, 3) }));
            gd.Rows.Add(r);
        }

        Par("Fecha", e.Fecha.ToString("dd/MM/yyyy"));
        Par("Pagado a", e.Beneficiario);
        Par("Concepto", e.Concepto);
        Par("Categoría", e.Categoria);
        Par("Forma de pago", e.FormaPago);
        Par("Factura / respaldo", e.NumeroComprobante);
        if (!string.IsNullOrWhiteSpace(e.Observacion))
            Par("Observación", e.Observacion);
        datos.RowGroups.Add(gd);
        doc.Blocks.Add(datos);

        doc.Blocks.Add(new Paragraph(new Bold(new Run($"IMPORTE:  {e.Monto.ToString("N2", Bo)} {e.MonedaCodigo}")))
        {
            FontSize = 18,
            Foreground = Azul,
            Background = ColoresPanel.Hex("#F3F6FA"),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 14, 0, 10),
            TextAlignment = TextAlignment.Right
        });

        if (e.Anulado)
        {
            doc.Blocks.Add(new Paragraph(new Bold(new Run(
                $"ANULADO el {e.FechaAnulacion?.ToLocalTime():dd/MM/yyyy HH:mm}. Motivo: {e.MotivoAnulacion}")))
            { Foreground = Rojo });
        }

        doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 50, 0, 0) });

        var firmas = new Table { CellSpacing = 40 };
        firmas.Columns.Add(new TableColumn());
        firmas.Columns.Add(new TableColumn());
        var gf = new TableRowGroup();
        var rf = new TableRow();
        foreach (var texto in new[] { "Entregado por (Tesorería)", "Recibí conforme\nNombre y C.I.:" })
        {
            rf.Cells.Add(new TableCell(new Paragraph(new Run(texto)) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) })
            { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0) });
        }
        gf.Rows.Add(rf);
        firmas.RowGroups.Add(gf);
        doc.Blocks.Add(firmas);

        doc.Blocks.Add(new Paragraph(new Run(
            $"Registrado el {e.FechaRegistro.ToLocalTime():dd/MM/yyyy HH:mm}"))
        { FontSize = 11, Foreground = Gris, Margin = new Thickness(0, 20, 0, 0) });

        return doc;
    }

    private void Imprimir()
    {
        var dialogo = new PrintDialog();
        if (dialogo.ShowDialog() != true)
            return;

        var doc = CrearDocumento();
        doc.PageWidth = dialogo.PrintableAreaWidth;
        doc.PageHeight = dialogo.PrintableAreaHeight;
        doc.ColumnWidth = dialogo.PrintableAreaWidth;

        dialogo.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,
            $"Egreso {_e.Numero}");
    }

    private void GuardarPdf()
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar comprobante de egreso en PDF",
            Filter = "Documento PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"Egreso_{_e.Numero:000000}.pdf",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            EgresoPdfService.Guardar(dialogo.FileName, _e);

            if (MessageBox.Show(this, "El comprobante se guardó en PDF.\n\n¿Deseas abrirlo ahora?",
                    "Mercadex", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dialogo.FileName,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo guardar el PDF:\n\n" + ex.Message,
                "Mercadex", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
