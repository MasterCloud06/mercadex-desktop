using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Mercadex.Desktop.App;

/// <summary>
/// Modo oscuro. El modo claro no cambia nada: es el diseño de siempre.
/// En modo oscuro, al arrancar, cada color se convierte según su papel:
/// <list type="bullet">
/// <item>Fondo: los claros pasan a oscuros; los fuertes (menú, botones, acentos) se conservan.</item>
/// <item>Texto: los oscuros pasan a claros; los claros (texto de botones) se conservan.</item>
/// <item>Borde: los grises claros pasan a grises oscuros.</item>
/// </list>
/// Se aplica a Styles.xaml (al cargarlo) y a los colores puestos en cada pantalla
/// (al cargarse cada elemento). Los PDF y recibos impresos no cambian.
/// Si algo falla al preparar el modo oscuro, Mercadex arranca en modo claro.
/// </summary>
public static class Tema
{
    public enum Rol
    {
        Fondo,
        Texto,
        Borde
    }

    public static bool Oscuro { get; private set; }

    /// <summary>Se llama al arrancar, antes de abrir cualquier ventana.</summary>
    public static void Iniciar(Application app)
    {
        if (!Preferencias.ModoOscuro)
            return;

        try
        {
            using var flujo = typeof(Tema).Assembly.GetManifestResourceStream("Mercadex.Estilos.xaml")
                              ?? throw new InvalidOperationException("No se encontró Styles.xaml.");
            var xaml = new StreamReader(flujo).ReadToEnd();
            var estilos = (ResourceDictionary)XamlReader.Parse(ConvertirXaml(xaml));

            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(estilos);

            Oscuro = true;

            EventManager.RegisterClassHandler(
                typeof(FrameworkElement),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler((origen, _) => Aplicar(origen as FrameworkElement)),
                handledEventsToo: true);
        }
        catch
        {
            // Si no se pudo preparar, se queda el modo claro: Mercadex funciona igual.
            Oscuro = false;
        }
    }

    // =====================================================================
    // COLORES
    // =====================================================================

    /// <summary>El color convertido si el modo oscuro está activo; si no, el mismo.</summary>
    public static Color Convertir(Color c, Rol rol) => Oscuro ? AOscuro(c, rol) : c;

    /// <summary>La versión oscura de un color según su papel.</summary>
    public static Color AOscuro(Color c, Rol rol)
    {
        var (h, s, l) = AHsl(c);

        switch (rol)
        {
            case Rol.Fondo when l >= 0.70:
                // Blanco → casi negro azulado; los avisos de color quedan teñidos.
                return DeHsl(c.A, h, s * 0.45, 0.10 + (1 - l) * 0.55 + (s > 0.3 ? 0.05 : 0));

            case Rol.Texto when l <= 0.50:
                // Negro y azul tinta → texto claro; los textos rojo, verde o ámbar se aclaran.
                return DeHsl(c.A, h, Math.Min(s, 0.65), 0.95 - l * 0.55);

            case Rol.Borde when l >= 0.60:
                return DeHsl(c.A, h, s * 0.5, 0.18 + (1 - l) * 0.5);

            default:
                return c;
        }
    }

    /// <summary>Un pincel con el color convertido (en modo claro, el mismo pincel).</summary>
    public static Brush Pincel(Brush pincel, Rol rol)
    {
        if (!Oscuro || pincel is not SolidColorBrush solido)
            return pincel;

        var convertido = AOscuro(solido.Color, rol);
        if (convertido == solido.Color)
            return pincel;

        var nuevo = new SolidColorBrush(convertido) { Opacity = solido.Opacity };
        nuevo.Freeze();
        return nuevo;
    }

    private static (double H, double S, double L) AHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;

        if (max != min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0)
              : max == g ? (b - r) / d + 2
              : (r - g) / d + 4;
            h /= 6;
        }

        return (h, s, l);
    }

    private static Color DeHsl(byte a, double h, double s, double l)
    {
        l = Math.Clamp(l, 0, 1);
        s = Math.Clamp(s, 0, 1);

        double Canal(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Canal(p, q, h + 1.0 / 3);
            g = Canal(p, q, h);
            b = Canal(p, q, h - 1.0 / 3);
        }

        return Color.FromArgb(a, (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    /// <summary>Papel de un color según la propiedad donde se usa.</summary>
    public static Rol RolDe(string propiedad)
    {
        if (propiedad.Contains("Foreground", StringComparison.OrdinalIgnoreCase) ||
            propiedad.Contains("Caret", StringComparison.OrdinalIgnoreCase) ||
            propiedad.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
            propiedad.Contains("Ink", StringComparison.OrdinalIgnoreCase) ||
            propiedad.Contains("Muted", StringComparison.OrdinalIgnoreCase))
            return Rol.Texto;

        if (propiedad.Contains("Border", StringComparison.OrdinalIgnoreCase) ||
            propiedad.Contains("Stroke", StringComparison.OrdinalIgnoreCase) ||
            propiedad.Contains("GridLines", StringComparison.OrdinalIgnoreCase))
            return Rol.Borde;

        return Rol.Fondo;
    }

    // =====================================================================
    // STYLES.XAML
    // =====================================================================

    private static readonly Regex Etiqueta = new(@"<[^!?][^>]*>", RegexOptions.Compiled);
    private static readonly Regex Atributo = new(@"(?<nombre>[\w.:]+)=""(?<valor>[^""]*)""", RegexOptions.Compiled);
    private static readonly Regex ColorElemento = new(@"(<Color\b[^>]*>)\s*(#[0-9A-Fa-f]{6,8})\s*(</Color>)", RegexOptions.Compiled);

    private static readonly HashSet<string> PropiedadesDeColor = new(StringComparer.Ordinal)
    {
        "Background", "Foreground", "BorderBrush", "Fill", "Stroke", "CaretBrush", "SelectionBrush",
        "HorizontalGridLinesBrush", "VerticalGridLinesBrush", "TextElement.Foreground", "Panel.Background",
        "Border.Background", "Control.Background", "Control.Foreground", "Control.BorderBrush"
    };

    /// <summary>
    /// Convierte los colores escritos en Styles.xaml. El papel sale del atributo
    /// (Background, Foreground…), de la propiedad de un Setter o de la clave
    /// del pincel (Mx.TextBrush, Mx.BorderBrush…).
    /// </summary>
    public static string ConvertirXaml(string xaml)
    {
        // Contexto para los <SolidColorBrush/> sin clave dentro de <Setter.Value> o <X.Background>.
        var contexto = Rol.Fondo;

        var resultado = Etiqueta.Replace(xaml, m =>
        {
            var etiqueta = m.Value;
            var nombre = Regex.Match(etiqueta, @"^</?([\w.:]+)").Groups[1].Value;
            var atributos = Atributo.Matches(etiqueta).ToDictionary(a => a.Groups["nombre"].Value, a => a.Groups["valor"].Value);

            // <Border.Background>, <Setter.Value>… : solo las propiedades de color cambian el contexto.
            if (nombre.Contains('.') && !etiqueta.StartsWith("</"))
            {
                var sufijo = nombre[(nombre.LastIndexOf('.') + 1)..];
                if (PropiedadesDeColor.Contains(sufijo))
                    contexto = RolDe(sufijo);
            }

            if (nombre == "Setter" && atributos.TryGetValue("Property", out var prop))
            {
                contexto = RolDe(prop);
                if (atributos.TryGetValue("Value", out var valor) && EsColor(valor) && PropiedadesDeColor.Contains(prop))
                    etiqueta = Reemplazar(etiqueta, "Value", valor, RolDe(prop));
                return etiqueta;
            }

            if (nombre is "SolidColorBrush" or "GradientStop" && atributos.TryGetValue("Color", out var color) && EsColor(color))
            {
                var rol = atributos.TryGetValue("x:Key", out var clave) ? RolDe(clave) : contexto;
                return Reemplazar(etiqueta, "Color", color, rol);
            }

            foreach (var (atributo, valor) in atributos)
            {
                if (PropiedadesDeColor.Contains(atributo) && EsColor(valor))
                    etiqueta = Reemplazar(etiqueta, atributo, valor, RolDe(atributo));
            }

            return etiqueta;
        });

        // <Color x:Key="PrimaryColor">#1F4E79</Color>
        return ColorElemento.Replace(resultado, m =>
            m.Groups[1].Value + Hex(AOscuro((Color)ColorConverter.ConvertFromString(m.Groups[2].Value), Rol.Fondo)) + m.Groups[3].Value);
    }

    private static bool EsColor(string valor) =>
        valor.StartsWith('#') || valor is "White" or "Black";

    private static string Reemplazar(string etiqueta, string atributo, string valor, Rol rol)
    {
        var color = (Color)ColorConverter.ConvertFromString(valor);
        return etiqueta.Replace($"{atributo}=\"{valor}\"", $"{atributo}=\"{Hex(AOscuro(color, rol))}\"");
    }

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    // =====================================================================
    // COLORES PUESTOS EN CADA PANTALLA
    // =====================================================================

    private static readonly Brush TextoCalendario = Congelado(Color.FromRgb(0x1E, 0x2A, 0x38));

    private static Brush Congelado(Color c)
    {
        var pincel = new SolidColorBrush(c);
        pincel.Freeze();
        return pincel;
    }

    private static void Aplicar(FrameworkElement? e)
    {
        switch (e)
        {
            case null:
                return;

            case Window w:
                // Texto y fondo por defecto de la ventana (los heredan las pantallas).
                Convertir(w, Control.ForegroundProperty, Rol.Texto, tambienPorDefecto: true);
                Convertir(w, Control.BackgroundProperty, Rol.Fondo, tambienPorDefecto: true);
                Convertir(w, Control.BorderBrushProperty, Rol.Borde);
                return;

            // El calendario desplegable conserva su aspecto claro (su plantilla tiene
            // colores propios): solo se le devuelve el texto oscuro que hereda.
            case Calendar cal:
                cal.Foreground = TextoCalendario;
                return;

            case System.Windows.Controls.Primitives.CalendarItem
                or System.Windows.Controls.Primitives.CalendarDayButton
                or System.Windows.Controls.Primitives.CalendarButton:
                return;

            case Control c:
                Convertir(c, Control.ForegroundProperty, Rol.Texto, tambienPorDefecto: true);
                Convertir(c, Control.BackgroundProperty, Rol.Fondo, tambienPorDefecto: c is TextBox or PasswordBox);
                Convertir(c, Control.BorderBrushProperty, Rol.Borde);
                return;

            case TextBlock t:
                Convertir(t, TextBlock.ForegroundProperty, Rol.Texto);
                Convertir(t, TextBlock.BackgroundProperty, Rol.Fondo);
                return;

            case Border b:
                Convertir(b, Border.BackgroundProperty, Rol.Fondo);
                Convertir(b, Border.BorderBrushProperty, Rol.Borde);
                return;

            case Panel p:
                Convertir(p, Panel.BackgroundProperty, Rol.Fondo);
                return;

            case Shape s:
                Convertir(s, Shape.FillProperty, Rol.Fondo);
                Convertir(s, Shape.StrokeProperty, Rol.Borde);
                return;
        }
    }

    private static void Convertir(DependencyObject o, DependencyProperty propiedad, Rol rol, bool tambienPorDefecto = false)
    {
        var origen = DependencyPropertyHelper.GetValueSource(o, propiedad);

        // Solo los colores puestos directamente en la pantalla (no los de los
        // estilos, que ya vienen convertidos, ni los enlazados con Binding).
        var local = origen.BaseValueSource == BaseValueSource.Local && !origen.IsExpression;
        var porDefecto = tambienPorDefecto &&
                         origen.BaseValueSource is BaseValueSource.Default or BaseValueSource.DefaultStyle;

        if (!local && !porDefecto)
            return;

        if (o.GetValue(propiedad) is not SolidColorBrush actual)
            return;

        var nuevo = Pincel(actual, rol);
        if (!ReferenceEquals(nuevo, actual))
            o.SetValue(propiedad, nuevo);
    }
}
