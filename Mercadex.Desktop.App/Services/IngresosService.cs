
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

    /// <summary>
    /// Titular activo de cada unidad: Id de unidad -> nombre del socio.
    /// </summary>
    public async Task<Dictionary<Guid, string>> ObtenerTitularesAsync()
    {
        var asignaciones = await _apiClient.GetAsync<List<AsignacionTitularDto>>(
            "/api/asignaciones-socio-caseta") ?? new();

        var socios = await _apiClient.GetAsync<List<SocioTitularDto>>(
            "/api/socios") ?? new();

        var nombres = socios.ToDictionary(x => x.Id, x => x.NombreCompleto);

        return asignaciones
            .Where(x => x.Activa && nombres.ContainsKey(x.SocioId))
            .GroupBy(x => x.UnidadComercialId)
            .ToDictionary(g => g.Key, g => nombres[g.First().SocioId]);
    }

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
