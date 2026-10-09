using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Mercadex.Desktop.App.Api;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Views;

public sealed class NuevoConceptoWindow : Window
{
    private readonly ApiClient _api = new();

    private readonly TextBox _codigo = new();
    private readonly TextBox _nombre = new();
    private readonly TextBox _descripcion = new();
    private readonly TextBox _usuario = new();
    private readonly PasswordBox _clave = new();

    private readonly TextBlock _estado = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 12),
        Foreground = Brushes.DarkSlateBlue
    };

    private readonly Button _guardar = new()
    {
        Content = "Guardar concepto",
        Padding = new Thickness(16, 8, 16, 8)
    };

    private bool _ocupado;

    public ConceptoIngresoModel? ConceptoCreado { get; private set; }

    public NuevoConceptoWindow()
    {
        Title = "Nuevo concepto de deuda";
        Width = 490;
        Height = Math.Min(680, SystemParameters.WorkArea.Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel
        {
            Margin = new Thickness(24)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Crear concepto de deuda",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        panel.Children.Add(new TextBlock
        {
            Text =
                "El concepto estará disponible para registrar " +
                "deudas en las casetas. Crearlo no genera deudas.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        AgregarCampo(panel, "Código — ejemplo: ACOMODACION", _codigo);
        AgregarCampo(panel, "Nombre — ejemplo: Acomodación", _nombre);
        AgregarCampo(panel, "Descripción opcional", _descripcion);

        panel.Children.Add(new TextBlock
        {
            Text = "Autorización de administrador",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 8)
        });

        AgregarCampo(panel, "Usuario", _usuario);

        panel.Children.Add(new TextBlock
        {
            Text = "Contraseña",
            Margin = new Thickness(0, 0, 0, 4)
        });

        _clave.Height = 34;
        _clave.Padding = new Thickness(7, 4, 7, 4);
        panel.Children.Add(_clave);

        panel.Children.Add(_estado);

        var botones = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        _guardar.Click += Guardar_Click;
        botones.Children.Add(_guardar);

        var cancelar = new Button
        {
            Content = "Cerrar",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(12, 0, 0, 0),
            IsCancel = true
        };

        cancelar.Click += (_, _) => Close();
        botones.Children.Add(cancelar);

        panel.Children.Add(botones);

        Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        Closing += (_, e) =>
        {
            if (_ocupado)
            {
                e.Cancel = true;
                _estado.Text = "Espera a que termine la operación.";
            }
        };

        Closed += (_, _) => _clave.Clear();
    }

    private static void AgregarCampo(
        StackPanel panel,
        string etiqueta,
        TextBox campo)
    {
        panel.Children.Add(new TextBlock
        {
            Text = etiqueta,
            Margin = new Thickness(0, 0, 0, 4)
        });

        campo.Height = 34;
        campo.Padding = new Thickness(7, 4, 7, 4);
        campo.Margin = new Thickness(0, 0, 0, 12);

        panel.Children.Add(campo);
    }

    private async void Guardar_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ocupado)
            return;

        var codigo = _codigo.Text.Trim().ToUpperInvariant();
        var nombre = _nombre.Text.Trim();
        var descripcion = _descripcion.Text.Trim();
        var usuario = _usuario.Text.Trim();

        if (string.IsNullOrWhiteSpace(codigo) ||
            string.IsNullOrWhiteSpace(nombre))
        {
            _estado.Text = "Introduce el código y el nombre del concepto.";
            return;
        }

        if (string.IsNullOrWhiteSpace(usuario) ||
            string.IsNullOrWhiteSpace(_clave.Password))
        {
            _estado.Text =
                "Introduce el usuario y la contraseña de administrador.";
            return;
        }

        bool envioCreacion = false;
        bool permitirReintento = true;

        try
        {
            _ocupado = true;
            _guardar.IsEnabled = false;
            _estado.Text = "Verificando administrador...";

            SesionConceptoRespuesta? sesion;

            try
            {
                sesion = await _api.PostAsync<
                    object,
                    SesionConceptoRespuesta>(
                    "/api/auth/login",
                    new
                    {
                        usuario,
                        password = _clave.Password
                    });
            }
            finally
            {
                _clave.Clear();
            }

            if (string.IsNullOrWhiteSpace(sesion?.Token))
            {
                throw new InvalidOperationException(
                    "No se recibió la autorización de acceso.");
            }

            _estado.Text = "Guardando concepto...";

            envioCreacion = true;

            var creado = await _api.PostAsync<
                object,
                ConceptoIngresoModel>(
                "/api/ingresos/catalogos/conceptos",
                new
                {
                    Codigo = codigo,
                    Nombre = nombre,
                    Descripcion = string.IsNullOrWhiteSpace(descripcion)
                        ? null
                        : descripcion
                },
                sesion.Token);

            if (creado == null || creado.Id == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "No se recibió la confirmación completa del registro.");
            }

            ConceptoCreado = creado;

            _ocupado = false;
            DialogResult = true;
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode.HasValue &&
                  (int)ex.StatusCode.Value >= 400 &&
                  (int)ex.StatusCode.Value < 500)
        {
            _estado.Text = ex.Message;
        }
        catch (Exception ex)
        {
            if (envioCreacion)
            {
                permitirReintento = false;

                _estado.Text =
                    "No se pudo confirmar el resultado. Cierra esta ventana " +
                    "y pulsa Actualizar para comprobar si el concepto " +
                    "ya existe antes de volver a crearlo.\n\n" +
                    ex.Message;
            }
            else
            {
                _estado.Text =
                    "No se pudo completar la autenticación. " +
                    ex.Message;
            }
        }
        finally
        {
            _ocupado = false;
            _guardar.IsEnabled = permitirReintento;
        }
    }
}

public sealed class SesionConceptoRespuesta
{
    public string Token { get; set; } = string.Empty;
}