# PosVentas

Punto de venta SaaS multi-tenant de HS Tech: ventas, caja, cuentas corrientes, compras y stock, facturación electrónica ARCA (WSFEv1), anulaciones con nota de crédito y reportes.

**Stack:** .NET 10 LTS · ASP.NET Core WebAPI · Blazor WebAssembly · EF Core 10 · SQL Server · Clean Architecture (Domain, Application, Infrastructure, WebAPI, Client).

## Desarrollo local (sin Docker)

Requisitos: .NET 10 SDK y SQL Server (Express alcanza) en `.\SQLEXPRESS`.

```powershell
# Secretos de desarrollo (una sola vez; nunca van al repositorio)
dotnet user-secrets set "Jwt:SigningKey" "<64 bytes aleatorios en Base64>" --project src/PosVentas.WebAPI
dotnet user-secrets set "Plataforma:AdminUsuario" "hs-admin" --project src/PosVentas.WebAPI
dotnet user-secrets set "Plataforma:AdminPassword" "<12+ caracteres con letras y números>" --project src/PosVentas.WebAPI

# API (https://localhost:7098). En Development aplica las migraciones y crea el comercio "hs-tech" al arrancar.
dotnet run --project src/PosVentas.WebAPI --launch-profile https

# Cliente Blazor (https://localhost:7212)
dotnet run --project src/PosVentas.Client --launch-profile https
```

## Entorno completo con Docker

Requisitos: Docker Desktop (con WSL 2 en Windows) y unos 4 GB de RAM libres para SQL Server.

```powershell
Copy-Item .env.example .env   # completar los secretos
docker compose up -d --build
```

| Servicio | Qué es | Acceso |
| --- | --- | --- |
| `proxy` | Nginx: un solo origen para la app y la API | http://localhost:8080 |
| `frontend` | Blazor WASM servido por Nginx | (detrás del proxy) |
| `api` | ASP.NET Core; migra la base al arrancar | http://localhost:8080/api, `/health` |
| `db` | SQL Server 2022 (volumen `sqlserver_data`) | `127.0.0.1,14333` con el usuario `sa` |

Las claves de Data Protection, que cifran los certificados de ARCA, viven en el volumen `dataprotection_keys`. **No lo borres**: sin esas claves hay que volver a cargar el certificado de cada comercio.

## Alta de comercios (por invitación)

1. Ingresá como administrador de la plataforma: comercio `hs-tech`, usuario y contraseña de `Plataforma:*` (o del `.env`).
2. En **Invitaciones**, creá una invitación y copiá el enlace. Se muestra una sola vez y vence en 1 a 30 días.
3. El comercio abre el enlace, completa sus datos en `/registro` y queda creado con su administrador.

`POST /api/auth/registro` (el alta abierta) está deshabilitado.

## Tests

```powershell
dotnet test tests/PosVentas.IntegrationTests
```

Son tests de integración contra SQL Server real. Cada corrida crea su propia base `PosVentasSaaS_Tests_<guid>` y la borra al terminar. ARCA está simulada.

Las reglas de arquitectura y de negocio están en [.github/copilot-instructions.md](.github/copilot-instructions.md).
