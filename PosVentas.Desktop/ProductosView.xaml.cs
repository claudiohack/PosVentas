using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class ProductosView : UserControl
{
    private readonly ApplicationDbContext _context;
    public ProductosView(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // Nos atamos al evento Loaded para cargar la grilla cuando el control se renderice
        this.Loaded += ProductosView_Loaded;
    }
    private async void ProductosView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Consultamos SQL Server mapeando los Value Objects a los nombres de columna del DataGrid
            var listaProductos = await _context.Productos
                .AsNoTracking()
                .Select(p => new
                {
                    Id = p.Id,
                    Codigo = p.Sku != null ? p.Sku.Value : "S/C",
                    Nombre = p.Nombre,
                    Precio = p.Precio != null ? p.Precio.Monto : 0.0m, // Cambiar 'Monto' por el nombre de la propiedad interna de tu Value Object Money
                    Stock = p.StockActual
                })
                .ToListAsync();

            // Seteamos la colección al componente visual
            GridProductos.ItemsSource = listaProductos;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al cargar los productos en la grilla:\n{ex.Message}",
                            "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private async void BtnNuevo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Para simplificar el alta en esta etapa, buscamos la primera categoría que exista en el sistema
            var categoriaDefault = await _context.Categorias.FirstOrDefaultAsync();

            if (categoriaDefault == null)
            {
                MessageBox.Show("No se encontró ninguna categoría en el sistema para asociar el producto. El sembrado inicial falló.",
                                "Error de Configuración", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Abrimos la ventana modal pasándole el contexto y el ID de la categoría
            var ventanaNuevo = new NuevoProductoWindow(_context, categoriaDefault.Id);
            ventanaNuevo.Owner = Window.GetWindow(this); // Setea el foco encima de la app principal

            // Si la ventana devuelve true, significa que se guardó un producto con éxito
            if (ventanaNuevo.ShowDialog() == true)
            {
                // Volvemos a ejecutar la función de carga que ya programamos para refrescar el DataGrid al instante
                ProductosView_Loaded(this, null!);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir el formulario de alta:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}