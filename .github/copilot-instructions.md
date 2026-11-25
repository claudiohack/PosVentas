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
8. **Seguridad**: contraseñas con BCrypt; JWT HS256 con claims `uid`, `tid`, `role` (nombre del rol, informativo) y `sst`; política de respaldo que exige usuario autenticado con comercio.
   - **Autorización por permisos**, no por rol. El catálogo cerrado está en `Domain/Seguridad/Permisos.cs` ("módulo.acción"). Cada comercio tiene roles (`Rol`, tabla `RolesPermisos`) configurables en `/api/roles`.
   - Roles de sistema por comercio (`CodigoSistema`): `ADMINISTRADOR` (todos los permisos, presentes y futuros; inmutable), `SUPERVISOR` y `CAJERO` (permisos editables; no se renombran ni eliminan). Se pueden crear roles personalizados.
   - **Los permisos no viajan en el JWT**: `ValidacionSesionMiddleware` los lee de la base en cada request (una consulta junto con estado y `SecurityStamp`) y los deja en `SesionActual`. Un cambio de permisos rige en el próximo request; cambiar el rol de un usuario renueva su sello (vuelve a ingresar).
   - Todo endpoint nuevo declara `[AutorizarPermiso(Permisos.X)]` (varios códigos = cualquiera; varios atributos = todos). Las políticas `perm:...` las crea `PoliticasPermisoProvider`.
   - Los casos de uso sensibles llevan además `[RequierePermiso(Permisos.X)]` (lo verifica `AutorizacionBehavior` → 403).
   - **Sin escalada de privilegios** (`ReglasRoles`): nadie otorga permisos que no tiene, ni asigna/edita/resetea/da de baja usuarios de un rol con permisos que él no tiene; el rol Administrador solo lo administra un Administrador. El comercio siempre conserva un administrador activo.
   - Agregar un permiso: constante + `Permisos.Todos` + `CatalogoPermisos` (texto) + atributo en el endpoint. Los roles existentes no lo reciben solos (salvo el Administrador); si un rol de sistema debe tenerlo, otorgarlo en la migración. Nunca renombrar un código publicado.
   - **Auditoría** (`RegistroAuditoria`, solo inserción): `RegistradorAuditoria.Registrar(...)` en el mismo `SaveChanges` que el cambio. Cubre roles, usuarios, anulaciones de venta, apertura y cierre de caja (con el arqueo), productos (alta, cambio de costo o precio de venta, ingreso manual de stock, baja/alta), importaciones aplicadas (una entrada con el resumen; la vista previa no se audita) y configuración fiscal (nunca el contenido del certificado). `GET /api/auditoria` con `auditoria.consultar`. Toda operación nueva que mueva dinero, stock o precios debe auditarse; los códigos de acción viven en `AccionesAuditoria` y no se renombran.
   - Las restricciones de la UI Blazor (`SesionService.Tiene(...)`) son solo visuales: la API siempre vuelve a verificar. El cliente refresca permisos con `GET /api/auth/sesion` al navegar.
   - **Permisos efectivos = rol − módulos que el plan no incluye** (`SuscripcionVigente.FiltrarPermisos`), también para el Administrador: `TienePermiso` no tiene atajos. Ver "Suscripciones".
9. **Cero secretos en el repositorio.** `Jwt:SigningKey` vive en User Secrets (desarrollo) o en la variable de entorno `Jwt__SigningKey` (producción). Las cadenas de conexión productivas también van por variables de entorno.
10. **Migraciones estrictas**: todo cambio de esquema se hace con `dotnet ef migrations add` y se versiona. Nunca editar la base a mano ni reescribir migraciones ya publicadas.
11. **Concurrencia optimista** con `RowVersion` en entidades que se modifican en paralelo (`Producto`, `Caja`, `CajaSesion`, `Cliente`).
12. **Cero advertencias**: `TreatWarningsAsErrors` y la auditoría NuGet están activos en `Directory.Build.props`. No desactivarlos.
13. **Tests de integración obligatorios** para cada endpoint nuevo, incluyendo al menos un caso de aislamiento entre dos comercios.
14. **CI** (`.github/workflows/ci.yml`, GitHub Actions en Linux): cada PR a `main` compila en Release y corre todos los tests contra un SQL Server en contenedor (`POSVENTAS_TEST_CONNECTION`). No se mergea con la CI en rojo. Todo test debe funcionar en Linux: zonas horarias IANA, rutas con `Path.Combine`, nada de autenticación de Windows.

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
- Alta de comercios SOLO por invitación: `POST /api/auth/invitaciones` (política `AdministradorGlobal` = administrador del comercio con `EsPlataforma`, claim `plat=1`) y `POST /api/auth/registro-invitacion`. El token se entrega una vez; solo se persiste su hash SHA-256. El alta abierta `POST /api/auth/registro` está deshabilitada.
- El comercio operador (HS Tech) lo crea `InicializadorBaseDatos` desde `Plataforma:*` cuando `BaseDatos:InicializarAlArrancar=true` (Docker y Development); los tests lo llaman después de migrar.
- Suscripciones (modelo comercial del SaaS):
  - `Plan` (global, lo administra HS Tech en `/api/plataforma/planes`): precio mensual, `MaxUsuarios` / `MaxProductos` activos (null = sin límite) y módulos del catálogo cerrado `Domain/Suscripciones/ModulosPlan.cs` (facturacion, compras, reportes, planillas, auditoria). Lo que no es módulo viene en todos los planes. El `Codigo` no se edita; los planes de ejemplo (INICIAL, COMERCIO, FULL) los carga la migración `AgregarPlanesYSuscripciones`.
  - Un único plan `EsPredeterminado` (lo garantiza `ActualizarPlanCommand`): con él arranca la prueba gratuita de `Suscripcion.DiasPrueba` (30) días al registrarse un comercio. HS Tech (el comercio con `EsPlataforma`) no tiene suscripción ni límites.
  - El estado no se persiste: `Suscripcion.EstadoEn(ahora)` lo deriva de `VenceUtc` → Prueba/Activa → EnGracia (`DiasGracia` = 7, opera con aviso) → SoloLectura. La hora sale de `IReloj` (no del `TimeProvider` global), así los tests la adelantan con `RelojDePrueba`.
  - `SuscripcionActual` (scoped) la resuelve una vez por request. `ValidacionSesionMiddleware` recorta los permisos y, en SoloLectura, rechaza todo método que no sea GET/HEAD/OPTIONS con **402** (`SuscripcionVencidaException`), salvo endpoints con `[PermitidoConSuscripcionVencida]`. Login y consultas/exportaciones siguen funcionando.
  - Límites y módulos sin permiso propio se verifican en el caso de uso con `ReglasPlan` (`LimitePlanException` → **403**): alta/reactivación de usuarios y productos, importación (sobre el total resultante, también en la vista previa) y emisión de facturas (`ModulosPlan.Facturacion`). Bajar de plan no borra nada: solo frena altas. Las notas de crédito de anulaciones se emiten siempre.
  - Pagos: `PagoSuscripcion.Acreditar` suma meses al vencimiento si está al día o desde hoy si ya venció, y pasa la prueba a activa. HS Tech registra pagos manuales (`POST /api/plataforma/comercios/{id}/pagos`, origen `Manual`).
  - Mercado Pago (API de Suscripciones / preapproval, vía `IMercadoPagoService`; los tests usan `MercadoPagoServiceFalso` y nunca salen a internet):
    - `POST /api/suscripcion/mercadopago` (solo el Administrador, `[PermitidoConSuscripcionVencida]`) crea la suscripción pendiente con `external_reference` = suscripción + plan y devuelve el `init_point`. No se guarda nada hasta el webhook.
    - `POST /api/webhooks/mercadopago` (anónimo): valida `x-signature` con `MercadoPago:WebhookSecret` (sin secreto → 503, salvo `PermitirWebhookSinFirma` en desarrollo) y **nunca confía en el cuerpo**: con el id consulta el recurso a la API. `subscription_preapproval` autorizada → `Suscripcion.VincularDebitoAutomatico` (cambia al plan pagado y cancela el débito anterior); pausada/cancelada → actualiza el estado. `subscription_authorized_payment` aprobado → acredita 1 mes con `Referencia` = id del cobro (índice único por origen + referencia: los avisos repetidos no duplican). Si el cobro llega antes que la autorización, primero vincula.
    - `DELETE /api/suscripcion/mercadopago` cancela el débito (primero en Mercado Pago); el servicio sigue hasta el vencimiento pagado.
    - `MercadoPago:AccessToken` va SOLO en User Secrets / `MercadoPago__AccessToken`. `MercadoPago:UrlRetorno` debe ser la página `/suscripcion` pública (https).
- Productos: SKU y código de barras únicos por comercio; la búsqueda acepta nombre parcial o SKU / código exactos (scanner). Precio por margen o precio de venta fijo (`FijarPrecioVenta` recalcula el margen). IVA solo en alícuotas de ARCA.
- Planilla de productos (Excel .xlsx vía `IPlanillaProductos`, implementada con Open XML SDK en `Infrastructure/Planillas`):
  - `GET /api/productos/exportar` (permiso `productos.exportar`): catálogo completo, incluidos los dados de baja; sirve de plantilla.
  - `POST /api/productos/importar?simular=` (permiso `productos.importar`): por SKU, crea o actualiza; celda vacía = no cambiar; categoría inexistente se crea.
  - Todo o nada en un solo `SaveChanges`: con cualquier error de fila no se guarda nada. `simular=true` es la vista previa.
  - El stock de los existentes no se toca (solo compras y ventas); en los nuevos la columna Stock es el stock inicial. Máximo 5 MB y 5.000 filas.
- Categorías: no se dan de baja con productos activos; el orden se fija con `PUT /api/categorias/orden`.
- Anulaciones (`POST /api/ventas/{id}/anular`, permiso `ventas.anular`): anulación total vía `AnulacionVenta.Registrar` en un solo `SaveChanges`.
  - Revierte: stock con `Producto.ReintegrarStock`, egresos `DevolucionAnulacion` (monto negativo, efectivo neto del vuelto) en la caja abierta, y `Cliente.RevertirCargo` (el saldo puede quedar a favor del cliente).
  - Una venta se anula una sola vez: índice único por `VentaId` (409). No se puede devolver más efectivo del que hay en la caja (422).
  - Si la venta tenía factura con CAE, la nota de crédito (misma letra, con `ComprobanteAsociadoId` a la factura) se pide **después** de confirmar la anulación. Si ARCA falla, queda `EstadoNotaCredito.Pendiente` y se reintenta con `POST /api/ventas/{id}/nota-credito`.
  - Las ventas anuladas no cuentan en los reportes de venta (`Consultas.VentasDel`); el dashboard las informa aparte.
- Reportes (`/api/reportes/*`, permiso `reportes.ver`): agregan en SQL sobre DbSets con filtro de tenant; nunca cargar ventas en memoria para sumarlas.
  - Períodos en fechas calendario argentinas (`FechaFiscal.InicioDelDiaUtc`), extremos incluidos, máximo 366 días; sin fechas = hoy.
  - Margen bruto = venta neta (sin IVA) − Σ cantidad × `VentaDetalle.CostoUnitario`. El costo se congela al vender: no usar el costo actual del producto.
  - Efectivo por medio de pago se informa neto del vuelto. La cuenta corriente es crédito, no dinero cobrado.
