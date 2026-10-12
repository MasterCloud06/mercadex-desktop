
using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Services;

public sealed class SociosService
{
    private readonly ApiClient _apiClient;

    public SociosService()
    {
        _apiClient = new ApiClient();
    }

    public async Task<List<SocioModel>> ObtenerAsync()
    {
        var socios = await _apiClient
            .GetAsync<List<SocioModel>>("/api/socios");

        return socios ?? new List<SocioModel>();
    }

    public async Task<SocioModel> CrearAsync(
        CrearSocioRequest solicitud)
    {
        var socio = await _apiClient
            .PostAsync<CrearSocioRequest, SocioModel>(
                "/api/socios",
                solicitud);

        return socio ?? throw new InvalidOperationException(
            "El servidor no devolvió los datos del socio.");
    }
}
