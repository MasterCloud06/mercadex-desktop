using System.Windows;
using System.Windows.Controls;
using Mercadex.Desktop.App.Services;

namespace Mercadex.Desktop.App.Views.Reportes;

/// <summary>
/// «Listado de personas» del sistema anterior: un socio por fila, ordenados
/// por su primer local, con apellidos y nombres y todos sus locales.
/// </summary>
public sealed class ListadoSociosReporteView : ReporteTablaView
{
    private readonly CheckBox _sinLocal = new()
    {
        Content = "Incluir socios sin local",
        IsChecked = false,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 14, 16)
    };

    public ListadoSociosReporteView()
        : base("Listado de socios",
            "Los socios activos ordenados por su local, con apellidos, nombres y todos sus locales, como el listado anterior.")
    {
        Filtros.Children.Add(_sinLocal);
        TextoGenerar = "Generar listado";
    }

    protected override async Task<Reporte?> ConstruirAsync()
    {
        // El padrón responde { total, activos, …, socios: [...] }.
        var socios = (await ApiJson.GetAsync<PadronRespuesta>("/api/socios/padron?estado=activos"))?.Socios ?? new();
        var filas = Filas(socios, _sinLocal.IsChecked == true);
        var conLocal = filas.Count(f => f.Celdas[0].Length > 0);

        return new Reporte
        {
            Titulo = "Listado de socios",
            Filtros = "Socios activos" + (_sinLocal.IsChecked == true ? ", incluidos los que no tienen local" : ""),
            Resumen = new() { $"Total: {filas.Count} socios ({conLocal} con local)" },
            PieSoloPagina = true,
            NombreArchivo = "Listado_de_socios",
            Columnas = new()
            {
                new("Local", 0.6),
                new("Apellido(s) y Nombre(s)", 3.2),
                new("Locales", 2.2)
            },
            Filas = filas
        };
    }

    /// <summary>Orden de los locales: primero los números (1, 2, 10…), luego los demás.</summary>
    public static List<string> OrdenLocales(IEnumerable<string> locales) =>
        locales
            .Select(l => (l ?? "").Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => int.TryParse(l, out _) ? 0 : 1)
            .ThenBy(l => int.TryParse(l, out var n) ? n : 0)
            .ThenBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Una fila por socio: su primer local, «APELLIDOS NOMBRES» y todos sus
    /// locales. Ordenado por el primer local; los que no tienen local, al final.
    /// </summary>
    public static List<FilaReporte> Filas(IEnumerable<SocioPadron> socios, bool incluirSinLocal)
    {
        var lista = socios
            .Where(s => s.Activo)
            .Select(s => (Locales: OrdenLocales(s.Casetas), Nombre: $"{s.Apellidos} {s.Nombres}".Trim().ToUpper(Bo)))
            .Where(s => incluirSinLocal || s.Locales.Count > 0)
            .ToList();

        var orden = OrdenLocales(lista.Where(s => s.Locales.Count > 0).Select(s => s.Locales[0]));

        return lista
            .OrderBy(s => s.Locales.Count == 0 ? 1 : 0)
            .ThenBy(s => s.Locales.Count == 0 ? 0 : orden.IndexOf(s.Locales[0]))
            .ThenBy(s => s.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(s => new FilaReporte(new[]
            {
                s.Locales.FirstOrDefault() ?? "",
                s.Nombre,
                string.Join("  ", s.Locales)
            }))
            .ToList();
    }

    /// <summary>Respuesta de GET /api/socios/padron (solo lo que usa el listado).</summary>
    public sealed class PadronRespuesta
    {
        public List<SocioPadron> Socios { get; set; } = new();
    }

    /// <summary>Un socio del padrón.</summary>
    public sealed class SocioPadron
    {
        public string Nombres { get; set; } = "";
        public string Apellidos { get; set; } = "";
        public bool Activo { get; set; }
        public List<string> Casetas { get; set; } = new();
    }
}
