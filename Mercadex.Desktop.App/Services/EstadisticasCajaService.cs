using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;


namespace Mercadex.Desktop.App.Services;


public class EstadisticasCajaService
{

    private readonly ApiClient _apiClient;


    public EstadisticasCajaService()
    {
        _apiClient = new ApiClient();
    }



    public async Task<EstadisticasCajaModel?> ObtenerAsync(
        DateTime inicio,
        DateTime fin)
    {

        var url =
            $"/api/estadisticas-caja?inicio={inicio:yyyy-MM-dd}&fin={fin:yyyy-MM-dd}";


        return await _apiClient
            .GetAsync<EstadisticasCajaModel>(url);

    }

}