using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;
using PosVentas.Domain.ValueObjects;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class NuevoProductoWindow : Window
{
    private readonly ApplicationDbContext _context;
    private readonly Producto? _productoAEditar; // Si es nulo, es Alta. Si tiene valor, es Edición.

    // Constructor para ALTA
    public NuevoProductoWindow(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        this.Loaded += NuevoProductoWindow_Loaded;
    }

    // Constructor para EDICIÓN
    public NuevoProductoWindow(ApplicationDbContext context, Producto producto) : this(context)
    {
        _productoAEditar = producto ?? throw new ArgumentNullException(nameof(producto));
    }

    private async void NuevoProductoWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. CORRECCIÓN CRÍTICA: Traemos las entidades Categoria reales en lugar de tipos anónimos.
            // Esto le permite a WPF comparar los GUIDs de manera nativa y directa.
            var listaCategorias = await _context.Categorias
                .AsNoTracking()
                .OrderBy(c => c.OrdenDisplay)
                .ToListAsync();

            CboCategorias.ItemsSource = listaCategorias;

            // 2. Evaluar el modo (Alta o Edición)
            if (_productoAEditar == null)
            {
                // Modo Alta: Valores limpios por defecto
                Title = "Registrar Nuevo Producto";
                LblTitulo.Text = "📦 Nuevo Producto";
                BtnGuardar.Content = "💾 Guardar";

                TxtPrecioCompra.Text = (0.00m).ToString("0.00", CultureInfo.InvariantCulture);
                TxtGanancia.Text = (30.00m).ToString("0.00", CultureInfo.InvariantCulture);

                // --- SELECCIÓN DIRECTA Y SIMPLE ---
                if (listaCategorias.Any())
                {
                    var catGeneral = listaCategorias
                        .FirstOrDefault(c => c.Nombre != null && c.Nombre.Trim().Equals("General", StringComparison.OrdinalIgnoreCase));

                    if (catGeneral != null)
                    {
                        // Al ser una entidad real, indicarle el Id al SelectedValue funciona al 100%
                        CboCategorias.SelectedValue = catGeneral.Id;
                    }
                    else
                    {
                        CboCategorias.SelectedIndex = 0;
                    }
                }
            }
            else
            {
                // Modo Edición: Precargar los datos de la entidad de Dominio
                Title = "Modificar Producto";
                LblTitulo.Text = "✏️ Editar Producto";
                BtnGuardar.Content = "💾 Actualizar";
                TxtSku.IsEnabled = false;

                TxtSku.Text = _productoAEditar.Sku?.Value ?? string.Empty;
                TxtNombre.Text = _productoAEditar.Nombre;
                TxtPrecioCompra.Text = _productoAEditar.PrecioCompra?.Monto.ToString("0.00", CultureInfo.InvariantCulture) ?? "0.00";
                TxtGanancia.Text = _productoAEditar.PorcentajeGanancia.ToString("0.00", CultureInfo.InvariantCulture);
                ChkSinStock.IsChecked = _productoAEditar.VenderSinStock;

                // Asignación directa del ID en modo edición
                CboCategorias.SelectedValue = _productoAEditar.CategoriaId;

                // Seleccionar el IVA correspondiente en el combo
                string ivaBuscado = $"{_productoAEditar.PorcentajeIva}%".Replace(".00", "").Replace(",00", "");
                foreach (ComboBoxItem item in CboIva.Items)
                {
                    if (item.Content.ToString() == ivaBuscado || item.Content.ToString()!.StartsWith(_productoAEditar.PorcentajeIva.ToString("0")))
                    {
                        CboIva.SelectedItem = item;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al inicializar el formulario:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Validaciones básicas de UI
            if (string.IsNullOrWhiteSpace(TxtSku.Text) || string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("Por favor, complete el Código y el Nombre del producto.", "Campos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CboCategorias.SelectedValue == null)
            {
                MessageBox.Show("Por favor, seleccione una categoría.", "Falta Categoría", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string costoTexto = TxtPrecioCompra.Text.Replace(',', '.').Trim();
            if (!decimal.TryParse(costoTexto, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal montoCompra) || montoCompra < 0)
            {
                MessageBox.Show("Ingrese un precio de costo/compra válido.", "Precio inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string gananciaTexto = TxtGanancia.Text.Replace(',', '.').Trim();
            if (!decimal.TryParse(gananciaTexto, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal porcentajeGanancia) || porcentajeGanancia < 0)
            {
                MessageBox.Show("Ingrese un porcentaje de ganancia válido.", "Ganancia inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Guid categoriaIdSelected = (Guid)CboCategorias.SelectedValue;
            string ivaTexto = ((ComboBoxItem)CboIva.SelectedItem).Content.ToString()!.Replace("%", "").Trim();
            decimal porcentajeIva = decimal.Parse(ivaTexto, CultureInfo.InvariantCulture);

            var sku = new Sku(TxtSku.Text.Trim());
            var precioCompra = new Money(montoCompra, "ARS");

            if (_productoAEditar == null)
            {
                // MODO ALTA
                var nuevoProducto = new Producto(
                    id: Guid.NewGuid(),
                    sku: sku,
                    nombre: TxtNombre.Text.Trim(),
                    precioCompra: precioCompra,
                    porcentajeGanancia: porcentajeGanancia,
                    porcentajeIva: porcentajeIva,
                    categoriaId: categoriaIdSelected,
                    venderSinStock: ChkSinStock.IsChecked ?? false
                );

                _context.Productos.Add(nuevoProducto);
            }
            else
            {
                // MODO EDICIÓN (Respetando las Reglas de Negocio encapsuladas en el Dominio)
                _productoAEditar.ActualizarDatosBasicos(sku, TxtNombre.Text.Trim(), porcentajeIva);
                _productoAEditar.AsignarPreciosYMargen(precioCompra, porcentajeGanancia);
                _productoAEditar.CambiarCategoria(categoriaIdSelected);
                _productoAEditar.ConfigurarVentaSinStock(ChkSinStock.IsChecked ?? false);

                _context.Productos.Update(_productoAEditar);
            }

            // Impactar cambios en SQL Server
            await _context.SaveChangesAsync();

            MessageBox.Show(_productoAEditar == null ? "Producto registrado con éxito." : "Producto actualizado con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true;
            this.Close();
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show($"Regla de negocio infringida:\n{ex.Message}", "Validación de Dominio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error crítico al guardar:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }
}