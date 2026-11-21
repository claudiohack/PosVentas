using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PosVentas.Client;
using PosVentas.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Vacío = la API está en el mismo origen (detrás del reverse proxy en Docker / producción).
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] is { Length: > 0 } configurada
    ? configurada
    : builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddSingleton<SesionService>();
builder.Services.AddScoped<PosVentasApi>();

await builder.Build().RunAsync();
