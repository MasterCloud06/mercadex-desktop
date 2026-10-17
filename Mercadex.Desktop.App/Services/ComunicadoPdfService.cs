using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;

namespace Mercadex.Desktop.App.Services;

/// <summary>Una carta de deuda para un titular (una caseta, comedor o ambulante).</summary>
public sealed record CartaDeuda(
    string Unidad,
    string? Titular,
    string? Ubicacion,
    List<(string Fecha, string Concepto, string Periodo, string Monto)> Detalle,
    string Total);

/// <summary>
/// Documentos para los socios con el membrete de la asociación:
/// comunicados de la directiva y cartas de deuda (una hoja por caseta).
/// </summary>
public static class ComunicadoPdfService
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private const string Asociacion = "ASOCIACIÓN DE COMERCIANTES 13 DE JULIO";
    private const string Lugar = "Feria Barrio Lindo";

    private static readonly XColor Azul = XColor.FromArgb(23, 59, 92);
    private static readonly XColor Amarillo = XColor.FromArgb(242, 183, 5);
    private static readonly XColor Gris = XColor.FromArgb(90, 107, 125);

    // =====================================================================
    // GUARDAR
    // =====================================================================

    public static void GuardarComunicado(Window? dueno, string titulo, string texto, DateTimeOffset fecha)
    {
        Guardar(dueno, $"Comunicado_{fecha:yyyy-MM-dd}", ruta =>
        {
            using var pdf = Nuevo(titulo);
            Comunicado(pdf, titulo, texto, fecha);
            pdf.Save(ruta);
        });
    }

    public static void GuardarCartas(Window? dueno, List<CartaDeuda> cartas, string? fechaLimite, string? nota)
    {
        if (cartas.Count == 0)
        {
            Dialogo.Mostrar(dueno, "No hay casetas con deuda para esos filtros.", "Cartas de deuda", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Guardar(dueno, $"Cartas_de_deuda_{DateTime.Now:yyyy-MM-dd}", ruta =>
        {
            using var pdf = Nuevo("Cartas de deuda");
            foreach (var c in cartas)
                Carta(pdf, c, fechaLimite, nota);
            pdf.Save(ruta);
        });
    }

    private static void Guardar(Window? dueno, string nombre, Action<string> escribir)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar PDF",
            Filter = "Documento PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = nombre + ".pdf",
            AddExtension = true,
            OverwritePrompt = true
        };

        if ((dueno is null ? dialogo.ShowDialog() : dialogo.ShowDialog(dueno)) != true)
            return;

        try
        {
            escribir(dialogo.FileName);
        }
        catch (IOException)
        {
            Dialogo.Mostrar(dueno, "No se pudo guardar: el archivo está abierto en otro programa. Ciérralo e inténtalo de nuevo.",
                "No se guardó", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(dueno, ex.Message, "No se guardó", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            // Se abre para imprimir o revisar.
            Process.Start(new ProcessStartInfo { FileName = dialogo.FileName, UseShellExecute = true });
        }
        catch
        {
            Dialogo.Mostrar(dueno, "Se guardó el PDF en:\n" + dialogo.FileName, "PDF guardado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static PdfDocument Nuevo(string titulo)
    {
        var pdf = new PdfDocument();
        pdf.Info.Title = titulo;
        pdf.Info.Creator = "Mercadex";
        return pdf;
    }

    // =====================================================================
    // HOJA CON MEMBRETE
    // =====================================================================

    private const double Margen = 56;

    /// <summary>Hoja carta vertical con el membrete; devuelve dónde empieza el cuerpo.</summary>
    private static (PdfPage Pagina, XGraphics G, double Y) Hoja(PdfDocument pdf)
    {
        var pagina = pdf.AddPage();
        pagina.Width = XUnit.FromPoint(612);   // Carta
        pagina.Height = XUnit.FromPoint(792);
        var g = XGraphics.FromPdfPage(pagina);
        var ancho = pagina.Width.Point - Margen * 2;

        g.DrawRectangle(new XSolidBrush(Azul), 0, 0, pagina.Width.Point, 10);
        g.DrawRectangle(new XSolidBrush(Amarillo), 0, 10, 120, 4);

        // Sello "M" y nombre de la asociación.
        g.DrawRoundedRectangle(new XSolidBrush(Amarillo), Margen, 36, 34, 34, 8, 8);
        g.DrawString("M", new XFont("Arial", 19, XFontStyleEx.Bold), new XSolidBrush(Azul), new XRect(Margen, 36, 34, 34), XStringFormats.Center);
        g.DrawString(Asociacion, new XFont("Arial", 13, XFontStyleEx.Bold), new XSolidBrush(Azul), new XRect(Margen + 46, 38, ancho - 46, 16), XStringFormats.TopLeft);
        g.DrawString(Lugar, new XFont("Arial", 9.5, XFontStyleEx.Regular), new XSolidBrush(Gris), new XRect(Margen + 46, 56, ancho - 46, 12), XStringFormats.TopLeft);
        g.DrawLine(new XPen(XColor.FromArgb(214, 222, 231), 1), Margen, 84, Margen + ancho, 84);

        return (pagina, g, 100);
    }

    private static string FechaLarga(DateTimeOffset fecha) =>
        fecha.ToOffset(TimeSpan.FromHours(-4)).ToString("d 'de' MMMM 'de' yyyy", Bo);

    private static void Comunicado(PdfDocument pdf, string titulo, string texto, DateTimeOffset fecha)
    {
        var (pagina, g, y) = Hoja(pdf);
        var ancho = pagina.Width.Point - Margen * 2;

        g.DrawString(FechaLarga(fecha), new XFont("Arial", 10, XFontStyleEx.Regular), new XSolidBrush(Gris),
            new XRect(Margen, y, ancho, 12), XStringFormats.TopRight);
        y += 30;

        g.DrawString("COMUNICADO", new XFont("Arial", 18, XFontStyleEx.Bold), new XSolidBrush(Azul), new XRect(Margen, y, ancho, 22), XStringFormats.TopCenter);
        y += 28;

        var fTitulo = new XFont("Arial", 13, XFontStyleEx.Bold);
        var tf = new XTextFormatter(g) { Alignment = XParagraphAlignment.Center };
        var altoTitulo = Math.Max(18, Math.Ceiling(g.MeasureString(titulo, fTitulo).Width / ancho) * 17);
        tf.DrawString(titulo, fTitulo, XBrushes.Black, new XRect(Margen, y, ancho, altoTitulo), XStringFormats.TopLeft);
        y += altoTitulo + 18;

        y = Parrafos(pdf, ref pagina, ref g, texto, y, ancho, new XFont("Arial", 11.5, XFontStyleEx.Regular));

        // Firma.
        if (y + 110 > pagina.Height.Point - 60)
        {
            g.Dispose();
            (pagina, g, y) = Hoja(pdf);
        }

        y += 70;
        var anchoFirma = 200.0;
        var xFirma = Margen + (ancho - anchoFirma) / 2;
        g.DrawLine(new XPen(XColors.Black, 0.8), xFirma, y, xFirma + anchoFirma, y);
        g.DrawString("LA DIRECTIVA", new XFont("Arial", 10.5, XFontStyleEx.Bold), XBrushes.Black, new XRect(xFirma, y + 5, anchoFirma, 14), XStringFormats.TopCenter);

        g.Dispose();
    }

    /// <summary>Escribe el texto por párrafos y pasa de hoja cuando hace falta.</summary>
    private static double Parrafos(PdfDocument pdf, ref PdfPage pagina, ref XGraphics g, string texto, double y, double ancho, XFont fuente)
    {
        var alto = fuente.GetHeight() * 1.25;
        var limite = pagina.Height.Point - 70;

        foreach (var parrafo in texto.Replace("\r\n", "\n").Split('\n'))
        {
            var lineas = Lineas(g, parrafo, fuente, ancho);
            if (lineas.Count == 0)
            {
                y += alto * 0.7;
                continue;
            }

            foreach (var linea in lineas)
            {
                if (y + alto > limite)
                {
                    g.Dispose();
                    (pagina, g, y) = Hoja(pdf);
                    limite = pagina.Height.Point - 70;
                }

                g.DrawString(linea, fuente, XBrushes.Black, new XRect(Margen, y, ancho, alto), XStringFormats.TopLeft);
                y += alto;
            }

            y += alto * 0.45;
        }

        return y;
    }

    private static List<string> Lineas(XGraphics g, string parrafo, XFont fuente, double ancho)
    {
        var lineas = new List<string>();
        var actual = "";
        foreach (var palabra in parrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var prueba = actual.Length == 0 ? palabra : actual + " " + palabra;
            if (g.MeasureString(prueba, fuente).Width <= ancho || actual.Length == 0)
            {
                actual = prueba;
            }
            else
            {
                lineas.Add(actual);
                actual = palabra;
            }
        }

        if (actual.Length > 0)
            lineas.Add(actual);
        return lineas;
    }

    // =====================================================================
    // CARTA DE DEUDA
    // =====================================================================

    private static void Carta(PdfDocument pdf, CartaDeuda c, string? fechaLimite, string? nota)
    {
        var (pagina, g, y) = Hoja(pdf);
        var ancho = pagina.Width.Point - Margen * 2;
        var fNormal = new XFont("Arial", 11, XFontStyleEx.Regular);
        var fNegrita = new XFont("Arial", 11, XFontStyleEx.Bold);
        var fTabla = new XFont("Arial", 9.5, XFontStyleEx.Regular);
        var fTablaNegrita = new XFont("Arial", 9.5, XFontStyleEx.Bold);

        g.DrawString(FechaLarga(DateTimeOffset.Now), new XFont("Arial", 10, XFontStyleEx.Regular), new XSolidBrush(Gris),
            new XRect(Margen, y, ancho, 12), XStringFormats.TopRight);
        y += 26;

        g.DrawString("Señor(a):", fNormal, XBrushes.Black, new XRect(Margen, y, ancho, 14), XStringFormats.TopLeft);
        y += 15;
        g.DrawString(string.IsNullOrWhiteSpace(c.Titular) ? "Titular de la unidad" : c.Titular!, fNegrita, XBrushes.Black, new XRect(Margen, y, ancho, 14), XStringFormats.TopLeft);
        y += 15;
        g.DrawString(c.Unidad + (string.IsNullOrWhiteSpace(c.Ubicacion) ? "" : " · " + c.Ubicacion), fNormal, XBrushes.Black, new XRect(Margen, y, ancho, 14), XStringFormats.TopLeft);
        y += 15;
        g.DrawString("Presente.-", fNormal, XBrushes.Black, new XRect(Margen, y, ancho, 14), XStringFormats.TopLeft);
        y += 30;

        g.DrawString("Ref.: AVISO DE DEUDA PENDIENTE", fNegrita, new XSolidBrush(Azul), new XRect(Margen, y, ancho, 14), XStringFormats.TopRight);
        y += 28;

        var cuerpo =
            $"Por medio de la presente, la directiva de la asociación le hace conocer que su {c.Unidad.ToLower(Bo)} " +
            $"tiene las siguientes deudas pendientes de pago, por un total de {c.Total}:";
        y = Parrafos(pdf, ref pagina, ref g, cuerpo, y, ancho, fNormal);
        y += 4;

        // Tabla del detalle.
        double[] w = { 80, ancho - 80 - 80 - 100, 80, 100 };
        string[] titulos = { "Fecha", "Concepto", "Período", "Monto" };
        void Fila(string[] celdas, XFont fuente, bool cabecera)
        {
            if (cabecera)
                g.DrawRectangle(new XSolidBrush(Azul), Margen, y, ancho, 18);
            var x = Margen;
            for (var i = 0; i < 4; i++)
            {
                g.DrawString(celdas[i], fuente, cabecera ? XBrushes.White : XBrushes.Black,
                    new XRect(x + 5, y + 4, w[i] - 10, 14), i == 3 ? XStringFormats.TopRight : XStringFormats.TopLeft);
                x += w[i];
            }
            if (!cabecera)
                g.DrawLine(new XPen(XColor.FromArgb(214, 222, 231), 0.6), Margen, y + 18, Margen + ancho, y + 18);
            y += 18;
        }

        Fila(titulos, fTablaNegrita, cabecera: true);
        foreach (var d in c.Detalle)
        {
            if (y + 20 > pagina.Height.Point - 180)
            {
                g.Dispose();
                (pagina, g, y) = Hoja(pdf);
                Fila(titulos, fTablaNegrita, cabecera: true);
            }
            Fila(new[] { d.Fecha, d.Concepto, d.Periodo, d.Monto }, fTabla, cabecera: false);
        }

        g.DrawString("TOTAL: " + c.Total, new XFont("Arial", 11.5, XFontStyleEx.Bold), new XSolidBrush(Azul),
            new XRect(Margen, y + 6, ancho - 5, 16), XStringFormats.TopRight);
        y += 34;

        var pedido = string.IsNullOrWhiteSpace(fechaLimite)
            ? "Le pedimos regularizar su deuda a la brevedad posible en la caja de la asociación o con el pago QR de la app Mercadex."
            : $"Le pedimos regularizar su deuda hasta el {fechaLimite} en la caja de la asociación o con el pago QR de la app Mercadex.";
        y = Parrafos(pdf, ref pagina, ref g, pedido, y, ancho, fNormal);

        if (!string.IsNullOrWhiteSpace(nota))
            y = Parrafos(pdf, ref pagina, ref g, nota!, y, ancho, fNormal);

        y = Parrafos(pdf, ref pagina, ref g, "Si ya realizó el pago, le pedimos no tomar en cuenta este aviso.", y, ancho,
            new XFont("Arial", 9.5, XFontStyleEx.Italic));

        // Firmas.
        if (y + 100 > pagina.Height.Point - 60)
        {
            g.Dispose();
            (pagina, g, y) = Hoja(pdf);
        }

        y += 60;
        var anchoFirma = 180.0;
        foreach (var (texto, x) in new[] { ("PRESIDENTE(A)", Margen + 10), ("TESORERO(A)", Margen + ancho - anchoFirma - 10) })
        {
            g.DrawLine(new XPen(XColors.Black, 0.8), x, y, x + anchoFirma, y);
            g.DrawString(texto, new XFont("Arial", 10, XFontStyleEx.Bold), XBrushes.Black, new XRect(x, y + 5, anchoFirma, 14), XStringFormats.TopCenter);
        }

        g.Dispose();
    }
}
