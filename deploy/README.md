# Despliegue de PosVentas

**https://pos.hstech.com.ar** — Droplet de DigitalOcean `pos-staging` (NYC3, Ubuntu 24.04, 1 vCPU / 2 GB).
Es el entorno de PosVentas: primero de pruebas y después del piloto. Antes de salir a vender: limpiar los datos
de prueba, pasar Mercado Pago a credenciales de producción y sumar un backup diario de la base fuera del servidor.

## Desplegar

Desde la raíz del repositorio, en `main` y con la CI de GitHub en verde para el último commit:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\deploy.ps1
```

`-TestsLocales` corre los tests en la PC en lugar de exigir la CI (necesita SQL Server local).

El script publica la API y el cliente, sube el paquete por SSH y lo activa con `activar-release.sh`:
reinicia la API, verifica `/health` (directo y a través de Nginx) y la página del cliente, y si algo falla
**vuelve solo al release anterior**. Al final verifica `https://pos.hstech.com.ar/health` desde internet.
Conserva los últimos 3 releases en `/var/www/posventas/releases`.

## Cómo llega un pedido

```
Navegador ──https──> Cloudflare (proxy, modo SSL Full strict)
                        ──https──> Nginx :443 (Let's Encrypt)  ─┬─ /         → Blazor estático (wwwroot)
                                                                 └─ /api, /health → API en 127.0.0.1:5100
```

- El firewall (ufw) solo deja entrar 80/443 **desde los rangos de Cloudflare**: la IP directa no atiende la web.
  `cloudflare-ips.sh` actualiza esos rangos (y la IP real del visitante en Nginx) todos los lunes.
- El puerto 80 solo atiende el desafío de Let's Encrypt y redirige todo lo demás a https.
- El certificado se renueva solo (`certbot.timer`; simulacro: `certbot renew --dry-run`).

## Servidor

| Qué | Dónde |
|---|---|
| SSH (la red de la oficina bloquea el 22) | `ssh -i ~/.ssh/posventas_do -p 2222 root@104.236.96.190` |
| Release activo | `/var/www/posventas/current` → `api/` (API) y `wwwroot/` (Blazor) |
| Servicio de la API | `systemctl status posventas-api` · logs: `journalctl -u posventas-api -f` |
| Secretos (root 600, nunca en el repo) | `/etc/posventas/posventas.env` |
| Claves de Data Protection (cifran certificados ARCA) | `/var/lib/posventas/keys` — **no borrar** |
| Nginx | `/etc/nginx/sites-available/posventas` (copia en `deploy/servidor/`) |
| Certificado | `/etc/letsencrypt/live/pos.hstech.com.ar/` |
| SQL Server 2025 Express | base `PosVentasSaaS`, login `posventas_app` (db_owner); puerto 1433 cerrado |
| Contraseña del admin de HS Tech (`hs-admin`) | `/root/.posventas-plataforma` |

`deploy/servidor/` tiene copia de la configuración del servidor (sin secretos): sitio de Nginx, servicio systemd
y script de Cloudflare. Si se cambia algo en el servidor, actualizar también la copia.

Volver a mano al release anterior: `ls -1dt /var/www/posventas/releases/*/`, apuntar `current` al elegido
(`ln -sfn <dir> /var/www/posventas/current`) y `systemctl restart posventas-api`.

Si se pierde el acceso SSH: **Web Console** del Droplet en el panel de DigitalOcean.
