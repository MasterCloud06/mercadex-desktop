using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Mercadex.Desktop.App.Views;

/// <summary>Un punto de un gráfico: etiqueta, valor y texto del tooltip.</summary>
public sealed record PuntoGrafico(string Etiqueta, decimal Valor, string Detalle);

/// <summary>Una fila de barras horizontales.</summary>
public sealed record FilaBarra(string Etiqueta, decimal Valor, string Texto, Brush Color, string Detalle);

/// <summary>Paleta del panel (serie única azul + estados).</summary>
public static class ColoresPanel
{
    public static readonly Brush Serie = Hex("#2A78D6");
    public static readonly Brush SerieHover = Hex("#1C5CAB");
    public static readonly Brush Pista = Tema.Pincel(Hex("#EEF2F7"), Tema.Rol.Fondo);
    public static readonly Brush Rejilla = Tema.Pincel(Hex("#E6ECF2"), Tema.Rol.Borde);
    public static readonly Brush TextoPrincipal = Tema.Pincel(Hex("#173B5C"), Tema.Rol.Texto);
    public static readonly Brush TextoSecundario = Tema.Pincel(Hex("#5A6B7D"), Tema.Rol.Texto);
    public static readonly Brush TextoTenue = Tema.Pincel(Hex("#7A8A9C"), Tema.Rol.Texto);

    // Estados (antigüedad de deuda): siempre van con etiqueta de texto.
    public static readonly Brush Bueno = Hex("#0CA30C");
    public static readonly Brush Aviso = Hex("#FAB219");
    public static readonly Brush Serio = Hex("#EC835A");
    public static readonly Brush Critico = Hex("#D03B3B");

    public static SolidColorBrush Hex(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
}

/// <summary>
/// Gráfico de columnas de una sola serie, dibujado con WPF puro.
/// Rejilla tenue, columnas finas con extremos redondeados y
/// un tooltip por columna (la zona de hover ocupa toda la columna).
/// </summary>
public sealed class GraficoColumnas : Canvas
{
    private IReadOnlyList<PuntoGrafico> _puntos = Array.Empty<PuntoGrafico>();
    private Func<decimal, string> _formatoEje = v => v.ToString("N0");
    private int _cadaEtiqueta = 1;

    public GraficoColumnas()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Dibujar();
    }

    public void Mostrar(
        IReadOnlyList<PuntoGrafico> puntos,
        Func<decimal, string> formatoEje,
        int cadaEtiqueta = 1)
    {
        _puntos = puntos;
        _formatoEje = formatoEje;
        _cadaEtiqueta = Math.Max(1, cadaEtiqueta);
        Dibujar();
    }

    private void Dibujar()
    {
        Children.Clear();

        if (ActualWidth < 60 || ActualHeight < 60 || _puntos.Count == 0)
            return;

        const double margenIzq = 64;
        const double margenInf = 26;
        const double margenSup = 8;

        var ancho = ActualWidth - margenIzq - 4;
        var alto = ActualHeight - margenInf - margenSup;

        var maximo = _puntos.Max(p => p.Valor);
        var tope = EscalaBonita(maximo);

        // Rejilla y eje Y (4 divisiones).
        for (var i = 0; i <= 4; i++)
        {
            var valor = tope * i / 4m;
            var y = margenSup + alto - (double)(valor / tope) * alto;

            Children.Add(new Line
            {
                X1 = margenIzq,
                X2 = margenIzq + ancho,
                Y1 = y,
                Y2 = y,
                Stroke = i == 0 ? ColoresPanel.TextoTenue : ColoresPanel.Rejilla,
                StrokeThickness = 1
            });

            var etiqueta = Texto(_formatoEje(valor), 11, ColoresPanel.TextoTenue);
            etiqueta.Width = margenIzq - 8;
            etiqueta.TextAlignment = TextAlignment.Right;
            SetLeft(etiqueta, 0);
            SetTop(etiqueta, y - 8);
            Children.Add(etiqueta);
        }

        var ranura = ancho / _puntos.Count;
        var anchoBarra = Math.Min(28, Math.Max(3, ranura * 0.62));

        for (var i = 0; i < _puntos.Count; i++)
        {
            var p = _puntos[i];
            var x = margenIzq + ranura * i;
            var h = tope == 0 ? 0 : (double)(p.Valor / tope) * alto;

            var barra = new Border
            {
                Width = anchoBarra,
                Height = Math.Max(0, h),
                Background = ColoresPanel.Serie,
                CornerRadius = new CornerRadius(Math.Min(4, anchoBarra / 2), Math.Min(4, anchoBarra / 2), 0, 0),
                IsHitTestVisible = false
            };

            SetLeft(barra, x + (ranura - anchoBarra) / 2);
            SetTop(barra, margenSup + alto - h);
            Children.Add(barra);

            // Zona de hover más grande que la barra.
            var zona = new Rectangle
            {
                Width = ranura,
                Height = alto + margenSup,
                Fill = Brushes.Transparent,
                ToolTip = p.Detalle,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            ToolTipService.SetInitialShowDelay(zona, 50);
            zona.MouseEnter += (_, _) => barra.Background = ColoresPanel.SerieHover;
            zona.MouseLeave += (_, _) => barra.Background = ColoresPanel.Serie;
            SetLeft(zona, x);
            SetTop(zona, 0);
            Children.Add(zona);

            var mostrarEtiqueta =
                i % _cadaEtiqueta == 0 || i == _puntos.Count - 1;

            if (mostrarEtiqueta &&
                !(i != _puntos.Count - 1 && _puntos.Count - 1 - i < _cadaEtiqueta / 2.0))
            {
                var et = Texto(p.Etiqueta, 11, ColoresPanel.TextoSecundario);
                et.Width = Math.Max(ranura, 44);
                et.TextAlignment = TextAlignment.Center;
                SetLeft(et, x + ranura / 2 - et.Width / 2);
                SetTop(et, margenSup + alto + 6);
                Children.Add(et);
            }
        }
    }

    private static TextBlock Texto(string texto, double tamano, Brush color) =>
        new() { Text = texto, FontSize = tamano, Foreground = color };

    /// <summary>Redondea el máximo a 1, 2, 2.5 o 5 por potencia de 10.</summary>
    private static decimal EscalaBonita(decimal maximo)
    {
        if (maximo <= 0)
            return 100;

        var exp = Math.Floor(Math.Log10((double)maximo));
        var base10 = (decimal)Math.Pow(10, exp);
        foreach (var f in new[] { 1m, 2m, 2.5m, 5m, 10m })
        {
            if (maximo <= f * base10)
                return f * base10;
        }
        return 10 * base10;
    }
}

/// <summary>
/// Barras horizontales: etiqueta, barra proporcional y valor.
/// El ancho se resuelve con columnas en estrella (sin medir).
/// </summary>
public static class BarrasHorizontales
{
    public static void Llenar(Panel destino, IReadOnlyList<FilaBarra> filas, string textoVacio)
    {
        destino.Children.Clear();

        if (filas.Count == 0 || filas.All(f => f.Valor <= 0))
        {
            destino.Children.Add(new TextBlock
            {
                Text = textoVacio,
                Foreground = ColoresPanel.TextoTenue,
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        var maximo = filas.Max(f => f.Valor);

        foreach (var f in filas)
        {
            var fila = new Grid { Margin = new Thickness(0, 0, 0, 10), ToolTip = f.Detalle, Background = Brushes.Transparent };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });

            var etiqueta = new TextBlock
            {
                Text = f.Etiqueta,
                Foreground = ColoresPanel.TextoPrincipal,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 10, 0)
            };
            fila.Children.Add(etiqueta);

            var pista = new Grid { Height = 12, VerticalAlignment = VerticalAlignment.Center };
            var proporcion = maximo == 0 ? 0 : (double)(f.Valor / maximo);
            pista.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(proporcion, 0.0001), GridUnitType.Star) });
            pista.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - proporcion, 0.0001), GridUnitType.Star) });

            var fondo = new Border { Background = ColoresPanel.Pista, CornerRadius = new CornerRadius(4) };
            Grid.SetColumnSpan(fondo, 2);
            pista.Children.Add(fondo);

            if (f.Valor > 0)
            {
                pista.Children.Add(new Border
                {
                    Background = f.Color,
                    CornerRadius = new CornerRadius(4),
                    MinWidth = 4
                });
            }

            Grid.SetColumn(pista, 1);
            fila.Children.Add(pista);

            var valor = new TextBlock
            {
                Text = f.Texto,
                Foreground = ColoresPanel.TextoPrincipal,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valor, 2);
            fila.Children.Add(valor);

            destino.Children.Add(fila);
        }
    }

    public static string Bs(decimal v) =>
        "Bs " + v.ToString("N2", CultureInfo.GetCultureInfo("es-BO"));
}
