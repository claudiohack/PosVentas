using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class CierreCajaWindow : Window
{
    private readonly ApplicationDbContext _context;
    private readonly Guid _cajaId;
    private readonly string _nombreCaja;
    private CajaSesion? _sesionActiva;

    public CierreCajaWindow(ApplicationDbContext context, Guid cajaId, string nombreCaja)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _cajaId = cajaId;
        _nombreCaja = nombreCaja;

        TxtTituloCierre.Text = $"Arqueo y Cierre: {nombreCaja}";
        CargarDatosSesion();
    }

    private async void CargarDatosSesion()
    {
        try
        {
            // Buscamos la sesión que está abierta actualmente para esta caja
            _sesionActiva = await _context.CajaSesiones
                .Where(s => s.CajaId == _cajaId && s.EnCurso)
                .FirstOrDefaultAsync();

            if (_sesionActiva == null)
            {
                MessageBox.Show("No se encontró ninguna sesión activa para cerrar en esta caja.",
                                "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                this.DialogResult = false;
                this.Close();
                return;
            }

            // Poblamos los datos informativos en la pantalla
            TxtCajero.Text = _sesionActiva.UsuarioApertura;
            TxtFechaApertura.Text = _sesionActiva.FechaApertura.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            TxtFondoInicial.Text = _sesionActiva.MontoApertura.ToString("C");

            // --- CÁLCULO DEL TEÓRICO EN SISTEMA ---
            // Cuando tengamos el módulo de Ventas, acá sumaríamos: Fondo Inicial + Sum(Ventas de esta Sesión)
            decimal totalVentas = 0.00m;
            decimal montoSistema = _sesionActiva.MontoApertura + totalVentas;

            TxtMontoSistema.Text = montoSistema.ToString("F2");
            TxtMontoReal.Text = montoSistema.ToString("F2"); // Sugerimos el valor por defecto

            CalcularDiferencia();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al recuperar los datos del turno:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CalcularDiferencia()
    {
        // CONTROL DE SEGURIDAD: Si la ventana se está inicializando y el control visual
        // todavía es nulo en memoria, salimos inmediatamente para evitar el crash.
        if (TxtDiferencia == null || TxtMontoSistema == null || TxtMontoReal == null)
        {
            return;
        }

        if (!decimal.TryParse(TxtMontoSistema.Text, out decimal sistema) ||
            !decimal.TryParse(TxtMontoReal.Text, out decimal real))
        {
            return;
        }

        decimal diferencia = real - sistema;
        TxtDiferencia.Text = diferencia.ToString("C");

        // Cambiamos el color de la diferencia para alertar al operador de forma visual
        if (diferencia == 0)
        {
            TxtDiferencia.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#27AE60")); // Verde: Caja perfecta
        }
        else if (diferencia > 0)
        {
            TxtDiferencia.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2980B9")); // Azul: Sobrante
            TxtDiferencia.Text = $"+{TxtDiferencia.Text} (Sobrante)";
        }
        else
        {
            TxtDiferencia.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0392B")); // Rojo: Faltante
            TxtDiferencia.Text = $"{TxtDiferencia.Text} (Faltante)";
        }
    }

    private async void BtnConfirmarCierre_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_sesionActiva == null) return;

            // 1. Validar montos ingresados
            if (!decimal.TryParse(TxtMontoReal.Text, out decimal montoReal) || montoReal < 0)
            {
                MessageBox.Show("Por favor, ingrese un monto real físico válido.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal montoSistema = decimal.Parse(TxtMontoSistema.Text);

            // Confirmación de seguridad si hay descuadres de caja
            if (montoReal != montoSistema)
            {
                var result = MessageBox.Show("El monto declarado no coincide con el sistema. ¿Desea cerrar la caja de todos modos registrando la diferencia en el arqueo?",
                    "Advertencia de Descuadre", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel) return;
            }

            // 2. Traer la caja física para cambiar su estado global
            var cajaDb = await _context.Cajas.FindAsync(_cajaId);
            if (cajaDb == null) return;

            // 3. Ejecutar comportamiento seguro desde las entidades de dominio
            cajaDb.MarcarComoCerrada();
            _sesionActiva.CerrarSesion(
                usuarioCierre: "Cajero Supervisor",
                montoCierreSistema: montoSistema,
                montoCierreReal: montoReal,
                observaciones: TxtObservaciones.Text
            );

            // 4. Guardar cambios de forma transaccional en SQL Server
            _context.Cajas.Update(cajaDb);
            _context.CajaSesiones.Update(_sesionActiva);
            await _context.SaveChangesAsync();

            MessageBox.Show($"¡Turno de la {_nombreCaja} cerrado y blindado con éxito!", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

            this.DialogResult = true;
            this.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error crítico al intentar cerrar la caja:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        this.DialogResult = false;
        this.Close();
    }

    // --- EVENTOS INTERACTIVOS ---
    private void TxtMontoReal_TextChanged(object sender, TextChangedEventArgs e)
    {
        CalcularDiferencia();
    }

    private void TxtMontoReal_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtMontoReal.Text == "0.00" || TxtMontoReal.Text == "0")
            TxtMontoReal.Text = string.Empty;
    }

    private void TxtMontoReal_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtMontoReal.Text))
            TxtMontoReal.Text = "0.00";
        else if (decimal.TryParse(TxtMontoReal.Text, out decimal valor))
            TxtMontoReal.Text = valor.ToString("F2");
    }
}