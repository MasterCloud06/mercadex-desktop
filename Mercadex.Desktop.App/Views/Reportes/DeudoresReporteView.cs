using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views.Reportes;

public enum TipoReporteDeudores
{
    /// <summary>Manual 5 "Aviso para corte" / "Lista de Deudores p/ realizar corte": cada caseta una sola vez.</summary>
    Corte,
    /// <summary>"Lista de Deudores p/ Contabilidad": cada deuda con fecha, servicio, monto individual y total.</summary>
    Contabilidad
}

/// <summary>Listas de deudores con filtros de fechas, bloque, moneda y servicios.</summary>
public sealed class DeudoresReporteView : ReporteTablaView
{
    private readonly TipoReporteDeudores _tipo;
    private readonly CheckBox _usarFechas = new() { Content = "Solo deudas emitidas entre:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
    private readonly DatePicker _desde;
    private readonly DatePicker _hasta;
    private readonly ComboBox _bloque = UiKit.Combo(130);
    private readonly ComboBox _moneda = UiKit.Combo(110);
    private readonly WrapPanel _servicios = new() { MaxWidth = 900 };

    public DeudoresReporteView() : this(TipoReporteDeudores.Corte)
    {
    }

    public DeudoresReporteView(TipoReporteDeudores tipo)
        : base(
            tipo == TipoReporteDeudores.Corte ? "Aviso para corte" : "Deudores para contabilidad",
            tipo == TipoReporteDeudores.Corte
                ? "Casetas que deben: cada una aparece una sola vez, aunque deba uno o varios servicios. Sirve para notificar antes de un corte."
                : "Cada deuda pendiente de cada caseta con su fecha, servicio y monto, el total por caseta y el total general.")
    {
        _tipo = tipo;

        var hoy = DateTime.Today;
        var fechas = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        fechas.Children.Add(_usarFechas);
        var rango = new StackPanel { Orientation = Orientation.Horizontal };
        _desde = UiKit.Fecha(new DateTime(hoy.Year, 1, 1));
        _hasta = UiKit.Fecha(hoy);
        _desde.Margin = new Thickness(0, 0, 8, 10);
        _hasta.Margin = new Thickness(0, 0, 0, 10);
        _desde.IsEnabled = _hasta.IsEnabled = false;
        _usarFechas.Checked += (_, _) => _desde.IsEnabled = _hasta.IsEnabled = true;
        _usarFechas.Unchecked += (_, _) => _desde.IsEnabled = _hasta.IsEnabled = false;
        rango.Children.Add(_desde);
        rango.Children.Add(_hasta);
        fechas.Children.Add(rango);
        Filtros.Children.Add(fechas);

        Filtros.Children.Add(UiKit.Campo("Bloque", _bloque));
        Filtros.Children.Add(UiKit.Campo("Moneda", _moneda));

        var servicios = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        var etiqueta = new DockPanel();
        var todos = new Button { Content = "Marcar todos", Style = UiKit.Estilo("Mx.NavLink"), Margin = new Thickness(12, 0, 0, 0) };
        todos.Click += (_, _) => Marcar(true);
        var ninguno = new Button { Content = "Ninguno", Style = UiKit.Estilo("Mx.NavLink"), Margin = new Thickness(12, 0, 0, 0) };
        ninguno.Click += (_, _) => Marcar(false);
        etiqueta.Children.Add(new TextBlock { Text = "Servicios pendientes (si no marcas ninguno, se incluyen todos)", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = UiKit.Etiqueta, VerticalAlignment = VerticalAlignment.Center });
        etiqueta.Children.Add(todos);
        etiqueta.Children.Add(ninguno);
        etiqueta.LastChildFill = false;
        servicios.Children.Add(etiqueta);
        servicios.Children.Add(_servicios);
        Filtros.Children.Add(servicios);

        Loaded += async (_, _) => await CargarFiltrosAsync();
    }

    private void Marcar(bool valor)
    {
        foreach (var c in _servicios.Children.OfType<CheckBox>())
            c.IsChecked = valor;
    }

    private async Task CargarFiltrosAsync()
    {
        if (_bloque.ItemsSource is not null)
            return;

        try
        {
            var c = await CatalogosAsync();

            _bloque.ItemsSource = new[] { new UiKit.Item<string?>(null, "Todos") }
                .Concat(c.Bloques.Select(b => new UiKit.Item<string?>(b, b))).ToList();
            _bloque.SelectedIndex = 0;

            _moneda.ItemsSource = new[] { new UiKit.Item<string?>(null, "Todas") }
                .Concat(c.Monedas.Values.Distinct().OrderBy(m => m).Select(m => new UiKit.Item<string?>(m, m))).ToList();
            _moneda.SelectedIndex = 0;

            _servicios.Children.Clear();
            foreach (var concepto in c.Conceptos)
                _servicios.Children.Add(new CheckBox
                {
                    Content = concepto.Texto,
                    Tag = concepto.Valor,
                    Margin = new Thickness(0, 4, 16, 4),
                    VerticalContentAlignment = VerticalAlignment.Center
                });

            await GenerarAsync();
        }
        catch (Exception ex)
        {
            Avisar("No se pudieron cargar los filtros: " + ex.Message);
        }
    }

    // =====================================================================

    protected override async Task<Reporte?> ConstruirAsync()
    {
        var parametros = new List<string>();
        var textoFiltros = new List<string>();

        if (_usarFechas.IsChecked == true)
        {
            if (LeerRango(_desde, _hasta) is not { } r)
                return null;
            parametros.Add($"desde={r.Desde:yyyy-MM-dd}");
            parametros.Add($"hasta={r.Hasta:yyyy-MM-dd}");
            textoFiltros.Add("Deudas emitidas " + Rango(r.Desde, r.Hasta).ToLowerInvariant());
        }
        else
        {
            textoFiltros.Add($"Todas las deudas pendientes al {Fecha(DateTime.Today)}");
        }

        var bloque = (_bloque.SelectedItem as UiKit.Item<string?>)?.Valor;
        if (bloque is not null)
            parametros.Add("bloque=" + Uri.EscapeDataString(bloque));
        textoFiltros.Add("Bloque: " + (bloque ?? "Todos"));

        var moneda = (_moneda.SelectedItem as UiKit.Item<string?>)?.Valor;
        if (moneda is not null)
            parametros.Add("moneda=" + Uri.EscapeDataString(moneda));
        textoFiltros.Add("Moneda: " + (moneda ?? "Todas"));

        var marcados = _servicios.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).ToList();
        if (marcados.Count > 0)
            parametros.Add("conceptos=" + string.Join(",", marcados.Select(c => c.Tag)));
        textoFiltros.Add("Servicios: " + (marcados.Count == 0 ? "Todos" : string.Join(", ", marcados.Select(c => c.Content))));

        var d = await ApiJson.GetAsync<Respuesta>("/api/reportes/deudores?" + string.Join("&", parametros)) ?? new Respuesta();

        var resumen = new List<string>
        {
            d.Casetas == 0
                ? "Ninguna caseta debe con estos filtros."
                : $"{d.Casetas} {(d.Casetas == 1 ? "caseta debe" : "casetas deben")}   ·   {d.Deudas} {(d.Deudas == 1 ? "deuda pendiente" : "deudas pendientes")}"
        };
        if (d.Totales.Count > 0)
            resumen.Add("Total por cobrar: " + string.Join(" + ", d.Totales.Select(t => $"{t.Moneda} {Monto(t.Monto)}")));

        var filtros = string.Join("   ·   ", textoFiltros);
        var sufijo = DateTime.Today.ToString("yyyyMMdd");

        return _tipo == TipoReporteDeudores.Corte
            ? Corte(d, filtros, resumen, "Aviso_para_corte_" + sufijo)
            : Contabilidad(d, filtros, resumen, "Deudores_contabilidad_" + sufijo);
    }

    private static string Totales(List<MontoMoneda> totales) =>
        string.Join(" + ", totales.Select(t => $"{t.Moneda} {Monto(t.Monto)}"));

    // ---------- Aviso / lista para corte: una fila por caseta ----------

    private static Reporte Corte(Respuesta d, string filtros, List<string> resumen, string nombre)
    {
        var filas = new List<FilaReporte>();
        foreach (var g in d.Filas.GroupBy(f => string.IsNullOrWhiteSpace(f.Bloque) ? "Sin bloque" : f.Bloque!))
        {
            filas.Add(new(new[] { $"Bloque {g.Key} — {g.Count()} {(g.Count() == 1 ? "caseta" : "casetas")}", "", "", "", "", "", "" }, Grupo: true));
            foreach (var f in g)
                filas.Add(new(new[]
                {
                    f.Codigo,
                    f.Titular ?? "Sin titular",
                    f.Telefono ?? "",
                    string.Join(", ", f.Servicios),
                    f.Cantidad.ToString(Bo),
                    Fecha(f.Desde.ToDateTime(TimeOnly.MinValue)),
                    Totales(f.Totales)
                }));
        }

        return new Reporte
        {
            Titulo = "Aviso para corte",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            Horizontal = true,
            Nota = "Listado preventivo para notificar a los socios. No es una orden automática de corte.",
            Columnas = new()
            {
                new("Caseta", 0.7), new("Titular", 2), new("Celular", 1), new("Servicios pendientes", 3),
                new("Deudas", 0.6, Derecha: true), new("Debe desde", 0.9), new("Total", 1.3, Derecha: true)
            },
            Filas = filas
        };
    }

    // ---------- Lista para contabilidad: detalle y totales ----------

    private static Reporte Contabilidad(Respuesta d, string filtros, List<string> resumen, string nombre)
    {
        var filas = new List<FilaReporte>();
        foreach (var f in d.Filas)
        {
            var encabezado = $"Caseta {f.Codigo}" +
                             (string.IsNullOrWhiteSpace(f.Bloque) ? "" : $" · Bloque {f.Bloque}") +
                             $" · {f.Titular ?? "Sin titular"}";
            filas.Add(new(new[] { encabezado, "", "", "", "" }, Grupo: true));

            foreach (var x in f.Deudas)
                filas.Add(new(new[] { "   " + Fecha(x.Fecha.ToDateTime(TimeOnly.MinValue)), x.Concepto, x.Periodo, x.Moneda, Monto(x.Monto) }));

            foreach (var t in f.Totales)
                filas.Add(new(new[] { $"   Total caseta {f.Codigo}", "", "", t.Moneda, Monto(t.Monto) }, Total: true));
        }

        if (d.Filas.Count > 0)
        {
            filas.Add(new(new[] { "LISTADO GENERAL POR SERVICIO", "", "", "", "" }, Grupo: true));
            foreach (var c in d.PorConcepto)
                filas.Add(new(new[] { $"   {c.Casetas} {(c.Casetas == 1 ? "caseta" : "casetas")} · {c.Cantidad} {(c.Cantidad == 1 ? "deuda" : "deudas")}", c.Concepto, "", c.Moneda, Monto(c.Monto) }));
            foreach (var t in d.Totales)
                filas.Add(new(new[] { "TOTAL GENERAL POR COBRAR", "", "", t.Moneda, Monto(t.Monto) }, Total: true));
        }

        return new Reporte
        {
            Titulo = "Deudores para contabilidad",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            Columnas = new()
            {
                new("Caseta / fecha", 2.2), new("Servicio", 2.2), new("Período", 0.8),
                new("Moneda", 0.7), new("Monto", 1.1, Derecha: true, Numero: true)
            },
            Filas = filas
        };
    }

    // =====================================================================

    private sealed class Respuesta
    {
        public int Casetas { get; set; }
        public int Deudas { get; set; }
        public List<MontoMoneda> Totales { get; set; } = new();
        public List<PorConceptoDto> PorConcepto { get; set; } = new();
        public List<FilaDeudor> Filas { get; set; } = new();
    }

    private sealed class MontoMoneda
    {
        public string Moneda { get; set; } = "";
        public decimal Monto { get; set; }
    }

    private sealed class PorConceptoDto
    {
        public string Concepto { get; set; } = "";
        public string Moneda { get; set; } = "";
        public int Casetas { get; set; }
        public int Cantidad { get; set; }
        public decimal Monto { get; set; }
    }

    private sealed class FilaDeudor
    {
        public string Codigo { get; set; } = "";
        public string? Bloque { get; set; }
        public string? Titular { get; set; }
        public string? Telefono { get; set; }
        public List<string> Servicios { get; set; } = new();
        public int Cantidad { get; set; }
        public DateOnly Desde { get; set; }
        public List<MontoMoneda> Totales { get; set; } = new();
        public List<DeudaDto> Deudas { get; set; } = new();
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
