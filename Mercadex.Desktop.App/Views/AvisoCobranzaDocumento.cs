using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Hoja impresa del «Aviso de cobranza», como la del sistema anterior:
/// asociación con fecha y hora, título, local/bloque/socio, moneda, los
/// servicios adeudados con su monto, el total y la invitación a pasar por oficina.
/// </summary>
public static class AvisoCobranzaDocumento
{
    public static FlowDocument Crear(AvisoImpreso a)
    {
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

        // Cabecera: asociación a la izquierda, fecha y hora a la derecha, título al centro.
        var cabecera = Tabla(documento, 1.3, 1.4, 0.8);
        Fila(cabecera,
            Celda(Parrafo(Negrita("ASOCIACIÓN DE COMERCIANTES"), new LineBreak(), Negrita("13 DE JULIO"))),
            Celda(Parrafo(new Run(AvisoCobranza.Titulo) { FontWeight = FontWeights.Bold, FontSize = 15 }), TextAlignment.Center, vertical: true),
            Celda(Parrafo(new Run($"Fecha:  {a.ImpresoFecha}"), new LineBreak(), new Run($"Hora:    {a.ImpresoHora}")), TextAlignment.Right, tamano: 11));

        // Local, bloque y socio.
        var datos = Tabla(documento, 1, 1.6);
        datos.Margin = new Thickness(0, 10, 0, 0);
        Fila(datos,
            Celda(Dato("LOCAL:", a.Local), borde: new Thickness(0, 1, 0, 0)),
            Celda(a.Bloque is null ? new Paragraph() : Dato("BLOQUE:", a.Bloque), borde: new Thickness(0, 1, 0, 0)));
        Fila(datos, Celda(Dato("SOCIO:", a.Socio), columnas: 2));

        if (a.Secciones.Count == 0)
        {
            documento.Blocks.Add(new Paragraph(new Run("NO TIENE DEUDAS PENDIENTES.")) { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 0) });
            return documento;
        }

        foreach (var s in a.Secciones)
        {
            var moneda = Tabla(documento, 1);
            moneda.Margin = new Thickness(0, 4, 0, 0);
            Fila(moneda, Celda(Dato("MONEDA:", s.Moneda), borde: new Thickness(0, 1, 0, 1)));

            var detalle = Tabla(documento, 5, 1);
            detalle.Margin = new Thickness(0, 6, 0, 0);
            Fila(detalle,
                Celda(Parrafo(Negrita("SERVICIO(S) ADEUDADO(S)")), TextAlignment.Center, borde: new Thickness(0, 1, 0, 1)),
                Celda(Parrafo(Negrita("MONTO")), TextAlignment.Right, borde: new Thickness(0, 1, 0, 1)));
            foreach (var (servicio, monto) in s.Lineas)
                Fila(detalle, Celda(Parrafo(new Run(servicio))), Celda(Parrafo(new Run(monto)), TextAlignment.Right));
            Fila(detalle,
                Celda(Parrafo(Negrita("TOTAL")), borde: new Thickness(0, 1, 0, 1)),
                Celda(Parrafo(Negrita(s.Total)), TextAlignment.Right, borde: new Thickness(0, 1, 0, 1)));
        }

        documento.Blocks.Add(new Paragraph(new Run(AvisoCobranza.Pie)) { FontStyle = FontStyles.Italic, Margin = new Thickness(0, 14, 0, 0) });
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
        TextAlignment alineacion = TextAlignment.Left,
        Thickness? borde = null,
        int columnas = 1,
        double? tamano = null,
        bool vertical = false)
    {
        parrafo.TextAlignment = alineacion;
        parrafo.Margin = new Thickness(0, vertical ? 8 : 0, 0, 0);
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

    private static Paragraph Dato(string etiqueta, string valor) => Parrafo(Negrita(etiqueta + "  "), new Run(valor));
}
