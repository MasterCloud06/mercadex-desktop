using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Socios (manual, 1. GESTIÓN DE SOCIOS): el padrón de socios con sus
/// casetas y deudas, y la ficha de cada uno. Desde la ficha se cobra, se
/// abre el Kardex de su caseta, se le asigna una caseta y se corrigen sus
/// datos. Los datos que dependen de la caseta (fecha de ingreso, vivo o
/// fallecido, propiedad, aporte, escaneos) se llenan en el Kardex.
/// </summary>
public sealed class SociosView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");
    private static readonly Brush Rojo = ColoresPanel.Hex("#A12A2A");

    private readonly bool _admin = SesionActual.EsAdministrador || !SesionActual.Iniciada;

    private readonly TextBox _buscar = UiKit.Texto(260, 80);
    private readonly ComboBox _estado = UiKit.Combo(170);
    private readonly TextBlock _resumen = new() { Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly StackPanel _ficha = new();

    private List<UnidadLibre> _unidades = new();
    private Guid? _seleccionarAlCargar;
    private FichaDto? _actual;
    private string? _avisoPendiente;

    public SociosView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);

        var titulo = new DockPanel();
        if (_admin)
        {
            var nuevo = UiKit.Boton("Registrar socio", principal: true);
            nuevo.VerticalAlignment = VerticalAlignment.Center;
            nuevo.Click += async (_, _) => await NuevoSocioAsync();
            DockPanel.SetDock(nuevo, Dock.Right);
            titulo.Children.Add(nuevo);
        }
        titulo.Children.Add(UiKit.Encabezado("Socios"));
        arriba.Children.Add(titulo);
        arriba.Children.Add(UiKit.Ayuda(
            "El padrón de socios con sus casetas y lo que deben. Elige un socio para ver su ficha, cobrarle o abrir el Kardex de su caseta.", 10));

        // ---------- Filtros ----------

        _estado.ItemsSource = new List<UiKit.Item<string>>
        {
            new("activos", "Activos"),
            new("con-deuda", "Con deuda"),
            new("sin-caseta", "Sin caseta"),
            new("fallecidos", "Fallecidos"),
            new("inactivos", "Dados de baja"),
            new("todos", "Todos")
        };
        _estado.SelectedIndex = 0;
        _estado.SelectionChanged += async (_, _) =>
        {
            if (IsLoaded)
                await CargarListaAsync();
        };

        var buscar = UiKit.Boton("Buscar", principal: true);
        buscar.Margin = new Thickness(0, 0, 0, 10);
        buscar.Click += async (_, _) => await CargarListaAsync();

        _buscar.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
                await CargarListaAsync();
        };

        var filtros = new WrapPanel();
        filtros.Children.Add(UiKit.Campo("Nombre, CI, celular o N.º de caseta", _buscar));
        filtros.Children.Add(UiKit.Campo("Mostrar", _estado));
        filtros.Children.Add(buscar);
        arriba.Children.Add(UiKit.Tarjeta(filtros, 8));
        arriba.Children.Add(_resumen);
        raiz.Children.Add(arriba);

        // ---------- Lista y ficha ----------

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });

        _tabla.Columns.Add(UiKit.Columna("Socio", nameof(SocioFila.NombreCompleto), 2));
        _tabla.Columns.Add(UiKit.Columna("CI", nameof(SocioFila.DocumentoTexto), 0.9));
        _tabla.Columns.Add(UiKit.Columna("Celular", nameof(SocioFila.TelefonoTexto), 0.9));
        _tabla.Columns.Add(UiKit.Columna("Casetas", nameof(SocioFila.CasetasTexto), 0.9));
        _tabla.Columns.Add(UiKit.Columna("Debe", nameof(SocioFila.DeudaTexto), 1.1, derecha: true));
        _tabla.Columns.Add(UiKit.Columna("Estado", nameof(SocioFila.EstadoTexto), 0.8));

        var estiloFila = new Style(typeof(DataGridRow), UiKit.Estilo("Mx.Row"));
        var deBaja = new DataTrigger { Binding = new Binding(nameof(SocioFila.Activo)), Value = false };
        deBaja.Setters.Add(new Setter(ForegroundProperty, UiKit.Suave));
        estiloFila.Triggers.Add(deBaja);
        _tabla.RowStyle = estiloFila;

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

        MostrarFichaVacia();
        Loaded += async (_, _) => await CargarTodoAsync();
    }

    /// <summary>Al abrir la pantalla, deja elegido este socio (desde Casetas u otra pantalla).</summary>
    public void SeleccionarAlCargar(Guid socioId)
    {
        _seleccionarAlCargar = socioId;
        _estado.SelectedIndex = 5; // Todos: el socio puede estar dado de baja.
    }

    // =====================================================================
    // CARGA
    // =====================================================================

    private async Task CargarTodoAsync()
    {
        await CargarListaAsync();

        if (!_admin)
            return;

        try
        {
            var r = await ApiJson.GetAsync<UnidadesRespuesta>("/api/casetas?estado=activas");
            _unidades = r?.Unidades ?? new();
        }
        catch
        {
            _unidades = new();
        }
    }

    private async Task CargarListaAsync()
    {
        var seleccion = _seleccionarAlCargar ?? (_tabla.SelectedItem as SocioFila)?.Id;
        var estado = (_estado.SelectedItem as UiKit.Item<string>)?.Valor ?? "activos";

        try
        {
            _resumen.Text = "Cargando…";
            var ruta = $"/api/socios/padron?estado={estado}";
            if (!string.IsNullOrWhiteSpace(_buscar.Text))
                ruta += "&texto=" + Uri.EscapeDataString(_buscar.Text.Trim());

            var r = await ApiJson.GetAsync<PadronRespuesta>(ruta);
            var filas = r?.Socios ?? new();
            _tabla.ItemsSource = filas;

            if (r is null || filas.Count == 0)
            {
                _resumen.Text = string.IsNullOrWhiteSpace(_buscar.Text)
                    ? "No hay socios en esta lista."
                    : "Ningún socio coincide con la búsqueda. Si es nuevo, regístralo con «Registrar socio».";
            }
            else
            {
                var conDeuda = filas.Count(f => f.DeudasPendientes > 0);
                var sinCaseta = filas.Count(f => f.Activo && f.Casetas.Count == 0);
                _resumen.Text =
                    $"{filas.Count} {(filas.Count == 1 ? "socio" : "socios")} en la lista" +
                    $"   ·   con deuda: {conDeuda}" +
                    (sinCaseta > 0 ? $"   ·   sin caseta: {sinCaseta}" : "") +
                    $"   ·   en total: {r.Activos} activos y {r.Inactivos} de baja";
            }

            if (seleccion is { } id && filas.FirstOrDefault(f => f.Id == id) is { } fila)
            {
                _tabla.SelectedItem = fila;
                _tabla.ScrollIntoView(fila);
            }
            else if (_tabla.SelectedItem is null)
            {
                MostrarFichaVacia();
            }

            _seleccionarAlCargar = null;

            if (_avisoPendiente is not null)
            {
                _resumen.Text = "✓ " + _avisoPendiente;
                _avisoPendiente = null;
            }
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudo cargar el padrón: " + ex.Message;
        }
    }

    // =====================================================================
    // FICHA
    // =====================================================================

    private void MostrarFichaVacia()
    {
        _actual = null;
        _ficha.Children.Clear();
        _ficha.Children.Add(new TextBlock
        {
            Text = "Elige un socio de la lista para ver su ficha: sus casetas, lo que debe y lo que falta en su Kardex.",
            Foreground = UiKit.Suave,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 8, 4, 0)
        });
    }

    private async Task MostrarFichaAsync()
    {
        if (_tabla.SelectedItem is not SocioFila fila)
        {
            MostrarFichaVacia();
            return;
        }

        try
        {
            var f = await ApiJson.GetAsync<FichaDto>($"/api/socios/{fila.Id}/ficha");
            if (f is null || (_tabla.SelectedItem as SocioFila)?.Id != fila.Id)
                return;

            _actual = f;
            ConstruirFicha(f);
        }
        catch (Exception ex)
        {
            _ficha.Children.Clear();
            _ficha.Children.Add(new TextBlock { Text = "No se pudo abrir la ficha: " + ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = Rojo });
        }
    }

    private void ConstruirFicha(FichaDto f)
    {
        _ficha.Children.Clear();

        // ---------- Encabezado ----------

        var (estadoTexto, fondo, tinta) = !f.Activo
            ? ("Dado de baja", "#EEF2F7", "#5A6B7D")
            : f.Fallecido
                ? ("Fallecido", "#F1ECF7", "#4B2A7A")
                : ("Activo", "#E7F5E7", "#0B5A0B");

        var cabeza = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 2),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 4, 0, 0),
            Background = ColoresPanel.Hex(fondo),
            Child = new TextBlock { Text = estadoTexto, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = ColoresPanel.Hex(tinta) }
        };
        DockPanel.SetDock(chip, Dock.Right);
        cabeza.Children.Add(chip);
        cabeza.Children.Add(new TextBlock
        {
            Text = $"{f.Apellidos} {f.Nombres}",
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = UiKit.Titulo,
            TextWrapping = TextWrapping.Wrap
        });
        _ficha.Children.Add(cabeza);
        _ficha.Children.Add(UiKit.Ayuda(
            $"CI {Texto(f.Documento)}   ·   Cel. {Texto(f.Telefono)}\n" +
            $"Registrado en el sistema el {f.FechaRegistro.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)}", 10));

        // ---------- Deuda ----------

        var deudas = f.DeudaTotal.Sum(d => d.Cantidad);
        var aviso = UiKit.Aviso(out var avisoTexto, deudas > 0 ? "aviso" : "ok");
        avisoTexto.Text = deudas > 0
            ? $"Debe {Montos(f.DeudaTotal)} ({deudas} {(deudas == 1 ? "deuda" : "deudas")})."
            : f.Casetas.Any(c => c.Actual) ? "Al día: no tiene deudas pendientes." : "No tiene casetas a su nombre.";
        if (f.UltimoPago is { } up)
            avisoTexto.Text += $"\nÚltimo pago: recibo N.º {up.Numero} del {up.FechaEmision.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)}.";
        _ficha.Children.Add(aviso);

        // ---------- Casetas ----------

        var actuales = f.Casetas.Where(c => c.Actual).ToList();
        _ficha.Children.Add(Seccion(actuales.Count == 1 ? "Su caseta" : "Sus casetas"));

        if (actuales.Count == 0)
            _ficha.Children.Add(UiKit.Ayuda("No es titular de ninguna caseta.", 8));

        foreach (var c in actuales)
            _ficha.Children.Add(TarjetaCaseta(c));

        if (_admin && f.Activo)
            AgregarAsignarCaseta(f);

        var anteriores = f.Casetas.Where(c => !c.Actual).ToList();
        if (anteriores.Count > 0)
        {
            _ficha.Children.Add(new TextBlock { Text = "Casetas que tuvo antes", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Etiqueta, Margin = new Thickness(0, 6, 0, 4) });
            foreach (var c in anteriores)
                _ficha.Children.Add(new TextBlock
                {
                    Text = $"• {c.TipoTexto} {c.Codigo} (titular desde el {c.Desde.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)})",
                    Foreground = UiKit.Suave,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2)
                });
        }

        // ---------- Kardex ----------

        _ficha.Children.Add(Seccion("Kardex"));
        _ficha.Children.Add(Ficha(new List<(string, string)>
        {
            ("Fecha de ingreso", f.FechaIngreso is { } fi ? fi.ToString("dd/MM/yyyy", Bo) : "Sin registrar"),
            ("Aporte para ser socio", f.AporteSocio),
            ("Documentos escaneados", f.DocumentosEscaneados == 0 ? "Ninguno" : f.DocumentosEscaneados.ToString(Bo)),
            ("App del socio", f.App + (f.AppUltimoAcceso is { } ua ? $" (último ingreso {ua.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)})" : ""))
        }));

        if (f.Faltan.Count > 0)
        {
            var falta = UiKit.Aviso(out var faltaTexto, "info");
            faltaTexto.Text = "Falta completar: " + string.Join(", ", f.Faltan) + "." +
                              (actuales.Count > 0 ? "\nSe completa en el Kardex de su caseta." : "");
            _ficha.Children.Add(falta);
        }

        _ficha.Children.Add(UiKit.Ayuda(
            "La fecha de ingreso, si vive o falleció, el documento de propiedad, el aporte y los escaneos se registran en el Kardex de cada caseta.", 4));

        // ---------- Acciones de administrador ----------

        if (!_admin)
            return;

        _ficha.Children.Add(Seccion("Datos del socio"));

        var botones = new WrapPanel();
        var corregir = Pequeno("Corregir datos", principal: false, async () => await CorregirAsync(f));
        botones.Children.Add(corregir);

        var estado = UiKit.Boton(f.Activo ? "Dar de baja" : "Reactivar", peligro: f.Activo);
        estado.Height = 32;
        estado.Padding = new Thickness(14, 0, 14, 0);
        estado.Margin = new Thickness(0, 0, 8, 6);
        estado.Click += async (_, _) => await CambiarEstadoAsync(f);
        botones.Children.Add(estado);
        _ficha.Children.Add(botones);

        if (f.Activo)
            _ficha.Children.Add(UiKit.Ayuda("Dar de baja no borra nada: sus recibos, deudas y documentos se conservan. Antes hay que pasar sus casetas a otro titular.", 0));
    }

    private Border TarjetaCaseta(CasetaSocio c)
    {
        var pila = new StackPanel();

        var linea = new DockPanel();
        var deuda = new TextBlock
        {
            Text = c.Deuda.Count == 0 ? "Al día" : "Debe " + Montos(c.Deuda),
            FontWeight = FontWeights.SemiBold,
            Foreground = c.Deuda.Count == 0 ? ColoresPanel.Hex("#0B5A0B") : ColoresPanel.Hex("#7A5200"),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(deuda, Dock.Right);
        linea.Children.Add(deuda);
        linea.Children.Add(new TextBlock { Text = $"{c.TipoTexto} {c.Codigo}", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = UiKit.Tinta });
        pila.Children.Add(linea);

        var ubicacion = new List<string>();
        if (!string.IsNullOrWhiteSpace(c.Bloque)) ubicacion.Add("Bloque " + c.Bloque);
        if (!string.IsNullOrWhiteSpace(c.Pasillo)) ubicacion.Add("Pasillo " + c.Pasillo);
        ubicacion.Add($"titular desde el {c.Desde.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)}");
        pila.Children.Add(new TextBlock { Text = string.Join("  ·  ", ubicacion), Foreground = UiKit.Suave, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) });

        var irA = new WrapPanel();
        if (c.Deuda.Count > 0 && (SesionActual.PuedeCobrar || !SesionActual.Iniciada))
            irA.Children.Add(Pequeno("Cobrar", principal: true, () => Navegacion.AbrirCobro(c.UnidadId, c.Codigo)));
        if (c.Tipo == "Caseta")
            irA.Children.Add(Pequeno(c.TieneKardex ? "Kardex" : "Llenar Kardex", principal: false, () => Navegacion.AbrirKardex(c.UnidadId)));
        irA.Children.Add(Pequeno("Ficha de la caseta", principal: false, () => Navegacion.AbrirCaseta(c.UnidadId)));
        pila.Children.Add(irA);

        return new Border
        {
            Background = UiKit.Fondo,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 4),
            Margin = new Thickness(0, 0, 0, 8),
            Child = pila
        };
    }

    private void AgregarAsignarCaseta(FichaDto f)
    {
        if (_unidades.Count == 0)
            return;

        var propias = f.Casetas.Where(c => c.Actual).Select(c => c.UnidadId).ToHashSet();
        var opciones = _unidades
            .Where(u => !propias.Contains(u.Id))
            .OrderBy(u => u.Titular is null ? 0 : 1)
            .ThenBy(u => u.Codigo.Length)
            .ThenBy(u => u.Codigo, StringComparer.OrdinalIgnoreCase)
            .Select(u => new UiKit.Item<UnidadLibre>(u, $"{u.TipoTexto} {u.Codigo}" + (u.Titular is null ? "  (sin titular)" : $"  — titular: {u.Titular}")))
            .ToList();

        var combo = new ComboBox
        {
            Width = 260,
            Height = 36,
            IsEditable = true,
            IsTextSearchEnabled = true,
            StaysOpenOnEdit = true,
            VerticalContentAlignment = VerticalAlignment.Center,
            ItemsSource = opciones
        };

        var asignar = UiKit.Boton("Asignar", principal: true);
        asignar.Height = 36;
        asignar.Margin = new Thickness(8, 0, 0, 0);
        asignar.Click += async (_, _) => await AsignarCasetaAsync(f, combo);

        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        fila.Children.Add(combo);
        fila.Children.Add(asignar);
        _ficha.Children.Add(UiKit.Campo("Asignarle una caseta (escribe el número)", fila, 0));
    }

    // =====================================================================
    // ACCIONES
    // =====================================================================

    private async Task<bool> Ejecutar(Func<Task<Mensaje?>> accion, string tituloError)
    {
        try
        {
            var r = await accion();
            await CargarTodoAsync();
            await MostrarFichaAsync();

            if (!string.IsNullOrWhiteSpace(r?.Texto))
                _resumen.Text = "✓ " + r!.Texto;
            return true;
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, tituloError, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private async Task AsignarCasetaAsync(FichaDto f, ComboBox combo)
    {
        if (combo.SelectedItem is not UiKit.Item<UnidadLibre> elegida)
        {
            Dialogo.Mostrar(Window.GetWindow(this),
                "Escribe el número de la caseta y elígela de la lista.",
                "Elige una caseta", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var u = elegida.Valor;
        var texto = $"Socio: {f.Apellidos} {f.Nombres}\nUnidad: {u.TipoTexto} {u.Codigo}\n\n";
        texto += u.Titular is null
            ? "Quedará como titular de esta unidad.\n\n¿Asignar?"
            : $"Esta unidad ya tiene titular: {u.Titular}. Pasará al historial y {f.Nombres} será el nuevo titular. " +
              "Las deudas pendientes siguen siendo de la unidad.\n\n¿Cambiar el titular?";

        var confirmar = Dialogo.Mostrar(Window.GetWindow(this), texto,
            u.Titular is null ? "Asignar caseta" : "Cambiar titular",
            MessageBoxButton.YesNo, u.Titular is null ? MessageBoxImage.Question : MessageBoxImage.Warning);

        if (confirmar == MessageBoxResult.Yes)
            await Ejecutar(() => ApiJson.PostAsync<Mensaje>($"/api/casetas/{u.Id}/titular", new { socioId = f.Id }),
                "No se asignó la caseta");
    }

    private async Task CambiarEstadoAsync(FichaDto f)
    {
        if (f.Activo)
        {
            var actuales = f.Casetas.Where(c => c.Actual).ToList();
            if (actuales.Count > 0)
            {
                Dialogo.Mostrar(Window.GetWindow(this),
                    $"{f.Nombres} {f.Apellidos} todavía es titular de {string.Join(", ", actuales.Select(c => c.Codigo))}.\n\n" +
                    "Primero asigna otro titular a esas casetas (en su ficha de caseta) y después da de baja al socio.",
                    "Todavía tiene casetas", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmar = Dialogo.Mostrar(Window.GetWindow(this),
                $"Socio: {f.Apellidos} {f.Nombres}\n\nYa no aparecerá entre los socios activos ni se le podrá asignar casetas. " +
                "Sus recibos, deudas y documentos se conservan, y puedes reactivarlo cuando quieras.\n\n¿Dar de baja?",
                "Dar de baja", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirmar != MessageBoxResult.Yes)
                return;
        }

        await Ejecutar(() => ApiJson.PostAsync<Mensaje>($"/api/socios/{f.Id}/{(f.Activo ? "desactivar" : "activar")}", new { }),
            "No se cambió el estado");
    }

    private async Task CorregirAsync(FichaDto f)
    {
        var ventana = new VentanaDatosSocio(f) { Owner = Window.GetWindow(this) };
        if (ventana.ShowDialog() == true)
        {
            await CargarListaAsync();
            await MostrarFichaAsync();
            _resumen.Text = "✓ " + ventana.Resultado;
        }
    }

    private async Task NuevoSocioAsync()
    {
        var ventana = new VentanaDatosSocio(null) { Owner = Window.GetWindow(this) };
        if (ventana.ShowDialog() != true)
            return;

        // Se muestra el socio nuevo para asignarle su caseta enseguida.
        _buscar.Clear();
        _seleccionarAlCargar = ventana.CreadoId;
        _avisoPendiente = ventana.Resultado + " Ahora asígnale su caseta en la ficha de la derecha.";
        if (_estado.SelectedIndex == 5)
            await CargarListaAsync();
        else
            _estado.SelectedIndex = 5; // recarga la lista
    }

    // =====================================================================
    // AYUDANTES
    // =====================================================================

    private static TextBlock Seccion(string texto) => new()
    {
        Text = texto,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Foreground = UiKit.Titulo,
        Margin = new Thickness(0, 14, 0, 6)
    };

    private static Button Pequeno(string texto, bool principal, Action accion)
    {
        var b = UiKit.Boton(texto, principal);
        b.Height = 32;
        b.Padding = new Thickness(14, 0, 14, 0);
        b.Margin = new Thickness(0, 0, 8, 6);
        b.Click += (_, _) => accion();
        return b;
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

    private static string Texto(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? "—" : valor!;

    internal static string Montos(IEnumerable<MontoMoneda> montos) =>
        string.Join(" + ", montos.Select(m => $"{m.Moneda} {m.Monto.ToString("N2", Bo)}"));

    // =====================================================================
    // MODELOS
    // =====================================================================

    private sealed class Mensaje
    {
        [System.Text.Json.Serialization.JsonPropertyName("mensaje")]
        public string? Texto { get; set; }
    }

    private sealed class PadronRespuesta
    {
        public int Total { get; set; }
        public int Activos { get; set; }
        public int Inactivos { get; set; }
        public List<SocioFila> Socios { get; set; } = new();
    }

    public sealed class MontoMoneda
    {
        public string Moneda { get; set; } = "";
        public decimal Monto { get; set; }
        public int Cantidad { get; set; }
    }

    public sealed class SocioFila
    {
        public Guid Id { get; set; }
        public string NombreCompleto { get; set; } = "";
        public string Documento { get; set; } = "";
        public string? Telefono { get; set; }
        public bool Activo { get; set; }
        public bool Fallecido { get; set; }
        public List<string> Casetas { get; set; } = new();
        public List<MontoMoneda> Deuda { get; set; } = new();
        public int DeudasPendientes { get; set; }

        public string DocumentoTexto => string.IsNullOrWhiteSpace(Documento) ? "—" : Documento;
        public string TelefonoTexto => string.IsNullOrWhiteSpace(Telefono) ? "—" : Telefono!;
        public string CasetasTexto => Casetas.Count == 0 ? "—" : string.Join(", ", Casetas);
        public string DeudaTexto => Deuda.Count == 0 ? "—" : Montos(Deuda);
        public string EstadoTexto => !Activo ? "De baja" : Fallecido ? "Fallecido" : "Activo";
    }

    public sealed class FichaDto
    {
        public Guid Id { get; set; }
        public string Nombres { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public string Documento { get; set; } = "";
        public string? Telefono { get; set; }
        public DateTimeOffset FechaRegistro { get; set; }
        public bool Activo { get; set; }
        public bool Fallecido { get; set; }
        public DateOnly? FechaIngreso { get; set; }
        public string AporteSocio { get; set; } = "";
        public List<CasetaSocio> Casetas { get; set; } = new();
        public List<MontoMoneda> DeudaTotal { get; set; } = new();
        public UltimoPagoDto? UltimoPago { get; set; }
        public int DocumentosEscaneados { get; set; }
        public List<string> Faltan { get; set; } = new();
        public string App { get; set; } = "";
        public DateTimeOffset? AppUltimoAcceso { get; set; }
    }

    public sealed class UltimoPagoDto
    {
        public long Numero { get; set; }
        public DateTimeOffset FechaEmision { get; set; }
    }

    public sealed class CasetaSocio
    {
        public Guid UnidadId { get; set; }
        public string Codigo { get; set; } = "";
        public string? Bloque { get; set; }
        public string Tipo { get; set; } = "";
        public string? Pasillo { get; set; }
        public bool Actual { get; set; }
        public DateTimeOffset Desde { get; set; }
        public bool TieneKardex { get; set; }
        public List<MontoMoneda> Deuda { get; set; } = new();

        public string TipoTexto => Tipo switch
        {
            "Comedor" => "Comedor",
            "Ambulante" => "Ambulante",
            _ => "Caseta"
        };
    }

    private sealed class UnidadesRespuesta
    {
        public List<UnidadLibre> Unidades { get; set; } = new();
    }

    public sealed class UnidadLibre
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public string TipoTexto { get; set; } = "";
        public string? Titular { get; set; }
    }
}

/// <summary>Registrar un socio nuevo o corregir los datos de uno existente.</summary>
internal sealed class VentanaDatosSocio : Window
{
    public Guid? CreadoId { get; private set; }

    public string Resultado { get; private set; } = "";

    public VentanaDatosSocio(SociosView.FichaDto? socio)
    {
        var nuevo = socio is null;

        Title = nuevo ? "Registrar socio" : "Corregir datos del socio";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var pila = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        pila.Children.Add(new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta });
        pila.Children.Add(UiKit.Ayuda(nuevo
            ? "Datos personales del socio. Después le asignas su caseta y completas su Kardex."
            : "Corrige errores de escritura o actualiza el celular. Sus casetas, recibos y deudas no cambian.", 14));

        var nombres = UiKit.Texto(200, 150);
        var apellidos = UiKit.Texto(200, 150);
        var documento = UiKit.Texto(200, 50);
        var telefono = UiKit.Texto(200, 50);

        if (socio is not null)
        {
            nombres.Text = socio.Nombres;
            apellidos.Text = socio.Apellidos;
            documento.Text = socio.Documento;
            telefono.Text = socio.Telefono ?? "";
        }

        var fila1 = new WrapPanel();
        fila1.Children.Add(UiKit.Campo("Apellidos", apellidos));
        fila1.Children.Add(UiKit.Campo("Nombres", nombres, 0));
        pila.Children.Add(fila1);
        var fila2 = new WrapPanel();
        fila2.Children.Add(UiKit.Campo("CI (con extensión, ej. 1234567 LP)", documento));
        fila2.Children.Add(UiKit.Campo("Celular", telefono, 0));
        pila.Children.Add(fila2);

        var error = new TextBlock { Foreground = ColoresPanel.Hex("#A12A2A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        pila.Children.Add(error);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelar = UiKit.Boton("Cancelar");
        cancelar.IsCancel = true;
        cancelar.Margin = new Thickness(0, 0, 10, 0);
        var guardar = UiKit.Boton(nuevo ? "Registrar" : "Guardar cambios", principal: true);
        guardar.IsDefault = true;
        guardar.Click += async (_, _) =>
        {
            if (apellidos.Text.Trim().Length == 0) { error.Text = "Escribe los apellidos."; apellidos.Focus(); return; }
            if (nombres.Text.Trim().Length == 0) { error.Text = "Escribe los nombres."; nombres.Focus(); return; }
            if (nuevo && documento.Text.Trim().Length == 0) { error.Text = "Escribe el CI."; documento.Focus(); return; }

            guardar.IsEnabled = false;
            error.Text = "";
            try
            {
                if (nuevo)
                {
                    var r = await ApiJson.PostAsync<Creado>("/api/socios", new
                    {
                        nombres = nombres.Text.Trim(),
                        apellidos = apellidos.Text.Trim(),
                        documentoIdentidad = documento.Text.Trim(),
                        telefono = string.IsNullOrWhiteSpace(telefono.Text) ? null : telefono.Text.Trim()
                    });
                    CreadoId = r?.Id;
                    Resultado = $"Se registró a {nombres.Text.Trim()} {apellidos.Text.Trim()}.";
                }
                else
                {
                    var r = await ApiJson.PutAsync<Respuesta>($"/api/socios/{socio!.Id}", new
                    {
                        nombres = nombres.Text.Trim(),
                        apellidos = apellidos.Text.Trim(),
                        documentoIdentidad = documento.Text.Trim(),
                        telefono = telefono.Text.Trim()
                    });
                    Resultado = r?.Mensaje ?? "Datos guardados.";
                }

                DialogResult = true;
            }
            catch (Exception ex)
            {
                error.Text = ex.Message;
            }
            finally
            {
                guardar.IsEnabled = true;
            }
        };
        botones.Children.Add(cancelar);
        botones.Children.Add(guardar);
        pila.Children.Add(botones);

        Content = pila;
        Loaded += (_, _) => apellidos.Focus();
    }

    private sealed class Creado
    {
        public Guid Id { get; set; }
    }

    private sealed class Respuesta
    {
        public string? Mensaje { get; set; }
    }
}
