#!/bin/sh
set -eu
test "$(id -u)" = 0
hostkey=${1:?verified backend SSH host public key required}
test -f "$hostkey"
stamp=$(date -u +%Y%m%d-%H%M%S)
install -d -m 700 /var/backups/dedsec-tests
cp -a /etc/caddy/Caddyfile "/var/backups/dedsec-tests/Caddyfile-$stamp"
if [ -f /etc/caddy/dedsec-infra/90-tests.caddy ]; then cp -a /etc/caddy/dedsec-infra/90-tests.caddy "/var/backups/dedsec-tests/90-tests-$stamp.caddy"; fi
printf '192.168.31.166 %s\n' "$(tr -d '\r\n' < "$hostkey")" > /var/lib/dedsec-tests-tunnel/.ssh/known_hosts
chown dedsec-tests-tunnel:dedsec-tests-tunnel /var/lib/dedsec-tests-tunnel/.ssh/known_hosts
chmod 600 /var/lib/dedsec-tests-tunnel/.ssh/known_hosts
cat > /etc/systemd/system/dedsec-tests-tunnel.service <<'EOF'
[Unit]
Description=Dedicated Dedsec Tests encrypted backend tunnel
After=network-online.target
Wants=network-online.target
StartLimitIntervalSec=0
[Service]
User=dedsec-tests-tunnel
ExecStart=/usr/bin/ssh -NT -i /var/lib/dedsec-tests-tunnel/.ssh/id_ed25519 -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile=/var/lib/dedsec-tests-tunnel/.ssh/known_hosts -o ExitOnForwardFailure=yes -o ServerAliveInterval=15 -o ServerAliveCountMax=3 -o ConnectTimeout=10 -L 127.0.0.1:18240:127.0.0.1:18240 dedsec-tests-forward@192.168.31.166
Restart=always
RestartSec=5
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=strict
ProtectHome=yes
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
MemoryMax=32M
TasksMax=8
[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable --now dedsec-tests-tunnel.service
sleep 2
curl --fail --max-time 10 http://127.0.0.1:18240/api/health
cat > /etc/caddy/dedsec-infra/90-tests.caddy <<'EOF'
tests.dedseckms.ru {
    encode zstd gzip
    request_body {
        max_size 32KB
    }
    header {
        Strict-Transport-Security "max-age=31536000"
        X-Content-Type-Options nosniff
        Referrer-Policy no-referrer
        Permissions-Policy "camera=(), microphone=(), geolocation=()"
    }
    reverse_proxy 127.0.0.1:18240 {
        header_up X-Real-IP {remote_host}
    }
}
EOF
if ! caddy validate --config /etc/caddy/Caddyfile; then rm /etc/caddy/dedsec-infra/90-tests.caddy; exit 1; fi
systemctl reload caddy
systemctl is-active caddy dedsec-tests-tunnel.service
