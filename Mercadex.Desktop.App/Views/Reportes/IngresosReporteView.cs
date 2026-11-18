using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views.Reportes;

public enum TipoReporteIngresos
{
    /// <summary>Manual 3.A y "Diario de Recaudaciones": total del período, día por día y por forma de pago.</summary>
    Diario,
    /// <summary>Manual 3.B "Servicios por Fechas": cantidad de pagos y total por servicio.</summary>
    Servicios,
    /// <summary>"Servicios Acumulados": cada servicio mes por mes con su acumulado.</summary>
    Acumulados,
    /// <summary>Manual 3.C: cada cobro, uno por uno, para auditar.</summary>
    Detalle,
    /// <summary>Manual 7 "Pago por grupo de bloques": los pagos de un servicio, agrupados por bloque.</summary>
    PorBloque
}

/// <summary>Reportes de ingresos (manual, 3. REPORTES DE INGRESOS y 7.). Solo recibos válidos: los anulados no suman.</summary>
public sealed class IngresosReporteView : ReporteTablaView
{
    private readonly TipoReporteIngresos _tipo;
    private readonly DatePicker _desde;
    private readonly DatePicker _hasta;
    private readonly ComboBox _concepto = UiKit.Combo(220);
    private readonly ComboBox _bloque = UiKit.Combo(130);

    // Pagos de la deuda de un mes (por ejemplo, la mensualidad de agosto),
    // sin importar la fecha en que se pagaron.
    private readonly CheckBox _porPeriodo = new() { Content = "Solo la deuda del mes:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox _mes = UiKit.Combo(120);
    private readonly ComboBox _anio = UiKit.Combo(90);

    public IngresosReporteView() : this(TipoReporteIngresos.Diario)
    {
    }

    public IngresosReporteView(TipoReporteIngresos tipo)
        : base(Titulo(tipo), Ayuda(tipo))
    {
        _tipo = tipo;

        var hoy = DateTime.Today;
        var inicio = tipo switch
        {
            TipoReporteIngresos.Diario => hoy,
            TipoReporteIngresos.Acumulados => new DateTime(hoy.Year, 1, 1),
            _ => new DateTime(hoy.Year, hoy.Month, 1)
        };
        (_desde, _hasta) = AgregarRango(inicio, hoy);

        Filtros.Children.Add(UiKit.Campo(tipo == TipoReporteIngresos.PorBloque ? "Servicio (obligatorio)" : "Servicio", _concepto));
        Filtros.Children.Add(UiKit.Campo("Bloque", _bloque));

        if (tipo != TipoReporteIngresos.Acumulados)
        {
            _mes.ItemsSource = Enumerable.Range(1, 12)
                .Select(m => new UiKit.Item<int>(m, Bo.TextInfo.ToTitleCase(new DateTime(2000, m, 1).ToString("MMMM", Bo))))
                .ToList();
            _mes.SelectedIndex = hoy.Month - 1;
            _anio.ItemsSource = Enumerable.Range(hoy.Year - 3, 5).Select(a => new UiKit.Item<int>(a, a.ToString(Bo))).ToList();
            _anio.SelectedIndex = 3;
            _mes.Margin = new Thickness(0, 0, 8, 10);
            _anio.Margin = new Thickness(0, 0, 0, 10);
            _mes.IsEnabled = _anio.IsEnabled = false;
            _porPeriodo.ToolTip = "Muestra solo lo pagado de las deudas de ese mes, sin importar la fecha en que se pagaron.";
            _porPeriodo.Click += (_, _) =>
            {
                var activo = _porPeriodo.IsChecked == true;
                _mes.IsEnabled = _anio.IsEnabled = activo;
                _desde.IsEnabled = _hasta.IsEnabled = !activo;
            };

            var periodo = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            periodo.Children.Add(_porPeriodo);
            var selectores = new StackPanel { Orientation = Orientation.Horizontal };
            selectores.Children.Add(_mes);
            selectores.Children.Add(_anio);
            periodo.Children.Add(selectores);
            Filtros.Children.Add(periodo);
        }

        Loaded += async (_, _) => await CargarFiltrosAsync();
    }

    private static string Titulo(TipoReporteIngresos t) => t switch
    {
        TipoReporteIngresos.Diario => "Recaudación de servicios acumulados",
        TipoReporteIngresos.Servicios => "Servicios por fechas",
        TipoReporteIngresos.Acumulados => "Servicios mes por mes",
        TipoReporteIngresos.Detalle => "Detalle de cobros diarios",
        _ => "Pagos de un servicio"
    };

    private static string Ayuda(TipoReporteIngresos t) => t switch
    {
        TipoReporteIngresos.Diario => "Lo cobrado en el período, servicio por servicio: código, cantidad de pagos, efectivo, QR y total. Los recibos anulados no se suman.",
        TipoReporteIngresos.Servicios => "Día por día: código, servicio y monto de lo cobrado, con el total de cada día.",
        TipoReporteIngresos.Acumulados => "Lo recaudado por cada servicio, mes por mes, con el total acumulado del período.",
        TipoReporteIngresos.Detalle => "Cada cobro del período, concepto por concepto: fecha, recibo, caseta, quién pagó y detalle. Primero los recibos anulados (no se suman).",
        _ => "Elige un servicio (por ejemplo, Alquiler de caseta) y verás solo sus pagos, caseta por caseta."
    };

    private async Task CargarFiltrosAsync()
    {
        if (_concepto.ItemsSource is not null)
            return;

        try
        {
            var c = await CatalogosAsync();
            var conceptos = new List<UiKit.Item<Guid?>>();
            if (_tipo != TipoReporteIngresos.PorBloque)
                conceptos.Add(new(null, "Todos"));
            conceptos.AddRange(c.Conceptos);
            _concepto.ItemsSource = conceptos;
            _concepto.SelectedIndex = _tipo == TipoReporteIngresos.PorBloque ? -1 : 0;

            _bloque.ItemsSource = new[] { new UiKit.Item<string?>(null, "Todos") }
                .Concat(c.Bloques.Select(b => new UiKit.Item<string?>(b, b))).ToList();
            _bloque.SelectedIndex = 0;

            if (_tipo == TipoReporteIngresos.Diario)
                await GenerarAsync();
        }
        catch (Exception ex)
        {
            Avisar("No se pudieron cargar los servicios y bloques: " + ex.Message);
        }
    }

    // =====================================================================

    protected override async Task<Reporte?> ConstruirAsync()
    {
        var porPeriodo = _porPeriodo.IsChecked == true &&
                         _mes.SelectedItem is UiKit.Item<int> mes && _anio.SelectedItem is UiKit.Item<int>;
        var periodo = porPeriodo ? new DateTime(((UiKit.Item<int>)_anio.SelectedItem!).Valor, ((UiKit.Item<int>)_mes.SelectedItem!).Valor, 1) : default;

        (DateTime Desde, DateTime Hasta) rango;
        if (porPeriodo)
        {
            rango = (periodo, periodo.AddMonths(1).AddDays(-1));
        }
        else if (LeerRango(_desde, _hasta) is { } leido)
        {
            rango = leido;
        }
        else
        {
            return null;
        }

        var concepto = _concepto.SelectedItem as UiKit.Item<Guid?>;
        var bloque = (_bloque.SelectedItem as UiKit.Item<string?>)?.Valor;

        if (_tipo == TipoReporteIngresos.PorBloque && concepto?.Valor is null)
        {
            Avisar("Elige el servicio que quieres revisar.");
            return null;
        }

        if (_tipo == TipoReporteIngresos.Acumulados && Meses(rango.Desde, rango.Hasta).Count > 12)
        {
            Avisar("Para el acumulado elige como máximo 12 meses.");
            return null;
        }

        var ruta = porPeriodo
            ? $"/api/reportes-caja?periodoAnio={periodo.Year}&periodoMes={periodo.Month}"
            : $"/api/reportes-caja?desde={rango.Desde:yyyy-MM-dd}&hasta={rango.Hasta:yyyy-MM-dd}";
        if (concepto?.Valor is { } cid)
            ruta += $"&conceptoId={cid}";
        if (bloque is not null)
            ruta += "&bloque=" + Uri.EscapeDataString(bloque);

        var datos = await ApiJson.GetAsync<RespuestaCaja>(ruta) ?? new RespuestaCaja();
        var cat = await CatalogosAsync();

        string Moneda(Guid id) => cat.Monedas.TryGetValue(id, out var m) ? m : "?";

        var filtros = (porPeriodo
                          ? $"Deuda de {periodo.ToString("MMMM yyyy", Bo)} (pagada en cualquier fecha)"
                          : Rango(rango.Desde, rango.Hasta)) +
                      $"   ·   Servicio: {concepto?.Texto ?? "Todos"}" +
                      $"   ·   Bloque: {bloque ?? "Todos"}";

        var resumen = datos.Resumen
            .Select(r => $"Total recaudado: {Moneda(r.MonedaId)} {Monto(r.Total)}  ({r.CantidadRecibos} {(r.CantidadRecibos == 1 ? "recibo" : "recibos")})")
            .ToList();
        if (resumen.Count == 0)
            resumen.Add("Total recaudado: 0,00 (no hubo cobros)");
        if (datos.CantidadAnulaciones > 0)
            resumen.Add($"Recibos anulados en el período: {datos.CantidadAnulaciones} (no se suman)");

        var nombre = porPeriodo
            ? $"{Titulo(_tipo)}_deuda_{periodo:yyyyMM}"
            : $"{Titulo(_tipo)}_{rango.Desde:yyyyMMdd}_{rango.Hasta:yyyyMMdd}";

        return _tipo switch
        {
            TipoReporteIngresos.Diario => Diario(datos, Moneda, filtros, resumen, nombre),
            TipoReporteIngresos.Servicios => Servicios(datos, Moneda, filtros, resumen, nombre),
            TipoReporteIngresos.Acumulados => Acumulados(datos, Moneda, filtros, resumen, nombre, rango.Desde, rango.Hasta),
            TipoReporteIngresos.Detalle => Detalle(datos, Moneda, cat, filtros, resumen, nombre),
            _ => PorBloque(datos, Moneda, cat, filtros, resumen, nombre, concepto!.Texto)
        };
    }

    // ---------- A. Recaudación de servicios acumulados (antes «Diario de recaudaciones») ----------

    private sealed record LineaServicio(Guid MonedaId, string FormaPago, int? Numero, string Codigo, string Concepto, decimal Monto);

    private static bool EsEfectivo(string forma) => forma.Equals("efectivo", StringComparison.OrdinalIgnoreCase);
    private static bool EsQr(string forma) => forma.Equals("qr", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Como la «Recaudación de servicios acumulados» del sistema anterior: por
    /// cada servicio, su código, cuántos pagos, cuánto en efectivo, cuánto por
    /// QR y el total. Vertical.
    /// </summary>
    private static Reporte Diario(RespuestaCaja d, Func<Guid, string> moneda, string filtros, List<string> resumen, string nombre)
    {
        var lineas = d.Cobros
            .SelectMany(c => c.Conceptos.Select(x =>
                new LineaServicio(c.MonedaId, c.FormaPago, x.Numero, CodigoServicio(x.Numero, x.Codigo), x.Concepto, x.Monto)))
            .ToList();

        var monedas = lineas.Select(l => l.MonedaId).Distinct().ToList();
        var hayOtras = lineas.Any(l => !EsEfectivo(l.FormaPago) && !EsQr(l.FormaPago));

        string[] Celdas(string codigo, string servicio, IEnumerable<LineaServicio> grupo)
        {
            var lista = grupo.ToList();
            var celdas = new List<string>
            {
                codigo, servicio, lista.Count.ToString(Bo),
                Monto(lista.Where(l => EsEfectivo(l.FormaPago)).Sum(l => l.Monto)),
                Monto(lista.Where(l => EsQr(l.FormaPago)).Sum(l => l.Monto))
            };
            if (hayOtras)
                celdas.Add(Monto(lista.Where(l => !EsEfectivo(l.FormaPago) && !EsQr(l.FormaPago)).Sum(l => l.Monto)));
            celdas.Add(Monto(lista.Sum(l => l.Monto)));
            return celdas.ToArray();
        }

        var filas = new List<FilaReporte>();
        foreach (var porMoneda in lineas.GroupBy(l => l.MonedaId))
        {
            if (monedas.Count > 1)
                filas.Add(new(new[] { "", $"Moneda: {moneda(porMoneda.Key)}", "", "", "", "" }.Concat(hayOtras ? new[] { "" } : Array.Empty<string>()).ToArray(), Grupo: true));

            foreach (var servicio in porMoneda
                         .GroupBy(l => (l.Codigo, l.Concepto, l.Numero))
                         .OrderBy(g => g.Key.Numero ?? int.MaxValue)
                         .ThenBy(g => g.Key.Concepto, StringComparer.Create(Bo, true)))
                filas.Add(new(Celdas(servicio.Key.Codigo, servicio.Key.Concepto.ToUpper(Bo), servicio)));

            filas.Add(new(Celdas("", "TOTAL GENERAL", porMoneda), Total: true));
        }

        var columnas = new List<ColumnaReporte>
        {
            new("Código", 0.75),
            new("Servicio", 2.8),
            new("Cant.", 0.6, Derecha: true),
            new("Efectivo", 1.05, Derecha: true, Numero: true),
            new("QR", 1.05, Derecha: true, Numero: true)
        };
        if (hayOtras)
            columnas.Add(new("Otros", 1, Derecha: true, Numero: true));
        columnas.Add(new("Total", 1.1, Derecha: true, Numero: true));

        var soloBs = monedas.Count <= 1 && monedas.All(m => moneda(m) == "BOB");

        return new Reporte
        {
            Titulo = soloBs ? "Recaudación de servicios acumulados (expresado en Bs)" : "Recaudación de servicios acumulados",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            PieSoloPagina = true,
            Columnas = columnas,
            Filas = filas
        };
    }

    // ---------- B. Servicios por fechas: día por día ----------

    /// <summary>
    /// Como el «Informe de servicios por rango de fecha» anterior: cada día con
    /// el código, el servicio y el monto, y el total del día. Vertical.
    /// </summary>
    private static Reporte Servicios(RespuestaCaja d, Func<Guid, string> moneda, string filtros, List<string> resumen, string nombre)
    {
        var lineas = d.Cobros
            .SelectMany(c => c.Conceptos.Select(x => new { Dia = c.FechaLocal.Date, c.MonedaId, Codigo = CodigoServicio(x.Numero, x.Codigo), x.Concepto, x.Monto }))
            .ToList();
        var variasMonedas = lineas.Select(l => l.MonedaId).Distinct().Count() > 1;
        string Importe(Guid monedaId, decimal monto) => variasMonedas ? $"{moneda(monedaId)} {Monto(monto)}" : Monto(monto);

        var filas = new List<FilaReporte>();
        foreach (var dia in lineas.GroupBy(l => l.Dia).OrderBy(g => g.Key))
        {
            filas.Add(new(new[] { "FECHA:", dia.Key.ToString("dddd, d 'de' MMMM 'de' yyyy", Bo), "" }, Grupo: true));
            foreach (var servicio in dia.GroupBy(l => (l.Codigo, l.Concepto, l.MonedaId)).OrderBy(g => g.Key.Codigo).ThenBy(g => g.Key.Concepto))
                filas.Add(new(new[] { servicio.Key.Codigo, servicio.Key.Concepto.ToUpper(Bo), Importe(servicio.Key.MonedaId, servicio.Sum(l => l.Monto)) }));
            foreach (var m in dia.GroupBy(l => l.MonedaId))
                filas.Add(new(new[] { "", $"TOTAL {Fecha(dia.Key)}", Importe(m.Key, m.Sum(l => l.Monto)) }, Total: true));
        }

        foreach (var m in lineas.GroupBy(l => l.MonedaId))
            filas.Add(new(new[] { "", "TOTAL DEL PERÍODO", Importe(m.Key, m.Sum(l => l.Monto)) }, Total: true));

        return new Reporte
        {
            Titulo = "Servicios por fechas",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            PieSoloPagina = true,
            Columnas = new()
            {
                new("Código", 0.8),
                new("Servicio", 3.4),
                new(variasMonedas ? "Monto" : "Monto Bs", 1.1, Derecha: true, Numero: !variasMonedas)
            },
            Filas = filas
        };
    }

    /// <summary>El número del servicio con cuatro cifras (0024); sin número, su código.</summary>
    private static string CodigoServicio(int? numero, string? codigo) =>
        numero is { } n ? n.ToString("0000", Bo) : codigo ?? "";

    // ---------- Servicios acumulados (mes por mes) ----------

    private static List<DateTime> Meses(DateTime desde, DateTime hasta)
    {
        var meses = new List<DateTime>();
        for (var m = new DateTime(desde.Year, desde.Month, 1); m <= hasta; m = m.AddMonths(1))
            meses.Add(m);
        return meses;
    }

    private static Reporte Acumulados(RespuestaCaja d, Func<Guid, string> moneda, string filtros, List<string> resumen, string nombre, DateTime desde, DateTime hasta)
    {
        var meses = Meses(desde, hasta);

        var lineas = d.Cobros
            .SelectMany(c => c.Conceptos.Select(x => new
            {
                Mes = new DateTime(c.FechaLocal.Year, c.FechaLocal.Month, 1),
                c.MonedaId,
                x.Concepto,
                x.Monto
            }))
            .ToList();

        var filas = new List<FilaReporte>();
        foreach (var porMoneda in lineas.GroupBy(l => l.MonedaId))
        {
            foreach (var servicio in porMoneda.GroupBy(l => l.Concepto).OrderBy(g => g.Key))
            {
                var celdas = new List<string> { servicio.Key, moneda(porMoneda.Key) };
                celdas.AddRange(meses.Select(m => Monto(servicio.Where(l => l.Mes == m).Sum(l => l.Monto))));
                celdas.Add(Monto(servicio.Sum(l => l.Monto)));
                filas.Add(new(celdas.ToArray()));
            }

            var total = new List<string> { "TOTAL", moneda(porMoneda.Key) };
            total.AddRange(meses.Select(m => Monto(porMoneda.Where(l => l.Mes == m).Sum(l => l.Monto))));
            total.Add(Monto(porMoneda.Sum(l => l.Monto)));
            filas.Add(new(total.ToArray(), Total: true));
        }

        var columnas = new List<ColumnaReporte> { new("Servicio", 2.2), new("Moneda", 0.7) };
        columnas.AddRange(meses.Select(m => new ColumnaReporte(m.ToString("MMM yyyy", Bo), 1, Derecha: true, Numero: true)));
        columnas.Add(new("Acumulado", 1.2, Derecha: true, Numero: true));

        return new Reporte
        {
            Titulo = "Servicios mes por mes",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            Horizontal = meses.Count > 3,
            Columnas = columnas,
            Filas = filas
        };
    }

    // ---------- C. Detalle de cobros diarios ----------

    private static readonly string[] MesesCortos = { "ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC" };

    /// <summary>«APORTE MENSUAL JUL/2026», como en el «Parte diario de recaudaciones» anterior.</summary>
    private static string DetalleConcepto(string concepto, int? anio, int? mes) =>
        anio is { } a && mes is >= 1 and <= 12 ? $"{concepto.ToUpper(Bo)} {MesesCortos[mes.Value - 1]}/{a}" : concepto.ToUpper(Bo);

    /// <summary>
    /// Vertical y con lo necesario (pedido de contabilidad): primero los recibos
    /// anulados del período (no se suman), después los vigentes, una fila por
    /// concepto. Sin hora, bloque, moneda ni quién cobró.
    /// </summary>
    private static Reporte Detalle(RespuestaCaja d, Func<Guid, string> moneda, Catalogos cat, string filtros, List<string> resumen, string nombre)
    {
        string Caseta(Guid id) => cat.Unidades.TryGetValue(id, out var u) ? u.Codigo : "?";
        string Fecha(DateTimeOffset f) => f.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy", Bo);
        var variasMonedas = d.Resumen.Select(r => r.MonedaId).Distinct().Count() > 1;
        string Importe(Guid monedaId, decimal monto) => variasMonedas ? $"{moneda(monedaId)} {Monto(monto)}" : Monto(monto);

        var filas = new List<FilaReporte>();

        var anulados = d.Anulaciones.Where(a => a.Conceptos.Count > 0).OrderBy(a => a.FechaEmisionUtc).ThenBy(a => a.NumeroRecibo).ToList();
        if (anulados.Count > 0)
        {
            filas.Add(new(new[] { "ESTADO: ANULADO", "", "", "", "", "" }, Grupo: true));
            foreach (var a in anulados)
                foreach (var x in a.Conceptos)
                    filas.Add(new(new[]
                    {
                        a.FechaEmisionUtc is { } f ? Fecha(f) : "", a.NumeroRecibo.ToString(Bo), Caseta(a.UnidadComercialId),
                        a.Pagador ?? "", DetalleConcepto(x.Concepto, x.PeriodoAnio, x.PeriodoMes),
                        Importe(a.MonedaId ?? Guid.Empty, x.Monto)
                    }));
            foreach (var m in anulados.GroupBy(a => a.MonedaId ?? Guid.Empty))
                filas.Add(new(new[] { "", "", "", "", "TOTAL ANULADO (no se suma)", Importe(m.Key, m.Sum(a => a.MontoExcluido)) }, Total: true));
        }

        if (d.Cobros.Count > 0)
        {
            filas.Add(new(new[] { "ESTADO: VIGENTE", "", "", "", "", "" }, Grupo: true));
            foreach (var c in d.Cobros)
                foreach (var x in c.Conceptos)
                    filas.Add(new(new[]
                    {
                        Fecha(c.FechaEmisionUtc), c.NumeroRecibo.ToString(Bo), Caseta(c.UnidadComercialId),
                        c.Pagador, DetalleConcepto(x.Concepto, x.PeriodoAnio, x.PeriodoMes), Importe(c.MonedaId, x.Monto)
                    }));
            foreach (var r in d.Resumen)
                filas.Add(new(new[] { "", "", "", "", variasMonedas ? $"TOTAL {moneda(r.MonedaId)}" : "TOTAL Bs", Importe(r.MonedaId, r.Total) }, Total: true));
        }

        // Total por forma de pago (efectivo, QR…), debajo del total recaudado.
        var formas = d.FormasPago
            .GroupBy(f => f.MonedaId)
            .Select(g => string.Join("   ·   ", g.OrderBy(f => f.FormaPago).Select(f => $"{FormaPago(f.FormaPago)}: {moneda(g.Key)} {Monto(f.Total)}")))
            .ToList();
        var lineas = new List<string>(resumen);
        lineas.InsertRange(Math.Min(d.Resumen.Count, lineas.Count), formas);

        return new Reporte
        {
            Titulo = "Detalle de cobros diarios",
            Filtros = filtros,
            Resumen = lineas,
            NombreArchivo = nombre,
            PieSoloPagina = true,
            Columnas = new()
            {
                new("Fecha", 0.95), new("Recibo", 0.7, Derecha: true), new("Caseta", 0.65),
                new("Pagado por", 2.3), new("Detalle", 2.7), new(variasMonedas ? "Monto" : "Monto Bs", 0.95, Derecha: true, Numero: !variasMonedas)
            },
            Filas = filas
        };
    }

    // ---------- 7. Pagos de un servicio (caseta por caseta) ----------

    private static Reporte PorBloque(RespuestaCaja d, Func<Guid, string> moneda, Catalogos cat, string filtros, List<string> resumen, string nombre, string servicio)
    {
        var lineas = d.Cobros
            .Select(c => new { c, Caseta = cat.Unidades.TryGetValue(c.UnidadComercialId, out var u) ? u.Codigo : "?" })
            .ToList();

        // Una lista continua por número de caseta, sin cortes por bloque.
        var filas = lineas
            .OrderBy(l => int.TryParse(l.Caseta, out _) ? 0 : 1)
            .ThenBy(l => int.TryParse(l.Caseta, out var n) ? n : 0)
            .ThenBy(l => l.Caseta, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.c.FechaEmisionUtc)
            .Select(l => new FilaReporte(new[] { l.c.FechaBolivia, l.c.NumeroRecibo.ToString(Bo), l.Caseta, l.c.Pagador, moneda(l.c.MonedaId), Monto(l.c.Monto) }))
            .ToList();

        foreach (var r in d.Resumen)
            filas.Add(new(new[] { "TOTAL GENERAL", $"{r.CantidadRecibos} pagos", "", "", moneda(r.MonedaId), Monto(r.Total) }, Total: true));

        return new Reporte
        {
            Titulo = $"Pagos de {servicio}",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            Columnas = new()
            {
                new("Fecha", 1.3), new("Recibo", 0.8, Derecha: true), new("Caseta", 0.7), new("Pagó", 2),
                new("Moneda", 0.7), new("Monto", 1.1, Derecha: true, Numero: true)
            },
            Filas = d.Cobros.Count == 0 ? new() : filas
        };
    }

    private static string FormaPago(string forma) => forma.ToLowerInvariant() switch
    {
        "efectivo" => "Efectivo",
        "qr" => "QR",
        "transferencia" => "Transferencia",
        "deposito" or "depósito" => "Depósito",
        _ => forma
    };

    // =====================================================================
    // RESPUESTA DE /api/reportes-caja
    // =====================================================================

    private sealed class RespuestaCaja
    {
        public int CantidadAnulaciones { get; set; }
        public List<ResumenMoneda> Resumen { get; set; } = new();
        public List<DiaCaja> Dias { get; set; } = new();
        public List<FormaPagoCaja> FormasPago { get; set; } = new();
        public List<ConceptoCaja> Conceptos { get; set; } = new();
        public List<CobroCaja> Cobros { get; set; } = new();
        public List<AnulacionCaja> Anulaciones { get; set; } = new();
    }

    private sealed class AnulacionCaja
    {
        public long NumeroRecibo { get; set; }
        public Guid UnidadComercialId { get; set; }
        public Guid? MonedaId { get; set; }
        public decimal MontoExcluido { get; set; }
        public string? Pagador { get; set; }
        public DateTimeOffset? FechaEmisionUtc { get; set; }
        public List<DetalleCaja> Conceptos { get; set; } = new();
    }

    private sealed class ResumenMoneda
    {
        public Guid MonedaId { get; set; }
        public int CantidadRecibos { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class DiaCaja
    {
        public string Fecha { get; set; } = "";
        public Guid MonedaId { get; set; }
        public int CantidadRecibos { get; set; }
        public decimal Total { get; set; }
        public decimal Acumulado { get; set; }
    }

    private sealed class FormaPagoCaja
    {
        public Guid MonedaId { get; set; }
        public string FormaPago { get; set; } = "";
        public int Cantidad { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class ConceptoCaja
    {
        public Guid MonedaId { get; set; }
        public string? Codigo { get; set; }
        public int? Numero { get; set; }
        public string Concepto { get; set; } = "";
        public int Cantidad { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class CobroCaja
    {
        public long NumeroRecibo { get; set; }
        public Guid UnidadComercialId { get; set; }
        public Guid MonedaId { get; set; }
        public decimal Monto { get; set; }
        public string FormaPago { get; set; } = "";
        public string Pagador { get; set; } = "";
        public string? CobradoPor { get; set; }
        public DateTimeOffset FechaEmisionUtc { get; set; }
        public string FechaBolivia { get; set; } = "";
        public List<DetalleCaja> Conceptos { get; set; } = new();

        public DateTime FechaLocal => FechaEmisionUtc.ToOffset(TimeSpan.FromHours(-4)).DateTime;
    }

    private sealed class DetalleCaja
    {
        public Guid ConceptoCobroId { get; set; }
        public string? Codigo { get; set; }
        public int? Numero { get; set; }
        public string Concepto { get; set; } = "";
        public decimal Monto { get; set; }
        public int? PeriodoAnio { get; set; }
        public int? PeriodoMes { get; set; }
    }
}
