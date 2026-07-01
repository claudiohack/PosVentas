using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;
using PosVentas.Domain.ValueObjects;
using PosVentas.Infrastructure.Persistence.Contexts;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PosVentas.Desktop;

public partial class ProductosView : UserControl
{
    private readonly ApplicationDbContext _context;

    public ProductosView(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));

        this.Loaded += ProductosView_Loaded;
    }

    private async void ProductosView_Loaded(object sender, RoutedEventArgs e)
    {
        // Llamamos al método de búsqueda pasándole vacío para que cargue todo al inicio
        await CargarProductosAsync(string.Empty);
    }

    /// <summary>
    /// Centraliza la consulta a la base de datos aplicando filtros si corresponden.
    /// </summary>
    private async Task CargarProductosAsync(string filtro)
    {
        try
        {
            var query = _context.Productos.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                filtro = filtro.Trim();

                // 1. Para buscar por SKU, creamos una instancia real de nuestro Value Object.
                // Esto obliga a EF Core a usar el ValueConverter correctamente para el parámetro.
                var skuFiltro = new Sku(filtro);

                // 2. Evaluamos contra el objeto Sku completo. EF Core traducirá esto a un "=" o "LIKE" en SQL
                // según cómo esté configurado tu mapeo de base de datos.
                query = query.Where(p => p.Nombre.Contains(filtro) ||
                                         p.Sku == skuFiltro);
            }

            // El resto del código (.Select...) se queda EXACTAMENTE igual
            var listaProductos = await query
                .Select(p => new
                {
                    Id = p.Id,
                    Codigo = p.Sku.Value,
                    Nombre = p.Nombre,
                    PrecioCosto = p.PrecioCompra != null ? p.PrecioCompra.Monto : 0.0m,
                    PorcentajeGanancia = p.PorcentajeGanancia,
                    PrecioVenta = p.Precio != null ? p.Precio.Monto : 0.0m,
                    Stock = p.StockActual
                })
                .ToListAsync();

            GridProductos.ItemsSource = listaProductos;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error crítico al procesar los productos:\n{ex.Message}\n\nDetalles internos: {ex.InnerException?.Message}",
                            "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void BtnNuevo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ventanaNuevo = new NuevoProductoWindow(_context);
            ventanaNuevo.Owner = Window.GetWindow(this);

            if (ventanaNuevo.ShowDialog() == true)
            {
                _ = CargarProductosAsync(TxtBuscar.Text);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir el formulario de alta:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnEditar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var seleccionado = GridProductos.SelectedItem;
            if (seleccionado == null)
            {
                MessageBox.Show("Por favor, seleccione un producto de la lista para editar.", "Ningún elemento seleccionado", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Guid productoId = (Guid)seleccionado.GetType().GetProperty("Id")?.GetValue(seleccionado)!;
            var productoDb = await _context.Productos.FirstOrDefaultAsync(p => p.Id == productoId);

            if (productoDb == null)
            {
                MessageBox.Show("El producto seleccionado ya no existe en la base de datos.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var ventanaEdicion = new NuevoProductoWindow(_context, productoDb);
            ventanaEdicion.Owner = Window.GetWindow(this);

            if (ventanaEdicion.ShowDialog() == true)
            {
                await CargarProductosAsync(TxtBuscar.Text);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al intentar editar el producto:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnEliminar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. Obtener fila seleccionada
            var seleccionado = GridProductos.SelectedItem;
            if (seleccionado == null)
            {
                MessageBox.Show("Por favor, seleccione el producto que desea eliminar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Extraer propiedades informativas mediante reflection del objeto anónimo
            Guid productoId = (Guid)seleccionado.GetType().GetProperty("Id")?.GetValue(seleccionado)!;
            string nombreProd = (string)seleccionado.GetType().GetProperty("Nombre")?.GetValue(seleccionado)!;
            string codigoProd = (string)seleccionado.GetType().GetProperty("Codigo")?.GetValue(seleccionado)!;

            // 3. Cuadro de confirmación al usuario
            var resultado = MessageBox.Show($"¿Está completamente seguro de que desea eliminar el siguiente producto?\n\n📦 [{codigoProd}] {nombreProd}",
                "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (resultado == MessageBoxResult.Yes)
            {
                // 4. Buscar la entidad rastreada y removerla
                var productoDb = await _context.Productos.FirstOrDefaultAsync(p => p.Id == productoId);
                if (productoDb != null)
                {
                    _context.Productos.Remove(productoDb);
                    await _context.SaveChangesAsync();

                    MessageBox.Show("Producto eliminado correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                    // 5. Refrescar la grilla manteniendo el filtro actual si lo hubiera
                    await CargarProductosAsync(TxtBuscar.Text);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error crítico al intentar eliminar el producto:\n{ex.Message}\n\nNota: Si el producto ya se usó en una venta, la base de datos impedirá borrarlo por integridad referencial.",
                "Error al Eliminar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnBuscar_Click(object sender, RoutedEventArgs e)
    {
        // Ejecuta el filtro con lo que el usuario escribió en el TextBox
        await CargarProductosAsync(TxtBuscar.Text);
    }
    private async void TxtBuscar_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Si el usuario borra el texto, TxtBuscar.Text será "" (vacío), 
        // lo que hará que CargarProductosAsync traiga la lista completa automáticamente.
        await CargarProductosAsync(TxtBuscar.Text);
    }
}