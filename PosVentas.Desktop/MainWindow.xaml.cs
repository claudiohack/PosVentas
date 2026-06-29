using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PosVentas.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Enlazamos los eventos de clic a los métodos de abajo
        BtnDashboard.Click += BtnDashboard_Click;
        BtnProductos.Click += BtnProductos_Click;

        // Mensajes temporales para los botones que programaremos más adelante
        BtnVentas.Click += (s, e) => MessageBox.Show("Módulo de Ventas en desarrollo.", "Próximamente");
        BtnClientes.Click += (s, e) => MessageBox.Show("Módulo de Clientes en desarrollo.", "Próximamente");
        BtnUsuarios.Click += (s, e) => MessageBox.Show("Módulo de Usuarios en desarrollo.", "Próximamente");
    }

    private void BtnDashboard_Click(object sender, RoutedEventArgs e)
    {
        ActualizarEstiloBotones(BtnDashboard);

        // Volvemos a cargar la vista de bienvenida de forma dinámica
        var gridBienvenida = new Grid { Background = Brushes.White };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        stack.Children.Add(new TextBlock
        {
            Text = "¡Bienvenido al Sistema PosVentas!",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50")),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Seleccioná un módulo de la barra lateral para empezar a operar.",
            FontSize = 14,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        gridBienvenida.Children.Add(stack);
        MainContentArea.Content = gridBienvenida;
    }

    private void BtnProductos_Click(object sender, RoutedEventArgs e)
    {
        ActualizarEstiloBotones(BtnProductos);

        // Pantalla temporal para cuando hagamos el ABM de Productos
        var gridProductosTemp = new Grid { Background = Brushes.White };
        var texto = new TextBlock
        {
            Text = "📦 Aquí enterraremos el futuro ABM de Productos",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C3E50"))
        };
        gridProductosTemp.Children.Add(texto);

        // Cambiamos el contenido del contenedor central por esta nueva "pantalla"
        MainContentArea.Content = gridProductosTemp;
    }

    private void ActualizarEstiloBotones(Button botonActivo)
    {
        // Limpiamos el fondo de los botones para que queden transparentes
        BtnDashboard.Background = Brushes.Transparent;
        BtnProductos.Background = Brushes.Transparent;

        // Pintamos el botón que está activo para que el usuario sepa dónde está parado
        botonActivo.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34495E"));
    }
}
