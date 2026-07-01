using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class CategoriasView : UserControl
{
    private readonly ApplicationDbContext _context;

    public CategoriasView(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // Nos colgamos del evento Loaded para disparar la consulta asíncrona a la base de datos
        this.Loaded += CategoriasView_Loaded;
    }

    private async void CategoriasView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // ESCALAMIENTO REAL: Filtramos para traer únicamente los registros que están activos.
            // Gracias a que agregamos 'EstaActivo' al dominio, la consulta es 100% segura y óptima.
            var listaCategorias = await _context.Categorias
                .Where(c => c.EstaActivo == true)
                .AsNoTracking()
                .OrderBy(c => c.OrdenDisplay)
                .ToListAsync();

            // Seteamos el origen de datos al DataGrid visual
            GridCategorias.ItemsSource = listaCategorias;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al cargar las categorías:\n{ex.Message}",
                            "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnNuevo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ventanaNuevo = new NuevaCategoriaWindow(_context);
            ventanaNuevo.Owner = Window.GetWindow(this);

            if (ventanaNuevo.ShowDialog() == true)
            {
                CategoriasView_Loaded(this, null!);
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
            if (GridCategorias.SelectedItem is Categoria categoriaSeleccionada)
            {
                var ventanaEditar = new NuevaCategoriaWindow(_context, categoriaSeleccionada);
                ventanaEditar.Owner = Window.GetWindow(this);

                if (ventanaEditar.ShowDialog() == true)
                {
                    CategoriasView_Loaded(this, null!);
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione una categoría de la lista para modificar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir el formulario de edición:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Realiza una BAJA LÓGICA de la categoría seleccionada usando las reglas del Dominio.
    /// </summary>
    private async void BtnEliminar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridCategorias.SelectedItem is Categoria categoriaSeleccionada)
            {
                var resultadoConfirmacion = MessageBox.Show(
                    $"¿Está seguro de que desea eliminar la categoría '{categoriaSeleccionada.Nombre}'?\nEsta acción no afectará a los productos ya registrados bajo esta categoría.",
                    "Confirmar Eliminación",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (resultadoConfirmacion == MessageBoxResult.Yes)
                {
                    // Buscamos la entidad fresca directamente del contexto para que EF pueda trackear la modificación
                    var categoriaDb = await _context.Categorias.FindAsync(categoriaSeleccionada.Id);

                    if (categoriaDb != null)
                    {
                        // --- LOGICA DE DOMINIO ---
                        // Invocamos el comportamiento encapsulado que acabamos de meter en Categoria.cs
                        categoriaDb.Desactivar();

                        _context.Categorias.Update(categoriaDb);
                        await _context.SaveChangesAsync();

                        MessageBox.Show("Categoría eliminada con éxito del sistema.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                        // Refrescamos la grilla para que desaparezca visualmente (ya que filtramos por EstaActivo == true)
                        CategoriasView_Loaded(this, null!);
                    }
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione la categoría que desea dar de baja.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al procesar la baja de la categoría:\n{ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}