using System;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Mercadex.Desktop.App.Dtos;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// PDF del recibo oficial de caja en media carta, como el del sistema anterior
/// (ver <see cref="FormatoRecibo"/>). Con muchos conceptos la hoja se alarga.
/// </summary>
public static class ReciboPdfService
{
    public static void Guardar(
        string ruta,
        ReciboDto recibo,
        PagoDto pago,
        string caseta,
        string bloque,
        string moneda,
        bool reimpresion = false)
    {
        ArgumentNullException.ThrowIfNull(recibo);
        ArgumentNullException.ThrowIfNull(pago);

        if (recibo.Id == Guid.Empty ||
            recibo.PagoId != pago.Id ||
            recibo.Detalles is null ||
            recibo.Detalles.Count == 0 ||
            recibo.Detalles.Sum(x => x.Monto) != recibo.Total)
        {
            throw new InvalidOperationException(
                "Los datos del recibo no son válidos.");
        }

        Escribir(ruta, FormatoRecibo.Crear(recibo, pago, caseta, bloque, moneda, DateTime.Now, reimpresion));
    }

    public static void Escribir(string ruta, ReciboImpreso r)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Recibo N.º {r.Numero}";
        pdf.Info.Creator = "Mercadex";

        var chica = new XFont("Arial", 8, XFontStyleEx.Regular);
        var normal = new XFont("Arial", 9, XFontStyleEx.Regular);
        var negrita = new XFont("Arial", 9, XFontStyleEx.Bold);
        var titulo = new XFont("Arial", 12, XFontStyleEx.Bold);
        var rojo = new XSolidBrush(XColor.FromArgb(165, 32, 32));
        var linea = new XPen(XColors.Black, 0.8);

        const double margen = 28;
        const double paso = 12;
        var extra = (r.FormaPago is null && r.TipoCambio is null ? 0 : paso) +
                    (r.Reimpresion is null ? 0 : paso) + (r.Anulacion is null ? 0 : paso * 2);

        // Media carta horizontal (21,6 × 14 cm); crece si hay muchos conceptos.
        var pagina = pdf.AddPage();
        pagina.Width = XUnit.FromPoint(612);
        pagina.Height = XUnit.FromPoint(Math.Max(396, 250 + extra + r.Lineas.Count * paso));

        using var g = XGraphics.FromPdfPage(pagina);
        var ancho = pagina.Width.Point - margen * 2;
        var der = margen + ancho;
        var y = 24.0;

        void Texto(string t, XFont f, double x, double w, XStringFormat fmt, XBrush? color = null) =>
            g.DrawString(t, f, color ?? XBrushes.Black, new XRect(x, y, w, paso), fmt);

        void Par(string etiqueta, string valor, double x, double w)
        {
            Texto(etiqueta, negrita, x, w, XStringFormats.TopLeft);
            var e = g.MeasureString(etiqueta + " ", negrita).Width;
            Texto(Recortar(valor, normal, w - e), normal, x + e, w - e, XStringFormats.TopLeft);
        }

        string Recortar(string t, XFont f, double w)
        {
            if (g.MeasureString(t, f).Width <= w)
                return t;
            while (t.Length > 1 && g.MeasureString(t + "…", f).Width > w)
                t = t[..^1];
            return t + "…";
        }

        // Cabecera: asociación a la izquierda, fecha y hora de impresión a la derecha.
        Texto(FormatoRecibo.Asociacion, negrita, margen, 230, XStringFormats.TopLeft);
        Texto($"Fecha:  {r.ImpresoFecha}", chica, der - 110, 110, XStringFormats.TopLeft);
        y += paso;
        Texto($"Hora:   {r.ImpresoHora}", chica, der - 110, 110, XStringFormats.TopLeft);
        y += paso + 2;
        Texto(r.Titulo, titulo, margen, ancho, XStringFormats.TopCenter, r.Anulado ? rojo : null);
        y += paso + 6;

        if (r.Reimpresion is { } reimp)
        {
            Texto(reimp, chica, margen, ancho, XStringFormats.TopCenter);
            y += paso;
        }

        // Datos del pago.
        var col2 = margen + ancho * 0.42;
        var col3 = margen + ancho * 0.74;
        Par("NRO RECIBO:", r.Numero.ToString(), margen, col2 - margen);
        if (r.Bloque is { } bloque)
            Par("BLOQUE:", bloque, col2, col3 - col2);
        Par("FECHA:", r.Fecha, col3, der - col3);
        y += paso + 2;

        Par("RECIBIMOS DE:", r.RecibimosDe, margen, col3 - margen - 8);
        Par("CASETA:", r.Caseta, col3, der - col3);
        y += paso + 2;

        if (r.FormaPago is not null || r.TipoCambio is not null)
        {
            if (r.FormaPago is { } forma)
                Par("FORMA DE PAGO:", forma, margen, col3 - margen - 8);
            if (r.TipoCambio is { } tc)
                Par("T/C:", tc, col3, der - col3);
            y += paso + 2;
        }

        Par("OBSERVACIÓN:", r.Observacion, margen, ancho);
        y += paso + 4;

        // Detalle.
        g.DrawLine(linea, margen, y, der, y);
        y += 2;
        Texto("DETALLE", negrita, margen, ancho - 90, XStringFormats.TopCenter);
        Texto("MONTO", negrita, der - 90, 90, XStringFormats.TopRight);
        y += paso;
        g.DrawLine(linea, margen, y, der, y);
        y += 3;

        foreach (var (detalle, monto) in r.Lineas)
        {
            Texto(Recortar(detalle, normal, ancho - 100), normal, margen, ancho - 100, XStringFormats.TopLeft);
            Texto(monto, normal, der - 90, 90, XStringFormats.TopRight);
            y += paso;
        }

        y += 4;
        g.DrawLine(linea, margen, y, der, y);
        y += 5;
        Par("SON:", r.Son, margen, ancho - 100);
        Texto(r.Total, negrita, der - 90, 90, XStringFormats.TopRight);
        y += paso;

        if (r.Anulacion is { } anulacion)
        {
            y += 4;
            Texto(Recortar(anulacion, negrita, ancho), negrita, margen, ancho, XStringFormats.TopLeft, rojo);
            y += paso;
            Texto("SIN VALIDEZ COMO COMPROBANTE DE PAGO", negrita, margen, ancho, XStringFormats.TopLeft, rojo);
            y += paso;
        }

        // Firmas: quien elaboró y quien entrega conforme.
        y = Math.Max(y + 50, pagina.Height.Point - 70);
        var firma = ancho * 0.45;
        g.DrawLine(linea, margen, y, margen + firma, y);
        g.DrawLine(linea, der - firma, y, der, y);
        y += 4;
        Texto(r.Emisor ?? "", normal, margen, firma, XStringFormats.TopCenter);
        Texto(FormatoRecibo.EntregueConforme, normal, der - firma, firma, XStringFormats.TopCenter);
        if (r.CargoEmisor is { } cargo)
        {
            y += paso;
            Texto(cargo, chica, margen, firma, XStringFormats.TopCenter);
        }

        pdf.Save(ruta);
    }
}
