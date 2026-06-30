using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
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
            // Traemos las categorías ordenadas por el campo OrdenDisplay que definiste en tu dominio
            var listaCategorias = await _context.Categorias
                .AsNoTracking()
                .OrderBy(c => c.OrdenDisplay)
                .Select(c => new
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    Descripcion = c.Descripcion,
                    OrdenDisplay = c.OrdenDisplay
                })
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
            // Creamos la ventana pasándole el contexto inyectado
            var ventanaNuevo = new NuevaCategoriaWindow(_context);
            ventanaNuevo.Owner = Window.GetWindow(this); // La anclamos arriba de la ventana principal

            // Si retorna true, refrescamos la grilla al toque
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
}