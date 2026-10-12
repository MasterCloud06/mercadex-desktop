using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;

using Microsoft.Win32;

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace Mercadex.Desktop.App.ViewModels;

public partial class EgresosViewModel : ObservableObject
{
    private readonly EgresosService _egresosService = new();
    private readonly IngresosService _ingresosService = new();

    private readonly CultureInfo _cultura =
        CultureInfo.GetCultureInfo("es-BO");

    // ============================================================
    // FORMULARIO DE REGISTRO
    // ============================================================

    [ObservableProperty]
    private DateTime fecha = DateTime.Today;

    [ObservableProperty]
    private string categoriaSeleccionada = "Servicios";

    [ObservableProperty]
    private string concepto = string.Empty;

    [ObservableProperty]
    private string beneficiario = string.Empty;

    [ObservableProperty]
    private string montoTexto = string.Empty;

    [ObservableProperty]
    private MonedaIngresoModel? monedaSeleccionada;

    [ObservableProperty]
    private string formaPagoSeleccionada = "Efectivo";

    [ObservableProperty]
    private string numeroComprobante = string.Empty;

    [ObservableProperty]
    private string observacion = string.Empty;

    // ============================================================
    // FILTROS
    // ============================================================

    [ObservableProperty]
    private DateTime fechaDesde =
        new(
            DateTime.Today.Year,
            DateTime.Today.Month,
            1);

    [ObservableProperty]
    private DateTime fechaHasta = DateTime.Today;

    [ObservableProperty]
    private string categoriaFiltro = "Todas";

    // ============================================================
    // ESTADO DE LA PANTALLA
    // ============================================================

    [ObservableProperty]
    private EgresoModel? egresoSeleccionado;

    [ObservableProperty]
    private bool cargando;

    [ObservableProperty]
    private string mensaje =
        "Cargando módulo de gastos y egresos...";

    [ObservableProperty]
    private string totalPeriodo =
        "Sin consultar";

    // ============================================================
    // CATÁLOGOS
    // ============================================================

    public ObservableCollection<string> Categorias
        { get; } = new()
    {
        "Servicios",
        "Mantenimiento",
        "Limpieza",
        "Seguridad",
        "Administración",
        "Reparaciones",
        "Compras",
        "Impuestos y tasas",
        "Otros"
    };

    public ObservableCollection<string> CategoriasFiltro
        { get; } = new()
    {
        "Todas",
        "Servicios",
        "Mantenimiento",
        "Limpieza",
        "Seguridad",
        "Administración",
        "Reparaciones",
        "Compras",
        "Impuestos y tasas",
        "Otros"
    };

    public ObservableCollection<string> FormasPago
        { get; } = new()
    {
        "Efectivo",
        "QR",
        "Transferencia",
        "Cheque",
        "Otro"
    };

    public ObservableCollection<MonedaIngresoModel> Monedas
        { get; } = new();

    // ============================================================
    // DATOS
    // ============================================================

    public ObservableCollection<EgresoModel> Egresos
        { get; } = new();

    public ObservableCollection<ResumenEgresoModel> Resumen
        { get; } = new();

    // ============================================================
    // CONSTRUCTOR
    // ============================================================

    public EgresosViewModel()
    {
        _ = InicializarAsync();
    }

    // ============================================================
    // INICIALIZACIÓN
    // ============================================================

    private async Task InicializarAsync()
    {
        try
        {
            Cargando = true;

            Mensaje =
                "Cargando módulo de gastos y egresos...";

            var monedas =
                await _ingresosService.ObtenerMonedasAsync();

            Monedas.Clear();

            foreach (var moneda in monedas)
            {
                Monedas.Add(moneda);
            }

            MonedaSeleccionada =
                Monedas.FirstOrDefault(
                    x => string.Equals(
                        x.Codigo,
                        "BOB",
                        StringComparison.OrdinalIgnoreCase))
                ?? Monedas.FirstOrDefault();

            await CargarAsync();
        }
        catch (Exception ex)
        {
            Mensaje =
                "No fue posible cargar Gastos / Egresos.";

            MessageBox.Show(
                ex.Message,
                "Gastos / Egresos",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Cargando = false;
        }
    }

    // ============================================================
    // GUARDAR EGRESO
    // ============================================================

    [RelayCommand]
    private async Task Guardar()
    {
        if (Cargando)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                CategoriaSeleccionada))
        {
            MessageBox.Show(
                "Seleccione una categoría.");

            return;
        }

        if (string.IsNullOrWhiteSpace(
                Concepto))
        {
            MessageBox.Show(
                "Ingrese el concepto del egreso.");

            return;
        }

        if (string.IsNullOrWhiteSpace(
                Beneficiario))
        {
            MessageBox.Show(
                "Ingrese el beneficiario o proveedor.");

            return;
        }

        if (!decimal.TryParse(
                MontoTexto,
                NumberStyles.Number,
                _cultura,
                out var monto))
        {
            if (!decimal.TryParse(
                    MontoTexto,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out monto))
            {
                MessageBox.Show(
                    "Ingrese un monto válido.");

                return;
            }
        }

        if (monto <= 0)
        {
            MessageBox.Show(
                "El monto debe ser mayor a cero.");

            return;
        }

        if (MonedaSeleccionada == null)
        {
            MessageBox.Show(
                "Seleccione una moneda.");

            return;
        }

        if (string.IsNullOrWhiteSpace(
                FormaPagoSeleccionada))
        {
            MessageBox.Show(
                "Seleccione una forma de pago.");

            return;
        }

        var confirmacion =
            MessageBox.Show(
                $"¿Registrar este egreso?\n\n" +
                $"Fecha: {Fecha:dd/MM/yyyy}\n" +
                $"Categoría: {CategoriaSeleccionada}\n" +
                $"Concepto: {Concepto.Trim()}\n" +
                $"Beneficiario: {Beneficiario.Trim()}\n" +
                $"Importe: {monto:N2} " +
                $"{MonedaSeleccionada.Codigo}",
                "Confirmar egreso",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (confirmacion !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Cargando = true;

            Mensaje =
                "Registrando egreso...";

            var solicitud =
                new CrearEgresoModel
                {
                    Fecha =
                        DateOnly.FromDateTime(
                            Fecha),

                    Categoria =
                        CategoriaSeleccionada.Trim(),

                    Concepto =
                        Concepto.Trim(),

                    Beneficiario =
                        Beneficiario.Trim(),

                    Monto =
                        monto,

                    MonedaId =
                        MonedaSeleccionada.Id,

                    FormaPago =
                        FormaPagoSeleccionada.Trim(),

                    NumeroComprobante =
                        string.IsNullOrWhiteSpace(
                            NumeroComprobante)
                            ? null
                            : NumeroComprobante.Trim(),

                    Observacion =
                        string.IsNullOrWhiteSpace(
                            Observacion)
                            ? null
                            : Observacion.Trim()
                };

            var resultado =
                await _egresosService.CrearAsync(
                    solicitud);

            if (resultado == null)
            {
                throw new InvalidOperationException(
                    "La API no devolvió el egreso registrado.");
            }

            MessageBox.Show(
                "Egreso registrado correctamente.",
                "Gastos / Egresos",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            LimpiarFormulario();

            Cargando = false;

            await CargarAsync();
        }
        catch (Exception ex)
        {
            Mensaje =
                "No se pudo registrar el egreso.";

            MessageBox.Show(
                ex.Message,
                "Error al registrar egreso",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Cargando = false;
        }
    }

    // ============================================================
    // ACTUALIZAR
    // ============================================================

    [RelayCommand]
    private async Task Actualizar()
    {
        await CargarAsync();
    }

    // ============================================================
    // CARGAR HISTORIAL
    // ============================================================

    private async Task CargarAsync()
    {
        if (FechaDesde.Date >
            FechaHasta.Date)
        {
            MessageBox.Show(
                "La fecha inicial no puede ser posterior a la final.");

            return;
        }

        try
        {
            Cargando = true;

            Mensaje =
                "Consultando egresos...";

            var datos =
                await _egresosService.ObtenerAsync(
                    FechaDesde,
                    FechaHasta,
                    true);

            var filtrados =
                CategoriaFiltro == "Todas"
                    ? datos
                    : datos
                        .Where(x =>
                            string.Equals(
                                x.Categoria,
                                CategoriaFiltro,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();

            var monedasPorId =
                Monedas
                    .GroupBy(x => x.Id)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First().Codigo);

            foreach (var item in filtrados)
            {
                item.MonedaCodigo =
                    monedasPorId.TryGetValue(
                        item.MonedaId,
                        out var codigo)
                        ? codigo
                        : "N/D";
            }

            Egresos.Clear();

            foreach (var item in filtrados
                .OrderByDescending(
                    x => x.Fecha)
                .ThenByDescending(
                    x => x.FechaRegistro))
            {
                Egresos.Add(item);
            }

            await CargarResumenAsync();

            Mensaje =
                $"Egresos encontrados: " +
                $"{Egresos.Count}";
        }
        catch (Exception ex)
        {
            Mensaje =
                "No se pudieron cargar los egresos.";

            MessageBox.Show(
                ex.Message,
                "Gastos / Egresos",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Cargando = false;
        }
    }

    // ============================================================
    // RESUMEN POR CATEGORÍA Y MONEDA
    // ============================================================

    private Task CargarResumenAsync()
{
    Resumen.Clear();

    var egresosActivos =
        Egresos
            .Where(x => !x.Anulado)
            .ToList();

    var resumenPorCategoria =
        egresosActivos
            .GroupBy(x => new
            {
                x.Categoria,
                x.MonedaId,
                x.MonedaCodigo
            })
            .Select(g =>
                new ResumenEgresoModel
                {
                    Categoria =
                        g.Key.Categoria,

                    MonedaId =
                        g.Key.MonedaId,

                    MonedaCodigo =
                        g.Key.MonedaCodigo,

                    Cantidad =
                        g.Count(),

                    Total =
                        g.Sum(x => x.Monto)
                })
            .OrderBy(x => x.Categoria)
            .ThenBy(x => x.MonedaCodigo)
            .ToList();

    foreach (var item in resumenPorCategoria)
    {
        Resumen.Add(item);
    }

    var totales =
        egresosActivos
            .GroupBy(x => x.MonedaCodigo)
            .OrderBy(x => x.Key)
            .Select(g =>
                $"{g.Key}: " +
                g.Sum(x => x.Monto)
                    .ToString(
                        "N2",
                        _cultura))
            .ToList();

    TotalPeriodo =
        totales.Count == 0
            ? "Sin egresos activos en el período."
            : "Total de egresos: " +
              string.Join(
                  "     |     ",
                  totales);

    return Task.CompletedTask;
}

    // ============================================================
    // CAMBIO DE FILTRO DE CATEGORÍA
    // ============================================================

    partial void OnCategoriaFiltroChanged(
        string value)
    {
        if (!Cargando)
        {
            _ = CargarAsync();
        }
    }

    // ============================================================
    // CAMBIO DE FECHA DESDE
    // ============================================================

    partial void OnFechaDesdeChanged(
        DateTime value)
    {
        if (!Cargando)
        {
            _ = CargarAsync();
        }
    }

    // ============================================================
    // CAMBIO DE FECHA HASTA
    // ============================================================

    partial void OnFechaHastaChanged(
        DateTime value)
    {
        if (!Cargando)
        {
            _ = CargarAsync();
        }
    }

    // ============================================================
    // ANULAR EGRESO
    // ============================================================

    [RelayCommand]
    private async Task Anular()
    {
        if (EgresoSeleccionado == null)
        {
            MessageBox.Show(
                "Seleccione un egreso.");

            return;
        }

        if (EgresoSeleccionado.Anulado)
        {
            MessageBox.Show(
                "El egreso seleccionado ya está anulado.");

            return;
        }

        var motivo =
            Microsoft.VisualBasic.Interaction.InputBox(
                "Indique el motivo de la anulación:",
                "Anular egreso",
                string.Empty);

        if (string.IsNullOrWhiteSpace(
                motivo))
        {
            return;
        }

        var confirmar =
            MessageBox.Show(
                $"¿Anular el egreso seleccionado?\n\n" +
                $"Concepto: " +
                $"{EgresoSeleccionado.Concepto}\n" +
                $"Importe: " +
                $"{EgresoSeleccionado.Monto:N2} " +
                $"{EgresoSeleccionado.MonedaCodigo}\n\n" +
                $"Motivo: {motivo.Trim()}",
                "Confirmar anulación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (confirmar !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Cargando = true;

            Mensaje =
                "Anulando egreso...";

            await _egresosService.AnularAsync(
                EgresoSeleccionado.Id,
                motivo.Trim());

            MessageBox.Show(
                "Egreso anulado correctamente.",
                "Gastos / Egresos",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            EgresoSeleccionado = null;

            Cargando = false;

            await CargarAsync();
        }
        catch (Exception ex)
        {
            Mensaje =
                "No se pudo anular el egreso.";

            MessageBox.Show(
                ex.Message,
                "Error al anular",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Cargando = false;
        }
    }

    // ============================================================
    // LIMPIAR FORMULARIO
    // ============================================================

    [RelayCommand]
    private void Limpiar()
    {
        LimpiarFormulario();
    }

    private void LimpiarFormulario()
    {
        Fecha =
            DateTime.Today;

        CategoriaSeleccionada =
            "Servicios";

        Concepto =
            string.Empty;

        Beneficiario =
            string.Empty;

        MontoTexto =
            string.Empty;

        FormaPagoSeleccionada =
            "Efectivo";

        NumeroComprobante =
            string.Empty;

        Observacion =
            string.Empty;

        MonedaSeleccionada =
            Monedas.FirstOrDefault(
                x => string.Equals(
                    x.Codigo,
                    "BOB",
                    StringComparison.OrdinalIgnoreCase))
            ?? Monedas.FirstOrDefault();
    }

    // ============================================================
    // EXPORTAR CSV
    // ============================================================

    [RelayCommand]
    private void Exportar()
    {
        if (Egresos.Count == 0)
        {
            MessageBox.Show(
                "No hay egresos para exportar.");

            return;
        }

        var dialogo =
            new SaveFileDialog
            {
                Title =
                    "Exportar reporte de gastos y egresos",

                FileName =
                    $"Egresos_{FechaDesde:yyyyMMdd}_" +
                    $"{FechaHasta:yyyyMMdd}.csv",

                DefaultExt =
                    ".csv",

                Filter =
                    "Archivos CSV (*.csv)|*.csv"
            };

        if (dialogo.ShowDialog() !=
            true)
        {
            return;
        }

        try
        {
            var csv =
                new StringBuilder();

            csv.AppendLine(
                "Fecha;Categoria;Concepto;" +
                "Beneficiario;Moneda;Monto;" +
                "FormaPago;Comprobante;" +
                "Estado;Observacion");

            foreach (var x in Egresos)
            {
                csv.AppendLine(
                    string.Join(
                        ";",
                        Csv(x.FechaTexto),
                        Csv(x.Categoria),
                        Csv(x.Concepto),
                        Csv(x.Beneficiario),
                        Csv(x.MonedaCodigo),
                        Csv(
                            x.Monto.ToString(
                                "0.00",
                                CultureInfo.InvariantCulture)),
                        Csv(x.FormaPago),
                        Csv(x.NumeroComprobante),
                        Csv(x.Estado),
                        Csv(x.Observacion)));
            }

            File.WriteAllText(
                dialogo.FileName,
                csv.ToString(),
                new UTF8Encoding(true));

            MessageBox.Show(
                "Reporte exportado correctamente.",
                "Mercadex",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Error al exportar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ============================================================
    // ESCAPAR VALORES CSV
    // ============================================================

    private static string Csv(
        string? valor)
    {
        var texto =
            valor ?? string.Empty;

        // Evita interpretación de fórmulas
        // al abrir el CSV con Excel.
        if (texto.Length > 0 &&
            "=+-@".Contains(texto[0]))
        {
            texto =
                "'" + texto;
        }

        return
            "\"" +
            texto.Replace(
                "\"",
                "\"\"") +
            "\"";
    }
}