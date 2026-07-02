using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PosVentas.Infrastructure.Persistence.Contexts;
using PosVentas.Domain.Entities;
using PosVentas.Domain.ValueObjects;

namespace PosVentas.Desktop;

public partial class App : System.Windows.Application
{
    public static IHost? AppHost { get; private set; }

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory());
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                string connectionString = context.Configuration.GetConnectionString("CentralConnection")
                    ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'CentralConnection'.");

                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(connectionString));

                services.AddSingleton<MainWindow>();
                services.AddTransient<ProductosView>();
                services.AddTransient<CategoriasView>();

                // MODIFICADO: Registramos el nuevo módulo de clientes en el contenedor de dependencias
                services.AddTransient<ClientesView>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost!.StartAsync();

        using (var scope = AppHost.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                if (await context.Database.CanConnectAsync())
                {
                    // Si no hay categorías, creamos la primera obligatoriamente
                    var categoriaSemilla = await context.Categorias.FirstOrDefaultAsync();
                    if (categoriaSemilla == null)
                    {
                        categoriaSemilla = new Categoria(
                            id: Guid.NewGuid(),
                            nombre: "General",
                            descripcion: "Categoría por defecto del sistema",
                            ordenDisplay: 0
                        );

                        context.Categorias.Add(categoriaSemilla);
                        await context.SaveChangesAsync();
                    }

                    // Si no hay productos, creamos el testigo usando el nuevo constructor de Dominio
                    if (!await context.Productos.AnyAsync())
                    {
                        var productoSemilla = new Producto(
                            id: Guid.NewGuid(),
                            sku: new Sku("PROD-001"),
                            nombre: "Producto Testigo de Prueba",
                            precioCompra: new Money(1000.00m), // Costo Base
                            porcentajeGanancia: 30.0m,         // 30% Margen (Venta automático a $1300)
                            porcentajeIva: 21.0m,
                            categoriaId: categoriaSemilla.Id,
                            venderSinStock: true
                        );

                        context.Productos.Add(productoSemilla);
                        await context.SaveChangesAsync();
                    }

                    // MODIFICADO (ESCALAMIENTO): Si no hay clientes, creamos el "Consumidor Final" por defecto
                    if (!await context.Clientes.AnyAsync())
                    {
                        var clienteDefecto = new Cliente(
                            id: Guid.NewGuid(),
                            razonSocial: "Consumidor Final",
                            documento: "99-99999999-9",
                            tipoResponsabilidadIva: "Consumidor Final",
                            telefono: null,
                            email: null,
                            direccion: null,
                            permiteCuentaCorriente: false,
                            limiteCredito: 0.00m
                        );

                        context.Clientes.Add(clienteDefecto);
                        await context.SaveChangesAsync();
                    }
                }
                else
                {
                    MessageBox.Show("No se pudo conectar a la base de datos SQL Server. Verifique que el servicio SQLEXPRESS esté corriendo.",
                                    "Error de Conexión", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ocurrió un error al inicializar la base de datos:\n{ex.Message}",
                                "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }
        }

        var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (AppHost != null)
        {
            await AppHost.StopAsync();
        }
        base.OnExit(e);
    }
}