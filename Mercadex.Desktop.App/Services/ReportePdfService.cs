using System.Diagnostics;
using System.Globalization;
using System.Windows;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Services;

/// <summary>Una columna del reporte: título, ancho relativo y si va alineada a la derecha (importes).</summary>
public sealed record ColumnaReporte(string Titulo, double Peso, bool Derecha = false, bool Numero = false);

/// <summary>
/// Una fila del reporte. Grupo = fila de título de grupo (sombreada, en negrita);
/// Total = fila de totales (en negrita, con línea arriba).
/// </summary>
public sealed record FilaReporte(string[] Celdas, bool Grupo = false, bool Total = false);

/// <summary>Contenido completo de un reporte listo para PDF o Excel.</summary>
public sealed class Reporte
{
    public string Titulo { get; init; } = "";
    public string Filtros { get; init; } = "";
    public List<string> Resumen { get; init; } = new();
    public List<ColumnaReporte> Columnas { get; init; } = new();
    public List<FilaReporte> Filas { get; init; } = new();
    public string? Nota { get; init; }
    public bool Horizontal { get; init; }
    public string NombreArchivo { get; init; } = "Reporte";
}

/// <summary>
/// PDF y Excel de los reportes (A4, con encabezado de la asociación,
/// filtros usados, quién lo generó y número de página).
/// </summary>
public static class ReportePdfService
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private const string Asociacion = "Asociación de Comerciantes 13 de Julio";

    // =====================================================================
    // GUARDAR CON DIÁLOGO
    // =====================================================================

    public static void GuardarPdf(Window? dueno, Reporte r) =>
        Guardar(dueno, r, "Documento PDF (*.pdf)|*.pdf", ".pdf", ruta => EscribirPdf(ruta, r));

    public static void GuardarExcel(Window? dueno, Reporte r) =>
        Guardar(dueno, r, "Libro de Excel (*.xlsx)|*.xlsx", ".xlsx", ruta => EscribirExcel(ruta, r));

    private static void Guardar(Window? dueno, Reporte r, string filtro, string extension, Action<string> escribir)
    {
        if (r.Filas.Count == 0)
        {
            Dialogo.Mostrar(dueno, "No hay datos para guardar. Genera primero el reporte.", "Reporte vacío",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialogo = new SaveFileDialog
        {
            Title = "Guardar " + r.Titulo,
            Filter = filtro,
            DefaultExt = extension,
            FileName = Limpiar(r.NombreArchivo) + extension,
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

        if (Dialogo.Mostrar(dueno, "Se guardó el reporte.\n\n¿Abrirlo ahora?", r.Titulo,
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = dialogo.FileName, UseShellExecute = true });
            }
            catch
            {
                // Sin programa asociado: el archivo quedó guardado igual.
            }
        }
    }

    private static string Limpiar(string nombre)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            nombre = nombre.Replace(c, '_');
        return nombre.Replace(' ', '_');
    }

    // =====================================================================
    // EXCEL
    // =====================================================================

    public static void EscribirExcel(string ruta, Reporte r)
    {
        var numericas = r.Columnas.Select(c => c.Numero).ToArray();
        var anchos = r.Columnas.Select(c => Math.Clamp(c.Peso * 12, 8, 60)).ToArray();

        // Los importes van como número (sin separador de miles) para poder sumarlos.
        var filas = r.Filas.Select(f => f.Celdas.Select((v, i) =>
            i < numericas.Length && numericas[i] && decimal.TryParse(v, NumberStyles.Number, Bo, out var d)
                ? d.ToString(CultureInfo.InvariantCulture)
                : v).ToArray());

        var hoja = r.Titulo.Length > 31 ? r.Titulo[..31] : r.Titulo;
        PlanillaExcel.EscribirXlsx(ruta, hoja, r.Columnas.Select(c => c.Titulo).ToArray(), filas, anchos, numericas);
    }

    // =====================================================================
    // PDF
    // =====================================================================

    public static void EscribirPdf(string ruta, Reporte r)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = r.Titulo;
        pdf.Info.Creator = "Mercadex";

        var azul = new XSolidBrush(XColor.FromArgb(23, 59, 92));
        var amarillo = new XSolidBrush(XColor.FromArgb(242, 183, 5));
        var gris = new XSolidBrush(XColor.FromArgb(90, 107, 125));
        var fondo = new XSolidBrush(XColor.FromArgb(238, 243, 248));
        var linea = new XPen(XColor.FromArgb(214, 222, 231), 0.6);
        var lineaFuerte = new XPen(XColor.FromArgb(23, 59, 92), 0.9);

        var fTitulo = new XFont("Arial", 15, XFontStyleEx.Bold);
        var fSub = new XFont("Arial", 9, XFontStyleEx.Regular);
        var fCabeza = new XFont("Arial", 8.5, XFontStyleEx.Bold);
        var fNormal = new XFont("Arial", 8.5, XFontStyleEx.Regular);
        var fNegrita = new XFont("Arial", 8.5, XFontStyleEx.Bold);
        var fPie = new XFont("Arial", 7.5, XFontStyleEx.Regular);

        const double margen = 36;
        const double altoFila = 15;
        XGraphics g = null!;
        PdfPage pagina = null!;
        double ancho = 0, y = 0;

        var generado = $"Generado el {DateTime.Now.ToString("dd/MM/yyyy HH:mm", Bo)}" +
                       (SesionActual.Iniciada ? $" por {SesionActual.Nombre}" : "");

        var pesoTotal = r.Columnas.Sum(c => c.Peso);
        double[] xs = Array.Empty<double>();
        double[] ws = Array.Empty<double>();

        void Medidas()
        {
            ws = r.Columnas.Select(c => ancho * c.Peso / pesoTotal).ToArray();
            xs = new double[ws.Length];
            var x = margen;
            for (var i = 0; i < ws.Length; i++) { xs[i] = x; x += ws[i]; }
        }

        string Ajustar(string texto, XFont fuente, double max)
        {
            if (g.MeasureString(texto, fuente).Width <= max)
                return texto;
            while (texto.Length > 1 && g.MeasureString(texto + "…", fuente).Width > max)
                texto = texto[..^1];
            return texto + "…";
        }

        void Celdas(string[] celdas, XFont fuente)
        {
            for (var i = 0; i < r.Columnas.Count && i < celdas.Length; i++)
            {
                var texto = Ajustar(celdas[i] ?? "", fuente, ws[i] - 8);
                var rect = new XRect(xs[i] + 4, y + 3, ws[i] - 8, altoFila - 3);
                g.DrawString(texto, fuente, XBrushes.Black, rect,
                    r.Columnas[i].Derecha ? XStringFormats.TopRight : XStringFormats.TopLeft);
            }
        }

        void Cabecera()
        {
            g.DrawRectangle(azul, margen, y, ancho, altoFila + 2);
            for (var i = 0; i < r.Columnas.Count; i++)
            {
                var rect = new XRect(xs[i] + 4, y + 4, ws[i] - 8, altoFila - 2);
                g.DrawString(Ajustar(r.Columnas[i].Titulo, fCabeza, ws[i] - 8), fCabeza, XBrushes.White, rect,
                    r.Columnas[i].Derecha ? XStringFormats.TopRight : XStringFormats.TopLeft);
            }
            y += altoFila + 2;
        }

        void NuevaPagina(bool primera)
        {
            g?.Dispose();
            pagina = pdf.AddPage();
            // A4 vertical (595 x 842 pt) u horizontal (842 x 595 pt).
            pagina.Width = XUnit.FromPoint(r.Horizontal ? 842 : 595);
            pagina.Height = XUnit.FromPoint(r.Horizontal ? 595 : 842);
            g = XGraphics.FromPdfPage(pagina);
            ancho = pagina.Width.Point - margen * 2;
            Medidas();

            g.DrawRectangle(azul, 0, 0, pagina.Width.Point, 8);
            g.DrawRectangle(amarillo, 0, 8, 90, 3);
            g.DrawString($"Mercadex · {r.Titulo} · Página {pdf.PageCount}", fPie, gris,
                new XRect(margen, pagina.Height.Point - 26, ancho, 10), XStringFormats.TopLeft);
            g.DrawString(generado, fPie, gris,
                new XRect(margen, pagina.Height.Point - 26, ancho, 10), XStringFormats.TopRight);

            y = 26;
            if (primera)
            {
                g.DrawString(Asociacion, fSub, gris, new XRect(margen, y, ancho, 12), XStringFormats.TopLeft);
                y += 13;
                g.DrawString(r.Titulo, fTitulo, azul, new XRect(margen, y, ancho, 20), XStringFormats.TopLeft);
                y += 21;
                if (!string.IsNullOrWhiteSpace(r.Filtros))
                {
                    g.DrawString(Ajustar(r.Filtros, fSub, ancho), fSub, gris, new XRect(margen, y, ancho, 12), XStringFormats.TopLeft);
                    y += 14;
                }

                if (r.Resumen.Count > 0)
                {
                    y += 3;
                    var alto = r.Resumen.Count * 13 + 8;
                    g.DrawRectangle(fondo, margen, y, ancho, alto);
                    var yy = y + 5;
                    foreach (var linea1 in r.Resumen)
                    {
                        g.DrawString(Ajustar(linea1, fNegrita, ancho - 16), fNegrita, azul, new XRect(margen + 8, yy, ancho - 16, 12), XStringFormats.TopLeft);
                        yy += 13;
                    }
                    y += alto + 8;
                }
                else
                {
                    y += 4;
                }
            }
            else
            {
                g.DrawString(r.Titulo + " (continuación)", fSub, gris, new XRect(margen, y, ancho, 12), XStringFormats.TopLeft);
                y += 16;
            }

            Cabecera();
        }

        NuevaPagina(primera: true);

        foreach (var f in r.Filas)
        {
            if (y + altoFila > pagina.Height.Point - 40)
                NuevaPagina(primera: false);

            if (f.Grupo)
            {
                g.DrawRectangle(fondo, margen, y, ancho, altoFila);
                Celdas(f.Celdas, fNegrita);
            }
            else if (f.Total)
            {
                g.DrawLine(lineaFuerte, margen, y, margen + ancho, y);
                Celdas(f.Celdas, fNegrita);
            }
            else
            {
                Celdas(f.Celdas, fNormal);
                g.DrawLine(linea, margen, y + altoFila, margen + ancho, y + altoFila);
            }

            y += altoFila;
        }

        if (!string.IsNullOrWhiteSpace(r.Nota))
        {
            if (y + 30 > pagina.Height.Point - 40)
                NuevaPagina(primera: false);
            y += 10;
            g.DrawString(Ajustar(r.Nota!, fSub, ancho), fSub, gris, new XRect(margen, y, ancho, 12), XStringFormats.TopLeft);
        }

        g.Dispose();
        pdf.Save(ruta);
    }
}
