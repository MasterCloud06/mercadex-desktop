using System.Globalization;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Informe de Ingresos y Gastos en PDF (A4, varias páginas si hace falta).
/// </summary>
public static class InformePdfService
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    public static void Guardar(string ruta, InformeIngresosGastos informe, InformeMoneda m)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Informe de Ingresos y Gastos {informe.Desde:dd/MM/yyyy}-{informe.Hasta:dd/MM/yyyy}";
        pdf.Info.Creator = "Mercadex";

        var azul = new XSolidBrush(XColor.FromArgb(23, 59, 92));
        var gris = new XSolidBrush(XColor.FromArgb(90, 107, 125));
        var fondo = new XSolidBrush(XColor.FromArgb(243, 246, 250));
        var rojo = new XSolidBrush(XColor.FromArgb(180, 35, 24));
        var verde = new XSolidBrush(XColor.FromArgb(0, 99, 0));
        var linea = new XPen(XColor.FromArgb(220, 228, 236), 0.8);

        var titulo = new XFont("Arial", 15, XFontStyleEx.Bold);
        var sub = new XFont("Arial", 12, XFontStyleEx.Bold);
        var normal = new XFont("Arial", 10, XFontStyleEx.Regular);
        var negrita = new XFont("Arial", 10, XFontStyleEx.Bold);
        var pequena = new XFont("Arial", 8.5, XFontStyleEx.Regular);

        const double margen = 48;
        XGraphics g = null!;
        PdfPage pagina = null!;
        double ancho = 0, y = 0;

        void NuevaPagina()
        {
            g?.Dispose();
            pagina = pdf.AddPage();
            pagina.Size = PageSize.A4;
            g = XGraphics.FromPdfPage(pagina);
            ancho = pagina.Width.Point - margen * 2;
            y = 40;
            g.DrawRectangle(azul, 0, 0, pagina.Width.Point, 9);
            g.DrawString($"Mercadex | Informe de Ingresos y Gastos | Página {pdf.PageCount}", pequena, XBrushes.Gray,
                new XRect(margen, pagina.Height.Point - 36, ancho, 12), XStringFormats.TopLeft);
        }

        void Espacio(double alto)
        {
            if (y + alto > pagina.Height.Point - 60)
                NuevaPagina();
        }

        string Monto(decimal v) => $"{v.ToString("N2", Bo)} {m.Moneda}";

        void Tabla(string tituloTabla, string columna, IEnumerable<(string Nombre, int Cantidad, decimal Total)> filas, decimal total)
        {
            Espacio(60);
            g.DrawString(tituloTabla, sub, azul, new XRect(margen, y, ancho, 16), XStringFormats.TopLeft);
            y += 22;

            var c1 = ancho * 0.6; var c2 = ancho * 0.15; var c3 = ancho * 0.25;
            g.DrawRectangle(azul, margen, y, ancho, 20);
            g.DrawString(columna, negrita, XBrushes.White, new XRect(margen + 8, y + 4, c1, 14), XStringFormats.TopLeft);
            g.DrawString("Cant.", negrita, XBrushes.White, new XRect(margen + c1, y + 4, c2 - 8, 14), XStringFormats.TopRight);
            g.DrawString("Total", negrita, XBrushes.White, new XRect(margen + c1 + c2, y + 4, c3 - 8, 14), XStringFormats.TopRight);
            y += 20;

            var lista = filas.ToList();
            if (lista.Count == 0)
            {
                g.DrawString("Sin movimientos en el período.", normal, gris, new XRect(margen + 8, y + 4, ancho, 14), XStringFormats.TopLeft);
                y += 22;
            }

            foreach (var f in lista)
            {
                Espacio(20);
                g.DrawString(f.Nombre, normal, XBrushes.Black, new XRect(margen + 8, y + 4, c1 - 12, 14), XStringFormats.TopLeft);
                g.DrawString(f.Cantidad.ToString("N0", Bo), normal, XBrushes.Black, new XRect(margen + c1, y + 4, c2 - 8, 14), XStringFormats.TopRight);
                g.DrawString(Monto(f.Total), normal, XBrushes.Black, new XRect(margen + c1 + c2, y + 4, c3 - 8, 14), XStringFormats.TopRight);
                y += 20;
                g.DrawLine(linea, margen, y, margen + ancho, y);
            }

            Espacio(22);
            g.DrawRectangle(fondo, margen, y, ancho, 22);
            g.DrawString("TOTAL", negrita, XBrushes.Black, new XRect(margen + 8, y + 5, c1, 14), XStringFormats.TopLeft);
            g.DrawString(Monto(total), negrita, XBrushes.Black, new XRect(margen + c1 + c2, y + 5, c3 - 8, 14), XStringFormats.TopRight);
            y += 34;
        }

        NuevaPagina();

        g.DrawString("ASOCIACIÓN DE COMERCIANTES 13 DE JULIO", titulo, azul, new XRect(margen, y, ancho, 18), XStringFormats.TopCenter);
        y += 22;
        g.DrawString("INFORME DE INGRESOS Y GASTOS", sub, XBrushes.Black, new XRect(margen, y, ancho, 16), XStringFormats.TopCenter);
        y += 20;
        g.DrawString($"Del {informe.Desde:dd/MM/yyyy} al {informe.Hasta:dd/MM/yyyy}  ·  Moneda: {m.Moneda}", normal, gris,
            new XRect(margen, y, ancho, 14), XStringFormats.TopCenter);
        y += 28;

        // Resumen
        var anchoCaja = (ancho - 20) / 3;
        void Caja(double x, string etiqueta, string valor, XBrush color)
        {
            g.DrawRectangle(fondo, x, y, anchoCaja, 50);
            g.DrawString(etiqueta, normal, gris, new XRect(x + 10, y + 8, anchoCaja - 20, 14), XStringFormats.TopLeft);
            g.DrawString(valor, sub, color, new XRect(x + 10, y + 26, anchoCaja - 20, 16), XStringFormats.TopLeft);
        }
        Caja(margen, "Ingresos", Monto(m.TotalIngresos), azul);
        Caja(margen + anchoCaja + 10, "Egresos", Monto(m.TotalEgresos), azul);
        Caja(margen + (anchoCaja + 10) * 2, m.Saldo >= 0 ? "Saldo (superávit)" : "Saldo (déficit)", Monto(m.Saldo), m.Saldo >= 0 ? verde : rojo);
        y += 66;

        Tabla("INGRESOS POR CONCEPTO", "Concepto",
            m.Ingresos.Select(i => (i.Concepto, i.Cantidad, i.Total)), m.TotalIngresos);

        Tabla("EGRESOS POR CATEGORÍA", "Categoría",
            m.Egresos.Select(e => (e.Categoria, e.Cantidad, e.Total)), m.TotalEgresos);

        Espacio(110);
        y += 50;
        var anchoFirma = (ancho - 60) / 2;
        var pluma = new XPen(XColors.Black, 0.8);
        g.DrawLine(pluma, margen, y, margen + anchoFirma, y);
        g.DrawLine(pluma, margen + anchoFirma + 60, y, margen + ancho, y);
        g.DrawString("Elaborado por (Tesorería)", normal, XBrushes.Black, new XRect(margen, y + 6, anchoFirma, 14), XStringFormats.TopCenter);
        g.DrawString("Revisado por (Directiva)", normal, XBrushes.Black, new XRect(margen + anchoFirma + 60, y + 6, anchoFirma, 14), XStringFormats.TopCenter);
        y += 30;
        g.DrawString($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}. Ingresos: cobros válidos (sin anulados). Egresos: registrados y no anulados.",
            pequena, gris, new XRect(margen, y, ancho, 12), XStringFormats.TopLeft);

        g.Dispose();
        pdf.Save(ruta);
    }
}
