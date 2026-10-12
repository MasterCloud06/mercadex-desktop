using System.Globalization;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Acta de arqueo y cierre de caja en PDF (A4, una página).
/// </summary>
public static class ArqueoPdfService
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    public static void Guardar(string ruta, ActaArqueo acta)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Arqueo de caja {acta.Fecha:dd/MM/yyyy} {acta.Moneda}";
        pdf.Info.Creator = "Mercadex";

        var pagina = pdf.AddPage();
        pagina.Size = PageSize.A4;

        using var g = XGraphics.FromPdfPage(pagina);

        var azul = new XSolidBrush(XColor.FromArgb(23, 59, 92));
        var gris = new XSolidBrush(XColor.FromArgb(90, 107, 125));
        var rojo = new XSolidBrush(XColor.FromArgb(180, 35, 24));
        var verde = new XSolidBrush(XColor.FromArgb(0, 99, 0));
        var fondoTabla = new XSolidBrush(XColor.FromArgb(243, 246, 250));
        var linea = new XPen(XColor.FromArgb(210, 220, 230), 1);

        var titulo = new XFont("Arial", 16, XFontStyleEx.Bold);
        var subtitulo = new XFont("Arial", 12, XFontStyleEx.Bold);
        var normal = new XFont("Arial", 10.5, XFontStyleEx.Regular);
        var negrita = new XFont("Arial", 10.5, XFontStyleEx.Bold);
        var grande = new XFont("Arial", 14, XFontStyleEx.Bold);
        var pequena = new XFont("Arial", 8.5, XFontStyleEx.Regular);

        const double m = 50;
        var ancho = pagina.Width.Point - m * 2;
        double y = 40;

        g.DrawRectangle(azul, 0, 0, pagina.Width.Point, 10);

        g.DrawString("ASOCIACIÓN DE COMERCIANTES 13 DE JULIO", titulo, azul,
            new XRect(m, y, ancho, 20), XStringFormats.TopCenter);
        y += 24;
        g.DrawString("ACTA DE ARQUEO Y CIERRE DE CAJA", subtitulo, XBrushes.Black,
            new XRect(m, y, ancho, 16), XStringFormats.TopCenter);
        y += 30;

        var estado = acta.Cerrado
            ? $"CERRADA el {acta.FechaCierre?.ToLocalTime():dd/MM/yyyy HH:mm}"
            : "PRELIMINAR (sin cierre registrado)";

        void Par(string etiqueta, string valor, XBrush? color = null)
        {
            g.DrawString(etiqueta, normal, gris, new XRect(m, y, 170, 14), XStringFormats.TopLeft);
            g.DrawString(valor, negrita, color ?? XBrushes.Black, new XRect(m + 170, y, ancho - 170, 14), XStringFormats.TopLeft);
            y += 18;
        }

        Par("Fecha del arqueo:", acta.Fecha.ToString("dddd, dd 'de' MMMM 'de' yyyy", Bo));
        Par("Moneda:", acta.Moneda);
        Par("Estado:", estado, acta.Cerrado ? verde : rojo);
        y += 10;

        // Tabla de subtotales
        var columnas = new[] { ancho * 0.46, ancho * 0.22, ancho * 0.32 };
        double alturaFila = 22;

        void Fila(string a, string b, string c, bool encabezado = false, bool total = false)
        {
            if (encabezado)
                g.DrawRectangle(azul, m, y, ancho, alturaFila);
            else if (total)
                g.DrawRectangle(fondoTabla, m, y, ancho, alturaFila);

            var f = encabezado || total ? negrita : normal;
            var color = encabezado ? XBrushes.White : XBrushes.Black;
            var x = m + 8;
            g.DrawString(a, f, color, new XRect(x, y + 5, columnas[0] - 16, 14), XStringFormats.TopLeft);
            x += columnas[0];
            g.DrawString(b, f, color, new XRect(x, y + 5, columnas[1] - 16, 14), XStringFormats.TopRight);
            x += columnas[1];
            g.DrawString(c, f, color, new XRect(x, y + 5, columnas[2] - 16, 14), XStringFormats.TopRight);
            y += alturaFila;
            if (!encabezado)
                g.DrawLine(linea, m, y, m + ancho, y);
        }

        string Monto(decimal v) => $"{v.ToString("N2", Bo)} {acta.Moneda}";

        Fila("Forma de pago", "Pagos", "Importe", encabezado: true);
        Fila("Efectivo", acta.CantidadEfectivo.ToString("N0", Bo), Monto(acta.Efectivo));
        Fila("QR", acta.CantidadQr.ToString("N0", Bo), Monto(acta.Qr));
        Fila("Otros", acta.CantidadOtros.ToString("N0", Bo), Monto(acta.Otros));
        Fila("TOTAL RECAUDADO", acta.CantidadPagos.ToString("N0", Bo), Monto(acta.Total), total: true);

        y += 20;
        g.DrawString("VERIFICACIÓN DEL EFECTIVO", subtitulo, azul, new XRect(m, y, ancho, 16), XStringFormats.TopLeft);
        y += 24;

        Par("Efectivo registrado:", Monto(acta.Efectivo));
        Par("Efectivo contado:", acta.EfectivoContado.HasValue ? Monto(acta.EfectivoContado.Value) : "—");

        if (acta.Diferencia.HasValue)
        {
            var d = acta.Diferencia.Value;
            var texto = d == 0 ? "Sin diferencia" : (d > 0 ? "Sobrante " : "Faltante ") + Monto(Math.Abs(d));
            g.DrawString("Diferencia:", normal, gris, new XRect(m, y, 170, 14), XStringFormats.TopLeft);
            g.DrawString(texto, grande, d == 0 ? verde : rojo, new XRect(m + 170, y - 2, ancho - 170, 18), XStringFormats.TopLeft);
            y += 24;
        }

        Par("Responsable del cierre:", string.IsNullOrWhiteSpace(acta.Responsable) ? "—" : acta.Responsable);

        if (!string.IsNullOrWhiteSpace(acta.Observacion))
        {
            g.DrawString("Observación:", normal, gris, new XRect(m, y, 170, 14), XStringFormats.TopLeft);
            y += 16;
            var tf = new PdfSharp.Drawing.Layout.XTextFormatter(g);
            var rect = new XRect(m, y, ancho, 60);
            tf.DrawString(acta.Observacion, normal, XBrushes.Black, rect, XStringFormats.TopLeft);
            y += 64;
        }

        y += 12;
        g.DrawString(
            "Los pagos QR y otros medios deben conciliarse con el extracto bancario y el libro mayor.",
            pequena, gris, new XRect(m, y, ancho, 12), XStringFormats.TopLeft);

        // Firmas
        y = Math.Max(y + 60, pagina.Height.Point - 170);
        var anchoFirma = (ancho - 60) / 2;
        g.DrawLine(new XPen(XColors.Black, 0.8), m, y, m + anchoFirma, y);
        g.DrawLine(new XPen(XColors.Black, 0.8), m + anchoFirma + 60, y, m + ancho, y);
        g.DrawString("Cajero / Responsable", normal, XBrushes.Black,
            new XRect(m, y + 6, anchoFirma, 14), XStringFormats.TopCenter);
        g.DrawString("Revisado por (Tesorería)", normal, XBrushes.Black,
            new XRect(m + anchoFirma + 60, y + 6, anchoFirma, 14), XStringFormats.TopCenter);

        g.DrawString(
            $"Mercadex | Generado el {DateTime.Now:dd/MM/yyyy HH:mm}",
            pequena, XBrushes.Gray,
            new XRect(m, pagina.Height.Point - 40, ancho, 12), XStringFormats.TopLeft);

        pdf.Save(ruta);
    }
}
