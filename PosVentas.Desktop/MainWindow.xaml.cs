using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;

namespace PosVentas.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void BtnDashboard_Click(object sender, RoutedEventArgs e)
    {
        ActualizarEstiloBotones(BtnDashboard);
        MainContentArea.Content = null;
    }

    private void BtnProductos_Click(object sender, RoutedEventArgs e)
    {
        ActualizarEstiloBotones(BtnProductos);

        if (App.AppHost != null)
        {
            var vistaProductos = App.AppHost.Services.GetRequiredService<ProductosView>();
            MainContentArea.Content = vistaProductos;
        }
    }

    private void BtnCategorias_Click(object sender, RoutedEventArgs e)
    {
        ActualizarEstiloBotones(BtnCategorias);

        // Ahora resolvemos y cargamos la vista real
        if (App.AppHost != null)
        {
            var vistaCategorias = App.AppHost.Services.GetRequiredService<CategoriasView>();
            MainContentArea.Content = vistaCategorias;
        }
    }

    private void BtnVentas_Click(object sender, RoutedEventArgs e)
    {
        ActualizarEstiloBotones(BtnVentas);
    }

    private void BtnClientes_Click(object sender, RoutedEventArgs e)
    {
        ActualizarEstiloBotones(BtnClientes);
    }

    private void BtnUsuarios_Click(object sender, RoutedEventArgs e)
    {
        ActualizarEstiloBotones(BtnUsuarios);
    }

    private void ActualizarEstiloBotones(Button botonSeleccionado)
    {
        // 1. Limpiamos estilos incluyendo el nuevo botón de Categorías
        ResetearEstiloBoton(BtnDashboard);
        ResetearEstiloBoton(BtnProductos);
        ResetearEstiloBoton(BtnCategorias);
        ResetearEstiloBoton(BtnVentas);
        ResetearEstiloBoton(BtnClientes);
        ResetearEstiloBoton(BtnUsuarios);

        // 2. Resaltamos el botón activo
        botonSeleccionado.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"));
        botonSeleccionado.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
    }

    private void ResetearEstiloBoton(Button boton)
    {
        if (boton != null)
        {
            boton.Background = Brushes.Transparent;
            boton.Foreground = Brushes.White;
        }
    }
}