using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using PosVentas.Domain.Entities;
using PosVentas.Infrastructure.Persistence.Contexts;

namespace PosVentas.Desktop;

public partial class CajasView : UserControl
{
    private readonly ApplicationDbContext _context;

    public CajasView(ApplicationDbContext context)
    {
        InitializeComponent();
        _context = context ?? throw new ArgumentNullException(nameof(context));

        this.Loaded += CajasView_Loaded;
    }

    private async void CajasView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Traemos las cajas físicas activas junto con los datos de su última sesión abierta o en curso
            var listaCajas = await _context.Cajas
                .Where(c => c.EstaActivo)
                .OrderBy(c => c.Nombre)
                .Select(c => new CajaFilaDto
                {
                    Id = c.Id,
                    Nombre = c.Nombre,
                    ComputadoraAsignada = c.ComputadoraAsignada,
                    EstaAbierta = c.EstaAbierta,

                    // Extraemos los datos dinámicos de la sesión si es que está abierta
                    CajeroActual = c.Sesiones.Where(s => s.EnCurso).Select(s => s.UsuarioApertura).FirstOrDefault() ?? "---",
                    MontoApertura = c.Sesiones.Where(s => s.EnCurso).Select(s => (decimal?)s.MontoApertura).FirstOrDefault() ?? 0.00m,
                    FechaApertura = c.Sesiones.Where(s => s.EnCurso).Select(s => (DateTime?)s.FechaApertura).FirstOrDefault()
                })
                .AsNoTracking()
                .ToListAsync();

            GridCajas.ItemsSource = listaCajas;
            ControlarBotonera();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al cargar el panel de control de cajas:\n{ex.Message}",
                            "Error de Carga", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GridCajas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ControlarBotonera();
    }

    private void ControlarBotonera()
    {
        if (GridCajas.SelectedItem is CajaFilaDto seleccion)
        {
            // Habilitamos o deshabilitamos botones según el estado de la terminal
            BtnAbrirCaja.IsEnabled = !seleccion.EstaAbierta;
            BtnCerrarCaja.IsEnabled = seleccion.EstaAbierta;
        }
        else
        {
            // Sin selección no se puede realizar ninguna acción
            BtnAbrirCaja.IsEnabled = false;
            BtnCerrarCaja.IsEnabled = false;
        }
    }

    private void BtnAbrirCaja_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridCajas.SelectedItem is CajaFilaDto seleccion)
            {
                // Instanciamos el formulario pasándole las dependencias requeridas
                var ventanaApertura = new AperturaCajaWindow(_context, seleccion.Id, seleccion.Nombre)
                {
                    Owner = Window.GetWindow(this) // Seteamos el MainWindow como dueño para que se centre prolijo
                };

                // Si el usuario confirma la apertura con éxito, recargamos la grilla
                if (ventanaApertura.ShowDialog() == true)
                {
                    CajasView_Loaded(this, null!); // Refresca los estados
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al intentar abrir el turno:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCerrarCaja_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridCajas.SelectedItem is CajaFilaDto seleccion)
            {
                var ventanaCierre = new CierreCajaWindow(_context, seleccion.Id, seleccion.Nombre)
                {
                    Owner = Window.GetWindow(this)
                };

                if (ventanaCierre.ShowDialog() == true)
                {
                    CajasView_Loaded(this, null!); // Refresca la grilla mostrando la caja cerrada
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al intentar abrir el arqueo:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

// DTO optimizado local para no sobrecargar el modelo de dominio
public class CajaFilaDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? ComputadoraAsignada { get; set; }
    public bool EstaAbierta { get; set; }
    public string CajeroActual { get; set; } = "---";
    public decimal MontoApertura { get; set; }
    public DateTime? FechaApertura { get; set; }
}