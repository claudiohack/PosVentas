using System;
using System.Windows;
using System.Windows.Controls;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class NuevoProveedorWindow : Window
{
    private readonly ApplicationDbContext _context;
    private readonly Proveedor? _proveedorExistente;
    private readonly bool _esEdicion;

    // CONSTRUCTOR PARA MODO ALTA
    public NuevoProveedorWindow(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _esEdicion = false;
        LblTitulo.Text = "Nuevo Proveedor";
    }

    // CONSTRUCTOR PARA MODO EDICIÓN
    public NuevoProveedorWindow(ApplicationDbContext context, Proveedor proveedor) : this(context)
    {
        _proveedorExistente = proveedor ?? throw new ArgumentNullException(nameof(proveedor));
        _esEdicion = true;
        LblTitulo.Text = "Modificar Proveedor";
        MapearEntidadAlFormulario();
    }

    private void MapearEntidadAlFormulario()
    {
        if (_proveedorExistente == null) return;

        TxtRazonSocial.Text = _proveedorExistente.RazonSocial;
        TxtCuit.Text = _proveedorExistente.Cuit;
        TxtNombreContacto.Text = _proveedorExistente.NombreContacto;
        TxtTelefono.Text = _proveedorExistente.Telefono;
        TxtEmail.Text = _proveedorExistente.Email;
        TxtDireccion.Text = _proveedorExistente.Direccion;

        ChkPermiteCtaCte.IsChecked = _proveedorExistente.PermiteCuentaCorriente;
        TxtDiasPlazo.Text = _proveedorExistente.DiasPlazoPago.ToString();
        TxtDiasPlazo.IsEnabled = _proveedorExistente.PermiteCuentaCorriente;

        // Seleccionar responsabilidad de IVA correspondiente
        foreach (ComboBoxItem item in CboIva.Items)
        {
            if (item.Content.ToString() == _proveedorExistente.TipoResponsabilidadIva)
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
            // --- VALIDACIONES DE INTERFAZ ---
            if (string.IsNullOrWhiteSpace(TxtRazonSocial.Text))
            {
                MessageBox.Show("El campo Razón Social / Empresa es obligatorio.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtRazonSocial.Focus();
                return;
            }

            int diasPlazo = 0;
            if (ChkPermiteCtaCte.IsChecked == true)
            {
                if (!int.TryParse(TxtDiasPlazo.Text, out diasPlazo) || diasPlazo < 0)
                {
                    MessageBox.Show("Por favor, ingrese una cantidad de días válida (entero mayor o igual a cero).", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtDiasPlazo.Focus();
                    return;
                }
            }

            string tipoIva = ((ComboBoxItem)CboIva.SelectedItem).Content.ToString()!;

            if (_esEdicion)
            {
                // --- MODO EDICIÓN ---
                var proveedorDb = await _context.Proveedores.FindAsync(_proveedorExistente!.Id);

                if (proveedorDb != null)
                {
                    // Comportamiento seguro de dominio
                    proveedorDb.ActualizarDatosComerciales(
                        TxtRazonSocial.Text,
                        TxtCuit.Text,
                        tipoIva,
                        TxtTelefono.Text,
                        TxtEmail.Text,
                        TxtDireccion.Text,
                        TxtNombreContacto.Text
                    );

                    proveedorDb.ConfigurarCreditoCompra(ChkPermiteCtaCte.IsChecked ?? false, diasPlazo);

                    _context.Proveedores.Update(proveedorDb);
                }
            }
            else
            {
                // --- MODO ALTA ---
                var nuevoProveedor = new Proveedor(
                    Guid.NewGuid(),
                    TxtRazonSocial.Text,
                    TxtCuit.Text,
                    tipoIva,
                    TxtTelefono.Text,
                    TxtEmail.Text,
                    TxtDireccion.Text,
                    TxtNombreContacto.Text,
                    ChkPermiteCtaCte.IsChecked ?? false,
                    diasPlazo
                );

                await _context.Proveedores.AddAsync(nuevoProveedor);
            }

            // Guardamos físicamente en SQL Server
            await _context.SaveChangesAsync();

            MessageBox.Show("Los datos del proveedor se guardaron correctamente.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true;
            this.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al intentar guardar el proveedor:\n{ex.Message}", "Error Guardado", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }

    // --- MANEJO DE INTERFAZ INTUITIVA ---
    private void ChkPermiteCtaCte_Checked(object sender, RoutedEventArgs e)
    {
        if (TxtDiasPlazo != null)
        {
            TxtDiasPlazo.IsEnabled = true;
            TxtDiasPlazo.Focus();
        }
    }

    private void ChkPermiteCtaCte_Unchecked(object sender, RoutedEventArgs e)
    {
        if (TxtDiasPlazo != null)
        {
            TxtDiasPlazo.IsEnabled = false;
            TxtDiasPlazo.Text = "0";
        }
    }
}