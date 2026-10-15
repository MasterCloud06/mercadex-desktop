using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Sesion;

namespace Mercadex.Desktop.App.Views;

/// <summary>
/// Usuarios de Mercadex (solo administradores): crear, cambiar el rol,
/// restablecer la contraseña y desactivar.
/// </summary>
public sealed class UsuariosView : UserControl
{
    private static readonly HttpClient Http = ApiConfiguration.CrearCliente(TimeSpan.FromSeconds(75));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Bo = CultureInfo.GetCultureInfo("es-BO");

    private static readonly (string Valor, string Texto)[] Roles =
    {
        ("Cajero", "Cajero — cobra, registra egresos y cierra caja"),
        ("Consulta", "Solo consulta — ve reportes y listas"),
        ("Administrador", "Administrador — todo, incluso usuarios y anulaciones")
    };

    private readonly TextBox _nombre;
    private readonly TextBox _usuario;
    private readonly ComboBox _rol;
    private readonly TextBox _cargo;
    private readonly Button _crear;
    private readonly DataGrid _tabla;
    private readonly TextBlock _estado;
    private readonly Button _cambiarRol;
    private readonly Button _restablecer;
    private readonly Button _activar;

    public UsuariosView()
    {
        Background = ColoresPanel.Hex("#F3F6FA");
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");
        FontSize = 14;

        var raiz = new DockPanel { Margin = new Thickness(26, 22, 26, 22) };

        // ---------- Encabezado ----------

        var arriba = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(arriba, Dock.Top);
        arriba.Children.Add(new TextBlock
        {
            Text = "Usuarios",
            FontSize = 27,
            FontWeight = FontWeights.Bold,
            Foreground = ColoresPanel.Hex("#173B5C")
        });
        arriba.Children.Add(new TextBlock
        {
            Text = "Quién puede ingresar a Mercadex y qué puede hacer. Cada cobro, anulación y cierre queda a nombre de quien lo hizo.",
            Foreground = ColoresPanel.TextoSecundario,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        raiz.Children.Add(arriba);

        // ---------- Nuevo usuario ----------

        var tarjeta = new Border
        {
            Style = (Style)Application.Current.FindResource("Mx.Card"),
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(tarjeta, Dock.Top);

        var formulario = new StackPanel();
        formulario.Children.Add(new TextBlock
        {
            Text = "Nuevo usuario",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColoresPanel.Hex("#173B5C"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        var fila = new WrapPanel();
        fila.Children.Add(Campo("Nombre completo", _nombre = Texto(240)));
        fila.Children.Add(Campo("Usuario para ingresar", _usuario = Texto(180)));

        _rol = new ComboBox { Width = 330, Height = 34, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var (valor, texto) in Roles)
            _rol.Items.Add(new ComboBoxItem { Content = texto, Tag = valor });
        _rol.SelectedIndex = 0;
        fila.Children.Add(Campo("Rol", _rol));
        fila.Children.Add(Campo("Cargo (sale en los recibos)", _cargo = Texto(200)));
        _cargo.MaxLength = 80;
        _cargo.ToolTip = "Por ejemplo: Cajera, Tesorero, Presidente. Si queda vacío, el recibo muestra el rol.";

        _crear = new Button
        {
            Content = "Crear usuario",
            Height = 36,
            Padding = new Thickness(18, 0, 18, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 6),
            Style = (Style)Application.Current.FindResource("Mx.PrimaryButton")
        };
        _crear.Click += async (_, _) => await CrearAsync();
        fila.Children.Add(_crear);

        formulario.Children.Add(fila);
        formulario.Children.Add(new TextBlock
        {
            Text = "El usuario solo puede tener letras sin tilde, números y punto (por ejemplo: maria.lopez). " +
                   "Mercadex genera una contraseña temporal; la persona elige la suya al ingresar.",
            FontSize = 12.5,
            Foreground = ColoresPanel.TextoTenue,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        tarjeta.Child = formulario;
        raiz.Children.Add(tarjeta);

        // ---------- Acciones sobre el seleccionado ----------

        var acciones = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(acciones, Dock.Bottom);

        var botones = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(botones, Dock.Right);

        _cambiarRol = Secundario("Cambiar nombre, rol o cargo");
        _cambiarRol.Click += async (_, _) => await CambiarRolAsync();
        _restablecer = Secundario("Restablecer contraseña");
        _restablecer.Click += async (_, _) => await RestablecerAsync();
        _activar = Secundario("Desactivar");
        _activar.Click += async (_, _) => await ActivarODesactivarAsync();

        botones.Children.Add(_cambiarRol);
        botones.Children.Add(_restablecer);
        botones.Children.Add(_activar);
        acciones.Children.Add(botones);

        _estado = new TextBlock
        {
            Foreground = ColoresPanel.TextoSecundario,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        acciones.Children.Add(_estado);
        raiz.Children.Add(acciones);

        // ---------- Tabla ----------

        _tabla = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserAddRows = false,
            Style = (Style)Application.Current.FindResource("Mx.DataGrid")
        };
        _tabla.Columns.Add(Columna("Nombre", nameof(FilaUsuario.NombreCompleto), 2));
        _tabla.Columns.Add(Columna("Usuario", nameof(FilaUsuario.NombreUsuario), 1.2));
        _tabla.Columns.Add(Columna("Rol", nameof(FilaUsuario.RolTexto), 1.1));
        _tabla.Columns.Add(Columna("Cargo en recibos", nameof(FilaUsuario.CargoTexto), 1.3));
        _tabla.Columns.Add(Columna("Estado", nameof(FilaUsuario.EstadoTexto), 1.4));
        _tabla.Columns.Add(Columna("Último ingreso", nameof(FilaUsuario.UltimoAccesoTexto), 1.2));
        _tabla.SelectionChanged += (_, _) => ActualizarAcciones();
        raiz.Children.Add(_tabla);

        Content = raiz;
        ActualizarAcciones();
        Loaded += async (_, _) => await CargarAsync();
    }

    // ---------------- Operaciones ----------------

    private async Task CargarAsync(Guid? seleccionar = null)
    {
        try
        {
            var lista = await EnviarAsync<List<FilaUsuario>>(HttpMethod.Get, "api/usuarios") ?? new();
            _tabla.ItemsSource = lista;

            if (seleccionar.HasValue)
                _tabla.SelectedItem = lista.FirstOrDefault(u => u.Id == seleccionar.Value);

            _estado.Text = lista.Count == 0
                ? "Todavía no hay usuarios. Crea uno por cada persona que usa Mercadex."
                : "Selecciona un usuario para cambiar su rol, restablecer su contraseña o desactivarlo.";
        }
        catch (Exception ex)
        {
            _estado.Text = ex.Message;
        }
    }

    private async Task CrearAsync()
    {
        var nombre = _nombre.Text.Trim();
        var usuario = _usuario.Text.Trim().ToLowerInvariant();
        var rol = (string)((ComboBoxItem)_rol.SelectedItem).Tag;
        var cargo = _cargo.Text.Trim();

        if (nombre.Length < 3 || usuario.Length < 3)
        {
            _estado.Text = "Escribe el nombre completo y el usuario (mínimo 3 caracteres).";
            return;
        }

        _crear.IsEnabled = false;

        try
        {
            var r = await EnviarAsync<ContrasenaEntregada>(HttpMethod.Post, "api/usuarios",
                new { nombreUsuario = usuario, nombreCompleto = nombre, rol, cargo });

            _nombre.Clear();
            _cargo.Clear();
            _usuario.Clear();
            await CargarAsync(r?.Id);

            if (r is not null)
                new VentanaContrasenaTemporal(r.NombreCompleto, r.NombreUsuario, r.ContrasenaTemporal, nuevo: true)
                    { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception ex)
        {
            _estado.Text = ex.Message;
        }
        finally
        {
            _crear.IsEnabled = true;
        }
    }

    private async Task CambiarRolAsync()
    {
        if (_tabla.SelectedItem is not FilaUsuario u)
            return;

        var ventana = new VentanaEditarUsuario(u.NombreCompleto, u.Rol, u.Cargo, Roles) { Owner = Window.GetWindow(this) };
        if (ventana.ShowDialog() != true)
            return;

        try
        {
            await EnviarAsync<object>(HttpMethod.Put, $"api/usuarios/{u.Id}",
                new { nombreCompleto = ventana.NombreCompleto, rol = ventana.Rol, cargo = ventana.Cargo });
            await CargarAsync(u.Id);
            _estado.Text = $"Se actualizó a {ventana.NombreCompleto}. Si estaba trabajando, debe volver a ingresar.";
        }
        catch (Exception ex)
        {
            _estado.Text = ex.Message;
        }
    }

    private async Task RestablecerAsync()
    {
        if (_tabla.SelectedItem is not FilaUsuario u)
            return;

        var respuesta = Dialogo.Mostrar(Window.GetWindow(this),
            $"¿Restablecer la contraseña de {u.NombreCompleto}?\n\n" +
            "Su contraseña actual deja de funcionar y se cierran sus sesiones. " +
            "Recibirá una contraseña temporal y elegirá una nueva al ingresar.",
            "Restablecer contraseña", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (respuesta != MessageBoxResult.Yes)
            return;

        try
        {
            var r = await EnviarAsync<ContrasenaEntregada>(HttpMethod.Post, $"api/usuarios/{u.Id}/restablecer");
            await CargarAsync(u.Id);

            if (r is not null)
                new VentanaContrasenaTemporal(r.NombreCompleto, r.NombreUsuario, r.ContrasenaTemporal, nuevo: false)
                    { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        catch (Exception ex)
        {
            _estado.Text = ex.Message;
        }
    }

    private async Task ActivarODesactivarAsync()
    {
        if (_tabla.SelectedItem is not FilaUsuario u)
            return;

        if (u.Activo)
        {
            var respuesta = Dialogo.Mostrar(Window.GetWindow(this),
                $"¿Desactivar a {u.NombreCompleto}?\n\nYa no podrá ingresar y se cerrarán sus sesiones. " +
                "Sus cobros y registros anteriores se conservan.",
                "Desactivar usuario", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (respuesta != MessageBoxResult.Yes)
                return;
        }

        try
        {
            var r = await EnviarAsync<Mensaje>(HttpMethod.Post, $"api/usuarios/{u.Id}/{(u.Activo ? "desactivar" : "activar")}");
            await CargarAsync(u.Id);
            _estado.Text = r?.Texto ?? "";
        }
        catch (Exception ex)
        {
            _estado.Text = ex.Message;
        }
    }

    private void ActualizarAcciones()
    {
        var u = _tabla.SelectedItem as FilaUsuario;
        var propio = u is not null && string.Equals(u.NombreUsuario, SesionActual.Usuario, StringComparison.OrdinalIgnoreCase);

        _cambiarRol.IsEnabled = u is not null;
        _restablecer.IsEnabled = u is not null && !propio;
        _activar.IsEnabled = u is not null && !propio;
        _activar.Content = u is { Activo: false } ? "Activar" : "Desactivar";
    }

    // ---------------- API ----------------

    private static async Task<T?> EnviarAsync<T>(HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        using var solicitud = new HttpRequestMessage(metodo, ruta);

        if (cuerpo is not null)
            solicitud.Content = JsonContent.Create(cuerpo);

        HttpResponseMessage respuesta;

        try
        {
            respuesta = await Http.SendAsync(solicitud);
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("El servidor tardó demasiado en responder. Vuelve a intentar.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("No hay conexión con el servidor.");
        }

        using (respuesta)
        {
            if (respuesta.IsSuccessStatusCode)
            {
                if (typeof(T) == typeof(object))
                    return default;

                return await respuesta.Content.ReadFromJsonAsync<T>(Json);
            }

            var mensaje = $"No se pudo completar (error {(int)respuesta.StatusCode}).";

            try
            {
                var error = await respuesta.Content.ReadFromJsonAsync<Mensaje>(Json);
                if (!string.IsNullOrWhiteSpace(error?.Texto))
                    mensaje = error!.Texto!;
            }
            catch
            {
                // Sin cuerpo JSON.
            }

            throw new InvalidOperationException(mensaje);
        }
    }

    // ---------------- Piezas de la interfaz ----------------

    private static StackPanel Campo(string etiqueta, Control control)
    {
        var pila = new StackPanel { Margin = new Thickness(0, 0, 16, 6) };
        pila.Children.Add(new TextBlock
        {
            Text = etiqueta,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColoresPanel.Hex("#4A5E73"),
            Margin = new Thickness(0, 0, 0, 5)
        });
        pila.Children.Add(control);
        return pila;
    }

    private static TextBox Texto(double ancho) => new()
    {
        Width = ancho,
        MaxLength = 120,
        Style = (Style)Application.Current.FindResource("Mx.TextBox")
    };

    private static Button Secundario(string texto) => new()
    {
        Content = texto,
        Height = 36,
        Padding = new Thickness(14, 0, 14, 0),
        Margin = new Thickness(10, 0, 0, 0),
        Style = (Style)Application.Current.FindResource("Mx.SecondaryButton")
    };

    private static DataGridTextColumn Columna(string titulo, string propiedad, double peso) => new()
    {
        Header = titulo,
        Binding = new Binding(propiedad),
        Width = new DataGridLength(peso, DataGridLengthUnitType.Star)
    };

    // ---------------- Modelos ----------------

    private sealed class FilaUsuario
    {
        public Guid Id { get; set; }
        public string NombreUsuario { get; set; } = "";
        public string NombreCompleto { get; set; } = "";
        public string Rol { get; set; } = "";
        public bool Activo { get; set; }
        public bool Bloqueado { get; set; }
        public bool DebeCambiarContrasena { get; set; }
        public DateTimeOffset? UltimoAcceso { get; set; }

        public string? Cargo { get; set; }
        public string RolTexto => Rol == "Consulta" ? "Solo consulta" : Rol;
        public string CargoTexto => string.IsNullOrWhiteSpace(Cargo) ? Rol + " (rol)" : Cargo!;

        public string EstadoTexto =>
            !Activo ? "Desactivado" :
            Bloqueado ? "Bloqueado por intentos" :
            DebeCambiarContrasena ? "Con contraseña temporal" :
            "Activo";

        public string UltimoAccesoTexto =>
            UltimoAcceso?.ToOffset(TimeSpan.FromHours(-4)).ToString("dd/MM/yyyy HH:mm", Bo) ?? "Nunca";
    }

    private sealed class ContrasenaEntregada
    {
        public Guid Id { get; set; }
        public string NombreUsuario { get; set; } = "";
        public string NombreCompleto { get; set; } = "";
        public string ContrasenaTemporal { get; set; } = "";
    }

    private sealed class Mensaje
    {
        [System.Text.Json.Serialization.JsonPropertyName("mensaje")]
        public string? Texto { get; set; }
    }
}

/// <summary>Muestra la contraseña temporal una sola vez.</summary>
internal sealed class VentanaContrasenaTemporal : Window
{
    public VentanaContrasenaTemporal(string nombre, string usuario, string contrasena, bool nuevo)
    {
        Title = nuevo ? "Usuario creado" : "Contraseña restablecida";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");

        var pila = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };

        pila.Children.Add(new TextBlock
        {
            Text = nuevo ? $"Se creó el usuario de {nombre}" : $"Contraseña temporal de {nombre}",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = ColoresPanel.Hex("#12304D"),
            TextWrapping = TextWrapping.Wrap
        });
        pila.Children.Add(new TextBlock
        {
            Text = "Entrégale estos datos en persona. La contraseña no se volverá a mostrar.",
            Foreground = ColoresPanel.TextoSecundario,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 16)
        });

        pila.Children.Add(Dato("Usuario", usuario));
        pila.Children.Add(Dato("Contraseña temporal", contrasena));

        pila.Children.Add(new TextBlock
        {
            Text = "Al ingresar, Mercadex le pedirá elegir su propia contraseña.",
            FontSize = 12.5,
            Foreground = ColoresPanel.TextoTenue,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 18)
        });

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var copiar = new Button
        {
            Content = "Copiar",
            Height = 38,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(0, 0, 10, 0),
            Style = (Style)Application.Current.FindResource("Mx.SecondaryButton")
        };
        copiar.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText($"Usuario: {usuario}\nContraseña temporal: {contrasena}");
                copiar.Content = "Copiado";
            }
            catch
            {
                copiar.Content = "No se pudo copiar";
            }
        };

        var listo = new Button
        {
            Content = "Listo",
            Height = 38,
            Padding = new Thickness(22, 0, 22, 0),
            IsDefault = true,
            IsCancel = true,
            Style = (Style)Application.Current.FindResource("Mx.PrimaryButton")
        };
        listo.Click += (_, _) => Close();

        botones.Children.Add(copiar);
        botones.Children.Add(listo);
        pila.Children.Add(botones);

        Content = pila;
    }

    private static Border Dato(string etiqueta, string valor)
    {
        var pila = new StackPanel();
        pila.Children.Add(new TextBlock { Text = etiqueta, FontSize = 12.5, Foreground = ColoresPanel.TextoSecundario });
        pila.Children.Add(new TextBox
        {
            Text = valor,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            Foreground = ColoresPanel.Hex("#12304D")
        });

        return new Border
        {
            Background = ColoresPanel.Hex("#EEF3F8"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = pila
        };
    }
}

/// <summary>Cambiar el nombre o el rol de un usuario.</summary>
internal sealed class VentanaEditarUsuario : Window
{
    private readonly TextBox _nombre;
    private readonly ComboBox _rol;
    private readonly TextBox _cargo;

    public string NombreCompleto => _nombre.Text.Trim();

    public string Cargo => _cargo.Text.Trim();

    public string Rol => (string)((ComboBoxItem)_rol.SelectedItem).Tag;

    public VentanaEditarUsuario(string nombre, string rol, string? cargo, IEnumerable<(string Valor, string Texto)> roles)
    {
        Title = "Cambiar nombre, rol o cargo";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        FontFamily = (FontFamily)Application.Current.FindResource("Mx.Font");

        var pila = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };

        pila.Children.Add(new TextBlock { Text = "Nombre completo", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        _nombre = new TextBox { Text = nombre, MaxLength = 120, Margin = new Thickness(0, 0, 0, 14) };
        _nombre.Style = (Style)Application.Current.FindResource("Mx.TextBox");
        pila.Children.Add(_nombre);

        pila.Children.Add(new TextBlock { Text = "Rol", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
        _rol = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var (valor, texto) in roles)
        {
            var item = new ComboBoxItem { Content = texto, Tag = valor };
            _rol.Items.Add(item);
            if (valor == rol)
                _rol.SelectedItem = item;
        }
        if (_rol.SelectedItem is null)
            _rol.SelectedIndex = 0;
        pila.Children.Add(_rol);

        pila.Children.Add(new TextBlock { Text = "Cargo (sale en los recibos)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 5) });
        _cargo = new TextBox { Text = cargo ?? "", MaxLength = 80, Margin = new Thickness(0, 0, 0, 4) };
        _cargo.Style = (Style)Application.Current.FindResource("Mx.TextBox");
        pila.Children.Add(_cargo);
        pila.Children.Add(new TextBlock
        {
            Text = "Por ejemplo: Cajera, Tesorero, Presidente. Si queda vacío, el recibo muestra el rol.",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ColoresPanel.TextoTenue,
            Margin = new Thickness(0, 0, 0, 10)
        });

        pila.Children.Add(new TextBlock
        {
            Text = "Si cambias el rol, la persona debe volver a ingresar.",
            FontSize = 12.5,
            Foreground = ColoresPanel.TextoTenue,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelar = new Button
        {
            Content = "Cancelar",
            Height = 38,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true,
            Style = (Style)Application.Current.FindResource("Mx.SecondaryButton")
        };
        var guardar = new Button
        {
            Content = "Guardar cambios",
            Height = 38,
            Padding = new Thickness(18, 0, 18, 0),
            IsDefault = true,
            Style = (Style)Application.Current.FindResource("Mx.PrimaryButton")
        };
        guardar.Click += (_, _) =>
        {
            if (NombreCompleto.Length < 3)
            {
                _nombre.Focus();
                return;
            }

            DialogResult = true;
        };

        botones.Children.Add(cancelar);
        botones.Children.Add(guardar);
        pila.Children.Add(botones);

        Content = pila;
    }
}
