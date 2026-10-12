using System.Collections.ObjectModel;
using System.Diagnostics;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Mercadex.Desktop.App.Models;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.ViewModels;

public partial class SociosViewModel : ObservableObject
{
    private readonly SociosService _sociosService = new();

    private readonly List<SocioModel> _todosLosSocios = new();

    public ObservableCollection<SocioModel> Socios { get; }
        = new();

    [ObservableProperty]
    private string nombres = string.Empty;

    [ObservableProperty]
    private string apellidos = string.Empty;

    [ObservableProperty]
    private string documentoIdentidad = string.Empty;

    [ObservableProperty]
    private string telefono = string.Empty;

    [ObservableProperty]
    private string textoBusqueda = string.Empty;

    [ObservableProperty]
    private bool soloActivos;

    [ObservableProperty]
    private string mensaje = string.Empty;

    [ObservableProperty]
    private string resumen = "0 socios";

    [ObservableProperty]
    private bool estaOcupado;

    partial void OnTextoBusquedaChanged(string value)
    {
        AplicarFiltro();
    }

    partial void OnSoloActivosChanged(bool value)
    {
        AplicarFiltro();
    }

    [RelayCommand]
    public async Task ConsultarAsync()
    {
        if (EstaOcupado)
            return;

        EstaOcupado = true;
        Mensaje = "Consultando socios...";

        var reloj = Stopwatch.StartNew();

        try
        {
            var resultado =
                await _sociosService.ObtenerAsync();

            _todosLosSocios.Clear();

            _todosLosSocios.AddRange(
                resultado
                    .OrderByDescending(x => x.FechaRegistro));

            AplicarFiltro();

            reloj.Stop();

            Mensaje =
                $"Información actualizada correctamente. " +
                $"{reloj.Elapsed.TotalSeconds:F2} s.";
        }
        catch (Exception ex)
        {
            reloj.Stop();

            Mensaje =
                "No se pudieron consultar los socios. " +
                ex.Message;
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        if (EstaOcupado)
            return;

        var nombresLimpios = Nombres.Trim();
        var apellidosLimpios = Apellidos.Trim();
        var documentoLimpio = DocumentoIdentidad.Trim();
        var telefonoLimpio = Telefono.Trim();

        if (nombresLimpios.Length < 2)
        {
            Mensaje =
                "Introduce los nombres del socio.";

            return;
        }

        if (apellidosLimpios.Length < 2)
        {
            Mensaje =
                "Introduce los apellidos del socio.";

            return;
        }

        if (documentoLimpio.Length < 4)
        {
            Mensaje =
                "Introduce un documento de identidad válido.";

            return;
        }

        if (telefonoLimpio.Length > 30)
        {
            Mensaje =
                "El teléfono es demasiado largo.";

            return;
        }

        var documentoDuplicado =
            _todosLosSocios.Any(x =>
                string.Equals(
                    x.DocumentoIdentidad?.Trim(),
                    documentoLimpio,
                    StringComparison.OrdinalIgnoreCase));

        if (documentoDuplicado)
        {
            Mensaje =
                $"Ya existe un socio con el documento {documentoLimpio}.";

            return;
        }

        EstaOcupado = true;
        Mensaje = "Guardando socio...";

        try
        {
            var solicitud =
                new CrearSocioRequest(
                    nombresLimpios,
                    apellidosLimpios,
                    documentoLimpio,
                    string.IsNullOrWhiteSpace(telefonoLimpio)
                        ? null
                        : telefonoLimpio);

            var socio =
                await _sociosService.CrearAsync(
                    solicitud);

            _todosLosSocios.Insert(0, socio);

            LimpiarFormulario();

            AplicarFiltro();

            Mensaje =
                $"Socio registrado correctamente: " +
                $"{socio.NombreCompleto}.";
        }
        catch (Exception ex)
        {
            Mensaje =
                "No se pudo registrar el socio. " +
                ex.Message;
        }
        finally
        {
            EstaOcupado = false;
        }
    }

    [RelayCommand]
    private void Limpiar()
    {
        LimpiarFormulario();

        Mensaje =
            "Formulario limpio.";
    }

    [RelayCommand]
    private void LimpiarBusqueda()
    {
        TextoBusqueda = string.Empty;
        SoloActivos = false;

        AplicarFiltro();

        Mensaje =
            "Filtros eliminados.";
    }

    private void LimpiarFormulario()
    {
        Nombres = string.Empty;
        Apellidos = string.Empty;
        DocumentoIdentidad = string.Empty;
        Telefono = string.Empty;
    }

    private void AplicarFiltro()
    {
        IEnumerable<SocioModel> consulta =
            _todosLosSocios;

        if (SoloActivos)
        {
            consulta =
                consulta.Where(x => x.Activo);
        }

        var buscar =
            TextoBusqueda.Trim();

        if (!string.IsNullOrWhiteSpace(buscar))
        {
            consulta =
                consulta.Where(x =>
                    Contiene(x.NombreCompleto, buscar) ||
                    Contiene(x.DocumentoIdentidad, buscar) ||
                    Contiene(x.Telefono, buscar));
        }

        var resultado =
            consulta
                .OrderBy(x => x.NombreCompleto)
                .ToList();

        Socios.Clear();

        foreach (var socio in resultado)
        {
            Socios.Add(socio);
        }

        Resumen =
            $"{Socios.Count} mostrado(s) de " +
            $"{_todosLosSocios.Count} socio(s)";
    }

    private static bool Contiene(
        string? texto,
        string buscar)
    {
        return !string.IsNullOrWhiteSpace(texto) &&
               texto.Contains(
                   buscar,
                   StringComparison.OrdinalIgnoreCase);
    }
}