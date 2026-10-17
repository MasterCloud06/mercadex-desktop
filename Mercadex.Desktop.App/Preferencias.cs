using System.IO;
using System.Text.Json;

namespace Mercadex.Desktop.App;

/// <summary>
/// Preferencias de esta computadora (por ejemplo, el menú plegado). Se
/// guardan en %LOCALAPPDATA%\Mercadex\preferencias.json; si no se pueden
/// leer o guardar, se usan los valores por defecto sin molestar.
/// </summary>
public static class Preferencias
{
    private static readonly string Ruta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mercadex", "preferencias.json");

    private sealed class Datos
    {
        public bool MenuPlegado { get; set; }
    }

    private static Datos? _datos;

    private static Datos Leer()
    {
        if (_datos is not null)
            return _datos;

        try
        {
            _datos = File.Exists(Ruta) ? JsonSerializer.Deserialize<Datos>(File.ReadAllText(Ruta)) : null;
        }
        catch
        {
            _datos = null;
        }

        return _datos ??= new Datos();
    }

    private static void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            File.WriteAllText(Ruta, JsonSerializer.Serialize(Leer()));
        }
        catch
        {
            // Sin permiso de escritura: la preferencia dura hasta cerrar el programa.
        }
    }

    public static bool MenuPlegado
    {
        get => Leer().MenuPlegado;
        set
        {
            Leer().MenuPlegado = value;
            Guardar();
        }
    }
}
