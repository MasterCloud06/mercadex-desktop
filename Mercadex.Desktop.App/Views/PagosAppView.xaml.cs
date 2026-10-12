using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

public sealed class PagoAppFila
{
    public Guid Id { get; init; }
    public string Socio { get; init; } = "";
    public string Documento { get; init; } = "";
    public string Caseta { get; init; } = "";
    public string? Bloque { get; init; }
    public string Moneda { get; init; } = "";
    public decimal Monto { get; init; }
    public string Referencia { get; init; } = "";
    public DateTimeOffset Fecha { get; init; }
    public string Estado { get; init; } = "";
    public string? MotivoRechazo { get; init; }
    public string? RevisadoPor { get; init; }
    public long? NumeroRecibo { get; init; }
    public string Deudas { get; init; } = "";

    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    public string FechaTexto => Fecha.ToString("dd/MM/yyyy HH:mm", Bo);
    public string MontoTexto => $"{(Moneda == "BOB" ? "Bs" : Moneda)} {Monto.ToString("N2", Bo)}";
}

/// <summary>
/// Revisión en caja de los pagos enviados desde la app de socios.
/// </summary>
public partial class PagosAppView : UserControl
{
    private static readonly string ArchivoRevisor = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mercadex", "revisor.txt");

    private bool _cargando;

    public PagosAppView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await CargarAsync();
    }

    private string FiltroActual => (CmbFiltro.SelectedItem as ComboBoxItem)?.Tag as string ?? "pendiente";

    private async void Actualizar_Click(object sender, RoutedEventArgs e) => await CargarAsync();

    private async void Filtro_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            await CargarAsync();
    }

    private void Configuracion_Click(object sender, RoutedEventArgs e) =>
        new ConfiguracionPagoAppWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private async Task CargarAsync(Guid? seleccionar = null)
    {
        if (_cargando)
            return;

        _cargando = true;
        BtnActualizar.IsEnabled = false;

        try
        {
            var r = await ApiSimple.GetAsync($"/api/pagos-app?estado={FiltroActual}");
            var filas = r.EnumerateArray().Select(x => new PagoAppFila
            {
                Id = x.GetProperty("id").GetGuid(),
                Socio = x.Texto("socio"),
                Documento = x.Texto("documento"),
                Caseta = x.Texto("caseta"),
                Bloque = x.Texto("bloque"),
                Moneda = x.Texto("moneda"),
                Monto = x.GetProperty("monto").GetDecimal(),
                Referencia = x.Texto("referencia"),
                Fecha = x.GetProperty("fecha").GetDateTimeOffset(),
                Estado = x.Texto("estado"),
                MotivoRechazo = x.Texto("motivoRechazo"),
                RevisadoPor = x.Texto("revisadoPor"),
                NumeroRecibo = x.TryGetProperty("numeroRecibo", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt64() : null,
                Deudas = x.Texto("deudas")
            }).ToList();

            Tabla.ItemsSource = filas;

            var pendientes = filas.Count(f => f.Estado == "Pendiente");
            TxtEstado.Text = FiltroActual == "pendiente"
                ? (pendientes == 0
                    ? "No hay comprobantes por revisar."
                    : $"{pendientes} {(pendientes == 1 ? "comprobante" : "comprobantes")} por revisar. Comprueba el depósito en el banco antes de aprobar.")
                : $"{filas.Count} pagos.";

            var elegido = filas.FirstOrDefault(f => f.Id == seleccionar) ?? filas.FirstOrDefault();
            Tabla.SelectedItem = elegido;
            if (elegido == null)
                MostrarVacio();
        }
        catch (Exception ex)
        {
            TxtEstado.Text = "No se pudo consultar: " + ex.Message;
        }
        finally
        {
            _cargando = false;
            BtnActualizar.IsEnabled = true;
        }
    }

    private void MostrarVacio()
    {
        PanelDetalle.Children.Clear();
        PanelDetalle.Children.Add(new TextBlock { Text = "Nada que mostrar", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex("#12304D") });
    }

    private async void Tabla_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tabla.SelectedItem is PagoAppFila fila)
            await MostrarDetalleAsync(fila);
    }

    private async Task MostrarDetalleAsync(PagoAppFila f)
    {
        PanelDetalle.Children.Clear();

        PanelDetalle.Children.Add(new TextBlock { Text = f.MontoTexto, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = ColoresPanel.Hex("#12304D") });
        PanelDetalle.Children.Add(new TextBlock
        {
            Text = $"Caseta {f.Caseta}" + (string.IsNullOrEmpty(f.Bloque) ? "" : $" · Bloque {f.Bloque}"),
            Foreground = ColoresPanel.Hex("#5A6B7D"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        Fila("Socio", $"{f.Socio} (CI {f.Documento})");
        Fila("Deudas que paga", f.Deudas);
        Fila("N.º de transacción", f.Referencia);
        Fila("Enviado", f.FechaTexto);

        // Comprobante.
        var imagen = new Image { MaxHeight = 360, Stretch = Stretch.Uniform, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Clic para ver en grande" };
        var marco = new Border
        {
            BorderBrush = ColoresPanel.Hex("#E1E7EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(4),
            Child = new TextBlock { Text = "Cargando comprobante...", Foreground = ColoresPanel.Hex("#5A6B7D"), Margin = new Thickness(8) }
        };
        PanelDetalle.Children.Add(marco);

        try
        {
            var bytes = await ApiSimple.GetBytesAsync($"/api/pagos-app/{f.Id}/comprobante");
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            imagen.Source = bmp;
            imagen.MouseLeftButtonUp += (_, _) => VerEnGrande(bmp, f);
            marco.Child = imagen;
        }
        catch (Exception ex)
        {
            marco.Child = new TextBlock { Text = "No se pudo cargar el comprobante: " + ex.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
        }

        if (f.Estado != "Pendiente")
        {
            Fila("Resultado", f.Estado == "Aprobado"
                ? $"Aprobado por {f.RevisadoPor}. Recibo N.º {f.NumeroRecibo}."
                : $"Rechazado por {f.RevisadoPor}: {f.MotivoRechazo}");
            return;
        }

        // Revisión.
        PanelDetalle.Children.Add(new TextBlock { Text = "Revisado por", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex("#4A5E73"), Margin = new Thickness(0, 16, 0, 4) });
        var revisor = new TextBox { Height = 36, Text = LeerRevisor(), Tag = "Tu nombre" };
        PanelDetalle.Children.Add(revisor);

        var aprobar = new Button
        {
            Content = "Aprobar y emitir recibo",
            Style = (Style)Application.Current.FindResource("Mx.PrimaryButton"),
            Background = ColoresPanel.Hex("#287A42"),
            BorderBrush = ColoresPanel.Hex("#287A42"),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        PanelDetalle.Children.Add(aprobar);

        PanelDetalle.Children.Add(new TextBlock { Text = "Motivo del rechazo (lo verá el socio)", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex("#4A5E73"), Margin = new Thickness(0, 18, 0, 4) });
        var motivo = new TextBox { Height = 64, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 300, Tag = "Ej.: el monto depositado no coincide" };
        PanelDetalle.Children.Add(motivo);

        var rechazar = new Button
        {
            Content = "Rechazar",
            Style = (Style)Application.Current.FindResource("Mx.SecondaryButton"),
            Foreground = ColoresPanel.Hex("#B42318"),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        PanelDetalle.Children.Add(rechazar);

        aprobar.Click += async (_, _) =>
        {
            if (MessageBox.Show(Window.GetWindow(this),
                    $"¿Confirmas que el depósito de {f.MontoTexto} (transacción {f.Referencia}) está en la cuenta de la asociación?\n\nSe emitirá el recibo y las deudas quedarán pagadas.",
                    "Aprobar pago", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await RevisarAsync($"/api/pagos-app/{f.Id}/aprobar", new { revisadoPor = revisor.Text }, revisor.Text, aprobar, rechazar, f.Id);
        };

        rechazar.Click += async (_, _) =>
            await RevisarAsync($"/api/pagos-app/{f.Id}/rechazar", new { revisadoPor = revisor.Text, motivo = motivo.Text }, revisor.Text, aprobar, rechazar, f.Id);
    }

    private async Task RevisarAsync(string url, object cuerpo, string revisor, Button aprobar, Button rechazar, Guid id)
    {
        aprobar.IsEnabled = rechazar.IsEnabled = false;
        try
        {
            var r = await ApiSimple.PostAsync(url, cuerpo);
            GuardarRevisor(revisor);
            TxtEstado.Text = r.Texto("mensaje");
            await CargarAsync(id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "No se pudo completar", MessageBoxButton.OK, MessageBoxImage.Warning);
            aprobar.IsEnabled = rechazar.IsEnabled = true;
        }
    }

    private void Fila(string etiqueta, string valor)
    {
        PanelDetalle.Children.Add(new TextBlock { Text = etiqueta, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex("#5A6B7D"), Margin = new Thickness(0, 10, 0, 1) });
        PanelDetalle.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(valor) ? "—" : valor, TextWrapping = TextWrapping.Wrap, Foreground = ColoresPanel.Hex("#1E2A38") });
    }

    private void VerEnGrande(BitmapSource imagen, PagoAppFila f)
    {
        new Window
        {
            Title = $"Comprobante — Caseta {f.Caseta} — {f.MontoTexto}",
            Width = 760,
            Height = 900,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ColoresPanel.Hex("#1E2A38"),
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Image { Source = imagen, Stretch = Stretch.Uniform, Margin = new Thickness(10) }
            }
        }.Show();
    }

    private static string LeerRevisor()
    {
        try { return File.Exists(ArchivoRevisor) ? File.ReadAllText(ArchivoRevisor).Trim() : ""; }
        catch { return ""; }
    }

    private static void GuardarRevisor(string nombre)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ArchivoRevisor)!);
            File.WriteAllText(ArchivoRevisor, nombre.Trim());
        }
        catch
        {
            // No es crítico: solo evita volver a escribir el nombre.
        }
    }
}
