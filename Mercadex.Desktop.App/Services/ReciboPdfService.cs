using System;
using System.Collections.Generic;
using System.Linq;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Mercadex.Desktop.App.Dtos;

namespace Mercadex.Desktop.App.Services;

public static class ReciboPdfService
{
    public static void Guardar(
        string ruta,
        ReciboDto recibo,
        PagoDto pago,
        string caseta,
        string bloque,
        string moneda,
        string titular,
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

        using var pdf = new PdfDocument();

        pdf.Info.Title = $"Recibo N.º {recibo.Numero}";
        pdf.Info.Creator = "Mercadex";

        var normal = new XFont("Arial", 11, XFontStyleEx.Regular);
        var pequeno = new XFont("Arial", 9, XFontStyleEx.Regular);
        var negrita = new XFont("Arial", 11, XFontStyleEx.Bold);
        var titulo = new XFont("Arial", 17, XFontStyleEx.Bold);
        var fuenteTotal = new XFont("Arial", 16, XFontStyleEx.Bold);

        var azul = new XSolidBrush(
            XColor.FromArgb(23, 59, 92));

        var rojo = new XSolidBrush(
            XColor.FromArgb(165, 32, 32));

        XGraphics? grafico = null;

        const double margen = 45;
        double ancho = 500;
        double alto = 840;
        double y = 45;

        void NuevaPagina()
        {
            grafico?.Dispose();

            var pagina = pdf.AddPage();
            pagina.Size = PageSize.A4;

            grafico = XGraphics.FromPdfPage(pagina);

            ancho = pagina.Width.Point - margen * 2;
            alto = pagina.Height.Point;
            y = 45;

            grafico.DrawRectangle(
                azul, 0, 0, pagina.Width.Point, 9);

            grafico.DrawString(
                $"Mercadex | Recibo {recibo.Numero} | Página {pdf.PageCount}",
                pequeno,
                XBrushes.Gray,
                new XRect(margen, alto - 40, ancho, 16),
                XStringFormats.TopLeft);
        }

        List<string> AjustarTexto(string texto, XFont fuente)
        {
            var lineas = new List<string>();
            var actual = "";

            var palabras = texto
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var palabra in palabras)
            {
                var candidato = actual.Length == 0
                    ? palabra
                    : actual + " " + palabra;

                if (actual.Length > 0 &&
                    grafico!.MeasureString(candidato, fuente).Width > ancho)
                {
                    lineas.Add(actual);
                    actual = palabra;
                }
                else
                {
                    actual = candidato;
                }
            }

            if (actual.Length > 0)
                lineas.Add(actual);

            return lineas;
        }

        void Linea(
            string texto,
            XFont fuente,
            bool centrada = false,
            XBrush? color = null)
        {
            foreach (var parte in AjustarTexto(texto, fuente))
            {
                if (y + fuente.Size + 9 > alto - 60)
                    NuevaPagina();

                grafico!.DrawString(
                    parte,
                    fuente,
                    color ?? XBrushes.Black,
                    new XRect(
                        margen, y, ancho, fuente.Size + 6),
                    centrada
                        ? XStringFormats.TopCenter
                        : XStringFormats.TopLeft);

                y += fuente.Size + 9;
            }
        }

        void Separador()
        {
            if (y + 18 > alto - 60)
                NuevaPagina();

            y += 6;

            grafico!.DrawLine(
                new XPen(
                    XColor.FromArgb(210, 220, 230), 1),
                margen, y, margen + ancho, y);

            y += 14;
        }

        NuevaPagina();

        Linea(
            "ASOCIACIÓN DE COMERCIANTES 13 DE JULIO",
            titulo, true, azul);

        Linea(
            string.Equals(recibo.Estado, "Anulado", StringComparison.OrdinalIgnoreCase)
                ? "RECIBO ANULADO"
                : "RECIBO DE CAJA",
            negrita, true);

        if (reimpresion)
            Linea(TextoRecibo.Reimpresion, pequeno, true);

        if (TextoRecibo.Anulacion(recibo) is { } anulacion)
            Linea(anulacion, negrita, false, rojo);

        Separador();

        Linea($"Recibo N.º: {recibo.Numero}", negrita);

        Linea(
            $"Fecha: {recibo.FechaEmision.ToLocalTime():dd/MM/yyyy HH:mm}",
            normal);

        Linea($"Caseta/local: {caseta}", normal);

        if (!string.IsNullOrWhiteSpace(bloque))
            Linea($"Bloque: {bloque}", normal);

        Linea(
            $"Titular actual registrado: {titular}",
            normal);

        Linea(
            $"Persona que realizó el pago: {pago.NombrePagador}",
            normal);

        Linea($"Forma de pago: {pago.FormaPago}", normal);

        if (TextoRecibo.Referencia(pago) is { } referencia)
            Linea(referencia, normal);

        Linea($"Moneda: {moneda}", normal);

        if (TextoRecibo.TipoCambio(pago) is { } tipoCambio)
            Linea(tipoCambio, normal);

        Linea(TextoRecibo.TipoIngreso(recibo), normal);
        Linea(TextoRecibo.Cantidad(recibo), normal);

        Separador();

        Linea("CONCEPTOS COBRADOS", negrita, false, azul);

        foreach (var detalle in recibo.Detalles)
        {
            Linea(TextoRecibo.Concepto(detalle, moneda), normal);
        }

        Separador();

        Linea(
            $"TOTAL: {recibo.Total:N2} {moneda}",
            fuenteTotal, false, azul);

        if (TextoRecibo.CobradoPor(recibo) is { } cobradoPor)
            Linea(cobradoPor, normal);

        if (string.Equals(
            recibo.Estado,
            "Anulado",
            StringComparison.OrdinalIgnoreCase))
        {
            Separador();

            Linea(
                "ANULADO - SIN VALIDEZ COMO COMPROBANTE DE PAGO",
                negrita, false, rojo);
        }

        grafico?.Dispose();

        pdf.Save(ruta);
    }
}