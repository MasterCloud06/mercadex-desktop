using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Obligaciones (manual: Generador de cobranza). Cuatro pestañas, cada una
/// con una sola tarea:
/// 1. Deudas registradas: buscar, modificar monto, ver historial, ir a cobrar.
/// 2. Cargo mensual a varias casetas: con vista previa antes de generar.
/// 3. Cargo a una caseta.
/// 4. Tarifas por caseta: el monto de cada caseta para cada concepto.
/// </summary>
public sealed class CobranzaView : UserControl
{
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private static readonly string[] Meses =
    {
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    };

    // Catálogos
    private List<ConceptoIngresoModel> _conceptos = new();
    private List<MonedaDto> _monedas = new();
    private List<UnidadDto> _unidades = new();
    private List<PasilloDto> _pasillos = new();
    private List<string> _bloques = new();

    private readonly TabControl _pestanas = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _estadoGeneral = new() { Foreground = UiKit.Suave, Margin = new Thickness(0, 0, 0, 6) };

    public CobranzaView()
    {
        Background = UiKit.Fondo;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };

        var arriba = new StackPanel();
        DockPanel.SetDock(arriba, Dock.Top);
        arriba.Children.Add(UiKit.Encabezado("Obligaciones"));
        arriba.Children.Add(UiKit.Ayuda(
            "Las deudas de cada caseta. Aquí se registran; después se cobran en Ingresos o con Cobro rápido.", 6));
        arriba.Children.Add(_estadoGeneral);
        raiz.Children.Add(arriba);

        _pestanas.Items.Add(Pestana("Deudas registradas", ConstruirDeudas()));

        if (SesionActual.EsAdministrador || !SesionActual.Iniciada)
        {
            _pestanas.Items.Add(Pestana("Cargo mensual a varias casetas", ConstruirCargoMasivo()));
            _pestanas.Items.Add(Pestana("Cargo a una caseta", ConstruirCargoIndividual()));
            _pestanas.Items.Add(Pestana("Tarifas por caseta", ConstruirTarifas()));
        }

        raiz.Children.Add(_pestanas);
        Content = raiz;

        Loaded += async (_, _) =>
        {
            await CargarCatalogosAsync();
            await BuscarDeudasAsync();
        };
    }

    /// <summary>Abre «Deudas registradas» filtrada por esa caseta (todas sus deudas).</summary>
    public void FiltrarCaseta(string codigo)
    {
        _fCaseta.Text = codigo;
        _fEstado.SelectedIndex = 3;
    }

    private static TabItem Pestana(string titulo, UIElement contenido) => new()
    {
        Header = titulo,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Foreground = UiKit.Titulo,
        Padding = new Thickness(14, 8, 14, 8),
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = contenido,
            Padding = new Thickness(2, 14, 2, 4)
        }
    };

    // =====================================================================
    // CATÁLOGOS
    // =====================================================================

    private async Task CargarCatalogosAsync()
    {
        try
        {
            _estadoGeneral.Text = "Cargando conceptos, monedas y casetas…";

            var catalogos = await ApiJson.GetAsync<CatalogosDto>("/api/ingresos/catalogos");
            _conceptos = (catalogos?.Conceptos ?? new()).OrderBy(c => c.Nombre).ToList();
            _monedas = await ApiJson.GetAsync<List<MonedaDto>>("/api/monedas") ?? new();
            _unidades = await ApiJson.GetAsync<List<UnidadDto>>("/api/unidades-comerciales") ?? new();

            try
            {
                _pasillos = (await ApiJson.GetAsync<List<PasilloDto>>("/api/pasillos") ?? new())
                    .Where(p => p.Activo).OrderBy(p => p.Codigo).ToList();
            }
            catch
            {
                _pasillos = new();
            }

            _bloques = _unidades
                .Where(u => u.Activa)
                .Select(u => u.Bloque?.Trim())
                .OfType<string>()
                .Where(b => b.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(b => b)
                .ToList();

            LlenarCombos();
            _estadoGeneral.Text = "";
        }
        catch (Exception ex)
        {
            _estadoGeneral.Text = "No se pudieron cargar los datos: " + ex.Message;
        }
    }

    private void LlenarCombos()
    {
        // Deudas registradas
        Llenar(_fConcepto, new[] { new UiKit.Item<Guid?>(null, "Todos los conceptos") }
            .Concat(_conceptos.Select(c => new UiKit.Item<Guid?>(c.Id, c.Nombre))));
        Llenar(_fBloque, new[] { new UiKit.Item<string?>(null, "Todos los bloques") }
            .Concat(_bloques.Select(b => new UiKit.Item<string?>(b, b))));

        // Cargo masivo
        Llenar(_mConcepto, _conceptos.Select(c => new UiKit.Item<Guid?>(c.Id, c.Nombre)));
        Llenar(_mBloque, _bloques.Select(b => new UiKit.Item<string?>(b, b)));
        Llenar(_mPasillo, _pasillos.Select(p => new UiKit.Item<Guid?>(p.Id, $"{p.Codigo} · {p.Nombre}")));
        Llenar(_mMoneda, _monedas.Select(m => new UiKit.Item<Guid?>(m.Id, m.Codigo)));

        // Cargo individual
        Llenar(_iConcepto, _conceptos.Select(c => new UiKit.Item<Guid?>(c.Id, c.Nombre)));
        Llenar(_iMoneda, _monedas.Select(m => new UiKit.Item<Guid?>(m.Id, m.Codigo)));

        // Tarifas
        Llenar(_tConcepto, _conceptos.Select(c => new UiKit.Item<Guid?>(c.Id, c.Nombre)), seleccionarPrimero: false);
        Llenar(_tMoneda, _monedas.Select(m => new UiKit.Item<Guid?>(m.Id, m.Codigo)));
        Llenar(_tBloque, _bloques.Select(b => new UiKit.Item<string?>(b, b)));
        Llenar(_tPasillo, _pasillos.Select(p => new UiKit.Item<Guid?>(p.Id, $"{p.Codigo} · {p.Nombre}")));

        foreach (var combo in new[] { _mMoneda, _iMoneda, _tMoneda })
        {
            if (combo.ItemsSource is IEnumerable<UiKit.Item<Guid?>> items)
                combo.SelectedItem = items.FirstOrDefault(i => i.Texto == "BOB") ?? combo.SelectedItem;
        }
    }

    private static void Llenar<T>(ComboBox combo, IEnumerable<UiKit.Item<T>> items, bool seleccionarPrimero = true)
    {
        var anterior = combo.SelectedItem as UiKit.Item<T>;
        var lista = items.ToList();
        combo.ItemsSource = lista;
        combo.SelectedItem = anterior is null ? null : lista.FirstOrDefault(i => Equals(i.Valor, anterior.Valor));

        if (combo.SelectedItem is null && seleccionarPrimero && lista.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static T? Valor<T>(ComboBox combo) =>
        combo.SelectedItem is UiKit.Item<T> item ? item.Valor : default;

    private static ComboBox ComboMeses(int? mes)
    {
        var combo = UiKit.Combo(130);
        combo.ItemsSource = Enumerable.Range(1, 12).Select(m => new UiKit.Item<int>(m, Meses[m - 1])).ToList();
        combo.SelectedIndex = (mes ?? DateTime.Today.Month) - 1;
        return combo;
    }

    private static ComboBox ComboAnios()
    {
        var combo = UiKit.Combo(90);
        var hoy = DateTime.Today.Year;
        combo.ItemsSource = Enumerable.Range(hoy - 3, 5).Select(a => new UiKit.Item<int>(a, a.ToString())).ToList();
        combo.SelectedIndex = 3;
        return combo;
    }

    private static string Fecha(DateTime? fecha) => fecha?.ToString("yyyy-MM-dd") ?? "";

    // =====================================================================
    // 1. DEUDAS REGISTRADAS
    // =====================================================================

    private readonly TextBox _fCaseta = UiKit.Texto(110, 20);
    private readonly ComboBox _fConcepto = UiKit.Combo(200);
    private readonly ComboBox _fEstado = UiKit.Combo(140);
    private readonly ComboBox _fMes = UiKit.Combo(130);
    private readonly ComboBox _fAnio = UiKit.Combo(100);
    private readonly ComboBox _fBloque = UiKit.Combo(150);
    private readonly DataGrid _tablaDeudas = UiKit.Tabla();
    private readonly TextBlock _resumenDeudas = new() { TextWrapping = TextWrapping.Wrap, Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Button _btnModificar = UiKit.Boton("Modificar monto");
    private readonly Button _btnHistorial = UiKit.Boton("Ver historial");
    private readonly Button _btnCobrar = UiKit.Boton("Ir a cobrar", principal: true);
    private readonly TextBlock _ayudaSeleccion = new() { Foreground = UiKit.Suave, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    private UIElement ConstruirDeudas()
    {
        var pila = new StackPanel();

        pila.Children.Add(UiKit.Ayuda(
            "Busca las deudas de una caseta o de todo el mercado. Elige una fila para modificar su monto, ver quién la cambió o ir a cobrarla."));

        _fEstado.ItemsSource = new List<UiKit.Item<string?>>
        {
            new("Pendiente", "Pendientes"),
            new("Pagada", "Pagadas"),
            new("Anulada", "Anuladas"),
            new(null, "Todas")
        };
        _fEstado.SelectedIndex = 0;

        _fMes.ItemsSource = new[] { new UiKit.Item<int?>(null, "Todos") }
            .Concat(Enumerable.Range(1, 12).Select(m => new UiKit.Item<int?>(m, Meses[m - 1]))).ToList();
        _fMes.SelectedIndex = 0;

        var hoy = DateTime.Today.Year;
        _fAnio.ItemsSource = new[] { new UiKit.Item<int?>(null, "Todos") }
            .Concat(Enumerable.Range(hoy - 4, 6).Reverse().Select(a => new UiKit.Item<int?>(a, a.ToString()))).ToList();
        _fAnio.SelectedIndex = 0;

        var buscar = UiKit.Boton("Buscar", principal: true);
        buscar.Margin = new Thickness(0, 0, 8, 10);
        buscar.Click += async (_, _) => await BuscarDeudasAsync();

        var limpiar = UiKit.Boton("Limpiar");
        limpiar.Margin = new Thickness(0, 0, 0, 10);
        limpiar.Click += async (_, _) =>
        {
            _fCaseta.Clear();
            _fConcepto.SelectedIndex = 0;
            _fEstado.SelectedIndex = 0;
            _fMes.SelectedIndex = 0;
            _fAnio.SelectedIndex = 0;
            _fBloque.SelectedIndex = 0;
            await BuscarDeudasAsync();
        };

        _fCaseta.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
                await BuscarDeudasAsync();
        };

        var filtros = new WrapPanel();
        filtros.Children.Add(UiKit.Campo("Caseta", _fCaseta));
        filtros.Children.Add(UiKit.Campo("Concepto", _fConcepto));
        filtros.Children.Add(UiKit.Campo("Estado", _fEstado));
        filtros.Children.Add(UiKit.Campo("Mes", _fMes));
        filtros.Children.Add(UiKit.Campo("Año", _fAnio));
        filtros.Children.Add(UiKit.Campo("Bloque", _fBloque));
        filtros.Children.Add(buscar);
        filtros.Children.Add(limpiar);
        pila.Children.Add(UiKit.Tarjeta(filtros));

        pila.Children.Add(_resumenDeudas);

        _tablaDeudas.Columns.Add(UiKit.Columna("Caseta", nameof(DeudaFila.Caseta), 0.8));
        _tablaDeudas.Columns.Add(UiKit.Columna("Bloque", nameof(DeudaFila.BloqueTexto), 1));
        _tablaDeudas.Columns.Add(UiKit.Columna("Concepto", nameof(DeudaFila.Concepto), 1.8));
        _tablaDeudas.Columns.Add(UiKit.Columna("Período", nameof(DeudaFila.Periodo), 1));
        _tablaDeudas.Columns.Add(UiKit.Columna("Monto", nameof(DeudaFila.Monto), 0.9, "N2", derecha: true));
        _tablaDeudas.Columns.Add(UiKit.Columna("Moneda", nameof(DeudaFila.Moneda), 0.7));
        _tablaDeudas.Columns.Add(UiKit.Columna("Emisión", nameof(DeudaFila.EmisionTexto), 1));
        _tablaDeudas.Columns.Add(UiKit.Columna("Vence", nameof(DeudaFila.VenceTexto), 1));
        _tablaDeudas.Columns.Add(UiKit.Columna("Estado", nameof(DeudaFila.Estado), 0.9));
        _tablaDeudas.Columns.Add(UiKit.Columna("Nota", nameof(DeudaFila.Nota), 1));
        _tablaDeudas.Height = 420;
        _tablaDeudas.SelectionChanged += (_, _) => ActualizarAccionesDeuda();
        _tablaDeudas.MouseDoubleClick += (_, _) =>
        {
            if (_btnCobrar.IsEnabled)
                IrACobrar();
        };
        pila.Children.Add(_tablaDeudas);

        _btnModificar.Margin = new Thickness(10, 0, 0, 0);
        _btnHistorial.Margin = new Thickness(10, 0, 0, 0);
        _btnCobrar.Margin = new Thickness(10, 0, 0, 0);
        _btnModificar.Click += async (_, _) => await ModificarMontoAsync();
        _btnHistorial.Click += async (_, _) => await VerHistorialAsync();
        _btnCobrar.Click += (_, _) => IrACobrar();

        var acciones = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var botones = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(botones, Dock.Right);
        botones.Children.Add(_btnHistorial);
        if (SesionActual.EsAdministrador || !SesionActual.Iniciada)
            botones.Children.Add(_btnModificar);
        if (SesionActual.PuedeCobrar || !SesionActual.Iniciada)
            botones.Children.Add(_btnCobrar);
        acciones.Children.Add(botones);
        acciones.Children.Add(_ayudaSeleccion);
        pila.Children.Add(acciones);

        ActualizarAccionesDeuda();
        return pila;
    }

    private async Task BuscarDeudasAsync()
    {
        var parametros = new List<string>();

        if (!string.IsNullOrWhiteSpace(_fCaseta.Text))
            parametros.Add("caseta=" + Uri.EscapeDataString(_fCaseta.Text.Trim()));
        if (Valor<Guid?>(_fConcepto) is { } concepto)
            parametros.Add($"conceptoId={concepto}");
        if (Valor<string?>(_fEstado) is { } estado)
            parametros.Add("estado=" + estado);
        if (Valor<int?>(_fMes) is { } mes)
            parametros.Add($"mes={mes}");
        if (Valor<int?>(_fAnio) is { } anio)
            parametros.Add($"anio={anio}");
        if (Valor<string?>(_fBloque) is { } bloque)
            parametros.Add("bloque=" + Uri.EscapeDataString(bloque));

        try
        {
            _resumenDeudas.Text = "Buscando…";
            var r = await ApiJson.GetAsync<DeudasRespuesta>("/api/cobranza/deudas?" + string.Join("&", parametros));

            _tablaDeudas.ItemsSource = r?.Deudas ?? new();

            if (r is null || r.Total == 0)
            {
                _resumenDeudas.Text = "No hay deudas con esos filtros.";
            }
            else
            {
                var partes = r.Resumen
                    .GroupBy(x => x.Estado)
                    .OrderBy(g => g.Key == "Pendiente" ? 0 : g.Key == "Pagada" ? 1 : 2)
                    .Select(g => $"{EstadoPlural(g.Key)}: {g.Sum(x => x.Cantidad)} · " +
                                 string.Join(" + ", g.Select(x => $"{x.Moneda} {x.Monto.ToString("N2", Bo)}")));

                _resumenDeudas.Text = string.Join("      ", partes) +
                    (r.Mostradas < r.Total ? $"      (se muestran las primeras {r.Mostradas} de {r.Total}; usa los filtros)" : "");
            }
        }
        catch (Exception ex)
        {
            _resumenDeudas.Text = "No se pudo buscar: " + ex.Message;
        }

        ActualizarAccionesDeuda();
    }

    private static string EstadoPlural(string estado) => estado switch
    {
        "Pendiente" => "Pendientes",
        "Pagada" => "Pagadas",
        "Anulada" => "Anuladas",
        _ => estado
    };

    private void ActualizarAccionesDeuda()
    {
        var d = _tablaDeudas.SelectedItem as DeudaFila;
        var pendiente = d?.Estado == "Pendiente";

        _btnModificar.IsEnabled = pendiente;
        _btnCobrar.IsEnabled = pendiente;
        _btnHistorial.IsEnabled = d is { Modificaciones: > 0 };

        _ayudaSeleccion.Text = d is null
            ? "Elige una deuda de la lista."
            : pendiente
                ? $"Caseta {d.Caseta} · {d.Concepto} {d.Periodo} · {d.Moneda} {d.Monto.ToString("N2", Bo)}"
                : $"Esta deuda está {d.Estado.ToLowerInvariant()}: no se puede modificar ni cobrar.";
    }

    private void IrACobrar()
    {
        if (_tablaDeudas.SelectedItem is DeudaFila d)
            Navegacion.AbrirCobro(d.UnidadId, d.Caseta);
    }

    private async Task ModificarMontoAsync()
    {
        if (_tablaDeudas.SelectedItem is not DeudaFila d || d.Estado != "Pendiente")
            return;

        var ventana = new VentanaModificarMonto(d) { Owner = Window.GetWindow(this) };
        if (ventana.ShowDialog() != true)
            return;

        try
        {
            await ApiJson.PutAsync<object>($"/api/obligaciones-correcciones/{d.Id}/monto", new
            {
                montoAnteriorEsperado = d.Monto,
                montoNuevo = ventana.MontoNuevo,
                motivo = ventana.Motivo
            });

            Dialogo.Mostrar(Window.GetWindow(this),
                $"Caseta: {d.Caseta}\nConcepto: {d.Concepto} {d.Periodo}\nAntes: {d.Moneda} {d.Monto.ToString("N2", Bo)}\n" +
                $"Ahora: {d.Moneda} {ventana.MontoNuevo.ToString("N2", Bo)}\n\nEl cambio quedó en el historial a tu nombre.",
                "Monto modificado", MessageBoxButton.OK, MessageBoxImage.Information);

            await BuscarDeudasAsync();
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se pudo modificar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task VerHistorialAsync()
    {
        if (_tablaDeudas.SelectedItem is not DeudaFila d)
            return;

        try
        {
            var r = await ApiJson.GetAsync<HistorialRespuesta>($"/api/obligaciones-correcciones/{d.Id}/historial");
            var lineas = (r?.Historial ?? new())
                .Select(h =>
                    $"{DateTime.SpecifyKind(h.FechaUtc, DateTimeKind.Utc).AddHours(-4).ToString("dd/MM/yyyy HH:mm", Bo)}: " +
                    $"{h.MontoAnterior.ToString("N2", Bo)} → {h.MontoNuevo.ToString("N2", Bo)} · {h.Responsable}\n{h.Motivo}");

            Dialogo.Mostrar(Window.GetWindow(this),
                $"Caseta {d.Caseta} · {d.Concepto} {d.Periodo}\n\n" + string.Join("\n\n", lineas),
                "Historial de modificaciones", MessageBoxButton.OK, MessageBoxImage.None);
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se pudo consultar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================
    // 2. CARGO MENSUAL A VARIAS CASETAS
    // =====================================================================

    private readonly ComboBox _mConcepto = UiKit.Combo(230);
    private readonly ComboBox _mMes = ComboMeses(null);
    private readonly ComboBox _mAnio = ComboAnios();
    private readonly DatePicker _mEmision = UiKit.Fecha(DateTime.Today);
    private readonly DatePicker _mVence = UiKit.Fecha(DateTime.Today.AddDays(30));
    private readonly RadioButton _mTodas = UiKit.Opcion("Todas las casetas", "alcanceMasivo", marcado: true);
    private readonly RadioButton _mPorBloque = UiKit.Opcion("Un bloque:", "alcanceMasivo");
    private readonly RadioButton _mPorPasillo = UiKit.Opcion("Un pasillo:", "alcanceMasivo");
    private readonly ComboBox _mBloque = UiKit.Combo(170);
    private readonly ComboBox _mPasillo = UiKit.Combo(220);
    private readonly CheckBox _mComedores = new() { Content = "Incluir comedores", Margin = new Thickness(0, 4, 22, 0) };
    private readonly CheckBox _mAmbulantes = new() { Content = "Incluir ambulantes", Margin = new Thickness(0, 4, 0, 0) };
    private readonly RadioButton _mFijo = UiKit.Opcion("El mismo monto para todas", "montoMasivo", marcado: true);
    private readonly RadioButton _mTarifa = UiKit.Opcion("La tarifa de cada caseta", "montoMasivo");
    private readonly TextBox _mMonto = UiKit.Texto(120, 15);
    private readonly ComboBox _mMoneda = UiKit.Combo(100);
    private readonly StackPanel _mPanelFijo = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 0, 0, 0) };
    private readonly TextBlock _mAyudaTarifa = new() { Foreground = UiKit.Suave, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 0, 0, 0) };
    private readonly Button _mVerPrevia = UiKit.Boton("Ver a quiénes se les cobra", principal: true);
    private readonly Button _mGenerar = UiKit.Boton("Generar deudas", principal: true);
    private Border? _mResumen;
    private TextBlock _mResumenTexto = new();
    private readonly DataGrid _mTabla = UiKit.Tabla();
    private int? _mListasPrevia;

    private UIElement ConstruirCargoMasivo()
    {
        var pila = new StackPanel();
        pila.Children.Add(UiKit.Ayuda(
            "Carga una deuda del mismo concepto a muchas casetas a la vez (por ejemplo, la mensualidad de octubre). " +
            "Primero verás la lista; nada se guarda hasta que pulses «Generar deudas». " +
            "Las casetas que ya tienen esa deuda en ese mes se omiten solas."));

        // ---------- 1. Qué ----------
        var que = new StackPanel();
        que.Children.Add(UiKit.Subtitulo("1. ¿Qué se cobra?"));
        var filaQue = new WrapPanel();

        var nuevoConcepto = UiKit.Boton("+ Nuevo concepto");
        nuevoConcepto.Margin = new Thickness(0, 0, 14, 10);
        nuevoConcepto.Click += async (_, _) => await NuevoConceptoAsync(_mConcepto);

        filaQue.Children.Add(UiKit.Campo("Concepto", _mConcepto, 8));
        filaQue.Children.Add(nuevoConcepto);
        filaQue.Children.Add(UiKit.Campo("Mes", _mMes));
        filaQue.Children.Add(UiKit.Campo("Año", _mAnio));
        filaQue.Children.Add(UiKit.Campo("Fecha de emisión", _mEmision));
        filaQue.Children.Add(UiKit.Campo("Vence", _mVence));
        que.Children.Add(filaQue);
        pila.Children.Add(UiKit.Tarjeta(que));

        // ---------- 2. A quién ----------
        var quien = new StackPanel();
        quien.Children.Add(UiKit.Subtitulo("2. ¿A quién?"));
        var filaQuien = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        filaQuien.Children.Add(_mTodas);
        filaQuien.Children.Add(_mPorBloque);
        _mBloque.Margin = new Thickness(-14, -6, 22, 8);
        filaQuien.Children.Add(_mBloque);
        filaQuien.Children.Add(_mPorPasillo);
        _mPasillo.Margin = new Thickness(-14, -6, 0, 8);
        filaQuien.Children.Add(_mPasillo);
        quien.Children.Add(filaQuien);
        var tipos = new StackPanel { Orientation = Orientation.Horizontal };
        tipos.Children.Add(_mComedores);
        tipos.Children.Add(_mAmbulantes);
        quien.Children.Add(tipos);
        pila.Children.Add(UiKit.Tarjeta(quien));

        // ---------- 3. Cuánto ----------
        var cuanto = new StackPanel();
        cuanto.Children.Add(UiKit.Subtitulo("3. ¿Cuánto?"));
        cuanto.Children.Add(new Border { Height = 6 });
        cuanto.Children.Add(_mFijo);
        _mPanelFijo.Children.Add(UiKit.Campo("Monto", _mMonto));
        _mPanelFijo.Children.Add(UiKit.Campo("Moneda", _mMoneda));
        cuanto.Children.Add(_mPanelFijo);
        cuanto.Children.Add(_mTarifa);
        _mAyudaTarifa.Text = "Cada caseta paga lo que tiene en la pestaña «Tarifas por caseta». " +
                             "Las que no tienen tarifa para este concepto no reciben la deuda (se avisan en la lista).";
        cuanto.Children.Add(_mAyudaTarifa);
        pila.Children.Add(UiKit.Tarjeta(cuanto));

        // ---------- Vista previa ----------
        _mVerPrevia.Margin = new Thickness(0, 0, 0, 12);
        _mVerPrevia.HorizontalAlignment = HorizontalAlignment.Left;
        _mVerPrevia.Click += async (_, _) => await VistaPreviaMasivaAsync();
        pila.Children.Add(_mVerPrevia);

        _mResumen = UiKit.Aviso(out _mResumenTexto, "info");
        _mResumen.Visibility = Visibility.Collapsed;
        pila.Children.Add(_mResumen);

        _mTabla.Columns.Add(UiKit.Columna("Caseta", nameof(FilaPrevia.Codigo), 0.8));
        _mTabla.Columns.Add(UiKit.Columna("Bloque", nameof(FilaPrevia.BloqueTexto), 1));
        _mTabla.Columns.Add(UiKit.Columna("Tipo", nameof(FilaPrevia.Tipo), 0.9));
        _mTabla.Columns.Add(UiKit.Columna("Resultado", nameof(FilaPrevia.Resultado), 2));
        _mTabla.Columns.Add(UiKit.Columna("Monto", nameof(FilaPrevia.Monto), 0.9, "N2", derecha: true));
        _mTabla.Columns.Add(UiKit.Columna("Moneda", nameof(FilaPrevia.Moneda), 0.7));
        _mTabla.Height = 320;
        _mTabla.Visibility = Visibility.Collapsed;
        pila.Children.Add(_mTabla);

        _mGenerar.Margin = new Thickness(0, 12, 0, 0);
        _mGenerar.HorizontalAlignment = HorizontalAlignment.Left;
        _mGenerar.Visibility = Visibility.Collapsed;
        _mGenerar.Click += async (_, _) => await GenerarMasivoAsync();
        pila.Children.Add(_mGenerar);

        // Cualquier cambio invalida la vista previa.
        void Cambio(object? s, EventArgs e) => InvalidarPrevia();
        foreach (var c in new[] { _mConcepto, _mMes, _mAnio, _mBloque, _mPasillo, _mMoneda })
            c.SelectionChanged += Cambio;
        foreach (var r in new[] { _mTodas, _mPorBloque, _mPorPasillo, _mFijo, _mTarifa })
            r.Checked += (s, e) => { InvalidarPrevia(); ActualizarModoMasivo(); };
        _mComedores.Click += Cambio;
        _mAmbulantes.Click += Cambio;
        _mMonto.TextChanged += Cambio;
        _mEmision.SelectedDateChanged += Cambio;
        _mVence.SelectedDateChanged += Cambio;
        _mBloque.DropDownOpened += (_, _) => _mPorBloque.IsChecked = true;
        _mPasillo.DropDownOpened += (_, _) => _mPorPasillo.IsChecked = true;

        ActualizarModoMasivo();
        return pila;
    }

    private void ActualizarModoMasivo()
    {
        _mPanelFijo.IsEnabled = _mFijo.IsChecked == true;
        _mPanelFijo.Opacity = _mFijo.IsChecked == true ? 1 : 0.45;
        _mAyudaTarifa.Opacity = _mTarifa.IsChecked == true ? 1 : 0.6;
    }

    private void InvalidarPrevia()
    {
        _mListasPrevia = null;
        _mGenerar.Visibility = Visibility.Collapsed;
        _mTabla.Visibility = Visibility.Collapsed;
        if (_mResumen is not null)
            _mResumen.Visibility = Visibility.Collapsed;
    }

    /// <summary>Datos del formulario o un mensaje de lo que falta.</summary>
    private (Dictionary<string, string?>? Datos, string? Falta) DatosMasivo()
    {
        if (Valor<Guid?>(_mConcepto) is not { } concepto)
            return (null, "Elige el concepto.");
        if (_mEmision.SelectedDate is null)
            return (null, "Indica la fecha de emisión.");

        var alcance = _mPorBloque.IsChecked == true ? "bloque" : _mPorPasillo.IsChecked == true ? "pasillo" : "todas";
        if (alcance == "bloque" && Valor<string?>(_mBloque) is null)
            return (null, "Elige el bloque.");
        if (alcance == "pasillo" && Valor<Guid?>(_mPasillo) is null)
            return (null, "Elige el pasillo.");

        var fijo = _mFijo.IsChecked == true;
        decimal? monto = null;
        if (fijo)
        {
            monto = ApiJson.LeerMonto(_mMonto.Text);
            if (monto is null)
                return (null, "Escribe el monto (mayor a cero, con hasta dos decimales).");
            if (Valor<Guid?>(_mMoneda) is null)
                return (null, "Elige la moneda.");
        }

        return (new Dictionary<string, string?>
        {
            ["conceptoCobroId"] = concepto.ToString(),
            ["anio"] = Valor<int>(_mAnio).ToString(),
            ["mes"] = Valor<int>(_mMes).ToString(),
            ["alcance"] = alcance,
            ["bloque"] = alcance == "bloque" ? Valor<string?>(_mBloque) : null,
            ["pasilloId"] = alcance == "pasillo" ? Valor<Guid?>(_mPasillo)?.ToString() : null,
            ["incluirComedores"] = (_mComedores.IsChecked == true).ToString().ToLowerInvariant(),
            ["incluirAmbulantes"] = (_mAmbulantes.IsChecked == true).ToString().ToLowerInvariant(),
            ["modoMonto"] = fijo ? "fijo" : "tarifa",
            ["monto"] = monto?.ToString(CultureInfo.InvariantCulture),
            ["monedaId"] = fijo ? Valor<Guid?>(_mMoneda)?.ToString() : null,
            ["fechaEmision"] = Fecha(_mEmision.SelectedDate),
            ["fechaVencimiento"] = _mVence.SelectedDate is null ? null : Fecha(_mVence.SelectedDate)
        }, null);
    }

    private async Task VistaPreviaMasivaAsync()
    {
        var (datos, falta) = DatosMasivo();
        if (datos is null)
        {
            Dialogo.Mostrar(Window.GetWindow(this), falta!, "Falta un dato", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _mVerPrevia.IsEnabled = false;

        try
        {
            var query = string.Join("&", datos.Where(d => d.Value is not null)
                .Select(d => $"{d.Key}={Uri.EscapeDataString(d.Value!)}"));

            var r = await ApiJson.GetAsync<PreviaRespuesta>("/api/cobranza/cargo-masivo/vista-previa?" + query)
                    ?? throw new InvalidOperationException("No se recibió la vista previa.");

            _mTabla.ItemsSource = r.Filas;
            _mTabla.Visibility = Visibility.Visible;

            var totales = string.Join(" + ", r.Totales.Select(t => $"{t.Moneda} {t.Total.ToString("N2", Bo)}"));
            var lineas = new List<string>
            {
                r.Listas > 0
                    ? $"Se generarán {r.Listas} deudas por un total de {totales}."
                    : "No se generará ninguna deuda."
            };
            if (r.YaTenian > 0)
                lineas.Add($"{r.YaTenian} ya tenían esta deuda en {Meses[Valor<int>(_mMes) - 1].ToLowerInvariant()} y se omiten.");
            if (r.SinTarifa > 0)
                lineas.Add($"{r.SinTarifa} no tienen tarifa para este concepto y se omiten. Puedes asignarla en «Tarifas por caseta».");

            _mResumenTexto.Text = string.Join("\n", lineas);
            _mResumen!.Visibility = Visibility.Visible;

            _mListasPrevia = r.Listas;
            _mGenerar.Content = r.Listas == 1 ? "Generar 1 deuda" : $"Generar {r.Listas} deudas";
            _mGenerar.Visibility = r.Listas > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            InvalidarPrevia();
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se pudo preparar la lista", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _mVerPrevia.IsEnabled = true;
        }
    }

    private async Task GenerarMasivoAsync()
    {
        var (datos, falta) = DatosMasivo();
        if (datos is null || _mListasPrevia is not { } listas)
        {
            Dialogo.Mostrar(Window.GetWindow(this), falta ?? "Primero revisa la lista.", "Falta un dato",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var concepto = (_mConcepto.SelectedItem as UiKit.Item<Guid?>)?.Texto;
        var periodo = $"{Meses[Valor<int>(_mMes) - 1]} {Valor<int>(_mAnio)}";

        var confirmar = Dialogo.Mostrar(Window.GetWindow(this),
            $"Concepto: {concepto}\nPeríodo: {periodo}\nDeudas nuevas: {listas}\n\n{_mResumenTexto.Text}\n\n¿Generar las deudas?",
            "Confirmar cargo mensual", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirmar != MessageBoxResult.Yes)
            return;

        _mGenerar.IsEnabled = false;

        try
        {
            var cuerpo = new Dictionary<string, object?>
            {
                ["conceptoCobroId"] = Guid.Parse(datos["conceptoCobroId"]!),
                ["anio"] = int.Parse(datos["anio"]!),
                ["mes"] = int.Parse(datos["mes"]!),
                ["alcance"] = datos["alcance"],
                ["bloque"] = datos["bloque"],
                ["pasilloId"] = datos["pasilloId"] is { } p ? Guid.Parse(p) : null,
                ["incluirComedores"] = datos["incluirComedores"] == "true",
                ["incluirAmbulantes"] = datos["incluirAmbulantes"] == "true",
                ["modoMonto"] = datos["modoMonto"],
                ["monto"] = datos["monto"] is { } m ? decimal.Parse(m, CultureInfo.InvariantCulture) : null,
                ["monedaId"] = datos["monedaId"] is { } mo ? Guid.Parse(mo) : null,
                ["fechaEmision"] = datos["fechaEmision"],
                ["fechaVencimiento"] = datos["fechaVencimiento"],
                ["listasEsperadas"] = listas
            };

            var r = await ApiJson.PostAsync<ResultadoGeneracion>("/api/cobranza/cargo-masivo", cuerpo);

            InvalidarPrevia();

            Dialogo.Mostrar(Window.GetWindow(this),
                $"Concepto: {concepto}\nPeríodo: {periodo}\nDeudas generadas: {r?.Generadas ?? 0}\n\n" +
                "Ya aparecen en «Deudas registradas» y se pueden cobrar en Ingresos.",
                "Cargo mensual generado", MessageBoxButton.OK, MessageBoxImage.Information);

            _fEstado.SelectedIndex = 0;
            await BuscarDeudasAsync();
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se generaron las deudas", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _mGenerar.IsEnabled = true;
        }
    }

    private async Task NuevoConceptoAsync(ComboBox destino)
    {
        var ventana = new NuevoConceptoWindow { Owner = Window.GetWindow(this) };
        if (ventana.ShowDialog() != true || ventana.ConceptoCreado is null)
            return;

        await CargarCatalogosAsync();

        if (destino.ItemsSource is IEnumerable<UiKit.Item<Guid?>> items)
            destino.SelectedItem = items.FirstOrDefault(i => i.Valor == ventana.ConceptoCreado.Id);
    }

    // =====================================================================
    // 3. CARGO A UNA CASETA
    // =====================================================================

    private readonly TextBox _iCaseta = UiKit.Texto(110, 20);
    private readonly TextBlock _iCasetaInfo = new() { Foreground = UiKit.Suave, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _iConcepto = UiKit.Combo(230);
    private readonly TextBox _iMonto = UiKit.Texto(120, 15);
    private readonly ComboBox _iMoneda = UiKit.Combo(100);
    private readonly CheckBox _iEventual = new() { Content = "Cargo eventual (no corresponde a un mes; por ejemplo una multa)", Margin = new Thickness(0, 4, 0, 10) };
    private readonly ComboBox _iMes = ComboMeses(null);
    private readonly ComboBox _iAnio = ComboAnios();
    private readonly DatePicker _iEmision = UiKit.Fecha(DateTime.Today);
    private readonly DatePicker _iVence = UiKit.Fecha(DateTime.Today.AddDays(30));

    private UIElement ConstruirCargoIndividual()
    {
        var pila = new StackPanel();
        pila.Children.Add(UiKit.Ayuda(
            "Registra una deuda para una sola caseta: un excedente de luz, una multa, un alquiler que faltó. " +
            "Si la caseta ya tiene el mismo concepto en ese mes, Mercadex no la duplica."));

        var tarjeta = new StackPanel();

        var fila1 = new WrapPanel();
        fila1.Children.Add(UiKit.Campo("Caseta", _iCaseta));
        var nuevoConcepto = UiKit.Boton("+ Nuevo concepto");
        nuevoConcepto.Margin = new Thickness(0, 0, 14, 10);
        nuevoConcepto.Click += async (_, _) => await NuevoConceptoAsync(_iConcepto);
        fila1.Children.Add(UiKit.Campo("Concepto", _iConcepto, 8));
        fila1.Children.Add(nuevoConcepto);
        fila1.Children.Add(UiKit.Campo("Monto", _iMonto));
        fila1.Children.Add(UiKit.Campo("Moneda", _iMoneda));
        tarjeta.Children.Add(fila1);
        tarjeta.Children.Add(_iCasetaInfo);

        tarjeta.Children.Add(_iEventual);

        var fila2 = new WrapPanel();
        var mes = UiKit.Campo("Mes", _iMes);
        var anio = UiKit.Campo("Año", _iAnio);
        fila2.Children.Add(mes);
        fila2.Children.Add(anio);
        fila2.Children.Add(UiKit.Campo("Fecha de emisión", _iEmision));
        fila2.Children.Add(UiKit.Campo("Vence", _iVence));
        tarjeta.Children.Add(fila2);

        _iEventual.Click += (_, _) =>
        {
            mes.IsEnabled = anio.IsEnabled = _iEventual.IsChecked != true;
            mes.Opacity = anio.Opacity = _iEventual.IsChecked == true ? 0.45 : 1;
        };

        _iCaseta.TextChanged += (_, _) => MostrarCasetaIndividual();

        var registrar = UiKit.Boton("Registrar deuda", principal: true);
        registrar.HorizontalAlignment = HorizontalAlignment.Left;
        registrar.Margin = new Thickness(0, 6, 0, 0);
        registrar.Click += async (_, _) => await RegistrarIndividualAsync(registrar);
        tarjeta.Children.Add(registrar);

        pila.Children.Add(UiKit.Tarjeta(tarjeta));
        MostrarCasetaIndividual();
        return pila;
    }

    private UnidadDto? BuscarUnidad(string codigo) =>
        _unidades.FirstOrDefault(u => string.Equals(u.Codigo.Trim(), codigo.Trim(), StringComparison.OrdinalIgnoreCase));

    private void MostrarCasetaIndividual()
    {
        var codigo = _iCaseta.Text.Trim();

        if (codigo.Length == 0)
        {
            _iCasetaInfo.Text = "Escribe el número de caseta.";
            _iCasetaInfo.Foreground = UiKit.Suave;
            return;
        }

        var unidad = BuscarUnidad(codigo);

        if (unidad is null)
        {
            _iCasetaInfo.Text = $"No existe la caseta «{codigo}».";
            _iCasetaInfo.Foreground = ColoresPanel.Hex("#A12A2A");
        }
        else
        {
            _iCasetaInfo.Text = $"Caseta {unidad.Codigo} · {unidad.TipoTexto} · Bloque {unidad.Bloque ?? "sin bloque"}" +
                                (unidad.Activa ? "" : " · INACTIVA");
            _iCasetaInfo.Foreground = unidad.Activa ? ColoresPanel.Hex("#0B5A0B") : ColoresPanel.Hex("#A12A2A");
        }
    }

    private async Task RegistrarIndividualAsync(Button boton)
    {
        var unidad = BuscarUnidad(_iCaseta.Text);
        var monto = ApiJson.LeerMonto(_iMonto.Text);

        string? falta =
            unidad is null ? "Escribe un número de caseta que exista." :
            Valor<Guid?>(_iConcepto) is null ? "Elige el concepto." :
            monto is null ? "Escribe el monto (mayor a cero, con hasta dos decimales)." :
            Valor<Guid?>(_iMoneda) is null ? "Elige la moneda." :
            _iEmision.SelectedDate is null ? "Indica la fecha de emisión." :
            null;

        if (falta is not null)
        {
            Dialogo.Mostrar(Window.GetWindow(this), falta, "Falta un dato", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var eventual = _iEventual.IsChecked == true;
        boton.IsEnabled = false;

        try
        {
            var r = await ApiJson.PostAsync<Mensaje>("/api/cobranza/cargo", new
            {
                unidadComercialId = unidad!.Id,
                conceptoCobroId = Valor<Guid?>(_iConcepto),
                monedaId = Valor<Guid?>(_iMoneda),
                monto,
                fechaEmision = Fecha(_iEmision.SelectedDate),
                fechaVencimiento = _iVence.SelectedDate is null ? null : Fecha(_iVence.SelectedDate),
                anio = eventual ? (int?)null : Valor<int>(_iAnio),
                mes = eventual ? (int?)null : Valor<int>(_iMes)
            });

            Dialogo.Mostrar(Window.GetWindow(this), r?.Texto ?? "Deuda registrada.", "Deuda registrada",
                MessageBoxButton.OK, MessageBoxImage.Information);

            _iMonto.Clear();
            _fCaseta.Text = unidad.Codigo;
            _fEstado.SelectedIndex = 0;
            await BuscarDeudasAsync();
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se registró la deuda", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    // =====================================================================
    // 4. TARIFAS POR CASETA
    // =====================================================================

    private readonly ComboBox _tConcepto = UiKit.Combo(230);
    private readonly TextBlock _tResumen = new() { Foreground = UiKit.Tinta, Margin = new Thickness(0, 0, 0, 8) };
    private readonly DataGrid _tTabla = UiKit.Tabla();
    private readonly TextBox _tMonto = UiKit.Texto(120, 15);
    private readonly ComboBox _tMoneda = UiKit.Combo(100);
    private readonly RadioButton _tSeleccionada = UiKit.Opcion("Solo la caseta elegida en la lista", "alcanceTarifa", marcado: true);
    private readonly RadioButton _tTodas = UiKit.Opcion("Todas las casetas", "alcanceTarifa");
    private readonly RadioButton _tPorBloque = UiKit.Opcion("Un bloque:", "alcanceTarifa");
    private readonly RadioButton _tPorPasillo = UiKit.Opcion("Un pasillo:", "alcanceTarifa");
    private readonly ComboBox _tBloque = UiKit.Combo(170);
    private readonly ComboBox _tPasillo = UiKit.Combo(220);
    private readonly CheckBox _tSoloSin = new() { Content = "No cambiar las casetas que ya tienen tarifa", IsChecked = true, Margin = new Thickness(0, 4, 0, 10) };

    private UIElement ConstruirTarifas()
    {
        var pila = new StackPanel();
        pila.Children.Add(UiKit.Ayuda(
            "Cuánto paga cada caseta por un concepto (por ejemplo, el alquiler de cada una). " +
            "Se usa en «Cargo mensual» con la opción «La tarifa de cada caseta». Cambiar una tarifa no cambia deudas ya generadas."));

        var cabecera = new WrapPanel();
        cabecera.Children.Add(UiKit.Campo("Concepto", _tConcepto));
        pila.Children.Add(cabecera);
        _tConcepto.SelectionChanged += async (_, _) => await CargarTarifasAsync();

        pila.Children.Add(_tResumen);

        _tTabla.Columns.Add(UiKit.Columna("Caseta", nameof(TarifaFila.Caseta), 0.8));
        _tTabla.Columns.Add(UiKit.Columna("Bloque", nameof(TarifaFila.BloqueTexto), 1));
        _tTabla.Columns.Add(UiKit.Columna("Tarifa", nameof(TarifaFila.MontoTexto), 1, derecha: true));
        _tTabla.Columns.Add(UiKit.Columna("Moneda", nameof(TarifaFila.MonedaTexto), 0.7));
        _tTabla.Height = 300;
        pila.Children.Add(_tTabla);

        var asignar = new StackPanel();
        asignar.Children.Add(UiKit.Subtitulo("Asignar tarifa"));

        var monto = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        monto.Children.Add(UiKit.Campo("Monto", _tMonto));
        monto.Children.Add(UiKit.Campo("Moneda", _tMoneda));
        asignar.Children.Add(monto);

        var alcance = new WrapPanel();
        alcance.Children.Add(_tSeleccionada);
        alcance.Children.Add(_tTodas);
        alcance.Children.Add(_tPorBloque);
        _tBloque.Margin = new Thickness(-14, -6, 22, 8);
        alcance.Children.Add(_tBloque);
        alcance.Children.Add(_tPorPasillo);
        _tPasillo.Margin = new Thickness(-14, -6, 0, 8);
        alcance.Children.Add(_tPasillo);
        asignar.Children.Add(alcance);
        asignar.Children.Add(_tSoloSin);

        _tBloque.DropDownOpened += (_, _) => _tPorBloque.IsChecked = true;
        _tPasillo.DropDownOpened += (_, _) => _tPorPasillo.IsChecked = true;
        _tSeleccionada.Checked += (_, _) => _tSoloSin.IsEnabled = false;
        foreach (var r in new[] { _tTodas, _tPorBloque, _tPorPasillo })
            r.Checked += (_, _) => _tSoloSin.IsEnabled = true;
        _tSoloSin.IsEnabled = false;

        _tTabla.SelectionChanged += (_, _) =>
        {
            if (_tTabla.SelectedItem is TarifaFila f && f.Monto is { } m)
                _tMonto.Text = m.ToString("0.##", CultureInfo.InvariantCulture);
        };

        var guardar = UiKit.Boton("Asignar tarifa", principal: true);
        guardar.HorizontalAlignment = HorizontalAlignment.Left;
        guardar.Click += async (_, _) => await AsignarTarifaAsync(guardar);
        asignar.Children.Add(guardar);

        var tarjeta = UiKit.Tarjeta(asignar);
        tarjeta.Margin = new Thickness(0, 12, 0, 0);
        pila.Children.Add(tarjeta);

        return pila;
    }

    private async Task CargarTarifasAsync()
    {
        if (Valor<Guid?>(_tConcepto) is not { } concepto)
        {
            _tResumen.Text = "Elige un concepto para ver sus tarifas.";
            _tTabla.ItemsSource = null;
            return;
        }

        try
        {
            _tResumen.Text = "Cargando tarifas…";
            var r = await ApiJson.GetAsync<TarifasRespuesta>($"/api/tarifas-unidad?conceptoCobroId={concepto}");
            var monedas = _monedas.ToDictionary(m => m.Id, m => m.Codigo);

            var filas = (r?.Casetas ?? new())
                .Select(c => { c.MonedaTexto = c.MonedaId is { } id && monedas.TryGetValue(id, out var cod) ? cod : "—"; return c; })
                .OrderBy(c => c.Caseta.Length).ThenBy(c => c.Caseta)
                .ToList();

            _tTabla.ItemsSource = filas;
            _tResumen.Text = r is null
                ? ""
                : $"Con tarifa: {r.CantidadConTarifa} de {r.CantidadCasetas} casetas." +
                  (r.CantidadConTarifa < r.CantidadCasetas ? " Las que no tienen tarifa no reciben el cargo mensual por tarifa." : "");
        }
        catch (Exception ex)
        {
            _tResumen.Text = "No se pudieron cargar las tarifas: " + ex.Message;
        }
    }

    private async Task AsignarTarifaAsync(Button boton)
    {
        var concepto = Valor<Guid?>(_tConcepto);
        var monto = ApiJson.LeerMonto(_tMonto.Text);
        var moneda = Valor<Guid?>(_tMoneda);

        string? falta =
            concepto is null ? "Elige el concepto." :
            monto is null ? "Escribe el monto (mayor a cero, con hasta dos decimales)." :
            moneda is null ? "Elige la moneda." :
            _tSeleccionada.IsChecked == true && _tTabla.SelectedItem is not TarifaFila ? "Elige una caseta en la lista." :
            _tPorBloque.IsChecked == true && Valor<string?>(_tBloque) is null ? "Elige el bloque." :
            _tPorPasillo.IsChecked == true && Valor<Guid?>(_tPasillo) is null ? "Elige el pasillo." :
            null;

        if (falta is not null)
        {
            Dialogo.Mostrar(Window.GetWindow(this), falta, "Falta un dato", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        boton.IsEnabled = false;

        try
        {
            string mensaje;

            if (_tSeleccionada.IsChecked == true && _tTabla.SelectedItem is TarifaFila fila)
            {
                await ApiJson.PutAsync<object>($"/api/tarifas-unidad/{fila.UnidadComercialId}/{concepto}",
                    new { monedaId = moneda, monto });
                mensaje = $"Tarifa de la caseta {fila.Caseta}: {monto!.Value.ToString("N2", Bo)}.";
            }
            else
            {
                var alcance = _tPorBloque.IsChecked == true ? "bloque" : _tPorPasillo.IsChecked == true ? "pasillo" : "todas";
                var destino = alcance switch
                {
                    "bloque" => $"todas las casetas del bloque {Valor<string?>(_tBloque)}",
                    "pasillo" => $"todas las casetas del pasillo {(_tPasillo.SelectedItem as UiKit.Item<Guid?>)?.Texto}",
                    _ => "todas las casetas"
                };

                var confirmar = Dialogo.Mostrar(Window.GetWindow(this),
                    $"Concepto: {(_tConcepto.SelectedItem as UiKit.Item<Guid?>)?.Texto}\n" +
                    $"Tarifa: {(_tMoneda.SelectedItem as UiKit.Item<Guid?>)?.Texto} {monto!.Value.ToString("N2", Bo)}\n" +
                    $"A: {destino}\n\n" +
                    (_tSoloSin.IsChecked == true
                        ? "Las casetas que ya tienen tarifa la conservan."
                        : "Las casetas que ya tienen tarifa la cambian por esta.") +
                    "\n\n¿Asignar la tarifa?",
                    "Asignar tarifa a varias casetas", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirmar != MessageBoxResult.Yes)
                    return;

                var r = await ApiJson.PostAsync<Mensaje>("/api/cobranza/tarifas-masivo", new
                {
                    conceptoCobroId = concepto,
                    monedaId = moneda,
                    monto,
                    alcance,
                    bloque = alcance == "bloque" ? Valor<string?>(_tBloque) : null,
                    pasilloId = alcance == "pasillo" ? Valor<Guid?>(_tPasillo) : null,
                    soloSinTarifa = _tSoloSin.IsChecked == true
                });
                mensaje = r?.Texto ?? "Tarifa asignada.";
            }

            await CargarTarifasAsync();
            Dialogo.Mostrar(Window.GetWindow(this), mensaje, "Tarifa asignada", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Dialogo.Mostrar(Window.GetWindow(this), ex.Message, "No se asignó la tarifa", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            boton.IsEnabled = true;
        }
    }

    // =====================================================================
    // MODELOS
    // =====================================================================

    private sealed class CatalogosDto
    {
        public List<ConceptoIngresoModel> Conceptos { get; set; } = new();
    }

    private sealed class MonedaDto
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
    }

    private sealed class UnidadDto
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public int Tipo { get; set; }
        public Guid? PasilloId { get; set; }
        public string? Bloque { get; set; }
        public bool Activa { get; set; }

        public string TipoTexto => Tipo switch { 2 => "Comedor", 3 => "Ambulante", _ => "Caseta" };
    }

    private sealed class PasilloDto
    {
        public Guid Id { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public bool Activo { get; set; }
    }

    private sealed class DeudasRespuesta
    {
        public int Total { get; set; }
        public int Mostradas { get; set; }
        public List<ResumenDeuda> Resumen { get; set; } = new();
        public List<DeudaFila> Deudas { get; set; } = new();
    }

    private sealed class ResumenDeuda
    {
        public string Estado { get; set; } = "";
        public string Moneda { get; set; } = "";
        public int Cantidad { get; set; }
        public decimal Monto { get; set; }
    }

    public sealed class DeudaFila
    {
        public Guid Id { get; set; }
        public Guid UnidadId { get; set; }
        public string Caseta { get; set; } = "";
        public string? Bloque { get; set; }
        public string Concepto { get; set; } = "";
        public int? Anio { get; set; }
        public int? Mes { get; set; }
        public decimal Monto { get; set; }
        public string Moneda { get; set; } = "";
        public DateOnly Emision { get; set; }
        public DateOnly? Vencimiento { get; set; }
        public string Estado { get; set; } = "";
        public int Modificaciones { get; set; }

        public string BloqueTexto => string.IsNullOrWhiteSpace(Bloque) ? "—" : Bloque!;
        public string Periodo => Anio is { } a && Mes is { } m ? $"{m:00}/{a}" : "Eventual";
        public string EmisionTexto => Emision.ToString("dd/MM/yyyy");
        public string VenceTexto => Vencimiento?.ToString("dd/MM/yyyy") ?? "—";
        public string Nota => Modificaciones > 0 ? "✎ Modificada" : "";
    }

    private sealed class PreviaRespuesta
    {
        public int Listas { get; set; }
        public int YaTenian { get; set; }
        public int SinTarifa { get; set; }
        public List<TotalMoneda> Totales { get; set; } = new();
        public List<FilaPrevia> Filas { get; set; } = new();
    }

    private sealed class TotalMoneda
    {
        public string Moneda { get; set; } = "";
        public int Cantidad { get; set; }
        public decimal Total { get; set; }
    }

    public sealed class FilaPrevia
    {
        public string Codigo { get; set; } = "";
        public string? Bloque { get; set; }
        public string Tipo { get; set; } = "";
        public string Estado { get; set; } = "";
        public decimal? Monto { get; set; }
        public string Moneda { get; set; } = "";

        public string BloqueTexto => string.IsNullOrWhiteSpace(Bloque) ? "—" : Bloque!;

        public string Resultado => Estado switch
        {
            "Lista" => "✓ Se genera",
            "YaTiene" => "Ya tiene esta deuda (se omite)",
            "SinTarifa" => "Sin tarifa (se omite)",
            _ => Estado
        };
    }

    private sealed class ResultadoGeneracion
    {
        public int Generadas { get; set; }
    }

    private sealed class TarifasRespuesta
    {
        public int CantidadCasetas { get; set; }
        public int CantidadConTarifa { get; set; }
        public List<TarifaFila> Casetas { get; set; } = new();
    }

    public sealed class TarifaFila
    {
        public Guid UnidadComercialId { get; set; }
        public string Caseta { get; set; } = "";
        public string? Bloque { get; set; }
        public bool TieneTarifa { get; set; }
        public Guid? MonedaId { get; set; }
        public decimal? Monto { get; set; }
        public string MonedaTexto { get; set; } = "—";

        public string BloqueTexto => string.IsNullOrWhiteSpace(Bloque) ? "—" : Bloque!;
        public string MontoTexto => Monto?.ToString("N2", Bo) ?? "Sin tarifa";
    }

    private sealed class HistorialRespuesta
    {
        public List<CambioMonto> Historial { get; set; } = new();
    }

    private sealed class CambioMonto
    {
        public decimal MontoAnterior { get; set; }
        public decimal MontoNuevo { get; set; }
        public string Motivo { get; set; } = "";
        public string Responsable { get; set; } = "";
        public DateTime FechaUtc { get; set; }
    }

    private sealed class Mensaje
    {
        [System.Text.Json.Serialization.JsonPropertyName("mensaje")]
        public string? Texto { get; set; }
    }
}

/// <summary>Nuevo monto y motivo para una deuda pendiente.</summary>
internal sealed class VentanaModificarMonto : Window
{
    private readonly TextBox _monto;
    private readonly TextBox _motivo;
    private readonly TextBlock _error = new() { Foreground = ColoresPanel.Hex("#A12A2A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

    public decimal MontoNuevo { get; private set; }

    public string Motivo { get; private set; } = "";

    public VentanaModificarMonto(CobranzaView.DeudaFila d)
    {
        Title = "Modificar monto";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var pila = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
        pila.Children.Add(new TextBlock { Text = "Modificar monto", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Tinta });
        pila.Children.Add(UiKit.Ayuda(
            $"Caseta {d.Caseta} · {d.Concepto} {d.Periodo}\nMonto actual: {d.Moneda} {d.Monto:N2}", 14));

        _monto = UiKit.Texto(140, 15);
        pila.Children.Add(UiKit.Campo($"Nuevo monto ({d.Moneda})", _monto));

        _motivo = UiKit.Texto(400, 500);
        _motivo.Height = 70;
        _motivo.TextWrapping = TextWrapping.Wrap;
        _motivo.AcceptsReturn = true;
        _motivo.VerticalContentAlignment = VerticalAlignment.Top;
        pila.Children.Add(UiKit.Campo("Motivo (queda en el historial)", _motivo));

        pila.Children.Add(_error);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelar = UiKit.Boton("Cancelar");
        cancelar.IsCancel = true;
        cancelar.Margin = new Thickness(0, 0, 10, 0);
        var guardar = UiKit.Boton("Guardar", principal: true);
        guardar.IsDefault = true;
        guardar.Click += (_, _) =>
        {
            var monto = ApiJson.LeerMonto(_monto.Text);
            var motivo = _motivo.Text.Trim();

            _error.Text =
                monto is null ? "Escribe el nuevo monto (mayor a cero, con hasta dos decimales)." :
                monto == d.Monto ? "El nuevo monto es igual al actual." :
                motivo.Length < 5 ? "Escribe el motivo (al menos 5 letras)." :
                "";

            if (_error.Text.Length > 0)
                return;

            MontoNuevo = monto!.Value;
            Motivo = motivo;
            DialogResult = true;
        };
        botones.Children.Add(cancelar);
        botones.Children.Add(guardar);
        pila.Children.Add(botones);

        Content = pila;
        Loaded += (_, _) => _monto.Focus();
    }
}
