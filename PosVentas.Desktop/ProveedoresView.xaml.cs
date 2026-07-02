using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class ProveedoresView : UserControl
{
    private readonly ApplicationDbContext _context;

    public ProveedoresView(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));

        this.Loaded += ProveedoresView_Loaded;
    }

    private async void ProveedoresView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Traemos solo los proveedores activos y ordenados alfabéticamente
            var listaProveedores = await _context.Proveedores
                .Where(p => p.EstaActivo)
                .OrderBy(p => p.RazonSocial)
                .AsNoTracking()
                .ToListAsync();

            GridProveedores.ItemsSource = listaProveedores;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al cargar el listado de proveedores:\n{ex.Message}",
                            "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnNuevo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ventanaNuevo = new NuevoProveedorWindow(_context);
            ventanaNuevo.Owner = Window.GetWindow(this);

            if (ventanaNuevo.ShowDialog() == true)
            {
                ProveedoresView_Loaded(this, null!);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir el formulario de alta:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnEditar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridProveedores.SelectedItem is Proveedor proveedorSeleccionado)
            {
                var ventanaEditar = new NuevoProveedorWindow(_context, proveedorSeleccionado);
                ventanaEditar.Owner = Window.GetWindow(this);

                if (ventanaEditar.ShowDialog() == true)
                {
                    ProveedoresView_Loaded(this, null!);
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un proveedor de la lista para modificar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir el formulario de edición:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnEliminar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridProveedores.SelectedItem is Proveedor proveedorSeleccionado)
            {
                var resultado = MessageBox.Show(
                    $"¿Está seguro de que desea dar de baja al proveedor '{proveedorSeleccionado.RazonSocial}'?\nEsto preservará intactos todos sus históricos de compras en el sistema.",
                    "Confirmar Baja",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (resultado == MessageBoxResult.Yes)
                {
                    var proveedorDb = await _context.Proveedores.FindAsync(proveedorSeleccionado.Id);

                    if (proveedorDb != null)
                    {
                        // Invocamos el comportamiento encapsulado en el Dominio (Baja lógica)
                        proveedorDb.Desactivar();

                        _context.Proveedores.Update(proveedorDb);
                        await _context.SaveChangesAsync();

                        MessageBox.Show("Proveedor dado de baja con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                        ProveedoresView_Loaded(this, null!);
                    }
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione el proveedor que desea dar de baja.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al procesar la baja del proveedor:\n{ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}