using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views.Reportes;

/// <summary>
/// Historial de pagos de una caseta (lo que piden los socios): fecha, cuenta
/// y monto de cada pago, desde el primer pago o en un rango de fechas, con
/// el total por cuenta. Solo recibos válidos: los anulados no se cuentan.
/// </summary>
public sealed class HistorialCasetaReporteView : ReporteTablaView
{
    private readonly TextBox _caseta = UiKit.Texto(110, 20);
    private readonly ComboBox _bloque = UiKit.Combo(130);
    private readonly CheckBox _todo = new()
    {
        Content = "Desde el primer pago",
        IsChecked = true,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 14, 16)
    };
    private readonly DatePicker _desde;
    private readonly DatePicker _hasta;

    public HistorialCasetaReporteView()
        : base("Historial de pagos de una caseta",
            "Todos los pagos de una caseta, cuenta por cuenta, desde su primer pago o entre dos fechas. " +
            "Los recibos anulados no se cuentan.")
    {
        Filtros.Children.Add(UiKit.Campo("Caseta", _caseta));
        Filtros.Children.Add(UiKit.Campo("Bloque (si el número se repite)", _bloque));
        Filtros.Children.Add(_todo);

        var hoy = DateTime.Today;
        (_desde, _hasta) = AgregarRango(new DateTime(hoy.Year, 1, 1), hoy);

        void Rango() => _desde.IsEnabled = _hasta.IsEnabled = _todo.IsChecked != true;
        _todo.Click += (_, _) => Rango();
        Rango();

        _caseta.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                await GenerarAsync();
        };

        Loaded += async (_, _) =>
        {
            _caseta.Focus();
            if (_bloque.ItemsSource is not null)
                return;
            try
            {
                var c = await CatalogosAsync();
                _bloque.ItemsSource = ComboBloques(c).ItemsSource;
                _bloque.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Avisar("No se pudieron cargar los bloques: " + ex.Message);
            }
        };
    }

    protected override async Task<Reporte?> ConstruirAsync()
    {
        var codigo = _caseta.Text.Trim();
        if (codigo.Length == 0)
        {
            Avisar("Escribe el número de la caseta.");
            return null;
        }

        (DateTime Desde, DateTime Hasta)? rango = null;
        if (_todo.IsChecked != true)
        {
            rango = LeerRango(_desde, _hasta);
            if (rango is null)
                return null;
        }

        var cat = await CatalogosAsync();
        var bloque = (_bloque.SelectedItem as UiKit.Item<string?>)?.Valor;
        var encontradas = cat.Unidades
            .Where(u => string.Equals(u.Value.Codigo.Trim(), codigo, StringComparison.OrdinalIgnoreCase) &&
                        (bloque is null || string.Equals(u.Value.Bloque?.Trim(), bloque, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (encontradas.Count == 0)
        {
            Avisar(bloque is null ? $"No se encontró la caseta {codigo}." : $"No se encontró la caseta {codigo} en el bloque {bloque}.");
            return null;
        }

        if (encontradas.Count > 1)
        {
            Avisar($"Hay {encontradas.Count} unidades con el número {codigo} (bloques " +
                   string.Join(", ", encontradas.Select(u => u.Value.Bloque ?? "sin bloque")) + "). Elige el bloque.");
            return null;
        }

        var ruta = $"/api/reportes-caja/caseta/{encontradas[0].Key}";
        if (rango is { } r)
            ruta += $"?desde={r.Desde:yyyy-MM-dd}&hasta={r.Hasta:yyyy-MM-dd}";

        var d = await ApiJson.GetAsync<Historial>(ruta) ?? new Historial();

        var variasMonedas = d.Lineas.Any(l => l.Moneda != "BOB");
        string Celda(string moneda, decimal monto) => variasMonedas ? $"{moneda} {Monto(monto)}" : Monto(monto);

        var filas = d.Lineas
            .Select(l => new FilaReporte(new[] { l.Fecha, l.NumeroRecibo.ToString(Bo), l.Concepto, l.Periodo ?? "", Celda(l.Moneda, l.Monto) }))
            .ToList();

        if (d.Totales.Count > 0)
        {
            filas.Add(new(new[] { "Total por cuenta", "", "", "", "" }, Grupo: true));
            foreach (var t in d.Totales)
                filas.Add(new(new[] { "", $"{t.Cantidad} {(t.Cantidad == 1 ? "pago" : "pagos")}", t.Concepto, "", Celda(t.Moneda, t.Total) }));
            foreach (var t in d.TotalPorMoneda)
                filas.Add(new(new[] { "TOTAL PAGADO", "", "", "", Celda(t.Moneda, t.Total) }, Total: true));
        }

        var periodo = rango is { } x ? Rango(x.Desde, x.Hasta) : "Desde el primer pago";
        var titular = string.IsNullOrWhiteSpace(d.Titular) ? "sin titular registrado" : d.Titular;
        var resumen = d.TotalPorMoneda.Count == 0
            ? new List<string> { "No hay pagos en ese período." }
            : d.TotalPorMoneda.Select(t => $"Total pagado: {t.Moneda} {Monto(t.Total)}  ({d.Recibos} {(d.Recibos == 1 ? "recibo" : "recibos")})").ToList();

        return new Reporte
        {
            Titulo = $"Historial de pagos · Caseta {d.Caseta}",
            Filtros = $"Caseta {d.Caseta}" + (string.IsNullOrWhiteSpace(d.Bloque) ? "" : $" · Bloque {d.Bloque}") +
                      $" · Titular: {titular}   ·   {periodo}",
            Resumen = resumen,
            NombreArchivo = $"Historial_pagos_caseta_{d.Caseta}",
            Columnas = new()
            {
                new("Fecha", 1),
                new("Recibo", 0.8, Derecha: true),
                new("Cuenta", 2.4),
                new("Período", 0.8),
                new(variasMonedas ? "Monto" : "Monto Bs", 1.1, Derecha: true, Numero: !variasMonedas)
            },
            Filas = d.Lineas.Count == 0 ? new() : filas
        };
    }

    // Respuesta de GET /api/reportes-caja/caseta/{id}.

    private sealed class Historial
    {
        public string Caseta { get; set; } = "";
        public string? Bloque { get; set; }
        public string? Titular { get; set; }
        public int Recibos { get; set; }
        public List<Linea> Lineas { get; set; } = new();
        public List<TotalCuenta> Totales { get; set; } = new();
        public List<TotalCuenta> TotalPorMoneda { get; set; } = new();
    }

    private sealed class Linea
    {
        public string Fecha { get; set; } = "";
        public long NumeroRecibo { get; set; }
        public string Concepto { get; set; } = "";
        public string? Periodo { get; set; }
        public string Moneda { get; set; } = "";
        public decimal Monto { get; set; }
    }

    private sealed class TotalCuenta
    {
        public string Concepto { get; set; } = "";
        public string Moneda { get; set; } = "";
        public int Cantidad { get; set; }
        public decimal Total { get; set; }
    }
}
