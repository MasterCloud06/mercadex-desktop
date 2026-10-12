using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Mercadex.Desktop.App.Configuration;

namespace Mercadex.Desktop.App.Services;

public sealed class TitularCasetaService
{
    private static readonly HttpClient Api = new()
    {
        BaseAddress = ApiConfiguration.BaseUri,
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<string> ObtenerAsync(Guid unidadId)
    {
        if (unidadId == Guid.Empty)
            return "Caseta no identificada";

        var asignaciones =
            await Api.GetFromJsonAsync<List<AsignacionTitularDto>>(
                "/api/asignaciones-socio-caseta")
            ?? new();

        var sociosAsignados = asignaciones
            .Where(x =>
                x.UnidadComercialId == unidadId &&
                x.Activa)
            .Select(x => x.SocioId)
            .Distinct()
            .ToList();

        if (sociosAsignados.Count == 0)
            return "Sin titular activo registrado";

        if (sociosAsignados.Count > 1)
            return "Por verificar: varias asignaciones activas";

        var socios =
            await Api.GetFromJsonAsync<List<SocioTitularDto>>(
                "/api/socios")
            ?? new();

        var socio = socios.FirstOrDefault(
            x => x.Id == sociosAsignados[0]);

        if (socio == null)
            return "Titular no encontrado";

        if (!socio.Activo)
            return "Por verificar: socio inactivo";

        if (string.IsNullOrWhiteSpace(socio.NombreCompleto))
            return "Nombre del titular no disponible";

        return socio.NombreCompleto.Trim();
    }
}

public sealed class AsignacionTitularDto
{
    public Guid SocioId { get; set; }

    public Guid UnidadComercialId { get; set; }

    public bool Activa { get; set; }
}

public sealed class SocioTitularDto
{
    public Guid Id { get; set; }

    public string NombreCompleto { get; set; }
        = string.Empty;

    public bool Activo { get; set; }
}