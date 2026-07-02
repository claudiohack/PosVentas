using System;
using System.Windows;
using System.Windows.Controls;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class NuevoClienteWindow : Window
{
    private readonly ApplicationDbContext _context;
    private readonly Cliente? _clienteExistente;
    private readonly bool _esEdicion;

    // CONSTRUCTOR PARA MODO ALTA
    public NuevoClienteWindow(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _esEdicion = false;
        LblTitulo.Text = "Nuevo Cliente";
    }

    // CONSTRUCTOR PARA MODO EDICIÓN
    public NuevoClienteWindow(ApplicationDbContext context, Cliente cliente) : this(context)
    {
        _clienteExistente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        _esEdicion = true;
        LblTitulo.Text = "Modificar Cliente";
        MapearEntidadAlFormulario();
    }

    private void MapearEntidadAlFormulario()
    {
        if (_clienteExistente == null) return;

        TxtRazonSocial.Text = _clienteExistente.RazonSocial;
        TxtDocumento.Text = _clienteExistente.Documento;
        TxtTelefono.Text = _clienteExistente.Telefono;
        TxtEmail.Text = _clienteExistente.Email;
        TxtDireccion.Text = _clienteExistente.Direccion;

        ChkPermiteCtaCte.IsChecked = _clienteExistente.PermiteCuentaCorriente;
        TxtLimiteCredito.Text = _clienteExistente.LimiteCredito.ToString("F2");
        TxtLimiteCredito.IsEnabled = _clienteExistente.PermiteCuentaCorriente;

        // Seleccionamos la responsabilidad de IVA correspondiente en el ComboBox
        foreach (ComboBoxItem item in CboIva.Items)
        {
            if (item.Content.ToString() == _clienteExistente.TipoResponsabilidadIva)
            {
                CboIva.SelectedItem = item;
                break;
            }
        }
    }

    private async void BtnGuardar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // --- VALIDACIONES DE INTERFAZ DE USUARIO ---
            if (string.IsNullOrWhiteSpace(TxtRazonSocial.Text))
            {
                MessageBox.Show("El campo Nombre / Razón Social es obligatorio.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtRazonSocial.Focus();
                return;
            }

            // Validamos que el límite de crédito sea un número válido si está habilitada la Cta Cte
            decimal limiteCredito = 0.00m;
            if (ChkPermiteCtaCte.IsChecked == true)
            {
                if (!decimal.TryParse(TxtLimiteCredito.Text, out limiteCredito) || limiteCredito < 0)
                {
                    MessageBox.Show("Por favor, ingrese un monto de límite de crédito válido y mayor o igual a cero.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtLimiteCredito.Focus();
                    return;
                }
            }

            string tipoIva = ((ComboBoxItem)CboIva.SelectedItem).Content.ToString()!;

            if (_esEdicion)
            {
                // --- MODO EDICIÓN ---
                // Buscamos la entidad con seguimiento (tracking) activo del contexto para poder actualizarla de manera segura
                var clienteDb = await _context.Clientes.FindAsync(_clienteExistente!.Id);

                if (clienteDb != null)
                {
                    // Invocamos el comportamiento del dominio para actualizar los datos de manera consistente
                    clienteDb.ActualizarDatosPersonales(
                        TxtRazonSocial.Text,
                        TxtDocumento.Text,
                        tipoIva,
                        TxtTelefono.Text,
                        TxtEmail.Text,
                        TxtDireccion.Text
                    );

                    clienteDb.ConfigurarCredito(ChkPermiteCtaCte.IsChecked ?? false, limiteCredito);

                    _context.Clientes.Update(clienteDb);
                }
            }
            else
            {
                // --- MODO ALTA ---
                // Instanciamos el nuevo objeto de dominio generando un nuevo Guid identificador
                var nuevoCliente = new Cliente(
                    Guid.NewGuid(),
                    TxtRazonSocial.Text,
                    TxtDocumento.Text,
                    tipoIva,
                    TxtTelefono.Text,
                    TxtEmail.Text,
                    TxtDireccion.Text,
                    ChkPermiteCtaCte.IsChecked ?? false,
                    limiteCredito
                );

                await _context.Clientes.AddAsync(nuevoCliente);
            }

            // Persistimos físicamente los cambios en SQL Server
            await _context.SaveChangesAsync();

            MessageBox.Show("Los datos del cliente se guardaron correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true; // Cierra la ventana e indica éxito a la vista que la invocó
            this.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al intentar guardar el cliente:\n{ex.Message}", "Error Guardado", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }

    // --- MANEJO DE INTERFAZ INTUITIVA (Habilitar/Deshabilitar TextBox según Checkbox) ---
    private void ChkPermiteCtaCte_Checked(object sender, RoutedEventArgs e)
    {
        if (TxtLimiteCredito != null)
        {
            TxtLimiteCredito.IsEnabled = true;
            TxtLimiteCredito.Focus();
        }
    }

    private void ChkPermiteCtaCte_Unchecked(object sender, RoutedEventArgs e)
    {
        if (TxtLimiteCredito != null)
        {
            TxtLimiteCredito.IsEnabled = false;
            TxtLimiteCredito.Text = "0.00"; // Reseteamos visualmente el valor a cero
        }
    }
}