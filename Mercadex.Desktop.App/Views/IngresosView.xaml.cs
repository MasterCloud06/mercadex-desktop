
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Mercadex.Desktop.App.ViewModels;

namespace Mercadex.Desktop.App.Views;

public partial class IngresosView : UserControl
{
    public IngresosView()
    {
        InitializeComponent();

        DataContext = new IngresosViewModel();

        Loaded += (_, _) =>
        {
            BuscarCaseta.Focus();
        };
    }

    private void BuscarCaseta_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (DataContext is not IngresosViewModel vm)
            return;

        var numero = BuscarCaseta.Text.Trim();

        if (string.IsNullOrWhiteSpace(numero))
        {
            vm.Mensaje = "Introduce el número de caseta.";
            BuscarCaseta.Focus();
            return;
        }

        if (vm.Cargando)
        {
            vm.Mensaje =
                "Espera a que termine la consulta anterior.";
            return;
        }

        // Buscar coincidencias exactas por código.
        var coincidencias = vm.Unidades
            .Where(u => string.Equals(
                u.Codigo?.Trim(),
                numero,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (coincidencias.Count == 0)
        {
            vm.Mensaje =
                $"No se encontró la caseta {numero}.";

            vm.UnidadSeleccionada = null;

            MessageBox.Show(
                $"No se encontró el local {numero}. " +
                "Comprueba que esté registrado y activo.",
                "Mercadex",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            BuscarCaseta.SelectAll();
            BuscarCaseta.Focus();
            return;
        }

        // Puede existir el mismo código en distintos
        // tipos de unidad o bloques. No elegimos al azar.
        if (coincidencias.Count > 1)
        {
            vm.Mensaje =
                $"Hay {coincidencias.Count} unidades con " +
                $"el código {numero}. " +
                "Revisa los codigos en Casetas.";

            return;
        }

        var unidad = coincidencias[0];

        // Evita disparar una nueva consulta si
        // ya está seleccionada la misma unidad.
        if (vm.UnidadSeleccionada?.Id == unidad.Id)
        {
            vm.Mensaje =
                $"El local {numero} ya está seleccionado.";

            return;
        }

        // El ViewModel ya sabe consultar las deudas
        // cuando cambia UnidadSeleccionada.
        vm.UnidadSeleccionada = unidad;
    }
}
