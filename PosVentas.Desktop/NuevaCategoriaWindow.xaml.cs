using System;
using System.Windows;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class NuevaCategoriaWindow : Window
{
    private readonly ApplicationDbContext _context;
    private readonly Categoria? _categoriaAEditar; // Si es nulo es Alta, sino es Edición.

    // Constructor para ALTA
    public NuevaCategoriaWindow(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        this.Loaded += NuevaCategoriaWindow_Loaded;
    }

    // Constructor para EDICIÓN
    public NuevaCategoriaWindow(ApplicationDbContext context, Categoria categoria) : this(context)
    {
        _categoriaAEditar = categoria ?? throw new ArgumentNullException(nameof(categoria));
    }

    private void NuevaCategoriaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_categoriaAEditar == null)
        {
            // Inicialización Modo Alta
            Title = "Registrar Nueva Categoría";
            LblTitulo.Text = "📂 Nueva Categoría";
            BtnGuardar.Content = "💾 Guardar";
            TxtOrden.Text = "0";
        }
        else
        {
            // Inicialización Modo Edición
            Title = "Modificar Categoría";
            LblTitulo.Text = "✏️ Editar Categoría";
            BtnGuardar.Content = "💾 Actualizar";

            // Precarga de datos desde la entidad
            TxtNombre.Text = _categoriaAEditar.Nombre;
            TxtDescripcion.Text = _categoriaAEditar.Descripcion ?? string.Empty;
            TxtOrden.Text = _categoriaAEditar.OrdenDisplay.ToString();
        }
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

            if (!int.TryParse(TxtOrden.Text, out int ordenVisual) || ordenVisual < 0)
            {
                MessageBox.Show("El orden visual debe ser un número entero válido mayor o igual a cero.", "Orden inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_categoriaAEditar == null)
            {
                // MODO ALTA
                var nuevaCategoria = new Categoria(
                    id: Guid.NewGuid(),
                    nombre: TxtNombre.Text.Trim(),
                    descripcion: TxtDescripcion.Text.Trim(),
                    ordenDisplay: ordenVisual
                );

                _context.Categorias.Add(nuevaCategoria);
            }
            else
            {
                // MODO EDICIÓN
                // Adaptá estos métodos según los nombres que tengan en tu entidad Categoria de Dominio
                _categoriaAEditar.ModificarDatos(TxtNombre.Text.Trim(), TxtDescripcion.Text.Trim());
                _categoriaAEditar.CambiarOrden(ordenVisual);

                // Si implementaste la auditoría sugerida, acá llamás al método de actualización:
                // _categoriaAEditar.RegistrarModificacion();

                _context.Categorias.Update(_categoriaAEditar);
            }

            // 3. Persistimos los cambios en la base de datos de manera asíncrona
            await _context.SaveChangesAsync();

            MessageBox.Show(_categoriaAEditar == null ? "Categoría guardada con éxito." : "Categoría actualizada con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true; // Notifica éxito al invocador para refrescar la grilla
            this.Close();
        }
        catch (ArgumentException ex)
        {
            // Captura las excepciones de reglas de negocio infringidas en el Dominio
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