
using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Services;

public sealed class IngresosService
{
    private readonly ApiClient _apiClient = new();

    public async Task<List<IngresoMovimientoModel>> ObtenerAsync()
        => await _apiClient.GetAsync<List<IngresoMovimientoModel>>(
            "/api/Ingresos") ?? new();

    public async Task<List<UnidadIngresoModel>> ObtenerUnidadesAsync()
        => await _apiClient.GetAsync<List<UnidadIngresoModel>>(
            "/api/unidades-comerciales") ?? new();

    public async Task<List<MonedaIngresoModel>> ObtenerMonedasAsync()
        => await _apiClient.GetAsync<List<MonedaIngresoModel>>(
            "/api/monedas") ?? new();

    public async Task<List<ObligacionIngresoModel>> ObtenerPendientesAsync(
        Guid unidadId)
        => await _apiClient.GetAsync<List<ObligacionIngresoModel>>(
            $"/api/Obligaciones/unidad/{unidadId}/pendientes") ?? new();

    public async Task<CatalogosIngresosModel?> ObtenerCatalogosAsync()
        => await _apiClient.GetAsync<CatalogosIngresosModel>(
            "/api/ingresos/catalogos");

    public async Task<ResultadoCobroIngresoModel?> CobrarAsync(
        ProcesarCobroIngresoModel solicitud)
        => await _apiClient.PostAsync<
            ProcesarCobroIngresoModel,
            ResultadoCobroIngresoModel>(
                "/api/Caja/cobrar",
                solicitud);
}
