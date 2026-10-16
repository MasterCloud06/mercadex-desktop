using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Vista previa del acta de arqueo con opciones de imprimir y guardar en PDF.
/// </summary>
public sealed class ActaArqueoWindow : Window
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private static readonly Brush Azul = ColoresPanel.Hex("#173B5C");
    private static readonly Brush Gris = ColoresPanel.Hex("#5A6B7D");
    private static readonly Brush Rojo = ColoresPanel.Hex("#B42318");
    private static readonly Brush Verde = ColoresPanel.Hex("#006300");

    private readonly ActaArqueo _acta;

    public ActaArqueoWindow(ActaArqueo acta)
    {
        _acta = acta;

        Title = $"Vista previa — Arqueo {acta.Fecha:dd/MM/yyyy} {acta.Moneda}";
        Width = 760;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ColoresPanel.Hex("#F3F6FA");

        var raiz = new DockPanel { Margin = new Thickness(14) };

        var botones = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(botones, Dock.Top);

        var imprimir = Boton("Imprimir", "#1F4E79");
        imprimir.Click += (_, _) => Imprimir();
        var pdf = Boton("Guardar PDF", "#287A42");
        pdf.Click += (_, _) => GuardarPdf();
        var cerrar = Boton("Cerrar", "#5A6B7D");
        cerrar.Click += (_, _) => Close();

        botones.Children.Add(imprimir);
        botones.Children.Add(pdf);
        botones.Children.Add(cerrar);
        raiz.Children.Add(botones);

        var visor = new FlowDocumentScrollViewer
        {
            Document = CrearDocumento(),
            Background = Brushes.White,
            BorderBrush = ColoresPanel.Hex("#DFE6EE"),
            BorderThickness = new Thickness(1)
        };
        raiz.Children.Add(visor);

        Content = raiz;
    }

    private static Button Boton(string texto, string color) => new()
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

    private string Monto(decimal v) => $"{v.ToString("N2", Bo)} {_acta.Moneda}";

    private FlowDocument CrearDocumento()
    {
        var a = _acta;

        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            PagePadding = new Thickness(48),
            ColumnWidth = double.PositiveInfinity
        };

        doc.Blocks.Add(new Paragraph(new Bold(new Run("ASOCIACIÓN DE COMERCIANTES 13 DE JULIO")))
        {
            FontSize = 18, Foreground = Azul, TextAlignment = TextAlignment.Center, Margin = new Thickness(0)
        });
        doc.Blocks.Add(new Paragraph(new Bold(new Run("ACTA DE ARQUEO Y CIERRE DE CAJA")))
        {
            FontSize = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 18)
        });

        var estado = a.Cerrado
            ? $"CERRADA el {a.FechaCierre?.ToLocalTime():dd/MM/yyyy HH:mm}"
            : "PRELIMINAR (sin cierre registrado)";

        doc.Blocks.Add(Datos(new[]
        {
            ("Fecha del arqueo", a.Fecha.ToString("dddd, dd 'de' MMMM 'de' yyyy", Bo), (Brush?)null),
            ("Moneda", a.Moneda, null),
            ("Caja", string.IsNullOrWhiteSpace(a.Caja) ? "General (todo el día)" : a.Caja!, null),
            ("Estado", estado, a.Cerrado ? Verde : Rojo)
        }));

        var tabla = new Table { CellSpacing = 0, Margin = new Thickness(0, 14, 0, 14), BorderBrush = ColoresPanel.Hex("#D2DCE6"), BorderThickness = new Thickness(1) };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(1.4, GridUnitType.Star) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(2, GridUnitType.Star) });
        var grupo = new TableRowGroup();
        tabla.RowGroups.Add(grupo);

        void Fila(string c1, string c2, string c3, bool encabezado = false, bool total = false)
        {
            var fila = new TableRow();
            if (encabezado) fila.Background = Azul;
            else if (total) fila.Background = ColoresPanel.Hex("#F3F6FA");

            foreach (var (texto, alin) in new[] { (c1, TextAlignment.Left), (c2, TextAlignment.Right), (c3, TextAlignment.Right) })
            {
                var p = new Paragraph(new Run(texto))
                {
                    TextAlignment = alin,
                    Margin = new Thickness(8, 5, 8, 5),
                    Foreground = encabezado ? Brushes.White : Brushes.Black,
                    FontWeight = encabezado || total ? FontWeights.Bold : FontWeights.Normal
                };
                fila.Cells.Add(new TableCell(p) { BorderBrush = ColoresPanel.Hex("#E3E9F0"), BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            grupo.Rows.Add(fila);
        }

        Fila("Forma de pago", "Pagos", "Importe", encabezado: true);
        Fila("Efectivo", a.CantidadEfectivo.ToString("N0", Bo), Monto(a.Efectivo));
        Fila("QR", a.CantidadQr.ToString("N0", Bo), Monto(a.Qr));
        Fila("Otros", a.CantidadOtros.ToString("N0", Bo), Monto(a.Otros));
        Fila("TOTAL RECAUDADO", a.CantidadPagos.ToString("N0", Bo), Monto(a.Total), total: true);
        doc.Blocks.Add(tabla);

        doc.Blocks.Add(new Paragraph(new Bold(new Run("VERIFICACIÓN DEL EFECTIVO")))
        {
            Foreground = Azul, FontSize = 14, Margin = new Thickness(0, 6, 0, 6)
        });

        var filas = new List<(string, string, Brush?)>
        {
            ("Cobrado en efectivo", Monto(a.Efectivo), null),
            ("Gastos pagados en efectivo (−)",
                a.EgresosEfectivo > 0 ? $"{Monto(a.EgresosEfectivo)} ({a.CantidadEgresos})" : Monto(0), null),
            ("Efectivo que debe haber", Monto(a.Efectivo - a.EgresosEfectivo), null),
            ("Efectivo contado", a.EfectivoContado.HasValue ? Monto(a.EfectivoContado.Value) : "—", null)
        };

        if (a.Diferencia.HasValue)
        {
            var d = a.Diferencia.Value;
            filas.Add(("Diferencia",
                d == 0 ? "Sin diferencia" : (d > 0 ? "Sobrante " : "Faltante ") + Monto(Math.Abs(d)),
                d == 0 ? Verde : Rojo));
        }

        filas.Add(("Responsable del cierre", string.IsNullOrWhiteSpace(a.Responsable) ? "—" : a.Responsable!, null));

        if (!string.IsNullOrWhiteSpace(a.Observacion))
            filas.Add(("Observación", a.Observacion!, null));

        doc.Blocks.Add(Datos(filas));

        doc.Blocks.Add(new Paragraph(new Run(
            "Los pagos QR y otros medios deben conciliarse con el extracto bancario y el libro mayor."))
        {
            FontSize = 11, Foreground = Gris, Margin = new Thickness(0, 10, 0, 60)
        });

        // Firmas
        var firmas = new Table { CellSpacing = 40 };
        firmas.Columns.Add(new TableColumn());
        firmas.Columns.Add(new TableColumn());
        var gf = new TableRowGroup();
        var rf = new TableRow();
        foreach (var texto in new[] { "Cajero / Responsable", "Revisado por (Tesorería)" })
        {
            rf.Cells.Add(new TableCell(new Paragraph(new Run(texto)) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) })
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0, 1, 0, 0)
            });
        }
        gf.Rows.Add(rf);
        firmas.RowGroups.Add(gf);
        doc.Blocks.Add(firmas);

        return doc;
    }

    private static Table Datos(IEnumerable<(string Etiqueta, string Valor, Brush? Color)> datos)
    {
        var t = new Table { CellSpacing = 0, Margin = new Thickness(0) };
        t.Columns.Add(new TableColumn { Width = new GridLength(190) });
        t.Columns.Add(new TableColumn());
        var g = new TableRowGroup();
        foreach (var (etiqueta, valor, color) in datos)
        {
            var r = new TableRow();
            r.Cells.Add(new TableCell(new Paragraph(new Run(etiqueta)) { Foreground = Gris, Margin = new Thickness(0, 2, 0, 2) }));
            r.Cells.Add(new TableCell(new Paragraph(new Bold(new Run(valor))) { Foreground = color ?? Brushes.Black, Margin = new Thickness(0, 2, 0, 2) }));
            g.Rows.Add(r);
        }
        t.RowGroups.Add(g);
        return t;
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

        dialogo.PrintDocument(
            ((IDocumentPaginatorSource)doc).DocumentPaginator,
            $"Arqueo {_acta.Fecha:dd-MM-yyyy} {_acta.Moneda}");
    }

    private void GuardarPdf()
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar acta de arqueo en PDF",
            Filter = "Documento PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"Arqueo_{_acta.Fecha:yyyy-MM-dd}_{_acta.Moneda}.pdf",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            ArqueoPdfService.Guardar(dialogo.FileName, _acta);

            if (Dialogo.Mostrar(this, "El acta se guardó en PDF.\n\n¿Deseas abrirla ahora?",
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
            Dialogo.Mostrar(this, "No se pudo guardar el PDF:\n\n" + ex.Message,
                "Mercadex", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
