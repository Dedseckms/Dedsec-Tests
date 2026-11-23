#!/bin/sh
set -eu
test "$(id -u)" = 0
pub=$(tr -d '\r\n' < "${1:?public key file required}")
printf '%s\n' "$pub" | grep -Eq '^ssh-ed25519 [A-Za-z0-9+/=]+ dedsec-tests-tunnel$'
id dedsec-tests-forward >/dev/null 2>&1 || useradd --system --create-home --home-dir /var/lib/dedsec-tests-forward --shell /bin/false dedsec-tests-forward
install -d -m 700 -o dedsec-tests-forward -g dedsec-tests-forward /var/lib/dedsec-tests-forward/.ssh
printf 'from="192.168.31.83,192.168.31.249",restrict,port-forwarding,permitopen="127.0.0.1:18240",command="/bin/false" %s\n' "$pub" > /var/lib/dedsec-tests-forward/.ssh/authorized_keys
chown dedsec-tests-forward:dedsec-tests-forward /var/lib/dedsec-tests-forward/.ssh/authorized_keys
chmod 600 /var/lib/dedsec-tests-forward/.ssh/authorized_keys
cat > /etc/ssh/sshd_config.d/93-dedsec-tests.conf <<'EOF'
Match User dedsec-tests-forward
    AllowTcpForwarding local
    AllowStreamLocalForwarding no
    PermitOpen 127.0.0.1:18240
    PermitTTY no
    X11Forwarding no
    AllowAgentForwarding no
    ForceCommand /bin/false
Match all
EOF
sshd -t
systemctl reload ssh
