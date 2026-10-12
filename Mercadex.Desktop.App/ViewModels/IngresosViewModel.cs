
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace Mercadex.Desktop.App.ViewModels;

public partial class IngresosViewModel : ObservableObject
{
    private readonly EstadisticasCajaService _estadisticasService = new();
    private readonly IngresosService _ingresosService = new();

    private readonly Dictionary<Guid, string> _nombresConceptos = new();

    [ObservableProperty]
    private DateTime fechaInicio = DateTime.Today;

    [ObservableProperty]
    private DateTime fechaFin = DateTime.Today;

    [ObservableProperty]
    private EstadisticasCajaModel? resultado;

    [ObservableProperty]
    private UnidadIngresoModel? unidadSeleccionada;

    [ObservableProperty]
    private MonedaIngresoModel? monedaSeleccionada;

    [ObservableProperty]
    private FormaPagoIngresoModel? formaPagoSeleccionada;

    [ObservableProperty]
    private string nombrePagador = string.Empty;

    [ObservableProperty]
    private string referencia = string.Empty;

    [ObservableProperty]
    private decimal totalSeleccionado;

    [ObservableProperty]
    private string mensaje = "Seleccione una unidad comercial.";

    [ObservableProperty]
    private bool cargando;

    public ObservableCollection<EstadisticaFormaPagoModel> FormasPago
        { get; } = new();

    public ObservableCollection<FormaPagoIngresoModel> FormasPagoDisponibles
        { get; } = new();

    public ObservableCollection<IngresoMovimientoModel> Movimientos
        { get; } = new();

    public ObservableCollection<UnidadIngresoModel> Unidades
        { get; } = new();

    public ObservableCollection<MonedaIngresoModel> Monedas
        { get; } = new();

    public ObservableCollection<ObligacionSeleccionable> Obligaciones
        { get; } = new();

    public IngresosViewModel()
    {
        _ = InicializarAsync();
    }

    private async Task InicializarAsync()
    {
        try
        {
            Cargando = true;
            Mensaje = "Cargando datos de Ingresos...";

            var unidades = await _ingresosService.ObtenerUnidadesAsync();

            // El titular no impide cobrar: si falla, se muestra "sin registrar".
            var titulares = new Dictionary<Guid, string>();
            try
            {
                titulares = await _ingresosService.ObtenerTitularesAsync();
            }
            catch
            {
            }

            Unidades.Clear();

            foreach (var unidad in unidades.Where(u => u.Activa))
            {
                unidad.Titular = titulares.GetValueOrDefault(unidad.Id);
                Unidades.Add(unidad);
            }

            var monedas = await _ingresosService.ObtenerMonedasAsync();

            Monedas.Clear();

            foreach (var moneda in monedas)
                Monedas.Add(moneda);

            var catalogos = await _ingresosService.ObtenerCatalogosAsync();

            FormasPagoDisponibles.Clear();
            _nombresConceptos.Clear();

            if (catalogos != null)
            {
                foreach (var forma in catalogos.FormasPago)
                    FormasPagoDisponibles.Add(forma);

                foreach (var concepto in catalogos.Conceptos)
                    _nombresConceptos[concepto.Id] = concepto.Nombre;
            }

            FormaPagoSeleccionada = FormasPagoDisponibles.FirstOrDefault();

            Mensaje = Unidades.Count == 0
                ? "No hay unidades comerciales registradas."
                : "Seleccione una unidad comercial.";
        }
        catch (Exception ex)
        {
            Mensaje = "No fue posible cargar los datos.";

            MessageBox.Show(
                ex.Message,
                "Error de conexión",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Cargando = false;
        }
    }

    partial void OnUnidadSeleccionadaChanged(
        UnidadIngresoModel? value)
    {
        Obligaciones.Clear();
        TotalSeleccionado = 0;
        MonedaSeleccionada = null;

        if (value != null)
            _ = CargarObligacionesAsync(value.Id);
    }

    private async Task CargarObligacionesAsync(Guid unidadId)
    {
        try
        {
            Cargando = true;
            Mensaje = "Consultando obligaciones pendientes...";

            var pendientes =
                await _ingresosService.ObtenerPendientesAsync(unidadId);

            if (UnidadSeleccionada?.Id != unidadId)
                return;

            Obligaciones.Clear();

            foreach (var obligacion in pendientes)
            {
                var nombreConcepto =
                    _nombresConceptos.TryGetValue(
                        obligacion.ConceptoCobroId,
                        out var nombre)
                    ? nombre
                    : obligacion.ConceptoCobroId.ToString();

                var item = new ObligacionSeleccionable(
                    obligacion,
                    nombreConcepto);

                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName ==
                        nameof(ObligacionSeleccionable.Seleccionada))
                    {
                        CalcularTotal();
                    }
                };

                Obligaciones.Add(item);
            }

            Mensaje = Obligaciones.Count == 0
                ? "Esta unidad no tiene obligaciones pendientes."
                : $"Se encontraron {Obligaciones.Count} obligaciones pendientes.";
        }
        catch (Exception ex)
        {
            Mensaje = "Error al consultar las obligaciones.";

            MessageBox.Show(
                ex.Message,
                "Ingresos",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Cargando = false;
        }
    }

    private void CalcularTotal()
    {
        var seleccionadas = Obligaciones
            .Where(o => o.Seleccionada)
            .ToList();

        var monedas = seleccionadas
            .Select(o => o.MonedaId)
            .Distinct()
            .ToList();

        if (monedas.Count > 1)
        {
            TotalSeleccionado = 0;
            MonedaSeleccionada = null;

            Mensaje =
                "Las obligaciones tienen monedas distintas. Cóbrellas por separado.";

            return;
        }

        TotalSeleccionado = seleccionadas.Sum(o => o.Monto);

        MonedaSeleccionada = monedas.Count == 1
            ? Monedas.FirstOrDefault(m => m.Id == monedas[0])
            : null;
    }

    [RelayCommand]
    private async Task Cobrar()
    {
        if (Cargando)
            return;

        if (UnidadSeleccionada == null)
        {
            MessageBox.Show("Seleccione una unidad comercial.");
            return;
        }

        var seleccionadas = Obligaciones
            .Where(o => o.Seleccionada)
            .ToList();

        if (seleccionadas.Count == 0)
        {
            MessageBox.Show("Seleccione al menos una obligación.");
            return;
        }

        if (seleccionadas.Select(o => o.MonedaId).Distinct().Count() != 1)
        {
            MessageBox.Show(
                "Las obligaciones de distintas monedas deben cobrarse por separado.");

            return;
        }

        if (MonedaSeleccionada == null ||
            seleccionadas.Any(o => o.MonedaId != MonedaSeleccionada.Id))
        {
            MessageBox.Show("La moneda seleccionada no corresponde al cobro.");
            return;
        }

        if (FormaPagoSeleccionada == null)
        {
            MessageBox.Show("Seleccione una forma de pago.");
            return;
        }

        // Si se cobra mediante QR, exigir el número de transacción.
        if (string.Equals(
                FormaPagoSeleccionada.Nombre?.Trim(),
                "QR",
                StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(Referencia))
        {
            MessageBox.Show(
                "Para registrar un pago mediante QR, " +
                "introduce el número de transacción.",
                "Número de transacción obligatorio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            Mensaje =
                "Introduce el número de transacción del pago QR.";

            return;
        }


        if (string.IsNullOrWhiteSpace(NombrePagador))
        {
            MessageBox.Show("Introduzca el nombre del pagador.");
            return;
        }

        var confirmacion = MessageBox.Show(
            $"¿Registrar el cobro de {TotalSeleccionado:N2} " +
            $"{MonedaSeleccionada.Codigo}?\n\n" +
            $"Unidad: {UnidadSeleccionada.Codigo}\n" +
            $"Obligaciones: {seleccionadas.Count}\n" +
            $"Forma de pago: {FormaPagoSeleccionada.Nombre}",
            "Confirmar ingreso",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmacion != MessageBoxResult.Yes)
            return;

        var solicitud = new ProcesarCobroIngresoModel
        {
            // El servidor asigna el número real del recibo.
            NumeroRecibo = 0,
            UnidadComercialId = UnidadSeleccionada.Id,
            MonedaId = MonedaSeleccionada.Id,
            Monto = TotalSeleccionado,
            FormaPago = FormaPagoSeleccionada.Id,
            NombrePagador = NombrePagador.Trim(),
            TipoCambio = 1m,
            Referencia = string.IsNullOrWhiteSpace(Referencia)
                ? null
                : Referencia.Trim(),
            Items = seleccionadas
                .Select(o => new ItemCobroIngresoModel
                {
                    ObligacionId = o.Id,
                    NombreConcepto = o.NombreConcepto
                })
                .ToList()
        };

                // Conservamos los datos de la caseta antes de enviar el cobro.
        var unidadIdCobro = solicitud.UnidadComercialId;
        var codigoCaseta = UnidadSeleccionada.Codigo;
        var bloqueCaseta = UnidadSeleccionada.Bloque ?? "";
        var codigoMoneda = MonedaSeleccionada.Codigo;

        bool cobroConfirmado = false;
        long numeroReciboConfirmado = 0;

        try
        {
            Cargando = true;
            Mensaje = "Registrando ingreso...";

            var resultado =
                await _ingresosService.CobrarAsync(solicitud);

            if (resultado?.Recibo == null)
            {
                throw new InvalidOperationException(
                    "La API no devolvió el recibo del cobro.");
            }

            // A partir de aquí, el servidor confirmó el cobro.
            cobroConfirmado = true;
            numeroReciboConfirmado = resultado.Recibo.Numero;

            Mensaje =
                $"Cobro registrado. Recibo N.º {numeroReciboConfirmado}.";

            NombrePagador = string.Empty;
            Referencia = string.Empty;

            // Retiramos las obligaciones cobradas de la pantalla.
            foreach (var obligacion in seleccionadas)
            {
                Obligaciones.Remove(obligacion);
            }

            CalcularTotal();

            // Un error de vista previa no significa que falló el cobro.
            try
            {
                var api = new Mercadex.Desktop.App.Api.ApiClient();

                // Solo consultamos el recibo existente.
                // Estas consultas no crean otro pago.
                var recibo =
                    await api.GetAsync<
                        Mercadex.Desktop.App.Dtos.ReciboDto>(
                        $"/api/Caja/recibos/{resultado.Recibo.Id}");

                if (recibo == null)
                {
                    throw new InvalidOperationException(
                        "No se pudo consultar el recibo registrado.");
                }

                var pago =
                    await api.GetAsync<
                        Mercadex.Desktop.App.Dtos.PagoDto>(
                        $"/api/Caja/pagos/{recibo.PagoId}");

                if (pago == null)
                {
                    throw new InvalidOperationException(
                        "No se pudieron consultar los datos del pago.");
                }

                var ventana =
                    new Mercadex.Desktop.App.Views.VistaPreviaReciboWindow(
                        recibo,
                        pago,
                        codigoCaseta,
                        bloqueCaseta,
                        codigoMoneda);

                var principal = System.Windows.Application
                    .Current?.MainWindow;

                if (principal != null && principal.IsVisible)
                {
                    ventana.Owner = principal;
                }

                ventana.ShowDialog();
            }
            catch (Exception exVistaPrevia)
            {
                MessageBox.Show(
                    $"El cobro ya está registrado.\n" +
                    $"Recibo N.º {numeroReciboConfirmado}.\n\n" +
                    "No vuelva a realizar el cobro.\n\n" +
                    "No se pudo abrir la vista previa. " +
                    "Puede consultar e imprimir este comprobante " +
                    "desde la pantalla Recibos.\n\n" +
                    exVistaPrevia.Message,
                    "Cobro registrado — vista previa no disponible",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // Actualizamos las deudas pendientes de la caseta.
            await CargarObligacionesAsync(unidadIdCobro);
        }
        catch (Exception ex)
        {
            if (cobroConfirmado)
            {
                Mensaje =
                    $"Cobro registrado. Recibo N.º {numeroReciboConfirmado}.";

                MessageBox.Show(
                    Mensaje + "\n\n" +
                    "No vuelva a realizar el cobro. " +
                    "Ocurrió un problema al actualizar la pantalla.\n\n" +
                    ex.Message,
                    "Cobro registrado",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                Mensaje = "No se pudo confirmar el cobro.";

                MessageBox.Show(
                    "No intente cobrar nuevamente sin verificar " +
                    "si el recibo fue registrado.\n\n" +
                    ex.Message,
                    "Error al registrar ingreso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            Cargando = false;
        }
        {
            Cargando = false;
        }
    }

    [RelayCommand]
    private async Task Consultar()
    {
        if (FechaInicio > FechaFin)
        {
            MessageBox.Show(
                "La fecha inicial no puede ser posterior a la final.");

            return;
        }

        try
        {
            Cargando = true;

            Resultado = await _estadisticasService.ObtenerAsync(
                FechaInicio,
                FechaFin);

            FormasPago.Clear();

            if (Resultado != null)
            {
                foreach (var item in Resultado.FormasPago)
                    FormasPago.Add(item);
            }

            var ingresos = await _ingresosService.ObtenerAsync();

            Movimientos.Clear();

            foreach (var item in ingresos.Where(i =>
                i.Fecha.Date >= FechaInicio.Date &&
                i.Fecha.Date <= FechaFin.Date)
                .OrderByDescending(i => i.Fecha))
            {
                Movimientos.Add(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Error cargando ingresos",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Cargando = false;
        }
    }
}

public partial class ObligacionSeleccionable : ObservableObject
{
    public Guid Id { get; }

    public Guid MonedaId { get; }

    public decimal Monto { get; }

    public string Estado { get; }

    public string Periodo { get; }

    public string NombreConcepto { get; }

    [ObservableProperty]
    private bool seleccionada;

    public ObligacionSeleccionable(
        ObligacionIngresoModel obligacion,
        string nombreConcepto)
    {
        Id = obligacion.Id;
        MonedaId = obligacion.MonedaId;
        Monto = obligacion.Monto;
        Estado = obligacion.Estado;
        NombreConcepto = nombreConcepto;

        Periodo = obligacion.PeriodoAnio.HasValue
            ? $"{obligacion.PeriodoMes:00}/{obligacion.PeriodoAnio}"
            : obligacion.FechaEmision.ToString("dd/MM/yyyy");
    }
}
