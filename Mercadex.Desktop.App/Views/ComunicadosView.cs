using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Reportes para los socios (comunicados): la directiva redacta un
/// comunicado, lo imprime con el membrete y, si quiere, lo publica en la app
/// de socios hasta una fecha. También genera las cartas de deuda, una hoja
/// por caseta, comedor o ambulante que debe.
/// </summary>
public sealed class ComunicadosView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly TextBlock _resumen = new() { Foreground = UiKit.Suave, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    private readonly RadioButton _verVigentes = UiKit.Opcion("Comunicados", "verComunicados", true);
    private readonly RadioButton _verArchivados = UiKit.Opcion("Archivados", "verComunicados");

    // Formulario
    private readonly TextBlock _tituloFicha = new() { FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _titulo = UiKit.Texto(double.NaN, 150);
    private readonly TextBox _texto = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 220,
        MaxLength = 4000,
        Padding = new Thickness(8, 6, 8, 6)
    };
    private readonly CheckBox _enApp = new() { Content = "Mostrar en la app de socios", Margin = new Thickness(0, 4, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _conVigencia = new() { Content = "Hasta el día:", VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 8, 0) };
    private readonly DatePicker _vigencia = UiKit.Fecha(DateTime.Today.AddDays(15));
    private readonly TextBlock _estado = new() { Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private readonly Button _guardar = UiKit.Boton("Guardar", principal: true);
    private readonly Button _imprimir = UiKit.Boton("Imprimir (PDF)");
    private readonly Button _archivar = UiKit.Boton("Archivar");
    private readonly Border _aviso;
    private readonly TextBlock _avisoTexto;

    private readonly bool _puedeEditar = Sesion.SesionActual.EsAdministrador;
    private ComunicadoDto? _actual;

    public ComunicadosView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        _aviso = UiKit.Aviso(out _avisoTexto, "error");
        _aviso.Visibility = Visibility.Collapsed;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        // ---------- Encabezado ----------
        var arriba = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(arriba, Dock.Top);

        var titulo = new DockPanel();
        var acciones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var cartas = UiKit.Boton("Cartas de deuda…");
        cartas.Click += (_, _) => AbrirCartas();
        acciones.Children.Add(cartas);
        if (_puedeEditar)
        {
            var nuevo = UiKit.Boton("+ Nuevo comunicado", principal: true);
            nuevo.Margin = new Thickness(10, 0, 0, 0);
            nuevo.Click += (_, _) => Nuevo();
            acciones.Children.Add(nuevo);
        }
        DockPanel.SetDock(acciones, Dock.Right);
        titulo.Children.Add(acciones);
        titulo.Children.Add(UiKit.Encabezado("Comunicados a los socios"));
        arriba.Children.Add(titulo);
        arriba.Children.Add(UiKit.Ayuda(
            "Redacta un comunicado de la directiva, imprímelo con el membrete de la asociación y, si quieres, publícalo en la app de socios hasta una fecha. " +
            "Las cartas de deuda se generan solas: una hoja por cada caseta, comedor o ambulante que debe.", 10));

        var barra = new StackPanel { Orientation = Orientation.Horizontal };
        _verVigentes.Checked += async (_, _) => await CargarAsync();
        _verArchivados.Checked += async (_, _) => await CargarAsync();
        barra.Children.Add(_verVigentes);
        barra.Children.Add(_verArchivados);
        barra.Children.Add(_resumen);
        arriba.Children.Add(barra);
        arriba.Children.Add(_aviso);
        raiz.Children.Add(arriba);

        // ---------- Cuerpo ----------
        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star), MinWidth = 420 });

        _tabla.Columns.Add(UiKit.Columna("Fecha", nameof(ComunicadoDto.FechaTexto), 0.8));
        _tabla.Columns.Add(UiKit.Columna("Título", nameof(ComunicadoDto.Titulo), 2));
        _tabla.Columns.Add(UiKit.Columna("En la app", nameof(ComunicadoDto.AppTexto), 1.1));
        _tabla.SelectionChanged += (_, _) =>
        {
            if (_tabla.SelectedItem is ComunicadoDto c)
                Mostrar(c);
        };

        var tarjetaLista = new Border
        {
            Background = Brushes.White,
            BorderBrush = ColoresPanel.Hex("#D6DEE7"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Child = _tabla
        };
        Grid.SetColumn(tarjetaLista, 0);
        cuerpo.Children.Add(tarjetaLista);

        var ficha = ArmarFicha();
        Grid.SetColumn(ficha, 2);
        cuerpo.Children.Add(ficha);

        raiz.Children.Add(cuerpo);
        Content = raiz;

        Loaded += async (_, _) => await CargarAsync();
    }

    private Border ArmarFicha()
    {
        var pila = new StackPanel();
        pila.Children.Add(_tituloFicha);
        pila.Children.Add(_estado);

        _titulo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _titulo.Width = double.NaN;
        var campoTitulo = UiKit.Campo("Título", _titulo, 0);
        pila.Children.Add(campoTitulo);

        pila.Children.Add(new TextBlock { Text = "Texto del comunicado", Foreground = UiKit.Etiqueta, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 4) });
        pila.Children.Add(_texto);

        pila.Children.Add(_enApp);
        var vigencia = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        vigencia.Children.Add(_conVigencia);
        vigencia.Children.Add(_vigencia);
        pila.Children.Add(vigencia);
        pila.Children.Add(new TextBlock
        {
            Text = "Sin fecha, se muestra en la app hasta que lo archives.",
            Foreground = UiKit.Suave,
            FontSize = 12,
            Margin = new Thickness(22, 0, 0, 14)
        });

        _enApp.Checked += (_, _) => ActualizarVigencia();
        _enApp.Unchecked += (_, _) => ActualizarVigencia();
        _conVigencia.Checked += (_, _) => ActualizarVigencia();
        _conVigencia.Unchecked += (_, _) => ActualizarVigencia();

        var botones = new WrapPanel();
        _guardar.Margin = new Thickness(0, 0, 10, 0);
        _imprimir.Margin = new Thickness(0, 0, 10, 0);
        _guardar.Click += async (_, _) => await GuardarAsync();
        _imprimir.Click += (_, _) => Imprimir();
        _archivar.Click += async (_, _) => await ArchivarAsync();
        botones.Children.Add(_guardar);
        botones.Children.Add(_imprimir);
        botones.Children.Add(_archivar);
        pila.Children.Add(botones);

        if (!_puedeEditar)
        {
            _titulo.IsReadOnly = true;
            _texto.IsReadOnly = true;
            _enApp.IsEnabled = false;
            _conVigencia.IsEnabled = false;
            _vigencia.IsEnabled = false;
            _guardar.Visibility = Visibility.Collapsed;
            _archivar.Visibility = Visibility.Collapsed;
        }

        Nuevo();

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = ColoresPanel.Hex("#D6DEE7"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = pila }
        };
    }

    private void ActualizarVigencia()
    {
        var app = _enApp.IsChecked == true;
        _conVigencia.IsEnabled = app && _puedeEditar;
        _vigencia.IsEnabled = app && _conVigencia.IsChecked == true && _puedeEditar;
    }

    // =====================================================================

    private async Task CargarAsync()
    {
        _aviso.Visibility = Visibility.Collapsed;
        try
        {
            var archivados = _verArchivados.IsChecked == true;
            var lista = await ApiJson.GetAsync<List<ComunicadoDto>>("/api/comunicados?archivados=" + (archivados ? "true" : "false")) ?? new();
            _tabla.ItemsSource = lista;
            _resumen.Text = lista.Count == 0
                ? (archivados ? "No hay comunicados archivados." : "Todavía no hay comunicados.")
                : $"{lista.Count} {(lista.Count == 1 ? "comunicado" : "comunicados")}" +
                  (archivados ? "" : $" · {lista.Count(c => c.VisibleEnApp)} visibles en la app");

            if (_actual is not null && lista.FirstOrDefault(c => c.Id == _actual.Id) is { } igual)
                _tabla.SelectedItem = igual;
            else if (!_puedeEditar && lista.Count > 0)
                _tabla.SelectedIndex = 0;
            else
                Nuevo();
        }
        catch (Exception ex)
        {
            Avisar("No se pudieron cargar los comunicados: " + ex.Message);
        }
    }

    private void Nuevo()
    {
        _actual = null;
        _tabla.SelectedItem = null;
        _tituloFicha.Text = _puedeEditar ? "Nuevo comunicado" : "Comunicado";
        _estado.Text = _puedeEditar ? "Escribe el título y el texto; luego guárdalo para imprimirlo o publicarlo." : "Elige un comunicado de la lista.";
        _titulo.Text = "";
        _texto.Text = "";
        _enApp.IsChecked = true;
        _conVigencia.IsChecked = false;
        _vigencia.SelectedDate = DateTime.Today.AddDays(15);
        _archivar.Visibility = Visibility.Collapsed;
        ActualizarVigencia();
    }

    private void Mostrar(ComunicadoDto c)
    {
        _actual = c;
        _tituloFicha.Text = c.Archivado ? "Comunicado archivado" : "Comunicado";
        _estado.Text = $"{c.FechaTexto} · {c.Autor}   ·   {c.AppTexto}";
        _titulo.Text = c.Titulo;
        _texto.Text = c.Texto;
        _enApp.IsChecked = c.MostrarEnApp;
        _conVigencia.IsChecked = c.VigenteHasta is not null;
        _vigencia.SelectedDate = c.VigenteHasta?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today.AddDays(15);
        _archivar.Content = c.Archivado ? "Restaurar" : "Archivar";
        _archivar.Visibility = _puedeEditar ? Visibility.Visible : Visibility.Collapsed;
        ActualizarVigencia();
    }

    private async Task GuardarAsync()
    {
        var titulo = _titulo.Text.Trim();
        var texto = _texto.Text.Trim();
        if (titulo.Length == 0 || texto.Length == 0)
        {
            Dialogo.Mostrar(Window.GetWindow(this), "Escribe el título y el texto del comunicado.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DateOnly? hasta = null;
        if (_enApp.IsChecked == true && _conVigencia.IsChecked == true)
        {
            if (_vigencia.SelectedDate is not { } d)
            {
                Dialogo.Mostrar(Window.GetWindow(this), "Elige hasta qué día se muestra en la app.", "Falta la fecha", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            hasta = DateOnly.FromDateTime(d);
        }

        var cuerpo = new { titulo, texto, mostrarEnApp = _enApp.IsChecked == true, vigenteHasta = hasta };

        _guardar.IsEnabled = false;
        try
        {
            var guardado = _actual is null
                ? await ApiJson.PostAsync<ComunicadoDto>("/api/comunicados", cuerpo)
                : await ApiJson.PutAsync<ComunicadoDto>($"/api/comunicados/{_actual.Id}", cuerpo);
            _actual = guardado;
            await CargarAsync();
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se guardó", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _guardar.IsEnabled = true;
        }
    }

    private async Task ArchivarAsync()
    {
        if (_actual is null)
            return;

        var restaurar = _actual.Archivado;
        if (!restaurar && Dialogo.Mostrar(Window.GetWindow(this),
                $"¿Archivar «{_actual.Titulo}»? Deja de mostrarse en la app; lo puedes restaurar desde «Archivados».",
                "Archivar comunicado", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        try
        {
            await ApiJson.PostAsync<ComunicadoDto>($"/api/comunicados/{_actual.Id}/{(restaurar ? "restaurar" : "archivar")}", new { });
            _actual = null;
            await CargarAsync();
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se pudo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Imprimir()
    {
        var titulo = _titulo.Text.Trim();
        var texto = _texto.Text.Trim();
        if (titulo.Length == 0 || texto.Length == 0)
        {
            Dialogo.Mostrar(Window.GetWindow(this), "Escribe el título y el texto antes de imprimir.", "Faltan datos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ComunicadoPdfService.GuardarComunicado(Window.GetWindow(this), titulo, texto, _actual?.Fecha ?? DateTimeOffset.Now);
    }

    private void AbrirCartas()
    {
        new VentanaCartasDeuda { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void Avisar(string texto)
    {
        _avisoTexto.Text = texto;
        _aviso.Visibility = Visibility.Visible;
    }

    // =====================================================================

    public sealed class ComunicadoDto
    {
        public Guid Id { get; set; }
        public string Titulo { get; set; } = "";
        public string Texto { get; set; } = "";
        public DateTimeOffset Fecha { get; set; }
        public string Autor { get; set; } = "";
        public bool MostrarEnApp { get; set; }
        public DateOnly? VigenteHasta { get; set; }
        public bool Archivado { get; set; }
        public bool VisibleEnApp { get; set; }

        public string FechaTexto => Fecha.ToString("dd/MM/yyyy", Bo);

        public string AppTexto =>
            Archivado ? "Archivado"
            : !MostrarEnApp ? "Solo impreso"
            : VisibleEnApp ? (VigenteHasta is { } h ? $"Visible hasta {h:dd/MM}" : "Visible")
            : "Vencido";
    }
}

/// <summary>Genera las cartas de deuda en un solo PDF (una hoja por unidad).</summary>
public sealed class VentanaCartasDeuda : Window
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly ComboBox _bloque = UiKit.Combo(200);
    private readonly TextBox _caseta = UiKit.Texto(120, 20);
    private readonly CheckBox _conFecha = new() { Content = "Pagar hasta el día:", VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly DatePicker _fecha = UiKit.Fecha(DateTime.Today.AddDays(10));
    private readonly TextBox _nota = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70, MaxLength = 600, Padding = new Thickness(8, 6, 8, 6) };
    private readonly TextBlock _estado = new() { Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) };
    private readonly Button _generar = UiKit.Boton("Generar PDF", principal: true);

    public VentanaCartasDeuda()
    {
        Title = "Cartas de deuda";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var pila = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        pila.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta });
        pila.Children.Add(UiKit.Ayuda(
            "Una carta por cada caseta, comedor o ambulante con deudas pendientes, con el detalle y el total. " +
            "Se guarda en un solo PDF listo para imprimir y entregar.", 14));

        _bloque.ItemsSource = new List<UiKit.Item<string?>> { new(null, "Todos") };
        _bloque.SelectedIndex = 0;

        var fila = new WrapPanel();
        fila.Children.Add(UiKit.Campo("Bloque", _bloque));
        fila.Children.Add(UiKit.Campo("Solo la unidad N.º (opcional)", _caseta, 0));
        pila.Children.Add(fila);

        var fecha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        fecha.Children.Add(_conFecha);
        fecha.Children.Add(_fecha);
        _fecha.IsEnabled = false;
        _conFecha.Checked += (_, _) => _fecha.IsEnabled = true;
        _conFecha.Unchecked += (_, _) => _fecha.IsEnabled = false;
        pila.Children.Add(fecha);

        pila.Children.Add(new TextBlock { Text = "Nota adicional (opcional)", Foreground = UiKit.Etiqueta, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 4) });
        pila.Children.Add(_nota);
        pila.Children.Add(_estado);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cerrar = UiKit.Boton("Cerrar");
        cerrar.Margin = new Thickness(0, 0, 10, 0);
        cerrar.Click += (_, _) => Close();
        _generar.Click += async (_, _) => await GenerarAsync();
        botones.Children.Add(cerrar);
        botones.Children.Add(_generar);
        pila.Children.Add(botones);

        Content = pila;
        Loaded += async (_, _) => await CargarBloquesAsync();
    }

    private async Task CargarBloquesAsync()
    {
        try
        {
            var mapa = await ApiJson.GetAsync<Respuesta>("/api/reportes/deudores") ?? new Respuesta();
            var bloques = mapa.Filas.Select(f => f.Bloque).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList();
            var items = new List<UiKit.Item<string?>> { new(null, "Todos") };
            items.AddRange(bloques.Select(b => new UiKit.Item<string?>(b, b!)));
            _bloque.ItemsSource = items;
            _bloque.SelectedIndex = 0;
            _estado.Text = mapa.Casetas == 0
                ? "Ahora nadie tiene deudas pendientes."
                : $"{mapa.Casetas} {(mapa.Casetas == 1 ? "unidad tiene" : "unidades tienen")} deudas pendientes.";
        }
        catch (Exception ex)
        {
            _estado.Text = "No se pudieron cargar los deudores: " + ex.Message;
        }
    }

    private async Task GenerarAsync()
    {
        var parametros = new List<string>();
        if ((_bloque.SelectedItem as UiKit.Item<string?>)?.Valor is { } bloque)
            parametros.Add("bloque=" + Uri.EscapeDataString(bloque));
        var caseta = _caseta.Text.Trim();
        if (caseta.Length > 0)
            parametros.Add("caseta=" + Uri.EscapeDataString(caseta));

        string? limite = null;
        if (_conFecha.IsChecked == true)
        {
            if (_fecha.SelectedDate is not { } d)
            {
                Dialogo.Mostrar(this, "Elige la fecha límite de pago.", "Falta la fecha", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            limite = d.ToString("d 'de' MMMM 'de' yyyy", Bo);
        }

        _generar.IsEnabled = false;
        try
        {
            var d = await ApiJson.GetAsync<Respuesta>("/api/reportes/deudores" + (parametros.Count > 0 ? "?" + string.Join("&", parametros) : "")) ?? new Respuesta();

            var filas = d.Filas.AsEnumerable();
            if (caseta.Length > 0)
                filas = filas.Where(f => Igual(f.Codigo, caseta));

            var cartas = filas.Select(f => new CartaDeuda(
                Unidad(f),
                f.Titular,
                string.IsNullOrWhiteSpace(f.Bloque) ? null : "Bloque " + f.Bloque,
                f.Deudas.Select(x => (x.Fecha.ToString("dd/MM/yyyy", Bo), x.Concepto, x.Periodo, $"{x.Moneda} {x.Monto.ToString("N2", Bo)}")).ToList(),
                string.Join(" + ", f.Totales.Select(t => $"{t.Moneda} {t.Monto.ToString("N2", Bo)}"))))
                .ToList();

            ComunicadoPdfService.GuardarCartas(this, cartas, limite, string.IsNullOrWhiteSpace(_nota.Text) ? null : _nota.Text.Trim());
            if (cartas.Count > 0)
                _estado.Text = $"Se generaron {cartas.Count} {(cartas.Count == 1 ? "carta" : "cartas")}.";
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(this, ex.Message, "No se generaron", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _generar.IsEnabled = true;
        }
    }

    private static bool Igual(string a, string b) =>
        string.Equals(a.TrimStart('0'), b.TrimStart('0'), StringComparison.OrdinalIgnoreCase);

    private static string Unidad(FilaDeudor f) => f.Tipo switch
    {
        "Comedor" => "Comedor N.º " + f.Codigo,
        "Ambulante" => "Puesto ambulante N.º " + f.Codigo,
        _ => "Caseta N.º " + f.Codigo
    };

    private sealed class Respuesta
    {
        public int Casetas { get; set; }
        public List<FilaDeudor> Filas { get; set; } = new();
    }

    private sealed class FilaDeudor
    {
        public string Codigo { get; set; } = "";
        public string? Tipo { get; set; }
        public string? Bloque { get; set; }
        public string? Titular { get; set; }
        public List<MontoMoneda> Totales { get; set; } = new();
        public List<DeudaDto> Deudas { get; set; } = new();
    }

    private sealed class MontoMoneda
    {
        public string Moneda { get; set; } = "";
        public decimal Monto { get; set; }
    }

    private sealed class DeudaDto
    {
        public DateOnly Fecha { get; set; }
        public string Concepto { get; set; } = "";
        public string Periodo { get; set; } = "";
        public string Moneda { get; set; } = "";
        public decimal Monto { get; set; }
    }
}
