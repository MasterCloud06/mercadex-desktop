using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Dtos;
using Mercadex.Desktop.App.Models;


namespace Mercadex.Desktop.App.Services;


public class RecibosService
{

    private readonly ApiClient _apiClient;


    public RecibosService()
    {
        _apiClient = new ApiClient();
    }



    public async Task<List<ReciboIngresoModel>> ObtenerIngresosAsync()
    {

        var recibos =
            await _apiClient
            .GetAsync<List<ReciboDto>>("/api/Caja/recibos");


        var resultado = new List<ReciboIngresoModel>();


        if (recibos == null)
        {
            return resultado;
        }



        foreach (var recibo in recibos)
        {

            var pago =
                await _apiClient
                .GetAsync<PagoDto>(
                    $"/api/Caja/pagos/{recibo.PagoId}");



            resultado.Add(
                new ReciboIngresoModel
                {

                    Numero = recibo.Numero,

                    Fecha = recibo.FechaEmision.DateTime,


                    Pagador =
                        pago?.NombrePagador
                        ?? "Sin nombre",


                    FormaPago =
                        pago?.FormaPago
                        ?? "Sin definir",


                    Total =
                        pago?.Monto
                        ?? recibo.Total,


                    Estado =
                        recibo.Estado

                });

        }


        return resultado;

    }

}