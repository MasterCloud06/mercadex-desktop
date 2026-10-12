using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Services;

public sealed class EgresosService
{
    private readonly ApiClient _api = new();

    // ============================================================
    // OBTENER EGRESOS
    //
    // El servidor devuelve todo el historial mediante:
    // GET /api/egresos/todos
    //
    // Las fechas se filtran en el Desktop.
    // ============================================================

    public async Task<List<EgresoModel>> ObtenerAsync(
        DateTime? desde = null,
        DateTime? hasta = null,
        bool incluirAnulados = true)
    {
        var url =
            incluirAnulados
                ? "/api/egresos/todos"
                : "/api/egresos";

        var datos =
            await _api.GetAsync<List<EgresoModel>>(url)
            ?? new List<EgresoModel>();

        if (desde.HasValue)
        {
            var fechaDesde =
                DateOnly.FromDateTime(
                    desde.Value.Date);

            datos =
                datos
                    .Where(x =>
                        x.Fecha >= fechaDesde)
                    .ToList();
        }

        if (hasta.HasValue)
        {
            var fechaHasta =
                DateOnly.FromDateTime(
                    hasta.Value.Date);

            datos =
                datos
                    .Where(x =>
                        x.Fecha <= fechaHasta)
                    .ToList();
        }

        return datos
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.FechaRegistro)
            .ToList();
    }

    // ============================================================
    // CREAR EGRESO
    // POST /api/egresos
    // ============================================================

    public async Task<EgresoModel?> CrearAsync(
        CrearEgresoModel solicitud)
    {
        return await _api.PostAsync<
            CrearEgresoModel,
            EgresoModel>(
                "/api/egresos",
                solicitud);
    }

    // ============================================================
    // ANULAR EGRESO
    // PUT /api/egresos/{id}/anular
    // ============================================================

    public async Task AnularAsync(
        Guid id,
        string motivo)
    {
        await _api.PutAsync(
            $"/api/egresos/{id}/anular",
            new AnularEgresoModel
            {
                Motivo = motivo
            });
    }
}