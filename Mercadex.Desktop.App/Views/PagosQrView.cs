using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Dtos;
using Mercadex.Desktop.App.Services;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Pagos QR enviados por los socios desde la app (observación de la
/// directiva: que la cajera se entere y los confirme desde el escritorio).
/// Antes de aprobar se verifica en la banca que el dinero llegó. Al aprobar,
/// el cobro se registra como pago QR y se emite el recibo a nombre de quien
/// aprobó; al rechazar, el socio ve el motivo en la app.
/// </summary>
public sealed class PagosQrView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private static readonly HttpClient Api = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(60));

    private readonly ComboBox _estado = UiKit.Combo(170);
    private readonly TextBlock _resumen = new() { Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly StackPanel _ficha = new();

    /// <summary>Se avisa a la ventana principal para actualizar el contador del menú.</summary>
    public static event Action? PagosRevisados;

    public PagosQrView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);
        arriba.Children.Add(UiKit.Encabezado("Pagos QR de la app"));
        arriba.Children.Add(UiKit.Ayuda(
            "Pagos que los socios hicieron con QR desde la app. Antes de aprobar, revisa en la banca que el dinero llegó: el mismo monto y el mismo número de transacción.", 10));

        _estado.ItemsSource = new List<UiKit.Item<string>>
        {
            new("pendiente", "Por revisar"),
            new("aprobado", "Aprobados"),
            new("rechazado", "Rechazados"),
            new("todos", "Todos")
        };
        _estado.SelectedIndex = 0;
        _estado.SelectionChanged += async (_, _) =>
        {
            if (IsLoaded)
                await CargarAsync();
        };

        var actualizar = UiKit.Boton("Actualizar");
        actualizar.Margin = new Thickness(0, 0, 0, 10);
        actualizar.VerticalAlignment = VerticalAlignment.Bottom;
        actualizar.Click += async (_, _) => await CargarAsync();

        var filtros = new WrapPanel();
        filtros.Children.Add(UiKit.Campo("Mostrar", _estado));
        filtros.Children.Add(actualizar);
        arriba.Children.Add(UiKit.Tarjeta(filtros, 8));
        arriba.Children.Add(_resumen);
        raiz.Children.Add(arriba);

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(430) });

        _tabla.Columns.Add(UiKit.Columna("Enviado", nameof(PagoQr.FechaTexto), 1.1));
        _tabla.Columns.Add(UiKit.Columna("Socio", nameof(PagoQr.Socio), 1.7));
        _tabla.Columns.Add(UiKit.Columna("Caseta", nameof(PagoQr.Caseta), 0.6));
        _tabla.Columns.Add(UiKit.Columna("Monto", nameof(PagoQr.MontoTexto), 1, derecha: true));
        _tabla.Columns.Add(UiKit.Columna("Transacción", nameof(PagoQr.Referencia), 1.1));
        _tabla.Columns.Add(UiKit.Columna("Estado", nameof(PagoQr.EstadoTexto), 0.9));

        var estilo = new Style(typeof(DataGridRow), UiKit.Estilo("Mx.Row"));
        var pendiente = new DataTrigger { Binding = new Binding(nameof(PagoQr.Pendiente)), Value = true };
        pendiente.Setters.Add(new Setter(FontWeightProperty, FontWeights.SemiBold));
        estilo.Triggers.Add(pendiente);
        _tabla.RowStyle = estilo;
        _tabla.SelectionChanged += async (_, _) => await MostrarFichaAsync();
        Grid.SetColumn(_tabla, 0);
        cuerpo.Children.Add(_tabla);

        var panel = new Border
        {
            Style = UiKit.Estilo("Mx.Card"),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _ficha }
        };
        Grid.SetColumn(panel, 2);
        cuerpo.Children.Add(panel);

        raiz.Children.Add(cuerpo);
        Content = raiz;

        MostrarVacio();
        Loaded += async (_, _) => await CargarAsync();
    }

    // =====================================================================

    private async Task CargarAsync(Guid? seleccionar = null)
    {
        seleccionar ??= (_tabla.SelectedItem as PagoQr)?.Id;
        var estado = (_estado.SelectedItem as UiKit.Item<string>)?.Valor ?? "pendiente";

        try
        {
            _resumen.Text = "Cargando…";
            var filas = await ApiJson.GetAsync<List<PagoQr>>($"/api/panel/pagos?estado={estado}") ?? new();
            _tabla.ItemsSource = filas;

            _resumen.Text = filas.Count == 0
                ? estado == "pendiente" ? "No hay pagos QR por revisar." : "No hay pagos en esta lista."
                : estado == "pendiente"
                    ? $"{filas.Count} {(filas.Count == 1 ? "pago por revisar" : "pagos por revisar")}   ·   " +
                      string.Join(" + ", filas.GroupBy(f => f.Moneda).Select(g => $"{g.Key} {g.Sum(f => f.Monto).ToString("N2", Bo)}"))
                    : $"{filas.Count} pagos";

            if (seleccionar is { } id && filas.FirstOrDefault(f => f.Id == id) is { } fila)
                _tabla.SelectedItem = fila;
            else if (filas.Count > 0 && estado == "pendiente")
                _tabla.SelectedItem = filas[0];
            else
                MostrarVacio();
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudieron cargar los pagos: " + ex.Message;
        }
    }

    private void MostrarVacio()
    {
        _ficha.Children.Clear();
        _ficha.Children.Add(new TextBlock
        {
            Text = "Elige un pago de la lista para ver el comprobante y aprobarlo o rechazarlo.",
            Foreground = UiKit.Suave,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
        });
    }

    private async Task MostrarFichaAsync()
    {
        if (_tabla.SelectedItem is not PagoQr p)
        {
            MostrarVacio();
            return;
        }

        _ficha.Children.Clear();

        var cabeza = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var (fondo, tinta) = p.Estado switch
        {
            "Aprobado" => ("#E7F5E7", "#0B5A0B"),
            "Rechazado" => ("#FDECEC", "#A12A2A"),
            _ => ("#FFF4D6", "#7A5200")
        };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = ColoresPanel.Hex(fondo),
            Child = new TextBlock { Text = p.EstadoTexto, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex(tinta) }
        };
        DockPanel.SetDock(chip, Dock.Right);
        cabeza.Children.Add(chip);
        cabeza.Children.Add(new TextBlock { Text = p.MontoTexto, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo });
        _ficha.Children.Add(cabeza);
        _ficha.Children.Add(UiKit.Ayuda($"Enviado el {p.FechaTexto}", 10));

        _ficha.Children.Add(Ficha(new List<(string, string)>
        {
            ("Socio", p.Socio),
            ("Carnet", string.IsNullOrWhiteSpace(p.Documento) || p.Documento == "SIN_DOCUMENTO" ? "—" : p.Documento!),
            ("Caseta", $"{p.Caseta}{(string.IsNullOrWhiteSpace(p.Bloque) ? "" : $" · Bloque {p.Bloque}")}"),
            ("Paga", p.Deudas),
            ("N.º de transacción", string.IsNullOrWhiteSpace(p.Referencia) ? "—" : p.Referencia!)
        }));

        // ---------- Comprobante ----------

        var marco = new Border
        {
            Background = UiKit.Fondo,
            CornerRadius = new CornerRadius(8),
            MinHeight = 120,
            MaxHeight = 360,
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            ToolTip = "Clic para ver el comprobante en grande",
            Child = new TextBlock { Text = "Cargando comprobante…", Foreground = UiKit.Suave, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        marco.MouseLeftButtonUp += (_, _) => _ = AbrirComprobanteAsync(p);
        _ficha.Children.Add(new TextBlock { Text = "Comprobante del banco", FontWeight = FontWeights.SemiBold, Foreground = UiKit.Etiqueta, Margin = new Thickness(0, 4, 0, 4) });
        _ficha.Children.Add(marco);
        _ = CargarComprobanteAsync(p, marco);

        // ---------- Resultado o acciones ----------

        if (p.Estado == "Aprobado")
        {
            var ok = UiKit.Aviso(out var okTexto, "ok");
            okTexto.Text = $"Aprobado por {p.RevisadoPor}. Recibo N.º {p.NumeroRecibo}.";
            _ficha.Children.Add(ok);
            return;
        }

        if (p.Estado == "Rechazado")
        {
            var no = UiKit.Aviso(out var noTexto, "error");
            noTexto.Text = $"Rechazado por {p.RevisadoPor}.\nMotivo que ve el socio: {p.MotivoRechazo}";
            _ficha.Children.Add(no);
            return;
        }

        if (!(SesionActual.PuedeCobrar || !SesionActual.Iniciada))
        {
            _ficha.Children.Add(UiKit.Ayuda("Solo el administrador o la cajera pueden aprobar o rechazar pagos.", 0));
            return;
        }

        var verificado = new CheckBox
        {
            Content = new TextBlock
            {
                Text = $"Verifiqué en la banca que llegó {p.MontoTexto} con la transacción {(string.IsNullOrWhiteSpace(p.Referencia) ? "indicada" : p.Referencia)}.",
                TextWrapping = TextWrapping.Wrap
            },
            Margin = new Thickness(0, 10, 0, 10)
        };
        _ficha.Children.Add(verificado);

        var acciones = new StackPanel { Orientation = Orientation.Horizontal };
        var aprobar = UiKit.Boton("Aprobar y emitir recibo", principal: true);
        aprobar.IsEnabled = false;
        aprobar.Margin = new Thickness(0, 0, 10, 0);
        verificado.Checked += (_, _) => aprobar.IsEnabled = true;
        verificado.Unchecked += (_, _) => aprobar.IsEnabled = false;
        aprobar.Click += async (_, _) => await AprobarAsync(p, aprobar);

        var rechazar = UiKit.Boton("Rechazar", peligro: true);
        rechazar.Click += async (_, _) => await RechazarAsync(p);

        acciones.Children.Add(aprobar);
        acciones.Children.Add(rechazar);
        _ficha.Children.Add(acciones);
        _ficha.Children.Add(UiKit.Ayuda(
            "Al aprobar, las deudas pasan a Pagadas y se emite el recibo a tu nombre. Si el dinero no llegó o el monto no coincide, rechaza el pago e indica el motivo: el socio lo verá en la app.", 0));

        await Task.CompletedTask;
    }

    // =====================================================================

    private async Task AprobarAsync(PagoQr p, Button boton)
    {
        var confirmar = Dialogo.Mostrar(Window.GetWindow(this),
            $"Socio: {p.Socio}\nCaseta: {p.Caseta}\nMonto: {p.MontoTexto}\nTransacción: {p.Referencia}\n\n" +
            "Se registrará el cobro como pago QR y se emitirá el recibo a tu nombre.\n\n¿Aprobar el pago?",
            "Aprobar pago QR", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmar != MessageBoxResult.Yes)
            return;

        boton.IsEnabled = false;
        try
        {
            var r = await ApiJson.PostAsync<Resultado>($"/api/panel/pagos/{p.Id}/aprobar",
                new { revisadoPor = SesionActual.Nombre, motivo = (string?)null });

            PagosRevisados?.Invoke();
            await CargarAsync();

            var abrir = Dialogo.Mostrar(Window.GetWindow(this),
                (r?.Mensaje ?? "Pago aprobado.") + "\n\n¿Ver e imprimir el recibo ahora?",
                "Pago aprobado", MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (abrir == MessageBoxResult.Yes && r?.ReciboId is { } reciboId)
                await AbrirReciboAsync(reciboId, p);
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this),
                ex.Message + "\n\nSi la cajera ya cobró esas deudas en ventanilla, rechaza este pago indicando el motivo.",
                "No se aprobó el pago", MessageBoxButton.OK, MessageBoxImage.Error);
            boton.IsEnabled = true;
        }
    }

    private async Task RechazarAsync(PagoQr p)
    {
        var motivo = Dialogo.PedirTexto(Window.GetWindow(this), "Rechazar pago QR",
            $"Socio: {p.Socio} · {p.MontoTexto}\n\nEl socio verá este motivo en la app. Sus deudas siguen pendientes.",
            "Motivo del rechazo (por ejemplo: el monto depositado no coincide)", "Rechazar pago", minimo: 5, peligro: true);
        if (motivo is null)
            return;

        try
        {
            await ApiJson.PostAsync<Resultado>($"/api/panel/pagos/{p.Id}/rechazar",
                new { revisadoPor = SesionActual.Nombre, motivo });
            PagosRevisados?.Invoke();
            await CargarAsync();
            _resumen.Text = "✓ Pago rechazado. El socio verá el motivo en la app.";
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se rechazó el pago", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AbrirReciboAsync(Guid reciboId, PagoQr p)
    {
        try
        {
            var api = new Api.ApiClient();
            var recibo = await api.GetAsync<ReciboDto>($"/api/Caja/recibos/{reciboId}");
            var pago = recibo is null ? null : await api.GetAsync<PagoDto>($"/api/Caja/pagos/{recibo.PagoId}");
            if (recibo is null || pago is null)
                return;

            new VistaPreviaReciboWindow(recibo, pago, p.Caseta, p.Bloque ?? "", p.Moneda)
            {
                Owner = Window.GetWindow(this)
            }.ShowDialog();
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message + "\n\nPuedes imprimirlo desde Recibos.",
                "No se pudo abrir el recibo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task CargarComprobanteAsync(PagoQr p, Border marco)
    {
        try
        {
            using var r = await Api.GetAsync($"/api/panel/pagos/{p.Id}/comprobante");
            r.EnsureSuccessStatusCode();
            var tipo = r.Content.Headers.ContentType?.MediaType ?? "";
            var bytes = await r.Content.ReadAsByteArrayAsync();

            if (!tipo.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                marco.Child = new TextBlock { Text = "Comprobante en PDF. Clic para abrirlo.", Foreground = UiKit.Tinta, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                return;
            }

            var imagen = new BitmapImage();
            using (var flujo = new MemoryStream(bytes))
            {
                imagen.BeginInit();
                imagen.CacheOption = BitmapCacheOption.OnLoad;
                imagen.DecodePixelWidth = 800;
                imagen.StreamSource = flujo;
                imagen.EndInit();
            }
            imagen.Freeze();

            if ((_tabla.SelectedItem as PagoQr)?.Id == p.Id)
                marco.Child = new Image { Source = imagen, Stretch = Stretch.Uniform, Margin = new Thickness(6) };
        }
        catch
        {
            marco.Child = new TextBlock { Text = "No se pudo mostrar el comprobante. Clic para intentar abrirlo.", Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Center };
        }
    }

    private async Task AbrirComprobanteAsync(PagoQr p)
    {
        try
        {
            using var r = await Api.GetAsync($"/api/panel/pagos/{p.Id}/comprobante");
            r.EnsureSuccessStatusCode();
            var tipo = r.Content.Headers.ContentType?.MediaType ?? "";
            var extension = tipo switch
            {
                "application/pdf" => ".pdf",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".jpg"
            };
            var ruta = Path.Combine(Path.GetTempPath(), $"mercadex-comprobante-qr-{p.Id.ToString("N")[..8]}{extension}");
            await File.WriteAllBytesAsync(ruta, await r.Content.ReadAsByteArrayAsync());
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se pudo abrir el comprobante", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Border Ficha(IEnumerable<(string Dato, string Valor)> filas)
    {
        var grilla = new Grid();
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grilla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var i = 0;
        foreach (var (dato, valor) in filas)
        {
            grilla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var e = new TextBlock { Text = dato, Foreground = UiKit.Suave, Margin = new Thickness(0, 2, 14, 2) };
            var v = new TextBlock { Text = valor, Foreground = UiKit.Tinta, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(e, i);
            Grid.SetRow(v, i);
            Grid.SetColumn(v, 1);
            grilla.Children.Add(e);
            grilla.Children.Add(v);
            i++;
        }

        return new Border
        {
            Background = UiKit.Fondo,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = grilla
        };
    }

    // =====================================================================

    private sealed class Resultado
    {
        public string? Mensaje { get; set; }
        public Guid? ReciboId { get; set; }
        public long? NumeroRecibo { get; set; }
    }

    public sealed class PagoQr
    {
        public Guid Id { get; set; }
        public string Socio { get; set; } = "";
        public string? Documento { get; set; }
        public string Caseta { get; set; } = "";
        public string? Bloque { get; set; }
        public string Moneda { get; set; } = "";
        public decimal Monto { get; set; }
        public string? Referencia { get; set; }
        public DateTimeOffset Fecha { get; set; }
        public string Estado { get; set; } = "";
        public string? MotivoRechazo { get; set; }
        public string? RevisadoPor { get; set; }
        public long? NumeroRecibo { get; set; }
        public string Deudas { get; set; } = "";

        public bool Pendiente => Estado == "Pendiente";
        public string EstadoTexto => Estado switch { "Aprobado" => "Aprobado", "Rechazado" => "Rechazado", _ => "Por revisar" };
        public string FechaTexto => Fecha.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy HH:mm", Bo);
        public string MontoTexto => $"{Moneda} {Monto.ToString("N2", Bo)}";
    }
}
