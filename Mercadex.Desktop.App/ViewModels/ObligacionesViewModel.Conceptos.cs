using System;
using System.Linq;
using System.Windows;

using CommunityToolkit.Mvvm.Input;
using Mercadex.Desktop.App.Views;

namespace Mercadex.Desktop.App.ViewModels;

public partial class ObligacionesViewModel
{
    [RelayCommand]
    private void NuevoConcepto()
    {
        if (Cargando)
            return;

        try
        {
            Cargando = true;

            var ventana = new NuevoConceptoWindow();

            var principal = System.Windows.Application
                .Current?.MainWindow;

            if (principal != null && principal.IsVisible)
            {
                ventana.Owner = principal;
            }

            if (ventana.ShowDialog() != true ||
                ventana.ConceptoCreado == null)
            {
                return;
            }

            var creado = ventana.ConceptoCreado;

            var existente = Conceptos
                .FirstOrDefault(x => x.Id == creado.Id);

            if (existente == null)
            {
                Conceptos.Add(creado);
                existente = creado;
            }

            ConceptoSeleccionado = existente;

            Mensaje =
                $"Concepto «{existente.Nombre}» creado y seleccionado. " +
                "Ya puedes utilizarlo para registrar una obligación.";
        }
        catch (Exception ex)
        {
            Mensaje =
                "No se pudo completar la actualización de la pantalla. " +
                "Pulsa Actualizar para comprobar los conceptos disponibles.";

            MessageBox.Show(
                ex.Message,
                "Conceptos de deuda",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            Cargando = false;
        }
    }
}