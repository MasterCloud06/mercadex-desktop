using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Services;

public sealed class ObligacionesService
{
    private readonly ApiClient _apiClient;

    public ObligacionesService()
    {
        _apiClient = new ApiClient();
    }

    public async Task<List<ObligacionModel>> ObtenerAsync()
    {
        var resultado =
            await _apiClient
                .GetAsync<List<ObligacionModel>>(
                    "/api/Obligaciones");

        return resultado ?? new List<ObligacionModel>();
    }

    public async Task<ObligacionModel> CrearAsync(
        CrearObligacionRequest solicitud)
    {
        var resultado =
            await _apiClient
                .PostAsync<
                    CrearObligacionRequest,
                    ObligacionModel>(
                        "/api/Obligaciones",
                        solicitud);

        return resultado
            ?? throw new InvalidOperationException(
                "El servidor no devolvió la obligación creada.");
    }
}