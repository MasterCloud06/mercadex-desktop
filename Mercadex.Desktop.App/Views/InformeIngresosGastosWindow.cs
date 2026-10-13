using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>Vista previa del Informe de Ingresos y Gastos.</summary>
public sealed class InformeIngresosGastosWindow : Window
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private static readonly Brush Azul = ColoresPanel.Hex("#173B5C");
    private static readonly Brush Gris = ColoresPanel.Hex("#5A6B7D");

    private readonly InformeIngresosGastos _informe;
    private readonly InformeMoneda _m;

    public InformeIngresosGastosWindow(InformeIngresosGastos informe, InformeMoneda moneda)
    {
        _informe = informe;
        _m = moneda;

        Title = $"Vista previa — Ingresos y Gastos {informe.Desde:dd/MM/yyyy} al {informe.Hasta:dd/MM/yyyy}";
        Width = 820;
        Height = 900;
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

    private string Monto(decimal v) => $"{v.ToString("N2", Bo)} {_m.Moneda}";

    private FlowDocument CrearDocumento()
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            PagePadding = new Thickness(44),
            ColumnWidth = double.PositiveInfinity
        };

        doc.Blocks.Add(new Paragraph(new Bold(new Run("ASOCIACIÓN DE COMERCIANTES 13 DE JULIO")))
        { FontSize = 18, Foreground = Azul, TextAlignment = TextAlignment.Center, Margin = new Thickness(0) });
        doc.Blocks.Add(new Paragraph(new Bold(new Run("INFORME DE INGRESOS Y GASTOS")))
        { FontSize = 14, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 2) });
        doc.Blocks.Add(new Paragraph(new Run($"Del {_informe.Desde:dd/MM/yyyy} al {_informe.Hasta:dd/MM/yyyy}  ·  Moneda: {_m.Moneda}"))
        { Foreground = Gris, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 16) });

        // Resumen
        var resumen = new Table { CellSpacing = 8 };
        for (var i = 0; i < 3; i++) resumen.Columns.Add(new TableColumn());
        var gr = new TableRowGroup();
        var rr = new TableRow();
        foreach (var (et, val, color) in new (string, string, Brush)[]
                 {
                     ("Ingresos", Monto(_m.TotalIngresos), Azul),
                     ("Egresos", Monto(_m.TotalEgresos), Azul),
                     (_m.Saldo >= 0 ? "Saldo (superávit)" : "Saldo (déficit)", Monto(_m.Saldo),
                         _m.Saldo >= 0 ? ColoresPanel.Hex("#006300") : ColoresPanel.Hex("#B42318"))
                 })
        {
            var p = new Paragraph { Margin = new Thickness(10, 8, 10, 8) };
            p.Inlines.Add(new Run(et) { Foreground = Gris });
            p.Inlines.Add(new LineBreak());
            p.Inlines.Add(new Bold(new Run(val)) { FontSize = 15, Foreground = color });
            rr.Cells.Add(new TableCell(p) { Background = ColoresPanel.Hex("#F3F6FA") });
        }
        gr.Rows.Add(rr);
        resumen.RowGroups.Add(gr);
        doc.Blocks.Add(resumen);

        doc.Blocks.Add(Tabla("INGRESOS POR CONCEPTO", "Concepto",
            _m.Ingresos.Select(i => (i.Concepto, i.Cantidad, i.Total)), _m.TotalIngresos));
        doc.Blocks.Add(Tabla("EGRESOS POR CATEGORÍA", "Categoría",
            _m.Egresos.Select(e => (e.Categoria, e.Cantidad, e.Total)), _m.TotalEgresos));

        doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 40, 0, 0) });
        var firmas = new Table { CellSpacing = 40 };
        firmas.Columns.Add(new TableColumn());
        firmas.Columns.Add(new TableColumn());
        var gf = new TableRowGroup();
        var rf = new TableRow();
        foreach (var t in new[] { "Elaborado por (Tesorería)", "Revisado por (Directiva)" })
            rf.Cells.Add(new TableCell(new Paragraph(new Run(t)) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) })
            { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 0) });
        gf.Rows.Add(rf);
        firmas.RowGroups.Add(gf);
        doc.Blocks.Add(firmas);

        doc.Blocks.Add(new Paragraph(new Run(
            $"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}. Ingresos: cobros válidos (sin anulados). Egresos: registrados y no anulados."))
        { FontSize = 10.5, Foreground = Gris, Margin = new Thickness(0, 16, 0, 0) });

        return doc;
    }

    private Block Tabla(string titulo, string columna, IEnumerable<(string Nombre, int Cantidad, decimal Total)> filas, decimal total)
    {
        var seccion = new Section();
        seccion.Blocks.Add(new Paragraph(new Bold(new Run(titulo))) { Foreground = Azul, FontSize = 14, Margin = new Thickness(0, 16, 0, 6) });

        var t = new Table { CellSpacing = 0, BorderBrush = ColoresPanel.Hex("#D2DCE6"), BorderThickness = new Thickness(1) };
        t.Columns.Add(new TableColumn { Width = new GridLength(3, GridUnitType.Star) });
        t.Columns.Add(new TableColumn { Width = new GridLength(0.8, GridUnitType.Star) });
        t.Columns.Add(new TableColumn { Width = new GridLength(1.4, GridUnitType.Star) });
        var g = new TableRowGroup();

        void Fila(string a, string b, string c, Brush? fondo = null, bool blanco = false, bool negrita = false)
        {
            var r = new TableRow { Background = fondo };
            foreach (var (texto, alin) in new[] { (a, TextAlignment.Left), (b, TextAlignment.Right), (c, TextAlignment.Right) })
                r.Cells.Add(new TableCell(new Paragraph(new Run(texto))
                {
                    TextAlignment = alin,
                    Margin = new Thickness(8, 4, 8, 4),
                    Foreground = blanco ? Brushes.White : Brushes.Black,
                    FontWeight = negrita ? FontWeights.Bold : FontWeights.Normal
                })
                { BorderBrush = ColoresPanel.Hex("#E3E9F0"), BorderThickness = new Thickness(0, 0, 0, 1) });
            g.Rows.Add(r);
        }

        Fila(columna, "Cant.", "Total", Azul, true, true);
        var lista = filas.ToList();
        if (lista.Count == 0)
            Fila("Sin movimientos en el período.", "", "");
        foreach (var f in lista)
            Fila(f.Nombre, f.Cantidad.ToString("N0", Bo), Monto(f.Total));
        Fila("TOTAL", "", Monto(total), ColoresPanel.Hex("#F3F6FA"), false, true);

        t.RowGroups.Add(g);
        seccion.Blocks.Add(t);
        return seccion;
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
        dialogo.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Informe de Ingresos y Gastos");
    }

    private void GuardarPdf()
    {
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar informe en PDF",
            Filter = "Documento PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"Ingresos_Gastos_{_informe.Desde:yyyyMMdd}_{_informe.Hasta:yyyyMMdd}_{_m.Moneda}.pdf",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialogo.ShowDialog(this) != true)
            return;

        try
        {
            InformePdfService.Guardar(dialogo.FileName, _informe, _m);

            if (Dialogo.Mostrar(this, "El informe se guardó en PDF.\n\n¿Deseas abrirlo ahora?", "Mercadex",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
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
            Dialogo.Mostrar(this, "No se pudo guardar el PDF:\n\n" + ex.Message, "Mercadex",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
