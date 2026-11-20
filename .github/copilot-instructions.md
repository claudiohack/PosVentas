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
8. **Seguridad**: contraseñas con BCrypt; JWT HS256 con claims `uid`, `tid`, `role`, `sst`; política de respaldo que exige usuario autenticado con comercio.
   - Roles: `Administrador` (todo, incluida la gestión de usuarios), `Supervisor` (mostrador + cierre/arqueo de caja), `Cajero` (vende, abre caja, cobra).
   - Políticas: `SoloAdministrador`, `Supervision` (Admin + Supervisor), `Operador` (los tres roles). Todo endpoint nuevo declara la suya.
   - Comandos solo para administradores implementan `IRequiereAdministrador` (lo verifica `AutorizacionBehavior`, devuelve 403).
   - `ValidacionSesionMiddleware` revoca el token si el usuario se desactivó o cambió su rol o contraseña (el `SecurityStamp` del usuario se renueva). No saltear ese middleware.
   - Las restricciones de la UI Blazor (`SesionService.EsAdministrador` / `PuedeSupervisar`) son solo visuales: la API siempre vuelve a verificar.
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
- Facturación electrónica ARCA (WSFEv1) vía `IAfipService`; los tests usan `AfipServiceFalso` y nunca salen a internet.
  - Las validaciones fiscales (letra según condición IVA de emisor y receptor, CUIT para Factura A, identificación del consumidor final desde `Facturacion:MontoMinimoIdentificacionConsumidorFinal`) corren **antes** de persistir la venta: error de datos → 400 sin efectos.
  - Si ARCA rechaza (400, con `erroresAfip` y `ventaId`) o no responde (503), la venta ya registrada queda `EstadoFacturacion.Pendiente` y se reintenta con `POST /api/ventas/{id}/facturar`. Nunca se anula la venta por un problema de ARCA.
  - Cada solicitud queda en `ComprobanteFiscal` (aprobada o rechazada). Índices únicos: un aprobado por venta y numeración por tenant + tipo + punto de venta.
  - El certificado y el ticket de acceso de WSAA se guardan cifrados con Data Protection; la API nunca los devuelve. En producción el anillo de claves va fuera del servidor (`DataProtection:DirectorioClaves` o Key Vault).
- El vuelto solo se entrega en efectivo.
- La venta a cuenta corriente requiere un cliente con la cuenta habilitada y no puede superar su límite de crédito.
- El cierre y arqueo de caja lo hacen solo supervisores y administradores.
- Compras: el stock solo ingresa por `Compra.AgregarItem` → `Producto.IngresarStock(cantidad, costo)`, que actualiza el costo por promedio ponderado y recalcula el precio de venta con el margen del producto.
- Una compra al contado (efectivo / transferencia) con caja abierta genera un `MovimientoCaja` tipo `PagoProveedor` con monto **negativo**; el arqueo lo descuenta solo. No se puede pagar en efectivo más de lo que hay en la caja.
- El mismo comprobante de un proveedor no se carga dos veces (409). El CUIT del proveedor es único por comercio.
- Stock bajo = producto activo con `StockActual <= StockMinimo`.
