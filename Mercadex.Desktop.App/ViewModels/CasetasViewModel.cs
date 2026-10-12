using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Mercadex.Desktop.App.Api;

namespace Mercadex.Desktop.App.ViewModels;

// ============================================================
// MODELOS PARA LA PANTALLA DE CASETAS
// ============================================================

public sealed class PasilloItem
{
    public Guid Id { get; set; }

    public string Codigo { get; set; } = "";

    public string Nombre { get; set; } = "";

    public bool Activo { get; set; }

    public string Descripcion =>
        string.IsNullOrWhiteSpace(Codigo)
            ? Nombre
            : $"{Codigo} - {Nombre}";
}

public sealed class UnidadComercialItem
{
    public Guid Id { get; set; }

    public string Codigo { get; set; } = "";

    public int Tipo { get; set; }

    public Guid? PasilloId { get; set; }

    public string? Bloque { get; set; }

    public bool Activa { get; set; }

    public DateTimeOffset FechaRegistro { get; set; }

    public string TipoNombre => Tipo switch
    {
        1 => "Caseta",
        2 => "Comedor",
        3 => "Ambulante",
        _ => "Desconocido"
    };

    public string PasilloNombre { get; set; } =
        "Sin pasillo";
}

public sealed record TipoUnidadOpcion(
    int Valor,
    string Nombre
);

public sealed record CrearPasilloRequest(
    string Codigo,
    string Nombre
);

public sealed record CrearUnidadRequest(
    string Codigo,
    int Tipo,
    Guid? PasilloId,
    string? Bloque
);

public sealed record ActualizarUbicacionUnidadRequest(
    Guid? PasilloId,
    string? Bloque
);

// ============================================================
// VIEWMODEL
// ============================================================

public partial class CasetasViewModel : ObservableObject
{
    private readonly ApiClient _api =
        App.ApiClient;

    public ObservableCollection<PasilloItem> Pasillos { get; }
        = new();

    public ObservableCollection<UnidadComercialItem> Unidades { get; }
        = new();

    public IReadOnlyList<TipoUnidadOpcion> Tipos { get; } =
    [
        new(1, "Caseta"),
        new(2, "Comedor"),
        new(3, "Ambulante")
    ];

    // ========================================================
    // REGISTRO DE PASILLO
    // ========================================================

    [ObservableProperty]
    private string codigoPasillo = "";

    [ObservableProperty]
    private string nombrePasillo = "";

    // ========================================================
    // REGISTRO DE UNIDAD
    // ========================================================

    [ObservableProperty]
    private string codigoUnidad = "";

    [ObservableProperty]
    private string bloque = "";

    [ObservableProperty]
    private PasilloItem? pasilloSeleccionado;

    [ObservableProperty]
    private TipoUnidadOpcion? tipoSeleccionado;

    // ========================================================
    // EDICIÓN DE UNIDAD EXISTENTE
    // ========================================================

    [ObservableProperty]
    private UnidadComercialItem? unidadSeleccionada;

    [ObservableProperty]
    private PasilloItem? pasilloEdicionSeleccionado;

    [ObservableProperty]
    private string bloqueEdicion = "";

    // ========================================================
    // ESTADO GENERAL
    // ========================================================

    [ObservableProperty]
    private string mensaje = "";

    [ObservableProperty]
    private bool estaOcupado;

    public CasetasViewModel()
    {
        TipoSeleccionado =
            Tipos.FirstOrDefault();
    }

    // ========================================================
    // CUANDO SE SELECCIONA UNA UNIDAD EXISTENTE
    // ========================================================

    partial void OnUnidadSeleccionadaChanged(
        UnidadComercialItem? value)
    {
        if (value is null)
        {
            PasilloEdicionSeleccionado = null;
            BloqueEdicion = "";
            return;
        }

        PasilloEdicionSeleccionado =
            Pasillos.FirstOrDefault(
                p => p.Id == value.PasilloId);

        BloqueEdicion =
            value.Bloque ?? "";
    }

    // ========================================================
    // CARGAR PASILLOS Y UNIDADES
    // ========================================================

    [RelayCommand]
    public async Task CargarAsync()
    {
        if (EstaOcupado)
            return;

        EstaOcupado = true;

        Mensaje =
            "Consultando pasillos y unidades comerciales...";

        try
        {
            var unidadSeleccionadaId =
                UnidadSeleccionada?.Id;

            var pasilloRegistroId =
                PasilloSeleccionado?.Id;

            var pasilloEdicionId =
                PasilloEdicionSeleccionado?.Id;

            var pasillosTask =
                _api.GetAsync<List<PasilloItem>>(
                    "/api/pasillos");

            var unidadesTask =
                _api.GetAsync<List<UnidadComercialItem>>(
                    "/api/unidades-comerciales");

            await Task.WhenAll(
                pasillosTask,
                unidadesTask);

            var pasillos =
                await pasillosTask
                ?? new List<PasilloItem>();

            var unidades =
                await unidadesTask
                ?? new List<UnidadComercialItem>();

            Pasillos.Clear();

            foreach (var pasillo in pasillos
                .OrderBy(p => p.Codigo)
                .ThenBy(p => p.Nombre))
            {
                Pasillos.Add(pasillo);
            }

            Unidades.Clear();

            foreach (var unidad in unidades
                .OrderBy(u => u.Tipo)
                .ThenBy(u => u.Codigo))
            {
                unidad.PasilloNombre =
                    pasillos.FirstOrDefault(
                        p => p.Id == unidad.PasilloId
                    )?.Nombre
                    ?? "Sin pasillo";

                Unidades.Add(unidad);
            }

            PasilloSeleccionado =
                Pasillos.FirstOrDefault(
                    p => p.Id == pasilloRegistroId)
                ?? Pasillos.FirstOrDefault(
                    p => p.Activo);

            UnidadSeleccionada =
                Unidades.FirstOrDefault(
                    u => u.Id == unidadSeleccionadaId);

            if (UnidadSeleccionada is not null)
            {
                PasilloEdicionSeleccionado =
                    Pasillos.FirstOrDefault(
                        p =>
                            p.Id ==
                            UnidadSeleccionada.PasilloId)
                    ??
                    Pasillos.FirstOrDefault(
                        p => p.Id == pasilloEdicionId);
            }

            var casetas =
                Unidades.Count(
                    u => u.Tipo == 1);

            var conPasillo =
                Unidades.Count(
                    u =>
                        u.Tipo == 1 &&
                        u.PasilloId.HasValue);

            var sinPasillo =
                casetas - conPasillo;

            Mensaje =
                $"Pasillos: {Pasillos.Count}. " +
                $"Unidades comerciales: {Unidades.Count}. " +
                $"Casetas sin pasillo: {sinPasillo}.";
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode ==
                  HttpStatusCode.NotFound)
        {
            Mensaje =
                "Los controladores requeridos todavía no están disponibles en la API.";
        }
        catch (Exception ex)
        {
            Mensaje =
                $"Error al consultar: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    // ========================================================
    // REGISTRAR PASILLO
    // ========================================================

    [RelayCommand]
    private async Task GuardarPasilloAsync()
    {
        if (EstaOcupado)
            return;

        var codigo =
            CodigoPasillo.Trim();

        var nombre =
            NombrePasillo.Trim();

        if (string.IsNullOrWhiteSpace(codigo) ||
            string.IsNullOrWhiteSpace(nombre))
        {
            Mensaje =
                "Completa el código y el nombre del pasillo.";

            return;
        }

        if (codigo.Length > 50)
        {
            Mensaje =
                "El código del pasillo es demasiado largo.";

            return;
        }

        if (nombre.Length > 150)
        {
            Mensaje =
                "El nombre del pasillo es demasiado largo.";

            return;
        }

        var duplicado =
            Pasillos.Any(
                p =>
                    p.Codigo.Equals(
                        codigo,
                        StringComparison.OrdinalIgnoreCase));

        if (duplicado)
        {
            Mensaje =
                $"Ya existe un pasillo con el código {codigo}.";

            return;
        }

        EstaOcupado = true;

        Mensaje =
            "Registrando pasillo...";

        try
        {
            var solicitud =
                new CrearPasilloRequest(
                    codigo,
                    nombre);

            var nuevo =
                await _api.PostAsync<
                    CrearPasilloRequest,
                    PasilloItem>(
                        "/api/pasillos",
                        solicitud);

            if (nuevo is null)
            {
                throw new InvalidOperationException(
                    "El servidor no devolvió los datos del pasillo.");
            }

            Pasillos.Add(nuevo);

            ReordenarPasillos();

            PasilloSeleccionado =
                nuevo;

            CodigoPasillo = "";
            NombrePasillo = "";

            Mensaje =
                $"Pasillo {nuevo.Nombre} registrado correctamente.";
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode ==
                  HttpStatusCode.Conflict)
        {
            Mensaje =
                "Ya existe un pasillo con ese código.";
        }
        catch (Exception ex)
        {
            Mensaje =
                $"Error al registrar el pasillo: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    // ========================================================
    // REGISTRAR CASETA / COMEDOR / AMBULANTE
    // ========================================================

    [RelayCommand]
    private async Task GuardarUnidadAsync()
    {
        if (EstaOcupado)
            return;

        var codigo =
            CodigoUnidad.Trim();

        if (string.IsNullOrWhiteSpace(codigo) ||
            TipoSeleccionado is null)
        {
            Mensaje =
                "Indica el código y el tipo de unidad.";

            return;
        }

        if (codigo.Length > 50)
        {
            Mensaje =
                "El código de la unidad es demasiado largo.";

            return;
        }

        if (TipoSeleccionado.Valor == 1 &&
            PasilloSeleccionado is null)
        {
            Mensaje =
                "Selecciona un pasillo para la caseta.";

            return;
        }

        var codigoDuplicado =
            Unidades.Any(
                u =>
                    u.Codigo.Equals(
                        codigo,
                        StringComparison.OrdinalIgnoreCase));

        if (codigoDuplicado)
        {
            Mensaje =
                $"Ya existe una unidad con el código {codigo}.";

            return;
        }

        EstaOcupado = true;

        Mensaje =
            "Registrando unidad comercial...";

        try
        {
            var bloqueLimpio =
                string.IsNullOrWhiteSpace(Bloque)
                    ? null
                    : Bloque.Trim();

            var solicitud =
                new CrearUnidadRequest(
                    codigo,
                    TipoSeleccionado.Valor,
                    PasilloSeleccionado?.Id,
                    bloqueLimpio);

            var nueva =
                await _api.PostAsync<
                    CrearUnidadRequest,
                    UnidadComercialItem>(
                        "/api/unidades-comerciales",
                        solicitud);

            if (nueva is null)
            {
                throw new InvalidOperationException(
                    "El servidor no devolvió los datos de la unidad.");
            }

            nueva.PasilloNombre =
                PasilloSeleccionado?.Nombre
                ?? "Sin pasillo";

            Unidades.Insert(
                0,
                nueva);

            CodigoUnidad = "";
            Bloque = "";

            Mensaje =
                $"Unidad {nueva.Codigo} registrada correctamente.";
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode ==
                  HttpStatusCode.Conflict)
        {
            Mensaje =
                "Ya existe una unidad comercial con ese código.";
        }
        catch (Exception ex)
        {
            Mensaje =
                $"Error al registrar la unidad: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    // ========================================================
    // GUARDAR PASILLO / BLOQUE DE UNA UNIDAD EXISTENTE
    // ========================================================

    [RelayCommand]
    private async Task GuardarUbicacionAsync()
    {
        if (EstaOcupado)
            return;

        if (UnidadSeleccionada is null)
        {
            Mensaje =
                "Selecciona una unidad comercial en la tabla.";

            return;
        }

        if (UnidadSeleccionada.Tipo == 1 &&
            PasilloEdicionSeleccionado is null)
        {
            Mensaje =
                "Selecciona un pasillo para la caseta.";

            return;
        }

        var bloqueLimpio =
            string.IsNullOrWhiteSpace(BloqueEdicion)
                ? null
                : BloqueEdicion.Trim();

        if (bloqueLimpio?.Length > 50)
        {
            Mensaje =
                "El bloque no puede superar los 50 caracteres.";

            return;
        }

        EstaOcupado = true;

        Mensaje =
            $"Actualizando ubicación de la unidad " +
            $"{UnidadSeleccionada.Codigo}...";

        try
        {
            var unidad =
                UnidadSeleccionada;

            var solicitud =
                new ActualizarUbicacionUnidadRequest(
                    PasilloEdicionSeleccionado?.Id,
                    bloqueLimpio);

            await _api.PutAsync(
                $"/api/unidades-comerciales/{unidad.Id}/ubicacion",
                solicitud);

            unidad.PasilloId =
                PasilloEdicionSeleccionado?.Id;

            unidad.PasilloNombre =
                PasilloEdicionSeleccionado?.Nombre
                ?? "Sin pasillo";

            unidad.Bloque =
                bloqueLimpio;

            // Forzar a que el DataGrid refresque visualmente
            // la fila modificada.

            var indice =
                Unidades.IndexOf(unidad);

            if (indice >= 0)
            {
                Unidades.RemoveAt(indice);
                Unidades.Insert(indice, unidad);
            }

            UnidadSeleccionada =
                unidad;

            Mensaje =
                $"Ubicación de la unidad {unidad.Codigo} " +
                $"actualizada correctamente.";
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode ==
                  HttpStatusCode.BadRequest)
        {
            Mensaje =
                "No fue posible actualizar la ubicación. " +
                "Verifica que el pasillo seleccionado sea válido.";
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode ==
                  HttpStatusCode.NotFound)
        {
            Mensaje =
                "La unidad comercial no fue encontrada.";
        }
        catch (Exception ex)
        {
            Mensaje =
                $"Error al actualizar la ubicación: {ex.Message}";
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    // ========================================================
    // LIMPIAR FORMULARIOS
    // ========================================================

    [RelayCommand]
    private void LimpiarUnidad()
    {
        CodigoUnidad = "";
        Bloque = "";

        TipoSeleccionado =
            Tipos.FirstOrDefault();

        PasilloSeleccionado =
            Pasillos.FirstOrDefault(
                p => p.Activo);

        Mensaje =
            "Formulario de unidad limpio.";
    }

    [RelayCommand]
    private void LimpiarUbicacion()
    {
        UnidadSeleccionada = null;
        PasilloEdicionSeleccionado = null;
        BloqueEdicion = "";

        Mensaje =
            "Selección de ubicación limpiada.";
    }

    // ========================================================
    // AYUDANTES
    // ========================================================

    private void ReordenarPasillos()
    {
        var ordenados =
            Pasillos
                .OrderBy(p => p.Codigo)
                .ThenBy(p => p.Nombre)
                .ToList();

        Pasillos.Clear();

        foreach (var pasillo in ordenados)
        {
            Pasillos.Add(pasillo);
        }
    }
}