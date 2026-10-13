using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Services;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Casetas, comedores y ambulantes. A la izquierda la lista con búsqueda y
/// filtros; a la derecha la ficha de la unidad elegida: ubicación, titular
/// (con historial), deuda pendiente y estado. Crear unidades, pasillos e
/// importar desde Excel están arriba, en ventanas propias.
/// </summary>
public sealed class CasetasView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private readonly bool _admin = SesionActual.EsAdministrador || !SesionActual.Iniciada;

    // Filtros
    private readonly TextBox _buscar = UiKit.Texto(230, 60);
    private readonly ComboBox _tipo = UiKit.Combo(140);
    private readonly ComboBox _bloque = UiKit.Combo(150);
    private readonly ComboBox _pasillo = UiKit.Combo(190);
    private readonly ComboBox _estado = UiKit.Combo(120);
    private readonly CheckBox _sinTitular = new() { Content = "Solo sin titular", Margin = new Thickness(0, 0, 0, 18), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _resumen = new() { Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly DataGrid _tabla = UiKit.Tabla();

    // Ficha
    private readonly StackPanel _ficha = new();
    private readonly TextBlock _fichaVacia = new()
    {
        Text = "Elige una unidad de la lista para ver su ficha: titular, ubicación y deudas.",
        Foreground = UiKit.Suave,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 8, 4, 0)
    };

    private List<PasilloDto> _pasillos = new();
    private List<SocioDto> _socios = new();
    private FichaDto? _actual;
    private Guid? _seleccionarAlCargar;

    public CasetasView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        // ---------- Encabezado ----------

        var encabezado = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(encabezado, Dock.Top);

        if (_admin)
        {
            var acciones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(acciones, Dock.Right);

            var pasillos = UiKit.Boton("Pasillos");
            pasillos.Margin = new Thickness(8, 0, 0, 0);
            pasillos.ToolTip = "Ver y registrar los pasillos del mercado";
            pasillos.Click += async (_, _) => await AbrirPasillosAsync();

            var importar = UiKit.Boton("Importar desde Excel");
            importar.Margin = new Thickness(8, 0, 0, 0);
            importar.ToolTip = "Registrar casetas, bloques, socios y titulares desde una planilla";
            importar.Click += async (_, _) =>
            {
                var ventana = new ImportarPadronWindow { Owner = Window.GetWindow(this) };
                ventana.ShowDialog();
                if (ventana.SeImporto)
                    await CargarTodoAsync();
            };

            var nueva = UiKit.Boton("+ Nueva unidad", principal: true);
            nueva.Margin = new Thickness(8, 0, 0, 0);
            nueva.Click += async (_, _) => await NuevaUnidadAsync();

            acciones.Children.Add(pasillos);
            acciones.Children.Add(importar);
            acciones.Children.Add(nueva);
            encabezado.Children.Add(acciones);
        }

        var titulo = new StackPanel();
        titulo.Children.Add(UiKit.Encabezado("Casetas"));
        titulo.Children.Add(UiKit.Ayuda("Casetas, comedores y ambulantes del mercado. Busca una unidad y elígela para ver o cambiar su titular y su ubicación.", 0));
        encabezado.Children.Add(titulo);
        raiz.Children.Add(encabezado);

        // ---------- Filtros ----------

        _tipo.ItemsSource = new List<UiKit.Item<int?>>
        {
            new(null, "Todos"), new(1, "Casetas"), new(2, "Comedores"), new(3, "Ambulantes")
        };
        _tipo.SelectedIndex = 0;

        _estado.ItemsSource = new List<UiKit.Item<string>>
        {
            new("activas", "Activas"), new("inactivas", "Inactivas"), new("todas", "Todas")
        };
        _estado.SelectedIndex = 0;

        var filtros = new WrapPanel();
        filtros.Children.Add(UiKit.Campo("Buscar código, titular o CI", _buscar));
        filtros.Children.Add(UiKit.Campo("Tipo", _tipo));
        filtros.Children.Add(UiKit.Campo("Bloque", _bloque));
        filtros.Children.Add(UiKit.Campo("Pasillo", _pasillo));
        filtros.Children.Add(UiKit.Campo("Estado", _estado));
        filtros.Children.Add(_sinTitular);

        var tarjetaFiltros = UiKit.Tarjeta(filtros, 8);
        DockPanel.SetDock(tarjetaFiltros, Dock.Top);
        raiz.Children.Add(tarjetaFiltros);

        DockPanel.SetDock(_resumen, Dock.Top);
        raiz.Children.Add(_resumen);

        // La lista se filtra sola al escribir o cambiar un filtro.
        var espera = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        espera.Tick += async (_, _) => { espera.Stop(); await CargarListaAsync(); };
        _buscar.TextChanged += (_, _) => { espera.Stop(); espera.Start(); };
        foreach (var c in new[] { _tipo, _bloque, _pasillo, _estado })
            c.SelectionChanged += async (_, _) => await CargarListaAsync();
        _sinTitular.Click += async (_, _) => await CargarListaAsync();

        // ---------- Lista y ficha ----------

        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });

        _tabla.Columns.Add(UiKit.Columna("Código", nameof(UnidadFila.Codigo), 0.7));
        _tabla.Columns.Add(UiKit.Columna("Tipo", nameof(UnidadFila.TipoTexto), 0.8));
        _tabla.Columns.Add(UiKit.Columna("Bloque", nameof(UnidadFila.BloqueTexto), 0.9));
        _tabla.Columns.Add(UiKit.Columna("Pasillo", nameof(UnidadFila.PasilloTexto), 1.1));
        _tabla.Columns.Add(UiKit.Columna("Titular", nameof(UnidadFila.TitularTexto), 2));
        _tabla.Columns.Add(UiKit.Columna("Deuda pendiente", nameof(UnidadFila.DeudaTexto), 1.2));
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
        _ficha.Children.Add(_fichaVacia);

        raiz.Children.Add(cuerpo);
        Content = raiz;

        Loaded += async (_, _) => await CargarTodoAsync();
    }

    /// <summary>Abre la pantalla con una unidad ya elegida (desde otra pantalla).</summary>
    public void SeleccionarAlCargar(Guid unidadId) => _seleccionarAlCargar = unidadId;

    // =====================================================================
    // CARGA
    // =====================================================================

    private bool _cargandoCatalogos;

    private async Task CargarTodoAsync()
    {
        _cargandoCatalogos = true;

        try
        {
            _pasillos = (await ApiJson.GetAsync<List<PasilloDto>>("/api/pasillos") ?? new())
                .OrderBy(p => p.Codigo).ToList();

            var unidades = await ApiJson.GetAsync<List<UnidadBasica>>("/api/unidades-comerciales") ?? new();
            var bloques = unidades.Select(u => u.Bloque?.Trim()).OfType<string>().Where(b => b.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(b => b).ToList();

            Rellenar(_bloque, new[] { new UiKit.Item<string?>(null, "Todos") }
                .Concat(bloques.Select(b => new UiKit.Item<string?>(b, b))));

            Rellenar(_pasillo, new[] { new UiKit.Item<Guid?>(null, "Todos"), new UiKit.Item<Guid?>(Guid.Empty, "Sin pasillo") }
                .Concat(_pasillos.Where(p => p.Activo).Select(p => new UiKit.Item<Guid?>(p.Id, $"{p.Codigo} · {p.Nombre}"))));

            if (_admin)
            {
                _socios = (await ApiJson.GetAsync<List<SocioDto>>("/api/socios") ?? new())
                    .Where(s => s.Activo)
                    .OrderBy(s => s.NombreCompleto)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudieron cargar los datos: " + ex.Message;
        }
        finally
        {
            _cargandoCatalogos = false;
        }

        await CargarListaAsync();
    }

    private static void Rellenar<T>(ComboBox combo, IEnumerable<UiKit.Item<T>> items)
    {
        var anterior = combo.SelectedItem as UiKit.Item<T>;
        var lista = items.ToList();
        combo.ItemsSource = lista;
        combo.SelectedItem = (anterior is null ? null : lista.FirstOrDefault(i => Equals(i.Valor, anterior.Valor))) ?? lista.FirstOrDefault();
    }

    private async Task CargarListaAsync()
    {
        if (_cargandoCatalogos)
            return;

        var p = new List<string>();

        if (!string.IsNullOrWhiteSpace(_buscar.Text))
            p.Add("buscar=" + Uri.EscapeDataString(_buscar.Text.Trim()));
        if (_tipo.SelectedItem is UiKit.Item<int?> { Valor: { } tipo })
            p.Add($"tipo={tipo}");
        if (_bloque.SelectedItem is UiKit.Item<string?> { Valor: { } bloque })
            p.Add("bloque=" + Uri.EscapeDataString(bloque));
        if (_pasillo.SelectedItem is UiKit.Item<Guid?> { Valor: { } pasillo })
            p.Add(pasillo == Guid.Empty ? "sinPasillo=true" : $"pasilloId={pasillo}");
        if (_estado.SelectedItem is UiKit.Item<string> estado)
            p.Add("estado=" + estado.Valor);
        if (_sinTitular.IsChecked == true)
            p.Add("sinTitular=true");

        var seleccion = _seleccionarAlCargar ?? (_tabla.SelectedItem as UnidadFila)?.Id;

        try
        {
            var r = await ApiJson.GetAsync<ListaRespuesta>("/api/casetas?" + string.Join("&", p));
            var filas = r?.Unidades ?? new();
            _tabla.ItemsSource = filas;

            _resumen.Text = r is null || r.Total == 0
                ? "Ninguna unidad coincide con la búsqueda."
                : $"{r.Total} unidades   ·   sin titular: {r.SinTitular}   ·   sin pasillo: {r.SinPasillo}   ·   con deuda pendiente: {r.ConDeuda}";

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
        }
        catch (Exception ex)
        {
            _resumen.Text = "No se pudo cargar la lista: " + ex.Message;
        }
    }

    // =====================================================================
    // FICHA
    // =====================================================================

    private void MostrarFichaVacia()
    {
        _actual = null;
        _ficha.Children.Clear();
        _ficha.Children.Add(_fichaVacia);
    }

    private async Task MostrarFichaAsync()
    {
        if (_tabla.SelectedItem is not UnidadFila fila)
        {
            MostrarFichaVacia();
            return;
        }

        try
        {
            var f = await ApiJson.GetAsync<FichaDto>($"/api/casetas/{fila.Id}");
            if (f is null || (_tabla.SelectedItem as UnidadFila)?.Id != fila.Id)
                return;

            _actual = f;
            ConstruirFicha(f);
        }
        catch (Exception ex)
        {
            _ficha.Children.Clear();
            _ficha.Children.Add(new TextBlock { Text = "No se pudo abrir la ficha: " + ex.Message, TextWrapping = TextWrapping.Wrap, Foreground = ColoresPanel.Hex("#A12A2A") });
        }
    }

    private void ConstruirFicha(FichaDto f)
    {
        _ficha.Children.Clear();

        // ---------- Encabezado ----------

        var cabeza = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Background = ColoresPanel.Hex(f.Activa ? "#E7F5E7" : "#EEF2F7"),
            Child = new TextBlock
            {
                Text = f.Activa ? "Activa" : "Inactiva",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ColoresPanel.Hex(f.Activa ? "#0B5A0B" : "#5A6B7D")
            }
        };
        DockPanel.SetDock(chip, Dock.Right);
        cabeza.Children.Add(chip);
        cabeza.Children.Add(new TextBlock
        {
            Text = $"{f.TipoTexto} {f.Codigo}",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = UiKit.Titulo
        });
        _ficha.Children.Add(cabeza);
        _ficha.Children.Add(UiKit.Ayuda($"Registrada el {f.FechaRegistro.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)}", 10));

        // ---------- Deuda ----------

        var deuda = UiKit.Aviso(out var deudaTexto, f.DeudasPendientes > 0 ? "aviso" : "ok");
        deudaTexto.Text = f.DeudasPendientes > 0
            ? $"Debe {Montos(f.Deuda)} ({f.DeudasPendientes} {(f.DeudasPendientes == 1 ? "deuda" : "deudas")})."
            : "Al día: no tiene deudas pendientes.";
        _ficha.Children.Add(deuda);

        var irA = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (f.DeudasPendientes > 0 && (SesionActual.PuedeCobrar || !SesionActual.Iniciada))
            irA.Children.Add(Pequeno("Cobrar", principal: true, () => Navegacion.AbrirCobro(f.Id, f.Codigo)));
        irA.Children.Add(Pequeno("Ver deudas", principal: false, () => Navegacion.AbrirDeudas(f.Codigo)));
        if (f.Tipo == 1)
            irA.Children.Add(Pequeno("Kardex", principal: false, () => Navegacion.AbrirKardex(f.Id)));
        _ficha.Children.Add(irA);

        // ---------- Titular ----------

        _ficha.Children.Add(Seccion("Titular"));

        if (f.Titular is { } t)
        {
            _ficha.Children.Add(new TextBlock { Text = t.Nombre, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta, TextWrapping = TextWrapping.Wrap });
            _ficha.Children.Add(UiKit.Ayuda(
                $"CI {Texto(t.Documento)}  ·  Cel. {Texto(t.Telefono)}\nDesde el {t.Desde.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)}", 8));
        }
        else
        {
            _ficha.Children.Add(UiKit.Ayuda("Sin titular asignado.", 8));
        }

        if (_admin)
        {
            var socio = new ComboBox
            {
                Width = 250,
                Height = 36,
                IsEditable = true,
                IsTextSearchEnabled = true,
                StaysOpenOnEdit = true,
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = _socios.Select(s => new UiKit.Item<Guid>(s.Id, $"{s.NombreCompleto} — CI {Texto(s.DocumentoIdentidad)}")).ToList()
            };

            var asignar = UiKit.Boton(f.Titular is null ? "Asignar" : "Cambiar", principal: true);
            asignar.Height = 36;
            asignar.Margin = new Thickness(8, 0, 0, 0);
            asignar.Click += async (_, _) => await AsignarTitularAsync(f, socio);

            var fila = new StackPanel { Orientation = Orientation.Horizontal };
            fila.Children.Add(socio);
            fila.Children.Add(asignar);
            _ficha.Children.Add(UiKit.Campo(f.Titular is null ? "Asignar titular (escribe para buscar)" : "Cambiar titular (escribe para buscar)", fila, 0));

            if (f.Titular is not null)
            {
                var quitar = new Button
                {
                    Content = "Dejar sin titular",
                    Style = UiKit.Estilo("Mx.NavLink"),
                    Foreground = ColoresPanel.Hex("#A12A2A"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, -4, 0, 8)
                };
                quitar.Click += async (_, _) => await QuitarTitularAsync(f);
                _ficha.Children.Add(quitar);
            }
        }

        var anteriores = f.Historial.Where(h => !h.Actual).ToList();
        if (anteriores.Count > 0)
        {
            _ficha.Children.Add(new TextBlock { Text = "Titulares anteriores", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Etiqueta, Margin = new Thickness(0, 4, 0, 4) });
            foreach (var h in anteriores)
            {
                _ficha.Children.Add(new TextBlock
                {
                    Text = $"• {h.Nombre} (desde {h.Desde.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo)})",
                    Foreground = UiKit.Suave,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }
        }

        // ---------- Ubicación ----------

        _ficha.Children.Add(Seccion("Ubicación"));

        if (_admin)
        {
            var bloque = UiKit.Texto(150, 50);
            bloque.Text = f.Bloque ?? "";

            var pasillo = UiKit.Combo(220);
            var opciones = new[] { new UiKit.Item<Guid?>(null, "Sin pasillo") }
                .Concat(_pasillos.Where(p => p.Activo || p.Id == f.PasilloId)
                    .Select(p => new UiKit.Item<Guid?>(p.Id, $"{p.Codigo} · {p.Nombre}")))
                .ToList();
            pasillo.ItemsSource = opciones;
            pasillo.SelectedItem = opciones.FirstOrDefault(o => o.Valor == f.PasilloId) ?? opciones[0];

            _ficha.Children.Add(UiKit.Campo("Bloque", bloque, 0));
            _ficha.Children.Add(UiKit.Campo(_pasillos.Count == 0 ? "Pasillo (todavía no hay pasillos registrados)" : "Pasillo", pasillo, 0));

            var guardar = UiKit.Boton("Guardar ubicación");
            guardar.HorizontalAlignment = HorizontalAlignment.Left;
            guardar.Margin = new Thickness(0, 0, 0, 8);
            guardar.Click += async (_, _) => await GuardarUbicacionAsync(f, bloque.Text, (pasillo.SelectedItem as UiKit.Item<Guid?>)?.Valor);
            _ficha.Children.Add(guardar);
        }
        else
        {
            _ficha.Children.Add(UiKit.Ayuda($"Bloque: {Texto(f.Bloque)}\nPasillo: {Texto(f.Pasillo)}", 8));
        }

        // ---------- Estado ----------

        if (_admin)
        {
            _ficha.Children.Add(Seccion("Estado"));
            _ficha.Children.Add(UiKit.Ayuda(f.Activa
                ? "Una unidad inactiva no recibe cargos mensuales. Sus deudas y recibos se conservan."
                : "Esta unidad no recibe cargos mensuales mientras esté inactiva.", 8));

            var estado = UiKit.Boton(f.Activa ? "Desactivar unidad" : "Activar unidad", peligro: f.Activa);
            estado.HorizontalAlignment = HorizontalAlignment.Left;
            estado.Click += async (_, _) => await CambiarEstadoAsync(f);
            _ficha.Children.Add(estado);
        }
    }

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

    private static string Texto(string? valor) =>
        string.IsNullOrWhiteSpace(valor) || valor == "SIN_DOCUMENTO" ? "—" : valor!;

    private static string Montos(List<MontoMoneda> montos) =>
        string.Join(" + ", montos.Select(m => $"{m.Moneda} {m.Total.ToString("N2", Bo)}"));

    // =====================================================================
    // ACCIONES
    // =====================================================================

    private async Task Ejecutar(Func<Task<Mensaje?>> accion, string tituloError)
    {
        try
        {
            var r = await accion();
            await CargarListaAsync();
            await MostrarFichaAsync();

            if (!string.IsNullOrWhiteSpace(r?.Texto))
                _resumen.Text = "✓ " + r!.Texto;
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, tituloError, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AsignarTitularAsync(FichaDto f, ComboBox combo)
    {
        if (combo.SelectedItem is not UiKit.Item<Guid> socio)
        {
            Dialogo.Mostrar(Window.GetWindow(this),
                "Escribe el nombre del socio y elígelo de la lista. Si no existe, regístralo primero en Socios.",
                "Elige un socio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (f.Titular is { } actual)
        {
            var confirmar = Dialogo.Mostrar(Window.GetWindow(this),
                $"Unidad: {f.TipoTexto} {f.Codigo}\nTitular actual: {actual.Nombre}\nNuevo titular: {socio.Texto}\n\n" +
                "El titular actual queda en el historial. Las deudas pendientes siguen siendo de la unidad.\n\n¿Cambiar el titular?",
                "Cambiar titular", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmar != MessageBoxResult.Yes)
                return;
        }

        await Ejecutar(() => ApiJson.PostAsync<Mensaje>($"/api/casetas/{f.Id}/titular", new { socioId = socio.Valor }),
            "No se asignó el titular");
    }

    private async Task QuitarTitularAsync(FichaDto f)
    {
        var confirmar = Dialogo.Mostrar(Window.GetWindow(this),
            $"Unidad: {f.TipoTexto} {f.Codigo}\nTitular: {f.Titular?.Nombre}\n\nLa unidad queda sin titular y él queda en el historial.\n\n¿Continuar?",
            "Dejar sin titular", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirmar == MessageBoxResult.Yes)
            await Ejecutar(() => ApiJson.PostAsync<Mensaje>($"/api/casetas/{f.Id}/quitar-titular", new { }), "No se pudo quitar el titular");
    }

    private Task GuardarUbicacionAsync(FichaDto f, string bloque, Guid? pasilloId) =>
        Ejecutar(() => ApiJson.PutAsync<Mensaje>($"/api/casetas/{f.Id}/ubicacion", new { bloque, pasilloId }),
            "No se guardó la ubicación");

    private async Task CambiarEstadoAsync(FichaDto f)
    {
        if (f.Activa)
        {
            var confirmar = Dialogo.Mostrar(Window.GetWindow(this),
                $"Unidad: {f.TipoTexto} {f.Codigo}\n\nDeja de recibir cargos mensuales. Sus deudas pendientes y recibos se conservan, " +
                "y puedes volver a activarla cuando quieras.\n\n¿Desactivar la unidad?",
                "Desactivar unidad", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirmar != MessageBoxResult.Yes)
                return;
        }

        await Ejecutar(() => ApiJson.PostAsync<Mensaje>($"/api/casetas/{f.Id}/{(f.Activa ? "desactivar" : "activar")}", new { }),
            "No se cambió el estado");
    }

    private async Task NuevaUnidadAsync()
    {
        var ventana = new VentanaNuevaUnidad(_pasillos.Where(p => p.Activo).ToList()) { Owner = Window.GetWindow(this) };
        if (ventana.ShowDialog() != true)
            return;

        _seleccionarAlCargar = ventana.CreadaId;
        await CargarTodoAsync();
    }

    private async Task AbrirPasillosAsync()
    {
        new VentanaPasillos { Owner = Window.GetWindow(this) }.ShowDialog();
        await CargarTodoAsync();
    }

    // =====================================================================
    // MODELOS
    // =====================================================================

    public sealed class PasilloDto
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }
    }

    private sealed class SocioDto
    {
        public Guid Id { get; set; }
        public string NombreCompleto { get; set; } = "";
        public string? DocumentoIdentidad { get; set; }
        public bool Activo { get; set; }
    }

    private sealed class UnidadBasica
    {
        public string? Bloque { get; set; }
    }

    private sealed class ListaRespuesta
    {
        public int Total { get; set; }
        public int SinTitular { get; set; }
        public int SinPasillo { get; set; }
        public int ConDeuda { get; set; }
        public List<UnidadFila> Unidades { get; set; } = new();
    }

    public sealed class MontoMoneda
    {
        public string Moneda { get; set; } = "";
        public decimal Total { get; set; }
    }

    public sealed class UnidadFila
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public string TipoTexto { get; set; } = "";
        public string? Bloque { get; set; }
        public string? Pasillo { get; set; }
        public string? Titular { get; set; }
        public int DeudasPendientes { get; set; }
        public List<MontoMoneda> Deuda { get; set; } = new();

        public string BloqueTexto => string.IsNullOrWhiteSpace(Bloque) ? "—" : Bloque!;
        public string PasilloTexto => string.IsNullOrWhiteSpace(Pasillo) ? "—" : Pasillo!;
        public string TitularTexto => string.IsNullOrWhiteSpace(Titular) ? "Sin titular" : Titular!;
        public string DeudaTexto => DeudasPendientes == 0 ? "Al día" : Montos(Deuda);
    }

    private sealed class FichaDto
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public int Tipo { get; set; }
        public string TipoTexto { get; set; } = "";
        public string? Bloque { get; set; }
        public Guid? PasilloId { get; set; }
        public string? Pasillo { get; set; }
        public bool Activa { get; set; }
        public DateTimeOffset FechaRegistro { get; set; }
        public TitularDto? Titular { get; set; }
        public List<TitularDto> Historial { get; set; } = new();
        public int DeudasPendientes { get; set; }
        public List<MontoMoneda> Deuda { get; set; } = new();
    }

    private sealed class TitularDto
    {
        public string Nombre { get; set; } = "";
        public string? Documento { get; set; }
        public string? Telefono { get; set; }
        public DateTimeOffset Desde { get; set; }
        public bool Actual { get; set; }
    }

    private sealed class Mensaje
    {
        [System.Text.Json.Serialization.JsonPropertyName("mensaje")]
        public string? Texto { get; set; }
    }
}

/// <summary>Registrar una caseta, comedor o ambulante.</summary>
internal sealed class VentanaNuevaUnidad : Window
{
    public Guid? CreadaId { get; private set; }

    public VentanaNuevaUnidad(List<CasetasView.PasilloDto> pasillos)
    {
        Title = "Nueva unidad";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var pila = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        pila.Children.Add(new TextBlock { Text = "Nueva unidad", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta });
        pila.Children.Add(UiKit.Ayuda("Después de registrarla puedes asignarle el titular desde su ficha.", 14));

        var codigo = UiKit.Texto(140, 20);
        var tipo = UiKit.Combo(160);
        tipo.ItemsSource = new List<UiKit.Item<int>> { new(1, "Caseta"), new(2, "Comedor"), new(3, "Ambulante") };
        tipo.SelectedIndex = 0;
        var bloque = UiKit.Texto(160, 50);
        var pasillo = UiKit.Combo(250);
        var opciones = new[] { new UiKit.Item<Guid?>(null, "Sin pasillo") }
            .Concat(pasillos.Select(p => new UiKit.Item<Guid?>(p.Id, $"{p.Codigo} · {p.Nombre}"))).ToList();
        pasillo.ItemsSource = opciones;
        pasillo.SelectedIndex = 0;

        var fila = new WrapPanel();
        fila.Children.Add(UiKit.Campo("Número o código", codigo));
        fila.Children.Add(UiKit.Campo("Tipo", tipo, 0));
        pila.Children.Add(fila);
        var fila2 = new WrapPanel();
        fila2.Children.Add(UiKit.Campo("Bloque", bloque));
        fila2.Children.Add(UiKit.Campo("Pasillo", pasillo, 0));
        pila.Children.Add(fila2);

        var error = new TextBlock { Foreground = ColoresPanel.Hex("#A12A2A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        pila.Children.Add(error);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelar = UiKit.Boton("Cancelar");
        cancelar.IsCancel = true;
        cancelar.Margin = new Thickness(0, 0, 10, 0);
        var guardar = UiKit.Boton("Registrar", principal: true);
        guardar.IsDefault = true;
        guardar.Click += async (_, _) =>
        {
            if (codigo.Text.Trim().Length == 0)
            {
                error.Text = "Escribe el número o código.";
                codigo.Focus();
                return;
            }

            guardar.IsEnabled = false;
            try
            {
                var r = await ApiJson.PostAsync<Creada>("/api/unidades-comerciales", new
                {
                    codigo = codigo.Text.Trim(),
                    tipo = (tipo.SelectedItem as UiKit.Item<int>)?.Valor ?? 1,
                    pasilloId = (pasillo.SelectedItem as UiKit.Item<Guid?>)?.Valor,
                    bloque = string.IsNullOrWhiteSpace(bloque.Text) ? null : bloque.Text.Trim()
                });

                CreadaId = r?.Id ?? r?.Unidad?.Id;
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
        Loaded += (_, _) => codigo.Focus();
    }

    private sealed class Creada
    {
        public Guid? Id { get; set; }
        public UnidadId? Unidad { get; set; }
    }

    private sealed class UnidadId
    {
        public Guid Id { get; set; }
    }
}

/// <summary>Pasillos del mercado: ver y registrar.</summary>
internal sealed class VentanaPasillos : Window
{
    private readonly DataGrid _tabla = UiKit.Tabla();
    private readonly TextBlock _estado = new() { Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

    public VentanaPasillos()
    {
        Title = "Pasillos";
        Width = 560;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);
        arriba.Children.Add(new TextBlock { Text = "Pasillos", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = UiKit.Titulo });
        arriba.Children.Add(UiKit.Ayuda("El mercado tiene pasillos con nombre propio. Regístralos aquí y luego elígelos en la ficha de cada caseta."));

        var codigo = UiKit.Texto(100, 30);
        var nombre = UiKit.Texto(220, 100);
        var registrar = UiKit.Boton("Registrar", principal: true);
        registrar.Margin = new Thickness(0, 0, 0, 10);
        registrar.Click += async (_, _) =>
        {
            if (codigo.Text.Trim().Length == 0 || nombre.Text.Trim().Length == 0)
            {
                _estado.Text = "Escribe el código y el nombre del pasillo.";
                return;
            }

            try
            {
                await ApiJson.PostAsync<object>("/api/pasillos", new { codigo = codigo.Text.Trim(), nombre = nombre.Text.Trim() });
                _estado.Text = $"✓ Pasillo «{nombre.Text.Trim()}» registrado.";
                codigo.Clear();
                nombre.Clear();
                codigo.Focus();
                await CargarAsync();
            }
            catch (Exception ex)
            {
                _estado.Text = ex.Message;
            }
        };

        var fila = new WrapPanel();
        fila.Children.Add(UiKit.Campo("Código", codigo));
        fila.Children.Add(UiKit.Campo("Nombre", nombre));
        fila.Children.Add(registrar);
        arriba.Children.Add(UiKit.Tarjeta(fila, 8));
        arriba.Children.Add(_estado);
        raiz.Children.Add(arriba);

        _tabla.Columns.Add(UiKit.Columna("Código", nameof(CasetasView.PasilloDto.Codigo), 1));
        _tabla.Columns.Add(UiKit.Columna("Nombre", nameof(CasetasView.PasilloDto.Nombre), 3));
        raiz.Children.Add(_tabla);

        Content = raiz;
        Loaded += async (_, _) => await CargarAsync();
    }

    private async Task CargarAsync()
    {
        try
        {
            var pasillos = await ApiJson.GetAsync<List<CasetasView.PasilloDto>>("/api/pasillos") ?? new();
            _tabla.ItemsSource = pasillos.OrderBy(p => p.Codigo).ToList();
            if (pasillos.Count == 0 && _estado.Text.Length == 0)
                _estado.Text = "Todavía no hay pasillos registrados.";
        }
        catch (Exception ex)
        {
            _estado.Text = ex.Message;
        }
    }
}
