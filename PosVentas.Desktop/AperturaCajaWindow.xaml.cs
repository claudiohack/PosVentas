using System;
using System.Windows;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class AperturaCajaWindow : Window
{
    private readonly ApplicationDbContext _context;
    private readonly Guid _cajaId;
    private readonly string _nombreCaja;

    public AperturaCajaWindow(ApplicationDbContext context, Guid cajaId, string nombreCaja)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cajaId = cajaId;
        _nombreCaja = nombreCaja;

        TxtTituloCaja.Text = $"Apertura de Turno: {nombreCaja}";
        TxtFechaHora.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
    }

    private async void BtnConfirmar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 1. Validar el monto de apertura
            if (!decimal.TryParse(TxtMontoApertura.Text, out decimal montoApertura) || montoApertura < 0)
            {
                MessageBox.Show("Por favor, ingrese un monto de apertura válido (mayor o igual a cero).",
                                "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtMontoApertura.Focus();
                return;
            }

            // 2. Traer la caja física de la base de datos para modificar su estado
            var cajaDb = await _context.Cajas.FindAsync(_cajaId);
            if (cajaDb == null)
            {
                MessageBox.Show("La caja seleccionada no existe en la base de datos.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 3. Modificar el estado de la caja y crear la sesión (Turno)
            // El dominio se encarga de tirar excepción si la caja ya estuviera abierta.
            cajaDb.MarcarComoAbierta();

            var nuevaSesion = new CajaSesion(
                id: Guid.NewGuid(),
                cajaId: _cajaId,
                usuarioApertura: "Cajero Supervisor", // Hardcodeado hasta tener módulo de Usuarios e identidad
                montoApertura: montoApertura
            );

            // 4. Guardar transaccionalmente en SQL Server
            _context.Cajas.Update(cajaDb);
            await _context.CajaSesiones.AddAsync(nuevaSesion);
            await _context.SaveChangesAsync();

            MessageBox.Show($"¡Turno iniciado con éxito en la {_nombreCaja}!", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true;
            this.Close();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Control de Negocio", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error crítico al intentar abrir la caja:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }

    // --- EFECTOS VISUALES INTUITIVOS ---
    private void TxtMontoApertura_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtMontoApertura.Text == "0.00" || TxtMontoApertura.Text == "0")
        {
            TxtMontoApertura.Text = string.Empty;
        }
    }

    private void TxtMontoApertura_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtMontoApertura.Text))
        {
            TxtMontoApertura.Text = "0.00";
        }
        else if (decimal.TryParse(TxtMontoApertura.Text, out decimal valor))
        {
            TxtMontoApertura.Text = valor.ToString("F2");
        }
    }
}