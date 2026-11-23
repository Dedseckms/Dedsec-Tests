#!/bin/sh
# Static-only update. No service, Caddy, database or infrastructure restart.
set -eu
source_dir=${1:?staged source directory required}
target=/opt/dedsec-tests/web
stamp=$(date -u +%Y%m%d-%H%M%S)
backup_dir="$HOME/dedsec-tests-web-backups/$stamp"
mkdir -p "$backup_dir"
chmod 700 "$backup_dir"
for name in app.js methodology.html style.css; do
    test -f "$source_dir/$name"
    test -f "$target/$name"
    test ! -L "$target/$name"
    cp "$target/$name" "$backup_dir/$name"
    chmod 600 "$backup_dir/$name"
done
for name in app.js methodology.html style.css; do
    staged=$(mktemp "$target/.dedsec-update-XXXXXX")
    cp "$source_dir/$name" "$staged"
    chmod 644 "$staged"
    cmp "$source_dir/$name" "$staged"
    mv -f "$staged" "$target/$name"
done
printf 'Static files updated; backup: %s\n' "$backup_dir"
sha256sum "$target/app.js" "$target/methodology.html" "$target/style.css"
