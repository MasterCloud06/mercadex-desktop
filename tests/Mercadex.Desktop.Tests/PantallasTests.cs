using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace Mercadex.Desktop.Tests;

/// <summary>
/// Prueba de humo de la interfaz: construye cada pantalla (UserControl)
/// y la ventana principal con los estilos de la aplicación cargados.
/// Detecta errores de XAML, recursos inexistentes y plantillas rotas
/// que el compilador no ve. No se conecta a ninguna API.
/// </summary>
public sealed class PantallasTests
{
    private static readonly Assembly Ensamblado = typeof(Mercadex.Desktop.App.App).Assembly;

    public static IEnumerable<object[]> Pantallas() =>
        Ensamblado.GetTypes()
            .Where(t => typeof(UserControl).IsAssignableFrom(t) &&
                        !t.IsAbstract &&
                        t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.Name)
            .Select(t => new object[] { t.FullName! });

    [Theory]
    [MemberData(nameof(Pantallas))]
    public void La_pantalla_se_construye(string tipo)
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var control = (UserControl)Activator.CreateInstance(Ensamblado.GetType(tipo)!)!;
            MedirYOrganizar(control);
        });
    }

    [Fact]
    public void La_ventana_principal_se_construye()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var tipo = Ensamblado.GetType("Mercadex.Desktop.App.MainWindow")!;
            var ventana = (Window)Activator.CreateInstance(tipo)!;
            MedirYOrganizar((FrameworkElement)ventana.Content);
            ventana.Close();
        });
    }

    [Fact]
    public void La_ventana_de_actualizacion_se_construye()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var datos = new Mercadex.Desktop.App.Updates.DatosVersion
            {
                SchemaVersion = 1,
                LatestVersion = "9.9.9",
                MinimumRequiredVersion = "9.9.9",
                ReleaseNotes = "Mercadex 9.9.9 - Prueba de la ventana.",
                InstallerUrl = "https://github.com/MasterCloud06/mercadex-updates/releases/download/v9.9.9/x.exe",
                Sha256 = new string('a', 64)
            };
            var ventana = new Mercadex.Desktop.App.Updates.VentanaActualizacionObligatoria(datos);
            MedirYOrganizar((FrameworkElement)ventana.Content);
            ventana.Close();
        });
    }

    [Fact]
    public void Las_ventanas_de_sesion_se_construyen()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();

            foreach (var ventana in new Window[]
                     {
                         new Mercadex.Desktop.App.Sesion.VentanaIngreso(),
                         new Mercadex.Desktop.App.Sesion.VentanaIngreso("maria.lopez", "Se bloqueó por inactividad."),
                         new Mercadex.Desktop.App.Sesion.VentanaCambiarContrasena(obligatorio: true),
                         new Mercadex.Desktop.App.Sesion.VentanaCambiarContrasena()
                     })
            {
                MedirYOrganizar((FrameworkElement)ventana.Content);
                ventana.Close();
            }
        });
    }

    [Fact]
    public void Los_estilos_globales_cargan()
    {
        EjecutarEnSta(() =>
        {
            AsegurarAplicacion();
            var recursos = Application.Current!.Resources;
            foreach (var clave in new[] { "Mx.PrimaryButton", "Mx.SecondaryButton", "Mx.DangerButton",
                                          "Mx.TextBox", "Mx.DataGrid", "Mx.Card", "Mx.NavRadio", "Mx.NavLink" })
            {
                Assert.NotNull(recursos[clave]);
            }
        });
    }

    private static void MedirYOrganizar(FrameworkElement elemento)
    {
        elemento.Measure(new Size(1280, 800));
        elemento.Arrange(new Rect(0, 0, 1280, 800));
        elemento.UpdateLayout();
    }

    private static void AsegurarAplicacion()
    {
        // Las vistas no deben conectarse: la URL apunta a una API local inexistente.
        Environment.SetEnvironmentVariable("MERCADEX_API_URL", "http://127.0.0.1:59999/");

        if (Application.Current != null)
            return;

        var app = new Mercadex.Desktop.App.App();
        var inicializar = typeof(Mercadex.Desktop.App.App).GetMethod("InitializeComponent");
        inicializar!.Invoke(app, null);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    // WPF exige un hilo STA, y todos los objetos de la aplicación deben
    // usarse desde el mismo hilo: las pruebas se encolan en uno solo.
    // No se procesa la cola del Dispatcher, así que las cargas asíncronas
    // de datos nunca continúan (no se muestran mensajes ni se llama a la API).
    private static readonly System.Collections.Concurrent.BlockingCollection<(Action Accion, TaskCompletionSource Fin)> Cola = Iniciar();

    private static System.Collections.Concurrent.BlockingCollection<(Action, TaskCompletionSource)> Iniciar()
    {
        var cola = new System.Collections.Concurrent.BlockingCollection<(Action, TaskCompletionSource)>();
        var hilo = new Thread(() =>
        {
            foreach (var (accion, fin) in cola.GetConsumingEnumerable())
            {
                try { accion(); fin.SetResult(); }
                catch (Exception ex) { fin.SetException(ex); }
            }
        })
        { IsBackground = true, Name = "Pruebas WPF" };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        return cola;
    }

    private static void EjecutarEnSta(Action accion)
    {
        var fin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Cola.Add((accion, fin));
        if (!fin.Task.Wait(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("La pantalla no terminó de construirse en 60 s (¿un mensaje modal?).");
        fin.Task.GetAwaiter().GetResult();
    }
}
