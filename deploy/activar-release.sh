#!/usr/bin/env bash
# Se ejecuta EN EL SERVIDOR (lo sube y lo invoca deploy.ps1). Uso: activar-release.sh <id-release> <paquete.tgz> <host-publico>
# Instala el release en /var/www/posventas/releases/<id> (api/ + wwwroot/), lo activa con el enlace "current",
# reinicia la API y verifica la API (/health) y el cliente (Nginx). Si algo falla, vuelve al release anterior.
# Los secretos NO pasan por acá: la API los lee de /etc/posventas/posventas.env (root 600) vía systemd.
# Basado en el despliegue probado de ContratistaSaaS.
set -euo pipefail

RELEASE_ID="$1"
PAQUETE="$2"
HOST_PUBLICO="$3"   # AllowedHosts rechaza (400) las peticiones con otro Host
BASE=/var/www/posventas
SERVICIO=posventas-api
ARCHIVO_ENTORNO=/etc/posventas/posventas.env
PUERTO_API=5100
ESPERA_MAXIMA_SEGUNDOS=120
RELEASES_A_CONSERVAR=3

fallar() { echo "ERROR: $*" >&2; exit 1; }

[ -f "$ARCHIVO_ENTORNO" ] || fallar "No existe $ARCHIVO_ENTORNO con los secretos."
[ "$(stat -c '%a %U' "$ARCHIVO_ENTORNO")" = "600 root" ] || fallar "$ARCHIVO_ENTORNO debe tener permisos 600 y dueño root."
[ -f "$PAQUETE" ] || fallar "No se encontró el paquete $PAQUETE."

DESTINO="$BASE/releases/$RELEASE_ID"
mkdir -p "$DESTINO"
tar -xzf "$PAQUETE" -C "$DESTINO"
rm -f "$PAQUETE"
[ -f "$DESTINO/api/PosVentas.WebAPI.dll" ] || fallar "El paquete no trae la API (api/PosVentas.WebAPI.dll)."
[ -f "$DESTINO/wwwroot/index.html" ] || fallar "El paquete no trae el cliente (wwwroot/index.html)."
# La API (usuario posventas) y Nginx (www-data) solo leen los binarios: dueño root, sin escritura para otros
chown -R root:root "$DESTINO"
chmod -R u=rwX,go=rX "$DESTINO"

ANTERIOR="$(readlink -f "$BASE/current" 2>/dev/null || true)"

activar() {
    ln -sfn "$1" "$BASE/current.nuevo"
    mv -T "$BASE/current.nuevo" "$BASE/current"
    systemctl restart "$SERVICIO"
}

codigo() { curl -s -o /dev/null -w '%{http_code}' -H "Host: $HOST_PUBLICO" -H 'X-Forwarded-Proto: https' "$1"; }

# Nginx por https contra este mismo servidor (el 80 redirige a https). Valida también el certificado.
codigo_https() { curl -s -o /dev/null -w '%{http_code}' --resolve "$HOST_PUBLICO:443:127.0.0.1" "https://$HOST_PUBLICO$1"; }

verificar() {
    local limite=$((SECONDS + ESPERA_MAXIMA_SEGUNDOS))
    while [ $SECONDS -lt $limite ]; do
        # API directa, la misma API a través de Nginx (https) y el cliente Blazor servido por Nginx
        if systemctl is-active --quiet "$SERVICIO" \
            && [ "$(codigo "http://127.0.0.1:$PUERTO_API/health")" = "200" ] \
            && [ "$(codigo_https "/health")" = "200" ] \
            && [ "$(codigo_https "/")" = "200" ]; then
            return 0
        fi
        sleep 3
    done
    return 1
}

echo "Activando release $RELEASE_ID..."
activar "$DESTINO"

if verificar; then
    echo "OK: API y cliente respondiendo con el release $RELEASE_ID."
else
    echo "FALLO: el release $RELEASE_ID no respondió. Últimas líneas del log:" >&2
    journalctl -u "$SERVICIO" --no-pager -n 40 -o cat >&2 || true
    if [ -n "$ANTERIOR" ] && [ -d "$ANTERIOR" ] && [ "$ANTERIOR" != "$DESTINO" ]; then
        echo "Volviendo al release anterior: $ANTERIOR" >&2
        activar "$ANTERIOR"
        verificar && echo "Release anterior restaurado y respondiendo." >&2
    fi
    exit 1
fi

# Limpieza: conserva los últimos releases para poder volver atrás
ls -1dt "$BASE"/releases/*/ | tail -n +$((RELEASES_A_CONSERVAR + 1)) | xargs -r rm -rf
