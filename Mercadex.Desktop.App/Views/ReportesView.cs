using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Sesion;
using Mercadex.Desktop.App.Views.Reportes;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Reportes (manual: 3. REPORTES DE INGRESOS, 4. EXTRACTO DE DEUDA,
/// 5. AVISO PARA CORTE, 6. ANULAR COBRO, 7. PAGO POR GRUPO DE BLOQUES y las
/// listas de deudores). A la izquierda se elige el reporte; a la derecha se
/// genera y se guarda en PDF o Excel.
/// </summary>
public sealed class ReportesView : UserControl
{
    private readonly ContentControl _contenido = new();
    private readonly Dictionary<string, UIElement> _abiertos = new();
    private readonly List<Border> _opciones = new();

    private sealed record Opcion(string Clave, string Titulo, string Detalle, Func<UIElement> Crear);

    public ReportesView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var admin = SesionActual.EsAdministrador || !SesionActual.Iniciada;

        var grupos = new List<(string Grupo, List<Opcion> Opciones)>
        {
            ("Ingresos", new()
            {
                new("diario", "Diario de recaudaciones", "Lo cobrado por servicio (cuenta)", () => new IngresosReporteView(TipoReporteIngresos.Diario)),
                new("servicios", "Servicios por fechas", "Día por día, servicio por servicio", () => new IngresosReporteView(TipoReporteIngresos.Servicios)),
                new("acumulados", "Servicios acumulados", "Cada servicio mes por mes", () => new IngresosReporteView(TipoReporteIngresos.Acumulados)),
                new("detalle", "Detalle de cobros", "Cada cobro, uno por uno", () => new IngresosReporteView(TipoReporteIngresos.Detalle)),
                new("bloques", "Pagos de un servicio", "Los pagos de un servicio, caseta por caseta", () => new IngresosReporteView(TipoReporteIngresos.PorBloque)),
                new("historial", "Historial de pagos de una caseta", "Fecha, cuenta y monto de cada pago", () => new HistorialCasetaReporteView())
            }),
            ("Deudas", new()
            {
                new("extracto", "Extracto de deuda", "Deudas por pagar de una caseta", () => new ExtractoDeudaView()),
                new("corte", "Aviso para corte", "Cada caseta deudora una sola vez", () => new DeudoresReporteView(TipoReporteDeudores.Corte)),
                new("por-cuenta", "Deudores por cuenta", "Solo los números de caseta, cuenta por cuenta", () => new DeudoresPorCuentaView()),
                new("contabilidad", "Deudores para contabilidad", "Fecha, servicio y monto de cada deuda", () => new DeudoresReporteView(TipoReporteDeudores.Contabilidad))
            }),
            ("Control", new List<Opcion>()
                .Concat(admin
                    ? new[] { new Opcion("anular", "Anular cobro", "Anula un recibo y devuelve la deuda", () => new AnularCobroView()) }
                    : Array.Empty<Opcion>())
                .Concat(new[]
                {
                    new Opcion("comprobantes", "Comprobantes de anulación", "Quién anuló, cuándo y por qué", () => Comprobantes()),
                    new Opcion("ingresos-gastos", "Ingresos y gastos", "Lo que entró y salió en el período", () => new InformeIngresosGastosView())
                })
                .ToList())
        };

        // ---------- Menú de reportes ----------

        var menu = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        menu.Children.Add(new TextBlock { Text = "Reportes", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo, Margin = new Thickness(6, 0, 0, 10) });

        foreach (var (grupo, opciones) in grupos)
        {
            menu.Children.Add(new TextBlock { Text = grupo, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Suave, Margin = new Thickness(8, 12, 0, 4) });
            foreach (var o in opciones)
                menu.Children.Add(Boton(o));
        }

        var lateral = new Border
        {
            Width = 250,
            Padding = new Thickness(10, 18, 10, 10),
            Background = Brushes.White,
            BorderBrush = ColoresPanel.Hex("#E1E7EF"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = menu }
        };

        var raiz = new DockPanel();
        DockPanel.SetDock(lateral, Dock.Left);
        raiz.Children.Add(lateral);
        raiz.Children.Add(new Border { Padding = new Thickness(22, 18, 22, 18), Child = _contenido });
        Content = raiz;

        Abrir(grupos[0].Opciones[0]);
    }

    /// <summary>Abre un reporte por su clave (por ejemplo "anular").</summary>
    public void AbrirReporte(string clave)
    {
        var opcion = _opciones.Select(b => b.Tag).OfType<Opcion>().FirstOrDefault(o => o.Clave == clave);
        if (opcion is not null)
            Abrir(opcion);
    }

    private Border Boton(Opcion o)
    {
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock { Text = o.Titulo, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta });
        pila.Children.Add(new TextBlock { Text = o.Detalle, FontSize = 12, Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap });

        var b = new Border
        {
            Tag = o,
            Padding = new Thickness(12, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = pila
        };
        b.MouseEnter += (_, _) => { if (!Elegido(b)) b.Background = ColoresPanel.Hex("#F3F6FA"); };
        b.MouseLeave += (_, _) => { if (!Elegido(b)) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => Abrir(o);
        _opciones.Add(b);
        return b;
    }

    private static bool Elegido(Border b) => b.BorderBrush is SolidColorBrush { Color: var c } && c.A != 0;

    private void Abrir(Opcion o)
    {
        foreach (var b in _opciones)
        {
            var esta = ReferenceEquals(b.Tag, o);
            b.Background = esta ? ColoresPanel.Hex("#E7F0F9") : Brushes.Transparent;
            b.BorderBrush = esta ? ColoresPanel.Hex("#F2B705") : Brushes.Transparent;
        }

        // Cada reporte se crea una vez: al volver a él conserva los filtros y el resultado.
        if (!_abiertos.TryGetValue(o.Clave, out var vista))
        {
            try
            {
                vista = o.Crear();
            }
            catch (Exception ex)
            {
                vista = new TextBlock { Text = "No se pudo abrir el reporte: " + ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = ColoresPanel.Hex("#A12A2A") };
            }

            _abiertos[o.Clave] = vista;
        }

        _contenido.Content = vista;
    }

    private UIElement Comprobantes()
    {
        var pila = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        pila.Children.Add(new TextBlock { Text = "Comprobantes de anulación", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo });
        pila.Children.Add(UiKit.Ayuda(
            "Cada anulación deja un comprobante digital con quién anuló, la fecha y el motivo. El recibo original no desaparece: queda como ANULADO y la deuda vuelve a estar pendiente.", 14));

        var abrir = UiKit.Boton("Ver comprobantes de anulación", principal: true);
        abrir.HorizontalAlignment = HorizontalAlignment.Left;
        abrir.Click += (_, _) =>
        {
            try
            {
                new ComprobantesAnulacionWindow(ApiVentanas) { Owner = Window.GetWindow(this) }.ShowDialog();
            }
            catch (Exception ex)
            {
                Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se pudo abrir", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        pila.Children.Add(abrir);
        return pila;
    }

    private static readonly System.Net.Http.HttpClient ApiVentanas =
        ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(90));
}
