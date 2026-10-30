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
        cabeza.Children.Add(new TextBlock { Text = "Deudores por cuenta (solo número de caseta)", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo, VerticalAlignment = VerticalAlignment.Center });
        arriba.Children.Add(cabeza);
        arriba.Children.Add(UiKit.Ayuda(
            "Marca las cuentas que quieres ver. Cada una se agrega abajo con los números de las casetas que la deben " +
            "y el total de casetas deudoras, en el orden en que las marcas.", 10));
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
        var fuente = new FontFamily("Times New Roman");
        foreach (var c in _marcadas)
        {
            _vista.Children.Add(new TextBlock { Text = c.Titulo, FontFamily = fuente, FontSize = 16, Foreground = negro, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            _vista.Children.Add(new TextBlock { Text = c.Numeros, FontFamily = fuente, FontSize = 15, Foreground = negro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            _vista.Children.Add(new TextBlock { Text = c.Total, FontFamily = fuente, FontSize = 15, Foreground = negro, Margin = new Thickness(0, 0, 0, 34) });
        }
    }

    /// <summary>El mismo contenido en texto, para pegarlo en Word o WhatsApp.</summary>
    public static string Texto(IEnumerable<Cuenta> cuentas) =>
        string.Join("\n\n", cuentas.Select(c => $"{c.Titulo}\n\n{c.Numeros}\n\n{c.Total}"));

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
        ReportePdfService.GuardarArchivo(Window.GetWindow(this), "Deudores por cuenta",
            $"Deudores_por_cuenta_{DateTime.Today:yyyyMMdd}", "Documento PDF (*.pdf)|*.pdf", ".pdf",
            ruta => EscribirPdf(ruta, cuentas));
    }

    public static void EscribirPdf(string ruta, IReadOnlyList<Cuenta> cuentas)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = "Deudores por cuenta";
        pdf.Info.Creator = "Mercadex";

        var fTitulo = new XFont("Times New Roman", 12, XFontStyleEx.Regular);
        var fTexto = new XFont("Times New Roman", 11.5, XFontStyleEx.Regular);
        var fPie = new XFont("Arial", 7, XFontStyleEx.Regular);
        var gris = new XSolidBrush(XColor.FromArgb(90, 107, 125));

        const double margen = 56;
        PdfPage? pagina = null;
        XGraphics? g = null;
        double y = 0, ancho = 0, alto = 0;
        var numero = 0;

        void NuevaPagina()
        {
            g?.Dispose();
            pagina = pdf.AddPage();
            pagina.Width = XUnit.FromPoint(595);   // A4 vertical
            pagina.Height = XUnit.FromPoint(842);
            g = XGraphics.FromPdfPage(pagina);
            ancho = pagina.Width.Point - margen * 2;
            alto = pagina.Height.Point;
            y = margen;
            numero++;
            g.DrawString($"Mercadex · Deudores por cuenta · Página {numero}", fPie, gris, new XRect(margen, alto - 30, ancho, 10), XStringFormats.TopLeft);
            g.DrawString($"Deudas pendientes al {DateTime.Today.ToString("dd/MM/yyyy", Bo)}", fPie, gris, new XRect(margen, alto - 30, ancho, 10), XStringFormats.TopRight);
        }

        double Alto(string texto, XFont fuente)
        {
            // Alto aproximado del párrafo: líneas según el ancho del texto.
            var linea = fuente.GetHeight() * 1.15;
            var palabras = texto.Split(' ');
            var lineas = 1;
            var actual = 0.0;
            foreach (var p in palabras)
            {
                var w = g!.MeasureString(p + " ", fuente).Width;
                if (actual + w > ancho && actual > 0)
                {
                    lineas++;
                    actual = 0;
                }
                actual += w;
            }
            return lineas * linea;
        }

        NuevaPagina();
        foreach (var c in cuentas)
        {
            var altoNumeros = Alto(c.Numeros, fTexto);
            var necesario = fTitulo.GetHeight() + 14 + altoNumeros + 14 + fTexto.GetHeight() + 30;
            if (y + Math.Min(necesario, 160) > alto - margen)
                NuevaPagina();

            g!.DrawString(c.Titulo, fTitulo, XBrushes.Black, new XRect(margen, y, ancho, fTitulo.GetHeight()), XStringFormats.TopCenter);
            y += fTitulo.GetHeight() + 14;

            // Si los números no entran en lo que queda de la hoja, siguen en la próxima.
            var restantes = c.Casetas.ToList();
            while (restantes.Count > 0)
            {
                var disponible = alto - margen - y - 20;
                var cuantas = restantes.Count;
                while (cuantas > 1 && Alto(string.Join(" - ", restantes.Take(cuantas)), fTexto) > disponible)
                    cuantas--;
                var parte = string.Join(" - ", restantes.Take(cuantas)) + (cuantas < restantes.Count ? " -" : "");
                var h = Alto(parte, fTexto);
                new XTextFormatter(g) { Alignment = XParagraphAlignment.Justify }
                    .DrawString(parte, fTexto, XBrushes.Black, new XRect(margen, y, ancho, h + 4), XStringFormats.TopLeft);
                y += h + 14;
                restantes = restantes.Skip(cuantas).ToList();
                if (restantes.Count > 0)
                    NuevaPagina();
            }

            if (y + fTexto.GetHeight() > alto - margen)
                NuevaPagina();
            g!.DrawString(c.Total, fTexto, XBrushes.Black, new XRect(margen, y, ancho, fTexto.GetHeight()), XStringFormats.TopLeft);
            y += fTexto.GetHeight() + 34;
        }

        g?.Dispose();
        pdf.Save(ruta);
    }

    // =====================================================================

    public sealed record Cuenta(string Concepto, string Periodo, List<string> Casetas)
    {
        public string Clave => Concepto + "|" + Periodo;

        /// <summary>Para ordenar los meses: 202608.</summary>
        public int Orden => Periodo.Split('/') is [var m, var a] && int.TryParse(m, out var mm) && int.TryParse(a, out var aa) ? aa * 100 + mm : 0;

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
