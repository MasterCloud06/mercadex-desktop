using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Anular una deuda o varias: primero la vista previa (cuántas, cuánto y
/// cuáles no se pueden anular) y el botón para confirmar. El motivo es
/// opcional. Nada cambia hasta pulsar «Anular». La API registra quién
/// anuló, cuándo y, si se escribió, por qué.
/// </summary>
internal sealed class VentanaAnularDeudas : Window
{
    public const string Ruta = "/api/obligaciones-anulaciones";

    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly Dictionary<string, object?> _pedido;

    // Una clave por confirmación: si el envío se repite, la API no anula dos veces.
    private readonly Guid _clave = Guid.NewGuid();

    private readonly TextBlock _resumen = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _filtros = new() { Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10) };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly TextBlock _bloqueadas = new() { TextWrapping = TextWrapping.Wrap, Foreground = UiKit.Suave, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBox _motivo = UiKit.Texto(560, 500);
    private readonly TextBlock _error = new() { Foreground = ColoresPanel.Hex("#A12A2A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button _anular = UiKit.Boton("Anular", peligro: true);
    private readonly StackPanel _confirmacion = new();

    private VistaPrevia? _previa;

    public string Mensaje { get; private set; } = "";

    /// <param name="pedido">obligacionIds, o los filtros (conceptoCobroId, anio, mes, caseta, bloque).</param>
    public VentanaAnularDeudas(Dictionary<string, object?> pedido)
    {
        _pedido = pedido;

        Title = "Anular deudas";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var pila = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        pila.Children.Add(new TextBlock { Text = "Anular deudas", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta });
        pila.Children.Add(UiKit.Ayuda(
            "La deuda no se borra: queda «Anulada», deja de sumar en los saldos y no se vuelve a generar. " +
            "Nada cambia hasta que pulses «Anular».", 12));

        pila.Children.Add(_resumen);
        pila.Children.Add(_filtros);

        _tabla.Columns.Add(UiKit.Columna("Caseta", nameof(FilaPrevia.Caseta), 0.8));
        _tabla.Columns.Add(UiKit.Columna("Concepto", nameof(FilaPrevia.Concepto), 1.8));
        _tabla.Columns.Add(UiKit.Columna("Período", nameof(FilaPrevia.Periodo), 0.9));
        _tabla.Columns.Add(UiKit.Columna("Monto", nameof(FilaPrevia.Monto), 0.9, "N2", derecha: true));
        _tabla.Columns.Add(UiKit.Columna("Moneda", nameof(FilaPrevia.Moneda), 0.7));
        _tabla.MaxHeight = 230;
        pila.Children.Add(_tabla);
        pila.Children.Add(_bloqueadas);

        _motivo.Height = 54;
        _motivo.TextWrapping = TextWrapping.Wrap;
        _motivo.AcceptsReturn = true;
        _motivo.VerticalContentAlignment = VerticalAlignment.Top;

        _confirmacion.Margin = new Thickness(0, 14, 0, 0);
        _confirmacion.Children.Add(UiKit.Campo("Motivo (opcional)", _motivo));
        _confirmacion.Visibility = Visibility.Collapsed;
        pila.Children.Add(_confirmacion);

        pila.Children.Add(_error);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelar = UiKit.Boton("Cancelar");
        cancelar.IsCancel = true;
        cancelar.Margin = new Thickness(0, 0, 10, 0);
        _anular.IsEnabled = false;
        _anular.Click += async (_, _) => await AnularAsync();
        botones.Children.Add(cancelar);
        botones.Children.Add(_anular);
        pila.Children.Add(botones);

        Content = pila;
        Loaded += async (_, _) => await CargarAsync();
    }

    private async Task CargarAsync()
    {
        _error.Text = "";
        _resumen.Text = "Preparando la vista previa…";
        _previa = null;
        Actualizar();

        try
        {
            _previa = await ApiJson.PostAsync<VistaPrevia>($"{Ruta}/vista-previa", _pedido);
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudo preparar la vista previa.";
            _error.Text = ex.Message;
            return;
        }

        if (_previa is null)
            return;

        var total = string.Join(" + ", _previa.Totales.Select(t => $"{t.Moneda} {t.Total.ToString("N2", Bo)}"));
        _resumen.Text = _previa.Cantidad switch
        {
            0 => "No hay deudas que se puedan anular.",
            1 => $"Se anulará 1 deuda · {total}",
            _ => $"Se anularán {_previa.Cantidad} deudas · {total}"
        };
        _filtros.Text = _previa.Filtros;
        _tabla.ItemsSource = _previa.Deudas;
        _tabla.Visibility = _previa.Deudas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _bloqueadas.Text = _previa.Bloqueadas.Count == 0
            ? ""
            : $"No se anularán {_previa.Bloqueadas.Count}:\n" + string.Join("\n", _previa.Bloqueadas.Take(15)
                .Select(b => $"• {b.Caseta} · {b.Concepto} {b.Periodo} · {b.Moneda} {b.Monto.ToString("N2", Bo)}: {b.Motivo}")) +
              (_previa.Bloqueadas.Count > 15 ? $"\n… y {_previa.Bloqueadas.Count - 15} más." : "");

        _confirmacion.Visibility = _previa.Cantidad > 0 ? Visibility.Visible : Visibility.Collapsed;
        _anular.Content = _previa.Cantidad == 1 ? "Anular 1 deuda" : $"Anular {_previa.Cantidad} deudas";
        Actualizar();
    }

    private void Actualizar() =>
        _anular.IsEnabled = _previa is { Cantidad: > 0 };

    private async Task AnularAsync()
    {
        if (_previa is null || !_anular.IsEnabled)
            return;

        _anular.IsEnabled = false;
        _error.Text = "";

        var cuerpo = new Dictionary<string, object?>(_pedido)
        {
            ["motivo"] = _motivo.Text.Trim(),
            ["confirmo"] = true,
            ["claveOperacion"] = _clave,
            ["huella"] = _previa.Huella,
            ["cantidadEsperada"] = _previa.Cantidad
        };

        try
        {
            var r = await ApiJson.PostAsync<Respuesta>(Ruta, cuerpo);
            Mensaje = r?.Mensaje ?? "Deudas anuladas.";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            // Si cambió algo desde la vista previa, la API no anula nada: se vuelve a cargar.
            _error.Text = ex.Message + "\nSe actualizó la vista previa: revísala otra vez.";
            await CargarAsync();
            _error.Text = ex.Message + "\nSe actualizó la vista previa: revísala otra vez.";
        }
    }

    private sealed class VistaPrevia
    {
        public string Filtros { get; set; } = "";
        public int Cantidad { get; set; }
        public string Huella { get; set; } = "";
        public List<TotalPrevia> Totales { get; set; } = new();
        public List<FilaPrevia> Deudas { get; set; } = new();
        public List<FilaBloqueada> Bloqueadas { get; set; } = new();
    }

    private sealed class TotalPrevia
    {
        public string Moneda { get; set; } = "";
        public decimal Total { get; set; }
    }

    public sealed class FilaPrevia
    {
        public string Caseta { get; set; } = "";
        public string Concepto { get; set; } = "";
        public string Periodo { get; set; } = "";
        public decimal Monto { get; set; }
        public string Moneda { get; set; } = "";
    }

    private sealed class FilaBloqueada
    {
        public string Caseta { get; set; } = "";
        public string Concepto { get; set; } = "";
        public string Periodo { get; set; } = "";
        public decimal Monto { get; set; }
        public string Moneda { get; set; } = "";
        public string Motivo { get; set; } = "";
    }

    private sealed class Respuesta
    {
        public string? Mensaje { get; set; }
    }
}
