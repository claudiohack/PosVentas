# Despliegue de PosVentas

Staging: **https://pos-staging.hstech.com.ar** — Droplet de DigitalOcean `pos-staging` (NYC3, Ubuntu 24.04).

## Desplegar

Desde la raíz del repositorio, en `main` y con la CI de GitHub en verde para el último commit:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\deploy.ps1
```

`-TestsLocales` corre los tests en la PC en lugar de exigir la CI (necesita SQL Server local).

El script publica la API y el cliente, sube el paquete por SSH y lo activa con `activar-release.sh`:
reinicia la API, verifica `/health` (directo y a través de Nginx) y la página del cliente, y si algo falla
**vuelve solo al release anterior**. Conserva los últimos 3 releases en `/var/www/posventas/releases`.

## Servidor

| Qué | Dónde |
|---|---|
| SSH (la red de la oficina bloquea el 22) | `ssh -i ~/.ssh/posventas_do -p 2222 root@104.236.96.190` |
| Release activo | `/var/www/posventas/current` → `api/` (API) y `wwwroot/` (Blazor) |
| Servicio de la API | `systemctl status posventas-api` · logs: `journalctl -u posventas-api -f` |
| Secretos (root 600, nunca en el repo) | `/etc/posventas/posventas.env` |
| Claves de Data Protection (cifran certificados ARCA) | `/var/lib/posventas/keys` — **no borrar** |
| Nginx | `/etc/nginx/sites-available/posventas` |
| SQL Server 2025 Express | base `PosVentasSaaS`, login `posventas_app` (db_owner); puerto 1433 cerrado por ufw |
| Contraseña del admin de HS Tech (`hs-admin`) | `/root/.posventas-plataforma` |

Volver a mano al release anterior: `ls -1dt /var/www/posventas/releases/*/`, apuntar `current` al elegido
(`ln -sfn <dir> /var/www/posventas/current`) y `systemctl restart posventas-api`.

Si se pierde el acceso SSH: **Web Console** del Droplet en el panel de DigitalOcean.
