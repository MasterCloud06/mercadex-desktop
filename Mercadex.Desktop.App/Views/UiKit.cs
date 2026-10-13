using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Mercadex.Desktop.App.Views;

/// <summary>Piezas de interfaz con el estilo de Mercadex para pantallas hechas en código.</summary>
public static class UiKit
{
    public static readonly Brush Tinta = ColoresPanel.Hex("#12304D");
    public static readonly Brush Titulo = ColoresPanel.Hex("#173B5C");
    public static readonly Brush Suave = ColoresPanel.Hex("#5A6B7D");
    public static readonly Brush Etiqueta = ColoresPanel.Hex("#4A5E73");
    public static readonly Brush Fondo = ColoresPanel.Hex("#F3F6FA");

    public static Style Estilo(string clave) => (Style)Application.Current.FindResource(clave);

    public static TextBlock Encabezado(string texto) => new()
    {
        Text = texto,
        FontSize = 27,
        FontWeight = FontWeights.Bold,
        Foreground = Titulo
    };

    public static TextBlock Ayuda(string texto, double margenAbajo = 12) => new()
    {
        Text = texto,
        Foreground = Suave,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 20,
        Margin = new Thickness(0, 2, 0, margenAbajo)
    };

    public static TextBlock Subtitulo(string texto) => new()
    {
        Text = texto,
        FontSize = 17,
        FontWeight = FontWeights.SemiBold,
        Foreground = Titulo,
        Margin = new Thickness(0, 0, 0, 4)
    };

    public static Border Tarjeta(UIElement contenido, double margenAbajo = 12) => new()
    {
        Style = Estilo("Mx.Card"),
        Margin = new Thickness(0, 0, 0, margenAbajo),
        Child = contenido
    };

    /// <summary>Etiqueta arriba y el control debajo.</summary>
    public static StackPanel Campo(string etiqueta, FrameworkElement control, double margenDerecho = 14)
    {
        var pila = new StackPanel { Margin = new Thickness(0, 0, margenDerecho, 10), VerticalAlignment = VerticalAlignment.Bottom };
        pila.Children.Add(new TextBlock
        {
            Text = etiqueta,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Etiqueta,
            Margin = new Thickness(0, 0, 0, 5)
        });
        pila.Children.Add(control);
        return pila;
    }

    public static TextBox Texto(double ancho, int maximo = 120) => new()
    {
        Width = ancho,
        MaxLength = maximo,
        Style = Estilo("Mx.TextBox")
    };

    public static ComboBox Combo(double ancho) => new()
    {
        Width = ancho,
        Height = 36,
        FontSize = 14,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    public static DatePicker Fecha(DateTime? valor) => new()
    {
        Width = 150,
        Height = 36,
        FontSize = 14,
        SelectedDate = valor,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    public static Button Boton(string texto, bool principal = false, bool peligro = false) => new()
    {
        Content = texto,
        Height = 38,
        Padding = new Thickness(18, 0, 18, 0),
        VerticalAlignment = VerticalAlignment.Bottom,
        Style = Estilo(peligro ? "Mx.DangerButton" : principal ? "Mx.PrimaryButton" : "Mx.SecondaryButton")
    };

    public static RadioButton Opcion(string texto, string grupo, bool marcado = false) => new()
    {
        Content = texto,
        GroupName = grupo,
        IsChecked = marcado,
        FontSize = 14,
        Margin = new Thickness(0, 0, 22, 8),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    public static DataGrid Tabla() => new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        SelectionMode = DataGridSelectionMode.Single,
        Style = Estilo("Mx.DataGrid")
    };

    public static DataGridTextColumn Columna(string titulo, string propiedad, double peso, string? formato = null, bool derecha = false)
    {
        var columna = new DataGridTextColumn
        {
            Header = titulo,
            Binding = new Binding(propiedad) { StringFormat = formato },
            Width = new DataGridLength(peso, DataGridLengthUnitType.Star)
        };

        if (derecha)
        {
            columna.HeaderStyle = Estilo("Mx.ColumnHeaderRight");
            var celda = new Style(typeof(TextBlock));
            celda.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            celda.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0)));
            columna.ElementStyle = celda;
        }

        return columna;
    }

    /// <summary>Recuadro de aviso: "info", "ok", "aviso" o "error".</summary>
    public static Border Aviso(out TextBlock texto, string tono = "info")
    {
        var (fondo, borde, tinta) = tono switch
        {
            "ok" => ("#E7F5E7", "#BFE3BF", "#0B5A0B"),
            "aviso" => ("#FFF4D6", "#E9C46A", "#7A5200"),
            "error" => ("#FDECEC", "#F2C0C0", "#A12A2A"),
            _ => ("#E7F0F9", "#D7E6F4", "#1F4E79")
        };

        texto = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = ColoresPanel.Hex(tinta),
            LineHeight = 20
        };

        return new Border
        {
            Background = ColoresPanel.Hex(fondo),
            BorderBrush = ColoresPanel.Hex(borde),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 12),
            Child = texto
        };
    }

    /// <summary>Elemento de lista con texto y valor (para ComboBox).</summary>
    public sealed record Item<T>(T Valor, string Texto)
    {
        public override string ToString() => Texto;
    }
}
