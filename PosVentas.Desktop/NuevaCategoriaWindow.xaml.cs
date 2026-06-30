using System;
using System.Windows;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class NuevaCategoriaWindow : Window
{
    private readonly ApplicationDbContext _context;

    public NuevaCategoriaWindow(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. Validaciones básicas de la UI
            if (string.IsNullOrWhiteSpace(TxtNombre.Text))
            {
                MessageBox.Show("Por favor, ingrese el nombre de la categoría.", "Campo incompleto", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtOrden.Text, out int ordenVisual))
            {
                MessageBox.Show("El orden visual debe ser un número entero válido.", "Orden inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Instanciamos la entidad usando su constructor reglamentario de Dominio
            // Esto dispara por dentro ModificarDatos() y CambiarOrden() aplicando tus validaciones de negocio
            var nuevaCategoria = new Categoria(
                id: Guid.NewGuid(),
                nombre: TxtNombre.Text.Trim(),
                descripcion: TxtDescripcion.Text.Trim(),
                ordenDisplay: ordenVisual
            );

            // 3. Persistimos en la base de datos
            _context.Categorias.Add(nuevaCategoria);
            await _context.SaveChangesAsync();

            MessageBox.Show("Categoría guardada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true; // Setea éxito para avisar a la grilla que refresque
            this.Close();
        }
        catch (ArgumentException ex)
        {
            // Captura las excepciones controladas del dominio de Categoria
            MessageBox.Show($"Regla de negocio infringida:\n{ex.Message}", "Validación de Dominio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error crítico al guardar la categoría:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }
}