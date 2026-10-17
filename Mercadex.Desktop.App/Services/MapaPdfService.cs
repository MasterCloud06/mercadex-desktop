using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Reporte del mapa del mercado (pedido de la directiva): una hoja dividida
/// en dos, arriba el sector de los pasillos y abajo el de las galerías,
/// cada caseta pintada según cómo está con sus pagos. Después, la lista de
/// casetas por pasillo con su titular y lo que debe.
/// </summary>
public static class MapaPdfService
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private const string Asociacion = "Asociación de Comerciantes 13 de Julio";

    // Situación → (nombre, relleno, borde, texto); mismos colores que la pantalla.
    private static readonly (string Clave, string Nombre, XColor Relleno, XColor Borde, XColor Texto)[] Situaciones =
    {
        ("al-dia", "Al día", XColor.FromArgb(234, 246, 234), XColor.FromArgb(150, 200, 155), XColor.FromArgb(29, 92, 33)),
        ("reciente", "Deuda reciente", XColor.FromArgb(255, 245, 217), XColor.FromArgb(230, 190, 90), XColor.FromArgb(110, 79, 0)),
        ("atrasada", "31 a 90 días", XColor.FromArgb(253, 233, 224), XColor.FromArgb(230, 150, 120), XColor.FromArgb(138, 52, 23)),
        ("critica", "Más de 90 días", XColor.FromArgb(208, 59, 59), XColor.FromArgb(181, 47, 47), XColor.FromArgb(255, 255, 255)),
        ("sin-titular", "Sin titular", XColor.FromArgb(255, 255, 255), XColor.FromArgb(185, 196, 210), XColor.FromArgb(90, 107, 125)),
        ("inactiva", "Inactiva", XColor.FromArgb(238, 241, 245), XColor.FromArgb(200, 208, 218), XColor.FromArgb(134, 150, 167)),
    };

    /// <summary>Línea que separa el sector de los pasillos (arriba) del de las galerías (abajo).</summary>
    private const int Division = 560;

    public static void Guardar(Window? dueno, MapaMercadoRespuesta datos)
    {
        var unidades = datos.Bloques.SelectMany(b => b.Unidades).ToList();
        if (unidades.Count == 0)
        {
            Dialogo.Mostrar(dueno, "No hay unidades para el reporte.", "Mapa del mercado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialogo = new SaveFileDialog
        {
            Title = "Guardar reporte del mapa",
            Filter = "Documento PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"Mapa_del_mercado_{DateTime.Now:yyyy-MM-dd}.pdf",
            AddExtension = true,
            OverwritePrompt = true
        };

        if ((dueno is null ? dialogo.ShowDialog() : dialogo.ShowDialog(dueno)) != true)
            return;

        try
        {
            Escribir(dialogo.FileName, datos);
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

        if (Dialogo.Mostrar(dueno, "Se guardó el reporte del mapa.\n\n¿Abrirlo ahora?", "Mapa del mercado",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo { FileName = dialogo.FileName, UseShellExecute = true }); }
            catch { /* Sin visor de PDF: el archivo quedó guardado. */ }
        }
    }

    public static void Escribir(string ruta, MapaMercadoRespuesta datos)
    {
        var unidades = datos.Bloques.SelectMany(b => b.Unidades).ToList();

        using var pdf = new PdfDocument();
        pdf.Info.Title = "Mapa del mercado";
        pdf.Info.Creator = "Mercadex";

        if (datos.Plano is { Ancho: > 0 } plano && unidades.Any(u => u.PlanoX != null))
            HojaDelMapa(pdf, plano, unidades);

        // Lista por pasillo (con el formato de los demás reportes) a continuación.
        var temporal = Path.Combine(Path.GetTempPath(), $"mercadex-mapa-{Guid.NewGuid():N}.pdf");
        try
        {
            ReportePdfService.EscribirPdf(temporal, Lista(unidades));
            using var lista = PdfReader.Open(temporal, PdfDocumentOpenMode.Import);
            foreach (var p in lista.Pages)
                pdf.AddPage(p);
        }
        finally
        {
            try { File.Delete(temporal); } catch { /* temporal */ }
        }

        pdf.Save(ruta);
    }

    // =====================================================================
    // HOJA 1: EL MAPA DIVIDIDO EN DOS
    // =====================================================================

    private static void HojaDelMapa(PdfDocument pdf, MapaPlano plano, List<MapaUnidad> unidades)
    {
        var pagina = pdf.AddPage();
        pagina.Width = XUnit.FromPoint(842);   // A4 horizontal
        pagina.Height = XUnit.FromPoint(595);

        using var g = XGraphics.FromPdfPage(pagina);

        var azul = new XSolidBrush(XColor.FromArgb(23, 59, 92));
        var amarillo = new XSolidBrush(XColor.FromArgb(242, 183, 5));
        var gris = new XSolidBrush(XColor.FromArgb(90, 107, 125));
        var grisClaro = new XSolidBrush(XColor.FromArgb(150, 163, 178));
        var fondo = new XSolidBrush(XColor.FromArgb(246, 248, 251));

        var fSub = new XFont("Arial", 8, XFontStyleEx.Regular);
        var fTitulo = new XFont("Arial", 14, XFontStyleEx.Bold);
        var fSector = new XFont("Arial", 9, XFontStyleEx.Bold);
        var fPasillo = new XFont("Arial", 5.2, XFontStyleEx.Bold);
        var fNumero = new XFont("Arial", 6.2, XFontStyleEx.Bold);
        var fPie = new XFont("Arial", 7, XFontStyleEx.Regular);

        const double margen = 24;
        var ancho = pagina.Width.Point - margen * 2;

        // Encabezado.
        g.DrawRectangle(azul, 0, 0, pagina.Width.Point, 7);
        g.DrawRectangle(amarillo, 0, 7, 90, 3);
        g.DrawString(Asociacion, fSub, gris, new XRect(margen, 16, ancho, 10), XStringFormats.TopLeft);
        g.DrawString("Mapa del mercado", fTitulo, azul, new XRect(margen, 26, ancho, 18), XStringFormats.TopLeft);

        // Resumen y leyenda a la derecha del título.
        var enPlano = unidades.Where(u => u.PlanoX != null).ToList();
        var conDeuda = enPlano.Count(u => u.Obligaciones > 0);
        var resumen = $"{enPlano.Count} casetas en el plano · {enPlano.Count - conDeuda} al día · {conDeuda} con deuda";
        g.DrawString(resumen, fSub, gris, new XRect(margen, 46, ancho, 10), XStringFormats.TopLeft);

        var xLeyenda = margen + ancho;
        foreach (var s in Enumerable.Reverse(Situaciones))
        {
            var texto = $"{s.Nombre} ({enPlano.Count(u => u.Situacion == s.Clave)})";
            var w = g.MeasureString(texto, fSub).Width;
            xLeyenda -= w + 16;
            g.DrawRectangle(new XPen(s.Borde, 0.6), new XSolidBrush(s.Relleno), xLeyenda, 30, 8, 8);
            g.DrawString(texto, fSub, gris, new XRect(xLeyenda + 11, 29, w + 4, 10), XStringFormats.TopLeft);
        }

        // Escala: el ancho del plano entra en la hoja; cada mitad tiene su franja.
        var escala = ancho / plano.Ancho;
        var altoNorte = Division * escala;
        var altoSur = (plano.Alto - Division) * escala;
        var disponible = pagina.Height.Point - 62 - 30 - 2 * 14;
        if (altoNorte + altoSur > disponible)
        {
            escala *= disponible / (altoNorte + altoSur);
            altoNorte = Division * escala;
            altoSur = (plano.Alto - Division) * escala;
        }

        var x0 = margen + (ancho - plano.Ancho * escala) / 2;
        var yNorte = 62.0 + 13;
        var ySur = yNorte + altoNorte + 16;

        void Mitad(double yTitulo, string titulo, double yOrigen, int desde, int hasta)
        {
            g.DrawString(titulo, fSector, azul, new XRect(margen, yTitulo - 12, ancho, 11), XStringFormats.TopLeft);
            g.DrawRectangle(fondo, x0 - 4, yOrigen - 2, plano.Ancho * escala + 8, (hasta - desde) * escala + 4);

            // Nombres de pasillos o galerías de esta mitad.
            foreach (var p in plano.Pasillos.Where(p => p.Sector != "frente" && p.Y >= desde && p.Y < hasta))
            {
                var px = x0 + p.X * escala;
                var py = yOrigen + (p.Y - desde) * escala;
                var estado = g.Save();
                g.RotateAtTransform(90, new XPoint(px, py));
                g.DrawString(p.Nombre.ToUpper(Bo), fPasillo, grisClaro, new XPoint(px - 10, py + 2));
                g.Restore(estado);
            }

            foreach (var u in enPlano.Where(u => u.PlanoY >= desde && u.PlanoY < hasta))
            {
                var s = Situaciones.FirstOrDefault(x => x.Clave == u.Situacion);
                if (s.Nombre is null) s = Situaciones[0];

                var rx = x0 + (u.PlanoX!.Value - 26) * escala;
                var ry = yOrigen + (u.PlanoY!.Value - 32 - desde) * escala;
                var rect = new XRect(rx, ry, 52 * escala, 64 * escala);
                g.DrawRectangle(new XPen(s.Borde, 0.5), new XSolidBrush(s.Relleno), rect);
                g.DrawString(u.Codigo, fNumero, new XSolidBrush(s.Texto), rect, XStringFormats.Center);
            }

            foreach (var c in plano.PorConfirmar.Where(c => c.Y >= desde && c.Y < hasta))
            {
                var rect = new XRect(x0 + (c.X - 26) * escala, yOrigen + (c.Y - 32 - desde) * escala, 52 * escala, 64 * escala);
                g.DrawRectangle(new XPen(XColor.FromArgb(154, 171, 189), 0.6) { DashStyle = XDashStyle.Dash }, rect);
                g.DrawString(c.Numero + "?", fNumero, grisClaro, rect, XStringFormats.Center);
            }
        }

        Mitad(yNorte, "Sector de los pasillos (Pasillos 1 al 11)", yNorte, 0, Division);
        Mitad(ySur, "Sector de las galerías", ySur, Division, plano.Alto);

        var generado = $"Generado el {DateTime.Now.ToString("dd/MM/yyyy HH:mm", Bo)}" +
                       (SesionActual.Iniciada ? $" por {SesionActual.Nombre}" : "");
        g.DrawString($"Mercadex · Mapa del mercado · Página 1 · Según el plano oficial ({plano.Fuente})", fPie, gris,
            new XRect(margen, pagina.Height.Point - 20, ancho, 10), XStringFormats.TopLeft);
        g.DrawString(generado, fPie, gris, new XRect(margen, pagina.Height.Point - 20, ancho, 10), XStringFormats.TopRight);
    }

    // =====================================================================
    // LISTA POR PASILLO
    // =====================================================================

    private static Reporte Lista(List<MapaUnidad> unidades)
    {
        var filas = new List<FilaReporte>();

        string Nombre(MapaUnidad u) => u.PlanoX != null || u.Pasillo != null
            ? u.Pasillo ?? "Sin pasillo"
            : u.Tipo switch { 2 => "Comedores", 3 => "Ambulantes", _ => "Sin pasillo" };

        var codigoDe = unidades.Where(u => u.Pasillo != null && u.PasilloCodigo != null)
            .GroupBy(u => u.Pasillo!)
            .ToDictionary(g => g.Key, g => g.First().PasilloCodigo!);

        foreach (var grupo in unidades
                     .GroupBy(Nombre)
                     .OrderBy(g => Orden(g.Key, codigoDe.GetValueOrDefault(g.Key)))
                     .ThenBy(g => g.Key, StringComparer.Create(Bo, true)))
        {
            var lista = grupo
                .OrderBy(u => int.TryParse(u.Codigo, out var n) ? n : int.MaxValue)
                .ThenBy(u => u.Codigo, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var deuda = lista.Count(u => u.Obligaciones > 0);

            filas.Add(new FilaReporte(new[] { grupo.Key, $"{lista.Count} unidades · {deuda} con deuda", "", "" }, Grupo: true));
            foreach (var u in lista)
            {
                filas.Add(new FilaReporte(new[]
                {
                    $"{u.TipoTexto} {u.Codigo}",
                    u.Titular ?? "—",
                    Situaciones.FirstOrDefault(s => s.Clave == u.Situacion).Nombre ?? u.Situacion,
                    u.Obligaciones > 0 ? Deuda(u) : ""
                }));
            }
        }

        var total = unidades.Count;
        var conDeuda = unidades.Count(u => u.Obligaciones > 0);

        return new Reporte
        {
            Titulo = "Casetas por pasillo",
            Filtros = "Todas las unidades del mercado, agrupadas por pasillo o galería.",
            Resumen = new() { $"{total} unidades · {total - conDeuda} al día · {conDeuda} con deuda" },
            Columnas = new()
            {
                new ColumnaReporte("Unidad", 1.1),
                new ColumnaReporte("Titular", 2.6),
                new ColumnaReporte("Situación", 1.2),
                new ColumnaReporte("Debe", 1.2, Derecha: true)
            },
            Filas = filas,
            NombreArchivo = "Casetas_por_pasillo"
        };
    }

    /// <summary>Pasillo 1…11, luego galerías, frente, comedores y ambulantes.</summary>
    private static int Orden(string nombre, string? codigo)
    {
        if (codigo is { Length: 6 } && int.TryParse(codigo[4..], out var nc))
        {
            if (codigo.StartsWith("PAS-")) return nc;
            if (codigo.StartsWith("GAL-")) return 100 + nc;
        }
        if (string.Equals(codigo, "FRENTE", StringComparison.OrdinalIgnoreCase)) return 200;
        if (nombre.StartsWith("Pasillo ", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(nombre[8..], out var n))
            return n;
        if (nombre.StartsWith("Galería", StringComparison.OrdinalIgnoreCase)) return 100;
        if (nombre.StartsWith("Frente", StringComparison.OrdinalIgnoreCase)) return 200;
        if (nombre == "Comedores") return 300;
        if (nombre == "Ambulantes") return 301;
        return 400;
    }

    private static string Deuda(MapaUnidad u) =>
        string.Join(" + ", u.Deudas.Select(d =>
            $"{(d.Moneda == "BOB" ? "Bs" : d.Moneda == "USD" ? "$us" : d.Moneda)} {d.Total.ToString("N2", Bo)}"));
}
