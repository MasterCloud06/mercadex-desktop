using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Pdf;

namespace Mercadex.Desktop.App.Views.Reportes;

/// <summary>
/// Lista de deudores solo con el número de caseta, cuenta por cuenta (como
/// la hoja que arma la directiva): se marcan las cuentas (servicio y mes) y
/// cada una se agrega abajo con sus casetas y el total de casetas deudoras.
/// </summary>
public sealed class DeudoresPorCuentaView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly StackPanel _cuentas = new();
    private readonly StackPanel _vista = new();
    private readonly TextBlock _estado = new() { Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button _pdf = UiKit.Boton("Guardar PDF");
    private readonly Button _copiar = UiKit.Boton("Copiar texto");

    // Las cuentas en el orden en que se marcaron: así se despliegan abajo.
    private readonly List<Cuenta> _marcadas = new();
    private List<Cuenta> _todas = new();

    public DeudoresPorCuentaView()
    {
        var raiz = new DockPanel();

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);
        var cabeza = new DockPanel();
        var acciones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _pdf.Margin = new Thickness(0, 0, 8, 0);
        _pdf.Click += (_, _) => GuardarPdf();
        _copiar.Click += (_, _) => Copiar();
        acciones.Children.Add(_pdf);
        acciones.Children.Add(_copiar);
        DockPanel.SetDock(acciones, Dock.Right);
        cabeza.Children.Add(acciones);
        cabeza.Children.Add(new TextBlock { Text = "Lista de corte", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo, VerticalAlignment = VerticalAlignment.Center });
        arriba.Children.Add(cabeza);
        arriba.Children.Add(UiKit.Ayuda(
            "Marca las cuentas (servicio y mes). Arriba salen todas las cuentas marcadas y debajo los locales que deben " +
            "alguna, cada uno una sola vez; entre paréntesis, cuántas de esas cuentas debe.", 10));
        arriba.Children.Add(_estado);
        raiz.Children.Add(arriba);

        // Izquierda: las cuentas con deuda pendiente.
        var izquierda = new DockPanel { Width = 330 };
        var enlaces = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var ninguna = new Button { Content = "Quitar todas", Style = UiKit.Estilo("Mx.NavLink"), Margin = new Thickness(0, 0, 14, 0) };
        ninguna.Click += (_, _) => { _marcadas.Clear(); Dibujar(); };
        var actualizar = new Button { Content = "Actualizar", Style = UiKit.Estilo("Mx.NavLink") };
        actualizar.Click += async (_, _) => await CargarAsync();
        enlaces.Children.Add(ninguna);
        enlaces.Children.Add(actualizar);
        DockPanel.SetDock(enlaces, Dock.Top);
        izquierda.Children.Add(enlaces);
        izquierda.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _cuentas });
        var tarjeta = UiKit.Tarjeta(izquierda, 0);
        tarjeta.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(tarjeta, Dock.Left);
        raiz.Children.Add(tarjeta);

        // Derecha: la hoja tal como se imprime.
        var hoja = new Border
        {
            Background = Brushes.White,
            BorderBrush = ColoresPanel.Hex("#D5DEE8"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(36, 28, 36, 28),
            Child = _vista
        };
        raiz.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = hoja });

        Content = raiz;
        Loaded += async (_, _) =>
        {
            if (_todas.Count == 0)
                await CargarAsync();
        };
        Dibujar();
    }

    private async Task CargarAsync()
    {
        _estado.Text = "Cargando las deudas pendientes…";
        try
        {
            var d = await ApiJson.GetAsync<Respuesta>("/api/reportes/deudores") ?? new Respuesta();
            _todas = Agrupar(d);

            // Si una cuenta marcada ya no tiene deudores, se quita.
            var claves = _todas.ToDictionary(c => c.Clave);
            for (var i = 0; i < _marcadas.Count; i++)
                _marcadas[i] = claves.GetValueOrDefault(_marcadas[i].Clave)!;
            _marcadas.RemoveAll(c => c is null);

            _estado.Text = _todas.Count == 0
                ? "No hay deudas pendientes."
                : $"Deudas pendientes al {DateTime.Today.ToString("dd/MM/yyyy", Bo)}. Actualizado a las {DateTime.Now:HH:mm}.";
        }
        catch (Exception ex)
        {
            _estado.Text = "No se pudieron cargar las deudas: " + ex.Message;
        }

        Dibujar();
    }

    /// <summary>Una cuenta por servicio y mes, con las casetas que la deben.</summary>
    public static List<Cuenta> Agrupar(Respuesta d) =>
        d.Filas
            .SelectMany(f => f.Deudas.Select(x => (Caseta: f.Codigo, x.Concepto, x.Periodo)))
            .GroupBy(x => (x.Concepto, x.Periodo))
            .Select(g => new Cuenta(g.Key.Concepto, g.Key.Periodo, Ordenar(g.Select(x => x.Caseta))))
            .OrderBy(c => c.Concepto, StringComparer.Create(Bo, true))
            .ThenBy(c => c.Orden)
            .ToList();

    /// <summary>De mayor a menor, como la lista en papel; los códigos no numéricos al final.</summary>
    public static List<string> Ordenar(IEnumerable<string> casetas) =>
        casetas.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => int.TryParse(c, out _) ? 0 : 1)
            .ThenByDescending(c => int.TryParse(c, out var n) ? n : 0)
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void Dibujar()
    {
        _cuentas.Children.Clear();
        foreach (var c in _todas)
        {
            var casilla = new CheckBox
            {
                IsChecked = _marcadas.Any(m => m.Clave == c.Clave),
                Margin = new Thickness(0, 0, 0, 8),
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    Text = $"{c.Nombre}  ·  {c.Casetas.Count} {(c.Casetas.Count == 1 ? "caseta" : "casetas")}",
                    TextWrapping = TextWrapping.Wrap
                }
            };
            casilla.Click += (_, _) =>
            {
                _marcadas.RemoveAll(m => m.Clave == c.Clave);
                if (casilla.IsChecked == true)
                    _marcadas.Add(c);
                DibujarVista();
            };
            _cuentas.Children.Add(casilla);
        }

        DibujarVista();
    }

    /// <summary>
    /// Los locales que deben alguna de las cuentas, cada uno una sola vez y de
    /// menor a mayor, con cuántas de esas cuentas debe.
    /// </summary>
    public static List<(string Caseta, int Cuentas)> Locales(IEnumerable<Cuenta> cuentas) =>
        cuentas.SelectMany(c => c.Casetas.Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Caseta: g.Key, Cuentas: g.Count()))
            .OrderBy(x => int.TryParse(x.Caseta, out _) ? 0 : 1)
            .ThenBy(x => int.TryParse(x.Caseta, out var n) ? n : 0)
            .ThenBy(x => x.Caseta, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>«1 (4)»: la caseta 1 debe 4 de las cuentas marcadas; «5» si debe una.</summary>
    public static string Local((string Caseta, int Cuentas) l) =>
        l.Cuentas > 1 ? $"{l.Caseta} ({l.Cuentas})" : l.Caseta;

    public static string Servicios(IEnumerable<Cuenta> cuentas) => string.Join(",  ", cuentas.Select(c => c.Etiqueta));

    private const string Leyenda = "(n) = cuántas de las cuentas de la lista debe ese local.";

    private void DibujarVista()
    {
        _vista.Children.Clear();
        _pdf.IsEnabled = _copiar.IsEnabled = _marcadas.Count > 0;

        if (_marcadas.Count == 0)
        {
            _vista.Children.Add(new TextBlock { Text = "Marca una o varias cuentas a la izquierda.", Foreground = Brushes.Gray });
            return;
        }

        var negro = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
        negro.Freeze();
        var fuente = new FontFamily("Arial");
        var locales = Locales(_marcadas);

        _vista.Children.Add(new TextBlock { Text = "LISTA DE CORTE", FontFamily = fuente, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = negro, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) });

        var servicios = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var etiqueta = new TextBlock { Text = "SERVICIOS:", FontFamily = fuente, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = negro, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(etiqueta, Dock.Left);
        servicios.Children.Add(etiqueta);
        servicios.Children.Add(new TextBlock { Text = Servicios(_marcadas), FontFamily = fuente, FontSize = 13, Foreground = negro, TextWrapping = TextWrapping.Wrap });
        _vista.Children.Add(servicios);

        _vista.Children.Add(new Border { BorderBrush = negro, BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock { Text = "LOCALES", FontFamily = fuente, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = negro, HorizontalAlignment = HorizontalAlignment.Center } });

        var grilla = new WrapPanel();
        foreach (var l in locales)
            grilla.Children.Add(new TextBlock { Text = Local(l), FontFamily = fuente, FontSize = 13, Foreground = negro, Width = 62, Margin = new Thickness(0, 0, 0, 4) });
        _vista.Children.Add(grilla);

        _vista.Children.Add(new TextBlock { Text = $"TOTAL DE CASETAS: {locales.Count}", FontFamily = fuente, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = negro, Margin = new Thickness(0, 14, 0, 4) });
        if (locales.Any(l => l.Cuentas > 1))
            _vista.Children.Add(new TextBlock { Text = Leyenda, FontFamily = fuente, FontSize = 11.5, Foreground = Brushes.Gray });
    }

    /// <summary>El mismo contenido en texto, para pegarlo en Word o WhatsApp.</summary>
    public static string Texto(IEnumerable<Cuenta> cuentas)
    {
        var lista = cuentas.ToList();
        var locales = Locales(lista);
        return $"LISTA DE CORTE\n\nSERVICIOS: {Servicios(lista)}\n\nLOCALES:\n{string.Join("   ", locales.Select(Local))}\n\nTOTAL DE CASETAS: {locales.Count}" +
               (locales.Any(l => l.Cuentas > 1) ? "\n" + Leyenda : "");
    }

    private void Copiar()
    {
        try
        {
            Clipboard.SetText(Texto(_marcadas));
            _estado.Text = "Texto copiado. Pégalo en Word o WhatsApp con Ctrl+V.";
        }
        catch (Exception ex)
        {
            _estado.Text = "No se pudo copiar: " + ex.Message;
        }
    }

    private void GuardarPdf()
    {
        if (_marcadas.Count == 0)
            return;

        var cuentas = _marcadas.ToList();
        ReportePdfService.GuardarArchivo(Window.GetWindow(this), "Lista de corte",
            $"Lista_de_corte_{DateTime.Today:yyyyMMdd}", "Documento PDF (*.pdf)|*.pdf", ".pdf",
            ruta => EscribirPdf(ruta, cuentas));
    }

    /// <summary>Como la «Lista de corte» del sistema anterior: servicios arriba y los locales en una grilla.</summary>
    public static void EscribirPdf(string ruta, IReadOnlyList<Cuenta> cuentas)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = "Lista de corte";
        pdf.Info.Creator = "Mercadex";

        var fAsociacion = new XFont("Arial", 10, XFontStyleEx.Bold);
        var fTitulo = new XFont("Arial", 13, XFontStyleEx.Bold);
        var fNegrita = new XFont("Arial", 10, XFontStyleEx.Bold);
        var fTexto = new XFont("Arial", 10, XFontStyleEx.Regular);
        var fPie = new XFont("Arial", 7.5, XFontStyleEx.Regular);
        var gris = new XSolidBrush(XColor.FromArgb(90, 107, 125));
        var linea = new XPen(XColors.Black, 0.8);

        const double margen = 42;
        XGraphics? g = null;
        double y = 0, ancho = 0, alto = 0;
        var numero = 0;
        var locales = Locales(cuentas);

        void NuevaPagina()
        {
            g?.Dispose();
            var pagina = pdf.AddPage();
            pagina.Width = XUnit.FromPoint(595);   // A4 vertical
            pagina.Height = XUnit.FromPoint(842);
            g = XGraphics.FromPdfPage(pagina);
            ancho = pagina.Width.Point - margen * 2;
            alto = pagina.Height.Point;
            y = margen;
            numero++;
            g.DrawString($"Página {numero}", fPie, gris, new XRect(margen, alto - 26, ancho, 10), XStringFormats.TopRight);
        }

        NuevaPagina();
        g!.DrawString("ASOCIACIÓN DE COMERCIANTES 13 DE JULIO", fAsociacion, XBrushes.Black, new XRect(margen, y, ancho, 12), XStringFormats.TopLeft);
        g.DrawString($"Fecha: {DateTime.Today.ToString("dd/MM/yyyy", Bo)}", fTexto, XBrushes.Black, new XRect(margen, y, ancho, 12), XStringFormats.TopRight);
        y += 18;
        g.DrawString("LISTA DE CORTE", fTitulo, XBrushes.Black, new XRect(margen, y, ancho, 16), XStringFormats.TopCenter);
        y += 22;
        g.DrawLine(linea, margen, y, margen + ancho, y);
        y += 8;

        // SERVICIOS: todas las cuentas marcadas, en un párrafo.
        const double sangria = 72;
        g.DrawString("SERVICIOS:", fNegrita, XBrushes.Black, new XRect(margen, y, sangria, 12), XStringFormats.TopLeft);
        var texto = Servicios(cuentas);
        var palabras = texto.Split(' ');
        var lineas = 1;
        var actual = 0.0;
        foreach (var p in palabras)
        {
            var w = g.MeasureString(p + " ", fTexto).Width;
            if (actual + w > ancho - sangria && actual > 0) { lineas++; actual = 0; }
            actual += w;
        }
        var altoServicios = lineas * fTexto.GetHeight() * 1.2 + 4;
        new XTextFormatter(g).DrawString(texto, fTexto, XBrushes.Black, new XRect(margen + sangria, y, ancho - sangria, altoServicios), XStringFormats.TopLeft);
        y += altoServicios + 6;

        void Encabezado()
        {
            g!.DrawLine(linea, margen, y, margen + ancho, y);
            g.DrawString("LOCALES", fNegrita, XBrushes.Black, new XRect(margen, y + 3, ancho, 12), XStringFormats.TopCenter);
            y += 18;
            g.DrawLine(linea, margen, y, margen + ancho, y);
            y += 6;
        }

        Encabezado();

        // Grilla de locales: columnas iguales, tantas como entren.
        var anchoCelda = Math.Max(34, locales.Count == 0 ? 34 : locales.Max(l => g.MeasureString(Local(l), fTexto).Width) + 10);
        var columnas = Math.Max(1, (int)(ancho / anchoCelda));
        anchoCelda = ancho / columnas;
        var altoFila = fTexto.GetHeight() * 1.45;
        for (var i = 0; i < locales.Count; i += columnas)
        {
            if (y + altoFila > alto - margen - 30)
            {
                NuevaPagina();
                Encabezado();
            }
            for (var k = 0; k < columnas && i + k < locales.Count; k++)
                g!.DrawString(Local(locales[i + k]), fTexto, XBrushes.Black, new XRect(margen + k * anchoCelda, y, anchoCelda, altoFila), XStringFormats.TopLeft);
            y += altoFila;
        }

        if (y + 40 > alto - margen)
            NuevaPagina();
        y += 4;
        g!.DrawLine(linea, margen, y, margen + ancho, y);
        y += 6;
        g.DrawString($"TOTAL DE CASETAS: {locales.Count}", fNegrita, XBrushes.Black, new XRect(margen, y, ancho, 12), XStringFormats.TopLeft);
        if (locales.Any(l => l.Cuentas > 1))
            g.DrawString(Leyenda, fPie, gris, new XRect(margen, y + 16, ancho, 10), XStringFormats.TopLeft);

        g.Dispose();
        pdf.Save(ruta);
    }

    // =====================================================================

    public sealed record Cuenta(string Concepto, string Periodo, List<string> Casetas)
    {
        public string Clave => Concepto + "|" + Periodo;

        /// <summary>Para ordenar los meses: 202608.</summary>
        public int Orden => Periodo.Split('/') is [var m, var a] && int.TryParse(m, out var mm) && int.TryParse(a, out var aa) ? aa * 100 + mm : 0;

        private static readonly string[] MesesCortos = { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" };

        /// <summary>«APORTE MENSUAL oct/2026», como en la lista de corte anterior.</summary>
        public string Etiqueta => Orden == 0
            ? (Concepto.ToUpper(Bo) + (Periodo.Length == 0 ? "" : " " + Periodo))
            : $"{Concepto.ToUpper(Bo)} {MesesCortos[Orden % 100 - 1]}/{Orden / 100}";

        /// <summary>«Mensualidad · Agosto 2026».</summary>
        public string Nombre => Periodo.Length == 0 ? Concepto : $"{Concepto} · {Mes(false)}";

        /// <summary>«DEUDORES DE MENSUALIDAD – AGOSTO 2026».</summary>
        public string Titulo => ("Deudores de " + Concepto + (Periodo.Length == 0 ? "" : " – " + Mes(true))).ToUpper(Bo);

        public string Numeros => string.Join(" - ", Casetas);

        public string Total => $"TOTAL DE CASETAS DEUDORAS: {Casetas.Count}";

        private string Mes(bool mayusculas)
        {
            if (Orden == 0)
                return Periodo;
            var texto = new DateTime(Orden / 100, Orden % 100, 1).ToString("MMMM yyyy", Bo);
            return mayusculas ? texto.ToUpper(Bo) : char.ToUpper(texto[0], Bo) + texto[1..];
        }
    }

    // Respuesta de GET /api/reportes/deudores (solo lo que se usa aquí).

    public sealed class Respuesta
    {
        public List<FilaDeudor> Filas { get; set; } = new();
    }

    public sealed class FilaDeudor
    {
        public string Codigo { get; set; } = "";
        public List<DeudaDto> Deudas { get; set; } = new();
    }

    public sealed class DeudaDto
    {
        public string Concepto { get; set; } = "";
        public string Periodo { get; set; } = "";
    }
}
