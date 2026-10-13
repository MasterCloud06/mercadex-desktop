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

        Loaded += async (_, _) => await CargarFiltrosAsync();
    }

    private static string Titulo(TipoReporteIngresos t) => t switch
    {
        TipoReporteIngresos.Diario => "Diario de recaudaciones",
        TipoReporteIngresos.Servicios => "Servicios por fechas",
        TipoReporteIngresos.Acumulados => "Servicios acumulados",
        TipoReporteIngresos.Detalle => "Detalle de cobros",
        _ => "Pagos por servicio y bloque"
    };

    private static string Ayuda(TipoReporteIngresos t) => t switch
    {
        TipoReporteIngresos.Diario => "Total recaudado en el período, día por día y por forma de pago. Los recibos anulados no se suman.",
        TipoReporteIngresos.Servicios => "Cuántos pagos hubo de cada servicio y cuánto dinero representa cada uno en el período.",
        TipoReporteIngresos.Acumulados => "Lo recaudado por cada servicio, mes por mes, con el total acumulado del período.",
        TipoReporteIngresos.Detalle => "Cada cobro del período, uno por uno, con su recibo, caseta y servicio. Sirve para auditar.",
        _ => "Elige un servicio (por ejemplo, Alquiler de caseta) y verás solo sus pagos en el período, agrupados por bloque."
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
        if (LeerRango(_desde, _hasta) is not { } rango)
            return null;

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

        var ruta = $"/api/reportes-caja?desde={rango.Desde:yyyy-MM-dd}&hasta={rango.Hasta:yyyy-MM-dd}";
        if (concepto?.Valor is { } cid)
            ruta += $"&conceptoId={cid}";
        if (bloque is not null)
            ruta += "&bloque=" + Uri.EscapeDataString(bloque);

        var datos = await ApiJson.GetAsync<RespuestaCaja>(ruta) ?? new RespuestaCaja();
        var cat = await CatalogosAsync();

        string Moneda(Guid id) => cat.Monedas.TryGetValue(id, out var m) ? m : "?";

        var filtros = Rango(rango.Desde, rango.Hasta) +
                      $"   ·   Servicio: {concepto?.Texto ?? "Todos"}" +
                      $"   ·   Bloque: {bloque ?? "Todos"}";

        var resumen = datos.Resumen
            .Select(r => $"Total recaudado: {Moneda(r.MonedaId)} {Monto(r.Total)}  ({r.CantidadRecibos} {(r.CantidadRecibos == 1 ? "recibo" : "recibos")})")
            .ToList();
        if (resumen.Count == 0)
            resumen.Add("Total recaudado: 0,00 (no hubo cobros)");
        if (datos.CantidadAnulaciones > 0)
            resumen.Add($"Recibos anulados en el período: {datos.CantidadAnulaciones} (no se suman)");

        var nombre = $"{Titulo(_tipo)}_{rango.Desde:yyyyMMdd}_{rango.Hasta:yyyyMMdd}";

        return _tipo switch
        {
            TipoReporteIngresos.Diario => Diario(datos, Moneda, filtros, resumen, nombre),
            TipoReporteIngresos.Servicios => Servicios(datos, Moneda, filtros, resumen, nombre),
            TipoReporteIngresos.Acumulados => Acumulados(datos, Moneda, filtros, resumen, nombre, rango.Desde, rango.Hasta),
            TipoReporteIngresos.Detalle => Detalle(datos, Moneda, cat, filtros, resumen, nombre),
            _ => PorBloque(datos, Moneda, cat, filtros, resumen, nombre, concepto!.Texto)
        };
    }

    // ---------- A. Diario de recaudaciones ----------

    private static Reporte Diario(RespuestaCaja d, Func<Guid, string> moneda, string filtros, List<string> resumen, string nombre)
    {
        var filas = new List<FilaReporte> { new(new[] { "Día por día", "", "", "", "" }, Grupo: true) };

        foreach (var dia in d.Dias)
        {
            var fecha = DateTime.TryParse(dia.Fecha, out var f) ? Fecha(f) : dia.Fecha;
            filas.Add(new(new[] { fecha, moneda(dia.MonedaId), dia.CantidadRecibos.ToString(Bo), Monto(dia.Total), Monto(dia.Acumulado) }));
        }

        if (d.FormasPago.Count > 0)
        {
            filas.Add(new(new[] { "Por forma de pago", "", "", "", "" }, Grupo: true));
            foreach (var fp in d.FormasPago)
                filas.Add(new(new[] { FormaPago(fp.FormaPago), moneda(fp.MonedaId), fp.Cantidad.ToString(Bo), Monto(fp.Total), "" }));
        }

        foreach (var r in d.Resumen)
            filas.Add(new(new[] { "TOTAL DEL PERÍODO", moneda(r.MonedaId), r.CantidadRecibos.ToString(Bo), Monto(r.Total), "" }, Total: true));

        return new Reporte
        {
            Titulo = "Diario de recaudaciones",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            Columnas = new()
            {
                new("Día / forma de pago", 2.2),
                new("Moneda", 0.8),
                new("Recibos", 0.8, Derecha: true),
                new("Total", 1.3, Derecha: true, Numero: true),
                new("Acumulado", 1.3, Derecha: true, Numero: true)
            },
            Filas = d.Dias.Count == 0 ? new() : filas
        };
    }

    // ---------- B. Servicios por fechas ----------

    private static Reporte Servicios(RespuestaCaja d, Func<Guid, string> moneda, string filtros, List<string> resumen, string nombre)
    {
        var filas = new List<FilaReporte>();
        foreach (var porMoneda in d.Conceptos.GroupBy(c => c.MonedaId))
        {
            var total = porMoneda.Sum(c => c.Total);
            foreach (var c in porMoneda.OrderByDescending(c => c.Total))
                filas.Add(new(new[]
                {
                    c.Concepto, moneda(c.MonedaId), c.Cantidad.ToString(Bo), Monto(c.Total),
                    total == 0 ? "" : (c.Total / total).ToString("P1", Bo)
                }));
            filas.Add(new(new[] { "TOTAL", moneda(porMoneda.Key), porMoneda.Sum(c => c.Cantidad).ToString(Bo), Monto(total), "100 %" }, Total: true));
        }

        return new Reporte
        {
            Titulo = "Servicios por fechas",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            Columnas = new()
            {
                new("Servicio", 2.6),
                new("Moneda", 0.8),
                new("Cantidad de pagos", 1.1, Derecha: true),
                new("Recaudado", 1.3, Derecha: true, Numero: true),
                new("% del total", 0.9, Derecha: true)
            },
            Filas = filas
        };
    }

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
            Titulo = "Servicios acumulados",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            Horizontal = meses.Count > 3,
            Columnas = columnas,
            Filas = filas
        };
    }

    // ---------- C. Detalle de cobros ----------

    private static Reporte Detalle(RespuestaCaja d, Func<Guid, string> moneda, Catalogos cat, string filtros, List<string> resumen, string nombre)
    {
        var filas = new List<FilaReporte>();
        foreach (var c in d.Cobros)
        {
            var (caseta, bloque) = cat.Unidades.TryGetValue(c.UnidadComercialId, out var u) ? u : ("?", null);
            foreach (var x in c.Conceptos)
                filas.Add(new(new[]
                {
                    c.FechaBolivia, c.NumeroRecibo.ToString(Bo), caseta, bloque ?? "", x.Concepto,
                    c.Pagador, FormaPago(c.FormaPago), moneda(c.MonedaId), Monto(x.Monto), c.CobradoPor ?? ""
                }));
        }

        foreach (var r in d.Resumen)
            filas.Add(new(new[] { "TOTAL", "", "", "", "", "", "", moneda(r.MonedaId), Monto(r.Total), "" }, Total: true));

        return new Reporte
        {
            Titulo = "Detalle de cobros",
            Filtros = filtros,
            Resumen = resumen,
            NombreArchivo = nombre,
            Horizontal = true,
            Columnas = new()
            {
                new("Fecha", 1.3), new("Recibo", 0.6, Derecha: true), new("Caseta", 0.6), new("Bloque", 0.6),
                new("Servicio", 1.8), new("Pagó", 1.7), new("Forma de pago", 0.9), new("Moneda", 0.6),
                new("Monto", 1, Derecha: true, Numero: true), new("Cobró", 1.1)
            },
            Filas = d.Cobros.Count == 0 ? new() : filas
        };
    }

    // ---------- 7. Pagos de un servicio por bloque ----------

    private static Reporte PorBloque(RespuestaCaja d, Func<Guid, string> moneda, Catalogos cat, string filtros, List<string> resumen, string nombre, string servicio)
    {
        var lineas = d.Cobros.Select(c =>
        {
            var (caseta, bloque) = cat.Unidades.TryGetValue(c.UnidadComercialId, out var u) ? u : ("?", null);
            return new { c, Caseta = caseta, Bloque = string.IsNullOrWhiteSpace(bloque) ? "Sin bloque" : bloque! };
        }).ToList();

        var filas = new List<FilaReporte>();
        foreach (var g in lineas.GroupBy(l => l.Bloque).OrderBy(g => g.Key == "Sin bloque").ThenBy(g => g.Key.Length).ThenBy(g => g.Key))
        {
            filas.Add(new(new[] { $"Bloque {g.Key}", "", "", "", "", "" }, Grupo: true));
            foreach (var l in g.OrderBy(l => l.c.FechaEmisionUtc))
                filas.Add(new(new[] { l.c.FechaBolivia, l.c.NumeroRecibo.ToString(Bo), l.Caseta, l.c.Pagador, moneda(l.c.MonedaId), Monto(l.c.Monto) }));
            foreach (var m in g.GroupBy(l => l.c.MonedaId))
                filas.Add(new(new[] { $"Subtotal bloque {g.Key}", $"{m.Count()} pagos", "", "", moneda(m.Key), Monto(m.Sum(l => l.c.Monto)) }, Total: true));
        }

        foreach (var r in d.Resumen)
            filas.Add(new(new[] { "TOTAL GENERAL", $"{r.CantidadRecibos} pagos", "", "", moneda(r.MonedaId), Monto(r.Total) }, Total: true));

        return new Reporte
        {
            Titulo = $"Pagos de {servicio} por bloque",
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
        public string Concepto { get; set; } = "";
        public decimal Monto { get; set; }
    }
}
