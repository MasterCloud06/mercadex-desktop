using System.Globalization;
using System.Windows.Media;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Services;

/// <summary>
/// Pintar el mapa por cuenta (concepto de cobro), como el plano en papel:
/// cada cuenta marcada tiene su color; la caseta que debe más de una de las
/// marcadas va en rojo y la que no debe ninguna, en blanco. Lo usan la
/// pantalla del mapa y su reporte PDF.
/// </summary>
public sealed class CuentasMapa
{
    public sealed record Colores(string Fondo, string Borde, string Texto)
    {
        public Brush PincelFondo { get; } = Pincel(Fondo);
        public Brush PincelBorde { get; } = Pincel(Borde);
        public Brush PincelTexto { get; } = Pincel(Texto);
    }

    public sealed record Marcada(MapaConcepto Cuenta, Colores Colores);

    // Colores vivos (como el resaltador) que se distinguen entre sí. El rojo queda para «varias».
    private static readonly Colores[] Paleta =
    {
        new("#FFE45C", "#D9B700", "#3D3000"), // amarillo
        new("#B57EDC", "#8E54BD", "#FFFFFF"), // lila
        new("#5DB8EE", "#2F8FCB", "#0B2F47"), // celeste
        new("#FFA94D", "#E0852A", "#3D1F00"), // naranja
        new("#7FCC6A", "#4E9E3A", "#123A08"), // verde
        new("#EC6FA8", "#C94A85", "#FFFFFF"), // rosa
        new("#4FC7BA", "#2A9E92", "#073531"), // turquesa
        new("#C9A27A", "#A07850", "#2E1C0A"), // café
    };

    public static readonly Colores Varias = new("#E04848", "#B52F2F", "#FFFFFF");
    public static readonly Colores Ninguna = new("#FFFFFF", "#D5DEE8", "#8696A7");

    private readonly List<Marcada> _marcadas;

    /// <param name="todas">Todas las cuentas con deuda (el color sale de su lugar en esta lista).</param>
    /// <param name="marcadas">Las cuentas elegidas para pintar.</param>
    public CuentasMapa(IReadOnlyList<MapaConcepto> todas, IReadOnlySet<Guid> marcadas)
    {
        _marcadas = todas
            .Select((c, i) => new Marcada(c, ColorDe(i)))
            .Where(m => marcadas.Contains(m.Cuenta.Id))
            .ToList();
    }

    public static Colores ColorDe(int posicion) => Paleta[posicion % Paleta.Length];

    public IReadOnlyList<Marcada> Marcadas => _marcadas;

    public bool Activa => _marcadas.Count > 0;

    /// <summary>Las cuentas marcadas que debe la unidad.</summary>
    public List<Marcada> QueDebe(MapaUnidad u) =>
        _marcadas.Where(m => u.Conceptos.Any(c => c.Id == m.Cuenta.Id)).ToList();

    public Colores ColoresDe(MapaUnidad u)
    {
        var debe = QueDebe(u);
        return debe.Count switch
        {
            0 => Ninguna,
            1 => debe[0].Colores,
            _ => Varias
        };
    }

    /// <summary>Cuántas unidades deben más de una de las cuentas marcadas.</summary>
    public int ConVarias(IEnumerable<MapaUnidad> unidades) => unidades.Count(u => QueDebe(u).Count > 1);

    /// <summary>Descripción para el reporte: «Alquiler de caseta, Escombros».</summary>
    public string Nombres() => string.Join(", ", _marcadas.Select(m => m.Cuenta.Nombre));

    private static Brush Pincel(string hex) =>
        Tema.Fijo(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

    public static (byte R, byte G, byte B) Rgb(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return (c.R, c.G, c.B);
    }

    public static string Monto(MapaDeuda d, CultureInfo cultura) =>
        $"{(d.Moneda == "BOB" ? "Bs" : d.Moneda == "USD" ? "$us" : d.Moneda)} {d.Total.ToString("N2", cultura)}";
}
