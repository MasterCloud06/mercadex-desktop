using System.Windows;
using Mercadex.Desktop.App.Api;
using System.Windows.Input;
using System.Windows.Threading;
using Mercadex.Desktop.App.Sesion;
using Mercadex.Desktop.App.Updates;

namespace Mercadex.Desktop.App;

public partial class App : Application
{
    public static ApiClient ApiClient { get; private set; } = null!;

    public App()
    {
        ApiClient = new ApiClient();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;


        while (true)
        {
            DatosVersion datos;

            try
            {
                datos = await ActualizadorObligatorio.ConsultarAsync();
            }
            catch (Exception ex)
            {
                var respuesta = MessageBox.Show(
                    "No se pudo comprobar la version de Mercadex." +
                    "\n\n" + ex.Message +
                    "\n\nSe requiere conexion a Internet." +
                    "\n\n¿Deseas reintentar?\n\n" +
                    "Si seleccionas No, Mercadex se cerrara.",
                    "Verificacion de actualizaciones",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (respuesta == MessageBoxResult.Yes)
                    continue;

                Shutdown();
                return;
            }

            if (ActualizadorObligatorio.RequiereActualizacion(datos))
            {
                var ventanaActualizacion =
                    new VentanaActualizacionObligatoria(datos);

                ventanaActualizacion.ShowDialog();

                Shutdown();
                return;
            }

            // Inicio de sesión: sin usuario no se abre Mercadex.
            if (new Sesion.VentanaIngreso().ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            AbrirVentanaPrincipal();
            IniciarControlDeSesion();
            return;
        }
    }

    // ---------------- Sesión ----------------

    /// <summary>Minutos sin usar el teclado ni el mouse antes de bloquear.</summary>
    private static readonly TimeSpan TiempoInactividad = TimeSpan.FromMinutes(15);

    private DateTime _ultimaActividadUtc = DateTime.UtcNow;
    private DispatcherTimer? _relojSesion;
    private bool _pidiendoSesion;

    private void AbrirVentanaPrincipal()
    {
        var ventanaPrincipal = new MainWindow();

        MainWindow = ventanaPrincipal;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        ventanaPrincipal.Show();
    }

    private void IniciarControlDeSesion()
    {
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is KeyboardEventArgs or MouseButtonEventArgs or MouseWheelEventArgs)
                _ultimaActividadUtc = DateTime.UtcNow;
        };

        SesionActual.SesionVencida += () =>
            PedirSesion("Tu sesión terminó o fue cerrada por el administrador. Vuelve a ingresar.");

        _relojSesion = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _relojSesion.Tick += (_, _) =>
        {
            if (!SesionActual.Iniciada || _pidiendoSesion)
                return;

            if (DateTime.UtcNow - _ultimaActividadUtc > TiempoInactividad)
                PedirSesion("Se bloqueó por inactividad. Escribe tu contraseña para continuar.");
            else if (DateTime.UtcNow >= SesionActual.ExpiraUtc.AddMinutes(-1))
                PedirSesion("Tu turno terminó. Vuelve a ingresar para continuar.");
        };
        _relojSesion.Start();
    }

    /// <summary>Cierra la sesión y pide ingresar con cualquier usuario.</summary>
    public void CerrarSesion()
    {
        SesionActual.Cerrar();
        PedirSesion(null);
    }

    /// <summary>
    /// Oculta Mercadex y pide la sesión. Si vuelve a ingresar el mismo usuario,
    /// la pantalla queda como estaba; con otro usuario se abre de nuevo.
    /// </summary>
    private void PedirSesion(string? motivo)
    {
        if (_pidiendoSesion)
            return;

        _pidiendoSesion = true;

        try
        {
            var anterior = MainWindow;
            var usuarioAnterior = SesionActual.Usuario;

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            anterior?.Hide();

            var ingreso = motivo is null
                ? new VentanaIngreso()
                : new VentanaIngreso(usuarioAnterior, motivo);

            if (ingreso.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            _ultimaActividadUtc = DateTime.UtcNow;

            if (anterior is not null && motivo is not null &&
                string.Equals(SesionActual.Usuario, usuarioAnterior, StringComparison.OrdinalIgnoreCase))
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                anterior.Show();
                anterior.Activate();
                return;
            }

            AbrirVentanaPrincipal();
            anterior?.Close();
        }
        finally
        {
            _pidiendoSesion = false;
        }
    }
}
