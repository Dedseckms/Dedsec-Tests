#!/bin/sh
set -eu
test "$(id -u)" = 0
id dedsec-tests-tunnel >/dev/null 2>&1 || useradd --system --create-home --home-dir /var/lib/dedsec-tests-tunnel --shell /usr/sbin/nologin dedsec-tests-tunnel
install -d -m 700 -o dedsec-tests-tunnel -g dedsec-tests-tunnel /var/lib/dedsec-tests-tunnel/.ssh
key=/var/lib/dedsec-tests-tunnel/.ssh/id_ed25519
if [ ! -f "$key" ]; then runuser -u dedsec-tests-tunnel -- ssh-keygen -q -t ed25519 -N '' -f "$key" -C dedsec-tests-tunnel; fi
cat "$key.pub"
