#!/usr/bin/env bash
# Abre 80/443 en ufw SOLO para los rangos oficiales de Cloudflare y actualiza real_ip de Nginx.
# La IP directa del servidor no atiende la web. Corre semanalmente (cron) y se puede ejecutar a mano.
set -euo pipefail
v4="$(curl -fsS --max-time 20 https://www.cloudflare.com/ips-v4)"
v6="$(curl -fsS --max-time 20 https://www.cloudflare.com/ips-v6)"
[ "$(echo "$v4" | grep -c /)" -ge 10 ] || { echo "Lista IPv4 de Cloudflare inesperada; no se toca nada." >&2; exit 1; }

# Nginx: IP real del visitante desde CF-Connecting-IP, solo si la conexion viene de Cloudflare
{
  echo "# Generado por cloudflare-ips.sh — no editar a mano"
  for r in $v4 $v6; do echo "set_real_ip_from $r;"; done
  echo "real_ip_header CF-Connecting-IP;"
} > /etc/nginx/conf.d/cloudflare-real-ip.conf.nuevo
mv /etc/nginx/conf.d/cloudflare-real-ip.conf.nuevo /etc/nginx/conf.d/cloudflare-real-ip.conf
nginx -t -q && systemctl reload nginx

# ufw: borra las reglas web anteriores de Cloudflare y vuelve a crearlas
while ufw status numbered | grep -q 'Cloudflare'; do
  n="$(ufw status numbered | grep 'Cloudflare' | head -1 | sed -E 's/^\[ *([0-9]+)\].*/\1/')"
  ufw --force delete "$n" >/dev/null
done
for r in $v4 $v6; do ufw allow proto tcp from "$r" to any port 80,443 comment 'Cloudflare' >/dev/null; done
echo "Rangos de Cloudflare aplicados: $(echo $v4 | wc -w) IPv4, $(echo $v6 | wc -w) IPv6"
