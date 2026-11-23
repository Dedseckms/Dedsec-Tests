#!/bin/sh
set -eu
test "$(id -u)" = 0
archive=${1:?archive path required}
stamp=$(date -u +%Y%m%d-%H%M%S)
mkdir -p /var/backups/dedsec-tests
if [ -d /opt/dedsec-tests ]; then tar -czf "/var/backups/dedsec-tests/code-$stamp.tar.gz" -C /opt dedsec-tests; fi
id dedsec-tests >/dev/null 2>&1 || useradd --system --home-dir /var/lib/dedsec-tests --shell /usr/sbin/nologin dedsec-tests
install -d -m 755 /opt/dedsec-tests
tar -xzf "$archive" -C /opt/dedsec-tests
chown -R root:root /opt/dedsec-tests
chmod -R go-w /opt/dedsec-tests
cat > /etc/systemd/system/dedsec-tests.service <<'EOF'
[Unit]
Description=Dedsec Tests anonymous benchmark registry
After=network.target
[Service]
User=dedsec-tests
Group=dedsec-tests
WorkingDirectory=/opt/dedsec-tests
ExecStart=/usr/bin/python3 /opt/dedsec-tests/app.py
Environment=DEDSEC_DB=/var/lib/dedsec-tests/results.sqlite3
Environment=PYTHONDONTWRITEBYTECODE=1
StateDirectory=dedsec-tests
StateDirectoryMode=0700
UMask=0077
Restart=on-failure
RestartSec=5
MemoryMax=160M
CPUQuota=40%
TasksMax=32
NoNewPrivileges=yes
PrivateTmp=yes
PrivateDevices=yes
ProtectSystem=strict
ProtectHome=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectControlGroups=yes
RestrictSUIDSGID=yes
RestrictAddressFamilies=AF_INET AF_UNIX
CapabilityBoundingSet=
ReadWritePaths=/var/lib/dedsec-tests
[Install]
WantedBy=multi-user.target
EOF
cat > /etc/systemd/system/dedsec-tests-backup.service <<'EOF'
[Unit]
Description=Consistent SQLite backup for Dedsec Tests
[Service]
Type=oneshot
User=dedsec-tests
Group=dedsec-tests
ExecStart=/usr/bin/python3 /opt/dedsec-tests/app.py --backup
Environment=DEDSEC_DB=/var/lib/dedsec-tests/results.sqlite3
Environment=PYTHONDONTWRITEBYTECODE=1
StateDirectory=dedsec-tests
UMask=0077
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=strict
ProtectHome=yes
ReadWritePaths=/var/lib/dedsec-tests
MemoryMax=100M
CPUQuota=20%
EOF
cat > /etc/systemd/system/dedsec-tests-backup.timer <<'EOF'
[Unit]
Description=Daily Dedsec Tests database backup
[Timer]
OnCalendar=*-*-* 04:50:00 Europe/Moscow
RandomizedDelaySec=600
Persistent=true
[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now dedsec-tests.service dedsec-tests-backup.timer
systemctl restart dedsec-tests.service
systemctl start dedsec-tests-backup.service
systemctl is-active dedsec-tests.service
EOF_MARKER=true
