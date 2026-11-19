# Instrucciones para asistentes de código — PosVentas (HS Tech)

PosVentas es un SaaS B2B multi-tenant de punto de venta, inventario y cuentas corrientes.
Cada comercio cliente es un **tenant**: sus datos jamás deben ser visibles ni modificables por otro comercio.

## Stack

- **.NET 10 (LTS)** en todos los proyectos. Paquetes de Microsoft en la serie 10.0.x (los 11.x son para .NET 11).
- **SQL Server** vía **Entity Framework Core 10**, con migraciones Code-First.
- **ASP.NET Core WebAPI** con controllers, MediatR 12.5 (última versión Apache-2.0) y FluentValidation.
- **Blazor WebAssembly** standalone como cliente de mostrador.
- **xUnit + WebApplicationFactory** para tests de integración contra SQL Server real.

## Estructura (Clean Architecture)

```
src/
  PosVentas.Domain          Entidades, value objects, reglas de negocio. Sin dependencias externas.
  PosVentas.Application     Casos de uso (MediatR), validadores, DTOs, interfaces (puertos).
  PosVentas.Infrastructure  ApplicationDbContext, configuraciones EF, migraciones, BCrypt, JWT.
  PosVentas.WebAPI          Controllers, middlewares, autenticación/autorización, DI.
  PosVentas.Client          Blazor WASM. Solo habla con la WebAPI por HTTP.
tests/
  PosVentas.IntegrationTests
```

Dependencias permitidas: Domain ← Application ← Infrastructure ← WebAPI. El Client no referencia a ningún otro proyecto.

## Reglas de oro

1. **Toda entidad de negocio hereda de `TenantEntity`** (`Entity<Guid>` + `ITenantEntity`). La única excepción es `Comercio`, que *es* el tenant.
2. **Nunca filtrar por `TenantId` a mano.** `ApplicationDbContext` aplica `HasQueryFilter` a toda `ITenantEntity` y el `TenantSaveChangesInterceptor` asigna y protege el `TenantId` al guardar. Un recurso de otro comercio simplemente "no existe": responder 404, no 403.
3. **No usar `IgnoreQueryFilters()`** salvo con aprobación explícita y un test que lo justifique.
4. **El `TenantId` y el `UsuarioId` salen siempre del JWT** (claims `tid` y `uid`), nunca del body ni de la query string. `ICurrentTenantService.EstablecerTenant` solo se usa en flujos anónimos (login y alta de comercio).
5. **El dominio protege sus invariantes**: setters privados, fábricas estáticas y métodos explícitos (`Producto.DescontarStock`, `CajaSesion.Cerrar`, `Cliente.RegistrarCargo`…). Prohibido modificar estado por reflexión.
6. **Validación de entrada con FluentValidation** en cada comando/query. Las reglas que dependen del estado van en el dominio.
7. **Errores como ProblemDetails** vía `GlobalExceptionMiddleware`: validación → 400, credenciales → 401, acceso entre comercios → 403, no encontrado → 404, conflicto/concurrencia → 409, regla de dominio → 422. Los 500 no exponen detalles internos.
8. **Seguridad**: contraseñas con BCrypt; JWT HS256 con claims `uid`, `tid`, `role`; políticas `SoloAdministrador` y `Operador`; política de respaldo que exige usuario autenticado con comercio.
9. **Cero secretos en el repositorio.** `Jwt:SigningKey` vive en User Secrets (desarrollo) o en la variable de entorno `Jwt__SigningKey` (producción). Las cadenas de conexión productivas también van por variables de entorno.
10. **Migraciones estrictas**: todo cambio de esquema se hace con `dotnet ef migrations add` y se versiona. Nunca editar la base a mano ni reescribir migraciones ya publicadas.
11. **Concurrencia optimista** con `RowVersion` en entidades que se modifican en paralelo (`Producto`, `Caja`, `CajaSesion`, `Cliente`).
12. **Cero advertencias**: `TreatWarningsAsErrors` y la auditoría NuGet están activos en `Directory.Build.props`. No desactivarlos.
13. **Tests de integración obligatorios** para cada endpoint nuevo, incluyendo al menos un caso de aislamiento entre dos comercios.

## Comandos habituales

```powershell
dotnet build PosVentas.sln
dotnet test PosVentas.sln
dotnet ef migrations add <Nombre> --project src/PosVentas.Infrastructure --startup-project src/PosVentas.WebAPI --output-dir Persistence/Migrations
dotnet ef database update --project src/PosVentas.Infrastructure --startup-project src/PosVentas.WebAPI
```

## Reglas de negocio vigentes

- La apertura de caja nunca es automática: siempre requiere una acción explícita del usuario.
- Solo se emiten tickets internos hasta integrar la facturación electrónica (ARCA).
- El vuelto solo se entrega en efectivo.
- La venta a cuenta corriente requiere un cliente con la cuenta habilitada y no puede superar su límite de crédito.
