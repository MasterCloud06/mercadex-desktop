using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;

using Mercadex.Desktop.App.Configuration;
using Mercadex.Desktop.App.Models;

namespace Mercadex.Desktop.App.Views;

public partial class TarifasUnidadWindow : Window
{
    private readonly HttpClient _api = new()
    {
        BaseAddress = ApiConfiguration.BaseUri,
        Timeout = TimeSpan.FromSeconds(45)
    };

    private List<MonedaIngresoModel> _monedas = new();
    private List<TarifaRespuesta> _casetas = new();
    private Guid _conceptoCargadoId = Guid.Empty;
    private bool _autorizado;
    private bool _ocupado;
    // MERCADEX_GENERACION_WPF_SEGURA
    private string? _huellaPreviaMensual;
    private bool _generandoObligaciones;
    private bool _consultandoPrevia;


    public TarifasUnidadWindow()
    {
        InitializeComponent();
        CmbConceptos.SelectionChanged += (_, _) =>
            InvalidarPreviaMensual();

        CmbMes.SelectionChanged += (_, _) =>
        {
            InvalidarPreviaMensual();
            ActualizarFechasMensuales();
        };

        CmbAnio.SelectionChanged += (_, _) =>
        {
            InvalidarPreviaMensual();
            ActualizarFechasMensuales();
        };

        BtnGuardar.Click += (_, _) => InvalidarPreviaMensual();


        Loaded += async (_, _) =>
            await ConectarAutomaticamenteAsync();

        CmbMes.ItemsSource = Enumerable.Range(1, 12).ToList();

        CmbAnio.ItemsSource = Enumerable.Range(
            DateTime.Today.Year - 2, 6).ToList();

        CmbMes.SelectedItem = DateTime.Today.Month;
        CmbAnio.SelectedItem = DateTime.Today.Year;

        Closed += (_, _) =>
        {
            _api.DefaultRequestHeaders.Authorization = null;
            _api.Dispose();
        };
    }

    // MERCADEX_TARIFAS_SIN_LOGIN
    // Conexion automatica solo para la API local de desarrollo.
    // La autorizacion del Backend permanece habilitada.

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [System.Runtime.InteropServices.DllImport(
        "crypt32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    private static string LeerClaveAdministradorLocal()
    {
        var archivo = System.IO.Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "MercadexDev",
            "Tarifas",
            "admin-password.dpapi");

        if (!System.IO.File.Exists(archivo))
            throw new InvalidOperationException(
                "No existe la credencial de desarrollo local.");

        var contenido = System.IO.File.ReadAllText(archivo).Trim();

        var cifrado = Convert.FromHexString(contenido);

        var entrada = new DataBlob
        {
            cbData = cifrado.Length,
            pbData = System.Runtime.InteropServices.Marshal
                .AllocHGlobal(cifrado.Length)
        };

        var salida = new DataBlob();

        try
        {
            System.Runtime.InteropServices.Marshal.Copy(
                cifrado,
                0,
                entrada.pbData,
                cifrado.Length);

            if (!CryptUnprotectData(
                    ref entrada,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    out salida))
            {
                throw new InvalidOperationException(
                    "Windows no pudo recuperar la credencial local.");
            }

            var bytes = new byte[salida.cbData];

            try
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    salida.pbData,
                    bytes,
                    0,
                    bytes.Length);

                return System.Text.Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        finally
        {
            Array.Clear(cifrado, 0, cifrado.Length);

            System.Runtime.InteropServices.Marshal.FreeHGlobal(
                entrada.pbData);

            if (salida.pbData != IntPtr.Zero)
                LocalFree(salida.pbData);
        }
    }

    private async Task ConectarAutomaticamenteAsync()
    {
        if (_ocupado || _autorizado)
            return;

        var dialogo = new Window
        {
            Title = "Administrador - Mercadex",
            Width = 370,
            Height = 270,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(22)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Usuario administrador"
        });

        var usuario = new TextBox
        {
            Margin = new Thickness(0, 5, 0, 12)
        };

        panel.Children.Add(usuario);

        panel.Children.Add(new TextBlock
        {
            Text = "Contrasena"
        });

        var clave = new PasswordBox
        {
            Margin = new Thickness(0, 5, 0, 15)
        };

        panel.Children.Add(clave);

        var aceptar = new Button
        {
            Content = "Iniciar sesion",
            Height = 35,
            IsDefault = true
        };

        aceptar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(usuario.Text) ||
                string.IsNullOrWhiteSpace(clave.Password))
            {
                MessageBox.Show(
                    "Completa tus credenciales.");
                return;
            }

            dialogo.DialogResult = true;
        };

        panel.Children.Add(aceptar);
        dialogo.Content = panel;

        if (dialogo.ShowDialog() != true)
        {
            TxtEstado.Text = "Inicio de sesion cancelado.";
            return;
        }

        _ocupado = true;

        try
        {
            TxtEstado.Text = "Conectando con Mercadex...";

            var password = clave.Password;
            clave.Clear();

            using var respuesta = await _api.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    usuario = usuario.Text.Trim(),
                    password
                });

            password = null;

            if (!respuesta.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    "Credenciales incorrectas o acceso no configurado.");

            var sesion = await respuesta.Content
                .ReadFromJsonAsync<SesionTarifas>();

            if (string.IsNullOrWhiteSpace(sesion?.Token))
                throw new InvalidOperationException(
                    "No se recibio un token valido.");

            _api.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    sesion.Token);

            var catalogos = await _api
                .GetFromJsonAsync<CatalogosIngresosModel>(
                    "/api/ingresos/catalogos");

            _monedas = await _api
                .GetFromJsonAsync<List<MonedaIngresoModel>>(
                    "/api/monedas")
                ?? new();

            CmbMonedas.ItemsSource = _monedas;
            CmbConceptos.ItemsSource =
                catalogos?.Conceptos ?? new();

            _autorizado = true;

            if (CmbConceptos.Items.Count > 0)
            {
                CmbConceptos.SelectedIndex = 0;
                await ActualizarCasetasAsync();

                TxtEstado.Text =
                    "Conexion correcta. Tarifas disponibles.";
            }
            else
            {
                TxtEstado.Text =
                    "Conexion correcta. No existen conceptos.";
            }
        }
        catch (Exception ex)
        {
            _autorizado = false;
            _api.DefaultRequestHeaders.Authorization = null;
            TxtEstado.Text = ex.Message;
        }
        finally
        {
            _ocupado = false;
            ActualizarBotonesEdicion();
        }
    }
    private async void Listar_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!ValidarConsulta(out var conceptoId))
            return;

        await EjecutarConsultaAsync(async () =>
        {
            var url =
                "/api/tarifas-unidad?conceptoCobroId=" +
                conceptoId;

            var respuesta = await _api
                .GetFromJsonAsync<ListadoTarifasRespuesta>(url);

            if (respuesta is null)
                throw new InvalidOperationException(
                    "La API no devolvió tarifas.");

            PrepararEdicion(conceptoId, respuesta);

            Tabla.ItemsSource = respuesta.Casetas
                .Select(x => new FilaTablaTarifa(
                    x.Caseta,
                    x.Bloque ?? "",
                    x.TieneTarifa ? "Tarifa asignada" : "Sin tarifa",
                    x.Monto,
                    NombreMoneda(x.MonedaId)))
                .ToList();

            TxtEstado.Text =
                $"Casetas: {respuesta.CantidadCasetas} | " +
                $"Con tarifa: {respuesta.CantidadConTarifa} | " +
                $"Sin tarifa: " +
                $"{respuesta.CantidadCasetas - respuesta.CantidadConTarifa}";
        });
    }

    private async void Previa_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!ValidarConsulta(out var conceptoId))
            return;

        if (CmbAnio.SelectedItem is not int anio ||
            CmbMes.SelectedItem is not int mes)
        {
            TxtEstado.Text = "Selecciona mes y año.";
            return;
        }

        await EjecutarConsultaAsync(async () =>
        {
            var url =
                "/api/tarifas-unidad/vista-previa" +
                $"?conceptoCobroId={conceptoId}" +
                $"&anio={anio}&mes={mes}";

            var respuesta = await _api
                .GetFromJsonAsync<VistaPreviaTarifasRespuesta>(url);

            if (respuesta is null)
                throw new InvalidOperationException(
                    "La API no devolvió la vista previa.");

            Tabla.ItemsSource = respuesta.Detalle
                .Select(x => new FilaTablaTarifa(
                    x.Caseta,
                    x.Bloque ?? "",
                    x.Estado,
                    x.Monto,
                    NombreMoneda(x.MonedaId)))
                .ToList();

            var totales = string.Join(" | ",
                respuesta.TotalesPorMoneda.Select(x =>
                    $"{NombreMoneda(x.MonedaId)} {x.Total:N2}"));

            TxtEstado.Text =
                $"Período {mes:00}/{anio} | " +
                $"Listas: {respuesta.Listas} | " +
                $"Sin tarifa: {respuesta.SinTarifa} | " +
                $"Deuda existente: {respuesta.ConDeudaExistente} | " +
                $"Totales: {(string.IsNullOrEmpty(totales) ? "0" : totales)}. " +
                "Solo vista previa; no se creó ninguna deuda.";
        });
    }

    private void ActualizarBotonesEdicion()
    {
        var habilitado =
            _autorizado &&
            !_ocupado &&
            _conceptoCargadoId != Guid.Empty &&
            CmbCasetas.SelectedItem is TarifaRespuesta;

        CmbCasetas.IsEnabled = _autorizado && !_ocupado &&
                               _casetas.Count > 0;

        CmbMonedas.IsEnabled = habilitado;
        TxtMonto.IsEnabled = habilitado;
        BtnGuardar.IsEnabled = habilitado;
    }

    private void PrepararEdicion(
        Guid conceptoId,
        ListadoTarifasRespuesta respuesta)
    {
        var anterior = _conceptoCargadoId == conceptoId &&
                       CmbCasetas.SelectedItem is TarifaRespuesta actual
            ? actual.UnidadComercialId
            : Guid.Empty;

        _conceptoCargadoId = conceptoId;
        _casetas = respuesta.Casetas ?? new();

        CmbCasetas.ItemsSource = _casetas;

        CmbCasetas.SelectedItem =
            _casetas.FirstOrDefault(x => x.UnidadComercialId == anterior)
            ?? _casetas.FirstOrDefault();

        ActualizarBotonesEdicion();
    }

    private async Task ActualizarCasetasAsync()
    {
        if (!ValidarConsulta(out var conceptoId))
            return;

        var url = "/api/tarifas-unidad?conceptoCobroId=" + conceptoId;

        var respuesta = await _api
            .GetFromJsonAsync<ListadoTarifasRespuesta>(url);

        if (respuesta?.Casetas is null)
            throw new InvalidOperationException(
                "La API no devolvio el listado de casetas.");

        if (CmbConceptos.SelectedItem is not ConceptoIngresoModel seleccionado ||
            seleccionado.Id != conceptoId)
            throw new InvalidOperationException(
                "El concepto cambio durante la consulta.");

        PrepararEdicion(conceptoId, respuesta);

        Tabla.ItemsSource = respuesta.Casetas
            .Select(x => new FilaTablaTarifa(
                x.Caseta,
                x.Bloque ?? "",
                x.TieneTarifa ? "Tarifa asignada" : "Sin tarifa",
                x.Monto,
                NombreMoneda(x.MonedaId)))
            .ToList();
    }

    private async void Concepto_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        _conceptoCargadoId = Guid.Empty;
        _casetas = new();
        CmbCasetas.ItemsSource = null;
        CmbMonedas.SelectedItem = null;
        TxtMonto.Clear();
        ActualizarBotonesEdicion();

        if (!_autorizado || _ocupado)
            return;

        await EjecutarConsultaAsync(ActualizarCasetasAsync);
        ActualizarBotonesEdicion();
    }

    private void Caseta_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (CmbCasetas.SelectedItem is not TarifaRespuesta caseta)
        {
            TxtMonto.Clear();
            CmbMonedas.SelectedItem = null;
            ActualizarBotonesEdicion();
            return;
        }

        TxtMonto.Text = caseta.Monto.HasValue
            ? caseta.Monto.Value.ToString(
                "0.00", CultureInfo.InvariantCulture)
            : "";

        CmbMonedas.SelectedItem = caseta.MonedaId.HasValue
            ? _monedas.FirstOrDefault(
                x => x.Id == caseta.MonedaId.Value)
            : (_monedas.Count == 1 ? _monedas[0] : null);

        ActualizarBotonesEdicion();
    }

    private async void Guardar_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ocupado || !ValidarConsulta(out var conceptoId))
            return;

        var uri = ApiConfiguration.BaseUri;

        if (!uri.IsLoopback ||
            uri.Port != 5189 ||
            uri.Scheme != Uri.UriSchemeHttp ||
            Environment.GetEnvironmentVariable(
                "MERCADEX_TARIFAS_DEV_MODE") != "SI")
        {
            TxtEstado.Text =
                "Guardado bloqueado: solo se admite la API local de pruebas.";
            return;
        }

        if (_conceptoCargadoId != conceptoId ||
            CmbCasetas.SelectedItem is not TarifaRespuesta caseta ||
            caseta.UnidadComercialId == Guid.Empty)
        {
            TxtEstado.Text = "Selecciona una caseta valida.";
            return;
        }

        if (CmbMonedas.SelectedItem is not MonedaIngresoModel moneda ||
            moneda.Id == Guid.Empty)
        {
            TxtEstado.Text = "Selecciona una moneda.";
            return;
        }

        var texto = TxtMonto.Text.Trim();

        if (!Regex.IsMatch(texto, @"^\d+(?:[.,]\d{1,2})?$") ||
            !decimal.TryParse(
                texto.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var monto) ||
            monto <= 0)
        {
            TxtEstado.Text =
                "Monto incorrecto. Usa un valor positivo con hasta 2 decimales.";
            return;
        }

        var confirmacion = MessageBox.Show(
            this,
            $"Caseta: {caseta.Caseta}\n" +
            $"Moneda: {moneda.Codigo}\n" +
            $"Monto: {monto:N2}\n\n" +
            "¿Guardar esta tarifa individual?\n" +
            "No se generaran obligaciones.",
            "Confirmar tarifa",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (confirmacion != MessageBoxResult.Yes)
            return;

        _ocupado = true;
        CmbConceptos.IsEnabled = false;
        CmbCasetas.IsEnabled = false;
        BtnGuardar.IsEnabled = false;

        var guardado = false;

        try
        {
            TxtEstado.Text = "Guardando tarifa...";

            using var respuesta = await _api.PutAsJsonAsync(
                $"/api/tarifas-unidad/{caseta.UnidadComercialId}/{conceptoId}",
                new { monedaId = moneda.Id, monto });

            if (!respuesta.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"El servidor rechazo el guardado: HTTP {(int)respuesta.StatusCode}.");

            guardado = true;

            await ActualizarCasetasAsync();

            if (CmbAnio.SelectedItem is int anio &&
                CmbMes.SelectedItem is int mes)
            {
                var url = "/api/tarifas-unidad/vista-previa" +
                    $"?conceptoCobroId={conceptoId}&anio={anio}&mes={mes}";

                var previa = await _api
                    .GetFromJsonAsync<VistaPreviaTarifasRespuesta>(url);

                if (previa?.Detalle is not null)
                {
                    Tabla.ItemsSource = previa.Detalle
                        .Select(x => new FilaTablaTarifa(
                            x.Caseta,
                            x.Bloque ?? "",
                            x.Estado,
                            x.Monto,
                            NombreMoneda(x.MonedaId)))
                        .ToList();
                }
            }

            TxtEstado.Text =
                $"Tarifa guardada: {caseta.Caseta} - " +
                $"{moneda.Codigo} {monto:N2}. " +
                "Listado actualizado. No se generaron deudas.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text = guardado
                ? "La tarifa se guardo, pero fallo la actualizacion " +
                  "de la pantalla. Consulta las tarifas antes de repetir. " +
                  ex.Message
                : "No se pudo confirmar el guardado. " +
                  "Revisa el listado antes de volver a guardar. " +
                  ex.Message;
        }
        finally
        {
            _ocupado = false;
            CmbConceptos.IsEnabled = _autorizado;
            ActualizarBotonesEdicion();
        }
    }
    private void InvalidarPreviaMensual()
    {
        _huellaPreviaMensual = null;
        BtnGenerarMensual.IsEnabled = false;
    }

    private void ActualizarFechasMensuales()
    {
        if (CmbAnio.SelectedItem is not int anio ||
            CmbMes.SelectedItem is not int mes ||
            anio < 2000 || anio > 2100 ||
            mes < 1 || mes > 12)
            return;

        DpEmision.SelectedDate = new DateTime(anio, mes, 1);

        DpVencimiento.SelectedDate = new DateTime(
            anio,
            mes,
            DateTime.DaysInMonth(anio, mes));
    }

    private static string HuellaPreviaMensual(
        Guid conceptoId,
        int anio,
        int mes,
        VistaPreviaTarifasRespuesta previa)
    {
        var cultura =
            System.Globalization.CultureInfo.InvariantCulture;

        var detalle = previa.Detalle
            .OrderBy(x => x.Caseta, StringComparer.Ordinal)
            .Select(x => string.Join("~", new[]
            {
                x.Caseta,
                x.Bloque ?? "",
                x.Estado,
                x.MonedaId?.ToString() ?? "-",
                x.Monto?.ToString("0.00", cultura) ?? "-"
            }));

        var totales = previa.TotalesPorMoneda
            .OrderBy(x => x.MonedaId)
            .Select(x =>
                (x.MonedaId?.ToString() ?? "-") + ":" +
                x.Total.ToString("0.00", cultura));

        return string.Join("|",
            conceptoId.ToString("N"),
            anio,
            mes,
            previa.Listas,
            previa.SinTarifa,
            previa.ConDeudaExistente,
            previa.Detalle.Count,
            string.Join(";", detalle),
            string.Join(";", totales));
    }

    private async Task<VistaPreviaTarifasRespuesta>
        ConsultarPreviaGeneracionAsync(
            Guid conceptoId,
            int anio,
            int mes)
    {
        var url = "/api/tarifas-unidad/vista-previa" +
                  $"?conceptoCobroId={conceptoId}" +
                  $"&anio={anio}&mes={mes}";

        var previa = await _api
            .GetFromJsonAsync<VistaPreviaTarifasRespuesta>(url);

        if (previa?.Detalle is null ||
            previa.TotalesPorMoneda is null)
        {
            throw new InvalidOperationException(
                "La API no devolvio una vista previa valida.");
        }

        return previa;
    }

    // MERCADEX_PREVIA_ETIQUETAS_V3
    private void MostrarPreviaGeneracion(
        VistaPreviaTarifasRespuesta previa,
        int anio,
        int mes)
    {
        Tabla.ItemsSource = previa.Detalle
            .Select(x => new FilaGeneracionMensual(
                x.Caseta,
                x.Bloque ?? "",
                x.Estado switch
                {
                    "DeudaExistente" => "Deuda existente",
                    "SinTarifa" => "Sin tarifa",
                    "Lista" => "Lista para generar",
                    _ => x.Estado
                },
                x.Estado == "DeudaExistente"
                    ? null
                    : x.Monto,
                x.Estado == "DeudaExistente"
                    ? "-"
                    : NombreMoneda(x.MonedaId)))
            .ToList();

        var totales = string.Join(" | ",
            previa.TotalesPorMoneda.Select(x =>
                $"{NombreMoneda(x.MonedaId)} {x.Total:N2}"));

        TxtEstado.Text =
            $"Periodo {mes:00}/{anio} | " +
            $"Listas: {previa.Listas} | " +
            $"Sin tarifa: {previa.SinTarifa} | " +
            $"Con deuda: {previa.ConDeudaExistente} | " +
            $"Total: {totales}.";
    }

    private bool PreviaAptaParaGenerar(
        VistaPreviaTarifasRespuesta previa)
    {
        return previa.Listas > 0 &&
               previa.SinTarifa == 0 &&
               previa.ConDeudaExistente == 0 &&
               previa.Detalle.Count == previa.Listas;
    }

    private bool EntornoLocalParaGeneracion()
    {
        var uri = ApiConfiguration.BaseUri;

        return uri.Scheme == Uri.UriSchemeHttp &&
               uri.Host == "127.0.0.1" &&
               uri.Port == 5189 &&
               Environment.GetEnvironmentVariable(
                   "MERCADEX_TARIFAS_DEV_MODE") == "SI";
    }

    // MERCADEX_PREVIA_CORREGIDA_V2
    // Consulta independiente con mensajes visibles.

    private async void PreviaMensualSegura_Click(
        object sender,
        RoutedEventArgs e)
    {
        TxtEstado.Text = "Preparando vista previa mensual...";

        if (_generandoObligaciones)
        {
            TxtEstado.Text =
                "Espera a que termine la generacion anterior.";
            return;
        }

        if (_consultandoPrevia)
        {
            TxtEstado.Text =
                "La vista previa ya se esta consultando.";
            return;
        }

        if (!ValidarConsulta(out var conceptoId))
            return;

        if (CmbAnio.SelectedItem is not int anio ||
            CmbMes.SelectedItem is not int mes)
        {
            TxtEstado.Text =
                "Selecciona un mes y un anio validos.";
            return;
        }

        if (mes < 1 || mes > 12 ||
            anio < 2000 || anio > 2100)
        {
            TxtEstado.Text =
                "El periodo seleccionado no es valido.";
            return;
        }

        InvalidarPreviaMensual();

        _consultandoPrevia = true;
        _ocupado = true;

        try
        {
            TxtEstado.Text =
                $"Consultando obligaciones de {mes:00}/{anio}...";

            var previa = await ConsultarPreviaGeneracionAsync(
                conceptoId,
                anio,
                mes);

            MostrarPreviaGeneracion(
                previa,
                anio,
                mes);

            if (!PreviaAptaParaGenerar(previa))
            {
                BtnGenerarMensual.IsEnabled = false;

                TxtEstado.Text +=
                    " GENERACION BLOQUEADA: existen " +
                    "obligaciones anteriores, tarifas faltantes " +
                    "o datos pendientes de revision.";

                return;
            }

            _huellaPreviaMensual = HuellaPreviaMensual(
                conceptoId,
                anio,
                mes,
                previa);

            BtnGenerarMensual.IsEnabled = true;

            TxtEstado.Text +=
                " VISTA PREVIA CORRECTA. " +
                "Generar obligaciones esta disponible " +
                "con confirmacion previa.";
        }
        catch (Exception ex)
        {
            InvalidarPreviaMensual();

            TxtEstado.Text =
                "ERROR EN VISTA PREVIA: " + ex.Message;
        }
        finally
        {
            _consultandoPrevia = false;
            _ocupado = false;
        }
    }

    private async void GenerarMensual_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ocupado || _generandoObligaciones ||
            !ValidarConsulta(out var conceptoId))
            return;

        if (!EntornoLocalParaGeneracion())
        {
            TxtEstado.Text =
                "Generacion bloqueada fuera de la API local.";
            return;
        }

        if (CmbAnio.SelectedItem is not int anio ||
            CmbMes.SelectedItem is not int mes ||
            string.IsNullOrWhiteSpace(_huellaPreviaMensual))
        {
            TxtEstado.Text =
                "Primero selecciona el periodo y pulsa " +
                "Vista previa mensual.";
            return;
        }

        if (DpEmision.SelectedDate is not DateTime emision ||
            DpVencimiento.SelectedDate is not DateTime vencimiento ||
            vencimiento.Date < emision.Date ||
            emision.Year != anio ||
            emision.Month != mes)
        {
            TxtEstado.Text =
                "Revisa las fechas. La emision debe " +
                "corresponder al mes seleccionado.";
            return;
        }

        _ocupado = true;
        _generandoObligaciones = true;
        BtnGenerarMensual.IsEnabled = false;

        var postAceptado = false;

        try
        {
            TxtEstado.Text =
                "Verificando nuevamente el periodo...";

            var previa = await ConsultarPreviaGeneracionAsync(
                conceptoId, anio, mes);

            if (!PreviaAptaParaGenerar(previa) ||
                HuellaPreviaMensual(
                    conceptoId, anio, mes, previa) !=
                    _huellaPreviaMensual)
            {
                InvalidarPreviaMensual();

                TxtEstado.Text =
                    "La vista previa cambio o contiene " +
                    "obligaciones existentes. " +
                    "Vuelve a consultar antes de generar.";
                return;
            }

            var totales = string.Join(
                Environment.NewLine,
                previa.TotalesPorMoneda.Select(x =>
                    $"{NombreMoneda(x.MonedaId)} {x.Total:N2}"));

            var confirmar = MessageBox.Show(
                this,
                $"Periodo: {mes:00}/{anio}\n" +
                $"Casetas: {previa.Listas}\n" +
                $"Sin tarifa: {previa.SinTarifa}\n" +
                $"Deudas existentes: {previa.ConDeudaExistente}\n\n" +
                $"Total:\n{totales}\n\n" +
                $"Emision: {emision:dd/MM/yyyy}\n" +
                $"Vencimiento: {vencimiento:dd/MM/yyyy}\n\n" +
                "Se crearan obligaciones en PostgreSQL " +
                "LOCAL de desarrollo.\n\n" +
                "¿Confirmas la generacion?",
                "Confirmar obligaciones mensuales",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirmar != MessageBoxResult.Yes)
            {
                TxtEstado.Text =
                    "Generacion cancelada. No se enviaron obligaciones.";
                return;
            }

            // Comprobar otra vez despues de la confirmacion.
            var ultima = await ConsultarPreviaGeneracionAsync(
                conceptoId, anio, mes);

            if (!PreviaAptaParaGenerar(ultima) ||
                HuellaPreviaMensual(
                    conceptoId, anio, mes, ultima) !=
                    _huellaPreviaMensual)
            {
                InvalidarPreviaMensual();

                TxtEstado.Text =
                    "Las tarifas cambiaron durante la confirmacion. " +
                    "No se generaron obligaciones desde esta ventana.";
                return;
            }

            InvalidarPreviaMensual();

            var solicitud = new
            {
                conceptoCobroId = conceptoId,
                anio,
                mes,
                fechaEmision = emision.ToString(
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
                fechaVencimiento = vencimiento.ToString(
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture),
                listasEsperadas = ultima.Listas,
                confirmar = true
            };

            TxtEstado.Text = "Generando obligaciones mensuales...";

            using var respuesta = await _api.PostAsJsonAsync(
                "/api/tarifas-unidad/generar-mensual",
                solicitud);

            if (!respuesta.IsSuccessStatusCode)
            {
                TxtEstado.Text =
                    $"La API rechazo la generacion " +
                    $"(HTTP {(int)respuesta.StatusCode}). " +
                    "Consulta nuevamente el periodo.";
                return;
            }

            postAceptado = true;

            var resultado = await respuesta.Content
                .ReadFromJsonAsync<ResultadoGeneracionMensual>();

            var cantidad = resultado?.ObligacionesGeneradas ?? 0;

            var posterior = await ConsultarPreviaGeneracionAsync(
                conceptoId, anio, mes);

            MostrarPreviaGeneracion(posterior, anio, mes);

            TxtEstado.Text =
                $"Generacion completada: {cantidad} obligaciones " +
                $"para {mes:00}/{anio}. " +
                "Vista previa invalidada para evitar duplicados.";

            MessageBox.Show(
                this,
                $"Se generaron {cantidad} obligaciones " +
                $"del periodo {mes:00}/{anio}.\n\n" +
                "La tabla se actualizo correctamente.",
                "Mercadex - Generacion completada",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            InvalidarPreviaMensual();

            TxtEstado.Text = postAceptado
                ? "La API confirmo la generacion, pero no " +
                  "se pudo completar la actualizacion visual. " +
                  "Consulta el periodo antes de repetir. " +
                  ex.Message
                : "No se pudo confirmar la generacion. " +
                  "Consulta nuevamente el periodo antes " +
                  "de intentar otra vez. " + ex.Message;
        }
        finally
        {
            _ocupado = false;
            _generandoObligaciones = false;
            BtnGenerarMensual.IsEnabled = false;
        }
    }

    private sealed class ResultadoGeneracionMensual
    {
        public int ObligacionesGeneradas { get; set; }
    }

    private sealed record FilaGeneracionMensual(
        string Caseta,
        string Bloque,
        string Estado,
        decimal? Monto,
        string Moneda);

    // MERCADEX_WPF_CORRECCIONES_V1
    // Reutiliza la conexion automatica existente.
    // No muestra ni solicita credenciales.

    private void AbrirCorrecciones_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ocupado)
        {
            TxtEstado.Text =
                "Espera a que termine la operacion actual.";
            return;
        }

        if (!ValidarConsulta(out var conceptoId))
            return;

        if (CmbAnio.SelectedItem is not int anio ||
            CmbMes.SelectedItem is not int mes)
        {
            TxtEstado.Text = "Selecciona mes y año.";
            return;
        }

        InvalidarPreviaMensual();

        var ventana = new CorreccionesObligacionesWindow(
            _api,
            conceptoId,
            anio,
            mes)
        {
            Owner = this
        };

        ventana.ShowDialog();

        TxtEstado.Text =
            "Ventana de deudas cerrada. " +
            "Actualiza la vista previa antes de generar obligaciones.";
    }

    // MERCADEX_WPF_KARDEX_DOCUMENTAL_V1
    // Reutiliza la sesion automatica del Desktop.

    private void AbrirDocumentosKardex_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_autorizado || _ocupado)
        {
            TxtEstado.Text =
                "Espera a que termine la conexión automática.";
            return;
        }

        var ventana = new DocumentosKardexWindow(_api)
        {
            Owner = this
        };

        ventana.ShowDialog();

        TxtEstado.Text =
            "Ventana del kardex cerrada.";
    }

    // MERCADEX_COMPROBANTES_ANULACION_WPF_V1

    private void AbrirComprobantesAnulacion_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_autorizado || _ocupado)
        {
            TxtEstado.Text =
                "Espera a que termine la conexión automática.";
            return;
        }

        var ventana = new ComprobantesAnulacionWindow(_api)
        {
            Owner = this
        };

        ventana.ShowDialog();
    }

    // MERCADEX_REPORTES_CAJA_WPF_V1


    // MERCADEX_DEUDAS_CONSOLIDADAS_WPF_V1

    private void AbrirDeudoresConsolidados_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_autorizado || _ocupado)
        {
            TxtEstado.Text =
                "Espera a que termine la conexión automática.";
            return;
        }

        var ventana = new DeudoresConsolidadosWindow(_api)
        {
            Owner = this
        };

        ventana.ShowDialog();
    }
    private void AbrirReportesCaja_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_autorizado || _ocupado)
        {
            TxtEstado.Text =
                "Espera a que termine la conexión automática.";
            return;
        }

        var ventana = new ReportesCajaWindow(_api)
        {
            Owner = this
        };

        ventana.ShowDialog();
    }
    private bool ValidarConsulta(out Guid conceptoId)
    {
        conceptoId = Guid.Empty;

        if (!_autorizado)
        {
            TxtEstado.Text =
                "Esperando conexion automatica a la API local.";
            return false;
        }

        if (CmbConceptos.SelectedItem is not ConceptoIngresoModel concepto)
        {
            TxtEstado.Text = "Selecciona un concepto.";
            return false;
        }

        conceptoId = concepto.Id;
        return true;
    }

    private async Task EjecutarConsultaAsync(Func<Task> consulta)
    {
        if (_ocupado)
            return;

        _ocupado = true;

        try
        {
            TxtEstado.Text = "Consultando información...";
            await consulta();
        }
        catch (HttpRequestException ex)
            when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            TxtEstado.Text =
                "La API de tarifas 1.5.0 todavía no está " +
                "disponible en el servidor configurado.";
        }
        catch (Exception ex)
        {
            TxtEstado.Text =
                "No se pudo completar la consulta: " +
                ex.Message;
        }
        finally
        {
            _ocupado = false;
        }
    }

    private string NombreMoneda(Guid? id)
    {
        if (!id.HasValue)
            return "-";

        return _monedas.FirstOrDefault(
            x => x.Id == id.Value)?.Codigo ?? "-";
    }

    private sealed class SesionTarifas
    {
        public string Token { get; set; } = "";
    }

    private sealed class ListadoTarifasRespuesta
    {
        public int CantidadCasetas { get; set; }
        public int CantidadConTarifa { get; set; }
        public List<TarifaRespuesta> Casetas { get; set; } = new();
    }

    private sealed class TarifaRespuesta
    {
        public Guid UnidadComercialId { get; set; }
        public string Caseta { get; set; } = "";
        public string? Bloque { get; set; }
        public bool TieneTarifa { get; set; }
        public Guid? MonedaId { get; set; }
        public decimal? Monto { get; set; }
    }

    private sealed class VistaPreviaTarifasRespuesta
    {
        public int Listas { get; set; }
        public int SinTarifa { get; set; }
        public int ConDeudaExistente { get; set; }

        public List<TarifaPreviaRespuesta> Detalle { get; set; } = new();

        public List<TotalMonedaRespuesta> TotalesPorMoneda { get; set; }
            = new();
    }

    private sealed class TarifaPreviaRespuesta
    {
        public string Caseta { get; set; } = "";
        public string? Bloque { get; set; }
        public string Estado { get; set; } = "";
        public Guid? MonedaId { get; set; }
        public decimal? Monto { get; set; }
    }

    private sealed class TotalMonedaRespuesta
    {
        public Guid? MonedaId { get; set; }
        public decimal Total { get; set; }
    }

    private sealed record FilaTablaTarifa(
        string Caseta,
        string Bloque,
        string Estado,
        decimal? Monto,
        string Moneda);
}