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

        public bool ModoOscuro { get; set; }

        public int MinutosBloqueo { get; set; } = BloqueoPorDefecto;
    }

    /// <summary>Minutos sin usar Mercadex antes de bloquearlo (a elegir).</summary>
    public static readonly int[] OpcionesBloqueo = { 10, 15, 20, 30, 60 };

    public const int BloqueoPorDefecto = 15;

    /// <summary>Un valor fuera de las opciones (archivo editado o viejo) vuelve a 15.</summary>
    public static int NormalizarBloqueo(int minutos) =>
        Array.IndexOf(OpcionesBloqueo, minutos) >= 0 ? minutos : BloqueoPorDefecto;

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

    /// <summary>Se aplica al abrir Mercadex (ver <see cref="Tema"/>).</summary>
    public static bool ModoOscuro
    {
        get => Leer().ModoOscuro;
        set
        {
            Leer().ModoOscuro = value;
            Guardar();
        }
    }

    /// <summary>Tiempo sin teclado ni mouse antes de pedir la contraseña.</summary>
    public static int MinutosBloqueo
    {
        get => NormalizarBloqueo(Leer().MinutosBloqueo);
        set
        {
            Leer().MinutosBloqueo = NormalizarBloqueo(value);
            Guardar();
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
