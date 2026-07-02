using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class ClientesView : UserControl
{
    private readonly ApplicationDbContext _context;

    public ClientesView(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));

        // Nos colgamos del evento Loaded para disparar la consulta asíncrona a la base de datos
        this.Loaded += ClientesView_Loaded;
    }

    private async void ClientesView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // ESCALAMIENTO COMERCIAL: Traemos solo los clientes activos. 
            // Usamos AsNoTracking para maximizar el rendimiento de la lectura en grillas.
            var listaClientes = await _context.Clientes
                .Where(c => c.EstaActivo)
                .OrderBy(c => c.RazonSocial)
                .AsNoTracking()
                .ToListAsync();

            // Seteamos el origen de datos al DataGrid visual
            GridClientes.ItemsSource = listaClientes;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al cargar los clientes:\n{ex.Message}",
                            "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnNuevo_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Instanciamos la ventana de formulario pasándole el contexto (Modo Alta)
            var ventanaNuevo = new NuevoClienteWindow(_context);
            ventanaNuevo.Owner = Window.GetWindow(this); // Queda anclada arriba de la pantalla principal

            // Si retorna true (guardado con éxito), refrescamos la grilla al toque
            if (ventanaNuevo.ShowDialog() == true)
            {
                ClientesView_Loaded(this, null!);
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
            // Validamos que haya una fila seleccionada y que corresponda al tipo de objeto Cliente
            if (GridClientes.SelectedItem is Cliente clienteSeleccionado)
            {
                // Instanciamos la misma ventana pero pasándole la entidad (Modo Edición)
                var ventanaEditar = new NuevoClienteWindow(_context, clienteSeleccionado);
                ventanaEditar.Owner = Window.GetWindow(this);

                if (ventanaEditar.ShowDialog() == true)
                {
                    ClientesView_Loaded(this, null!);
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione un cliente de la lista para modificar.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (GridClientes.SelectedItem is Cliente clienteSeleccionado)
            {
                var resultadoConfirmacion = MessageBox.Show(
                    $"¿Está seguro de que desea eliminar al cliente '{clienteSeleccionado.RazonSocial}'?\nEsta acción mantendrá sus registros históricos de ventas intactos.",
                    "Confirmar Eliminación",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (resultadoConfirmacion == MessageBoxResult.Yes)
                {
                    // Buscamos la entidad fresca del contexto para que EF realice el seguimiento
                    var clienteDb = await _context.Clientes.FindAsync(clienteSeleccionado.Id);

                    if (clienteDb != null)
                    {
                        // --- BAJA LÓGICA DE DOMINIO ---
                        // Invocamos el comportamiento encapsulado para preservar la integridad referencial
                        clienteDb.Desactivar();

                        _context.Clientes.Update(clienteDb);
                        await _context.SaveChangesAsync();

                        MessageBox.Show("Cliente dado de baja con éxito.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                        // Refrescamos la grilla para ocultar el registro inactivo
                        ClientesView_Loaded(this, null!);
                    }
                }
            }
            else
            {
                MessageBox.Show("Por favor, seleccione el cliente que desea dar de baja.", "Atención", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al procesar la baja del cliente:\n{ex.Message}", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}