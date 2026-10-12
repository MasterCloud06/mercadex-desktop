using System.Globalization;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Comprobante de egreso en PDF (media hoja A4, con firmas).
/// </summary>
public static class EgresoPdfService
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    public static void Guardar(string ruta, EgresoModel e)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Comprobante de egreso N.º {e.Numero}";
        pdf.Info.Creator = "Mercadex";

        var pagina = pdf.AddPage();
        pagina.Size = PageSize.A4;
        using var g = XGraphics.FromPdfPage(pagina);

        var azul = new XSolidBrush(XColor.FromArgb(23, 59, 92));
        var gris = new XSolidBrush(XColor.FromArgb(90, 107, 125));
        var rojo = new XSolidBrush(XColor.FromArgb(180, 35, 24));
        var linea = new XPen(XColor.FromArgb(210, 220, 230), 1);

        var titulo = new XFont("Arial", 15, XFontStyleEx.Bold);
        var sub = new XFont("Arial", 12, XFontStyleEx.Bold);
        var normal = new XFont("Arial", 10.5, XFontStyleEx.Regular);
        var negrita = new XFont("Arial", 10.5, XFontStyleEx.Bold);
        var total = new XFont("Arial", 15, XFontStyleEx.Bold);
        var pequena = new XFont("Arial", 8.5, XFontStyleEx.Regular);

        const double m = 50;
        var ancho = pagina.Width.Point - m * 2;
        double y = 36;

        g.DrawRectangle(azul, 0, 0, pagina.Width.Point, 10);
        g.DrawString("ASOCIACIÓN DE COMERCIANTES 13 DE JULIO", titulo, azul,
            new XRect(m, y, ancho, 18), XStringFormats.TopCenter);
        y += 22;
        g.DrawString("COMPROBANTE DE EGRESO", sub, XBrushes.Black,
            new XRect(m, y, ancho, 16), XStringFormats.TopCenter);
        g.DrawString($"N.º {e.Numero:000000}", sub, rojo,
            new XRect(m, y, ancho, 16), XStringFormats.TopRight);
        y += 28;
        g.DrawLine(linea, m, y, m + ancho, y);
        y += 12;

        void Par(string etiqueta, string? valor)
        {
            g.DrawString(etiqueta, normal, gris, new XRect(m, y, 150, 14), XStringFormats.TopLeft);
            var tf = new XTextFormatter(g);
            tf.DrawString(string.IsNullOrWhiteSpace(valor) ? "—" : valor, negrita, XBrushes.Black,
                new XRect(m + 150, y, ancho - 150, 30), XStringFormats.TopLeft);
            y += (valor?.Length ?? 0) > 70 ? 32 : 18;
        }

        Par("Fecha:", e.Fecha.ToString("dd/MM/yyyy"));
        Par("Pagado a:", e.Beneficiario);
        Par("Concepto:", e.Concepto);
        Par("Categoría:", e.Categoria);
        Par("Forma de pago:", e.FormaPago);
        Par("Factura / respaldo:", e.NumeroComprobante);
        if (!string.IsNullOrWhiteSpace(e.Observacion))
            Par("Observación:", e.Observacion);

        y += 8;
        g.DrawRectangle(new XSolidBrush(XColor.FromArgb(243, 246, 250)), m, y, ancho, 34);
        g.DrawString("IMPORTE", negrita, gris, new XRect(m + 12, y + 10, 200, 16), XStringFormats.TopLeft);
        g.DrawString($"{e.Monto.ToString("N2", Bo)} {e.MonedaCodigo}", total, azul,
            new XRect(m, y + 7, ancho - 12, 20), XStringFormats.TopRight);
        y += 46;

        if (e.Anulado)
        {
            g.DrawString(
                $"ANULADO el {e.FechaAnulacion?.ToLocalTime():dd/MM/yyyy HH:mm}. Motivo: {e.MotivoAnulacion}",
                negrita, rojo, new XRect(m, y, ancho, 16), XStringFormats.TopLeft);
            y += 22;
        }

        // Firmas
        y += 60;
        var anchoFirma = (ancho - 60) / 2;
        var pluma = new XPen(XColors.Black, 0.8);
        g.DrawLine(pluma, m, y, m + anchoFirma, y);
        g.DrawLine(pluma, m + anchoFirma + 60, y, m + ancho, y);
        g.DrawString("Entregado por (Tesorería)", normal, XBrushes.Black,
            new XRect(m, y + 6, anchoFirma, 14), XStringFormats.TopCenter);
        g.DrawString("Recibí conforme", normal, XBrushes.Black,
            new XRect(m + anchoFirma + 60, y + 6, anchoFirma, 14), XStringFormats.TopCenter);
        g.DrawString("Nombre y C.I.:", pequena, gris,
            new XRect(m + anchoFirma + 60, y + 22, anchoFirma, 12), XStringFormats.TopLeft);

        y += 52;
        g.DrawString(
            $"Mercadex | Registrado el {e.FechaRegistro.ToLocalTime():dd/MM/yyyy HH:mm} | Impreso el {DateTime.Now:dd/MM/yyyy HH:mm}",
            pequena, XBrushes.Gray, new XRect(m, y, ancho, 12), XStringFormats.TopLeft);

        pdf.Save(ruta);
    }
}
