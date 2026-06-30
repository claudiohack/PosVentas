using System;
using System.Windows;
using System.Windows.Controls;
using PosVentas.Domain.Entities;
using PosVentas.Domain.ValueObjects;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class NuevoProductoWindow : Window
{
    private readonly ApplicationDbContext _context;
    private readonly Guid _categoriaPorDefectoId;

    public NuevoProductoWindow(ApplicationDbContext context, Guid categoriaPorDefectoId)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _categoriaPorDefectoId = categoriaPorDefectoId; // Necesitamos asociarlo a una categoría válida
    }

    private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. Validaciones básicas de UI antes de pasar al dominio
            if (string.IsNullOrWhiteSpace(TxtSku.Text) || string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("Por favor, complete el Código y el Nombre del producto.", "Campos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Nota: Usamos temporalmente TxtPrecio como Precio de Compra hasta cambiar el XAML
            if (!decimal.TryParse(TxtPrecio.Text, out decimal montoCompra) || montoCompra < 0)
            {
                MessageBox.Show("Ingrese un precio de costo/compra válido (mayor o igual a cero).", "Precio inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Extraer el porcentaje de IVA seleccionado en el ComboBox
            string ivaTexto = ((ComboBoxItem)CboIva.SelectedItem).Content.ToString()!.Replace("%", "").Trim();
            decimal porcentajeIva = decimal.Parse(ivaTexto);

            // 3. Instanciar los Value Objects
            var sku = new Sku(TxtSku.Text.Trim());
            var precioCompra = new Money(montoCompra);
            decimal porcentajeGanancia = 30.0m; // Hardcodeado temporalmente hasta agregar la caja de texto en el XAML

            // 4. Crear la entidad Producto usando el NUEVO constructor formal de Dominio
            var nuevoProducto = new Producto(
                id: Guid.NewGuid(),
                sku: sku,
                nombre: TxtNombre.Text.Trim(),
                precioCompra: precioCompra,
                porcentajeGanancia: porcentajeGanancia,
                porcentajeIva: porcentajeIva,
                categoriaId: _categoriaPorDefectoId,
                venderSinStock: ChkSinStock.IsChecked ?? false
            );

            // 5. Impactar en la base de datos
            _context.Productos.Add(nuevoProducto);
            await _context.SaveChangesAsync();

            MessageBox.Show("Producto registrado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true; // Avisa a la grilla que se guardó correctamente para recargar
            this.Close();
        }
        catch (ArgumentException ex)
        {
            // Captura las excepciones de validación de tu Dominio
            MessageBox.Show($"Regla de negocio infringida:\n{ex.Message}", "Validación de Dominio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error crítico al guardar el producto:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }
}