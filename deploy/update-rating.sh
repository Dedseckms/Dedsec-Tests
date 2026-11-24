#!/bin/sh
# Scoped update: registry code + four static files, additive DB migration.
set -eu
test "$(id -u)" = 0
source_dir=${1:?staged directory required}
target=/opt/dedsec-tests
test "$(readlink -f "$target")" = /opt/dedsec-tests
for name in app.py app.js index.html methodology.html style.css; do test -f "$source_dir/$name"; done
test ! -L "$target/app.py"
test ! -L "$target/web"
python3 -c 'import pathlib,sys; compile(pathlib.Path(sys.argv[1]).read_bytes(),sys.argv[1],"exec")' "$source_dir/app.py"
umask 077
stamp=$(date -u +%Y%m%d-%H%M%S)
backup_dir="/var/backups/dedsec-tests/overall-$stamp"
install -d -m 700 "$backup_dir/web"
cp "$target/app.py" "$backup_dir/app.py"
for name in app.js index.html methodology.html style.css; do cp "$target/web/$name" "$backup_dir/web/$name"; done
python3 - "$backup_dir/results.sqlite3" <<'PY'
import sqlite3,sys
with sqlite3.connect('/var/lib/dedsec-tests/results.sqlite3') as source, sqlite3.connect(sys.argv[1]) as destination:
    source.backup(destination)
PY
rollback() {
    trap - 0
    install -o root -g root -m 644 "$backup_dir/app.py" "$target/app.py"
    for name in app.js index.html methodology.html style.css; do install -o root -g root -m 644 "$backup_dir/web/$name" "$target/web/$name"; done
    systemctl restart dedsec-tests
    printf 'Update failed; previous code restored. Additive DB table retained; raw reports not rolled back. Backup: %s\n' "$backup_dir"
    exit 1
}
trap rollback 0
install -o root -g root -m 644 "$source_dir/app.py" "$target/app.py"
systemctl restart dedsec-tests
ready=0
for attempt in 1 2 3 4 5 6 7 8 9 10; do
    if curl --noproxy '*' --max-time 2 -fsS http://127.0.0.1:18240/api/health >/dev/null; then ready=1; break; fi
    sleep 1
done
test "$ready" = 1
curl --noproxy '*' --max-time 5 -fsS 'http://127.0.0.1:18240/api/results?metric=overall' >/dev/null
for name in app.js index.html methodology.html style.css; do install -o root -g root -m 644 "$source_dir/$name" "$target/web/$name"; done
chown root:root "$target/web"
chmod 755 "$target/web"
python3 - "$backup_dir/results.sqlite3" <<'PY'
import sqlite3,sys
with sqlite3.connect(sys.argv[1]) as before,sqlite3.connect('/var/lib/dedsec-tests/results.sqlite3') as after:
    rows=before.execute('SELECT id,payload FROM results').fetchall()
    for identifier,payload in rows:
        current=after.execute('SELECT payload FROM results WHERE id=?',(identifier,)).fetchone()
        assert current and current[0]==payload, 'Existing raw report changed'
    assert after.execute('SELECT COUNT(*) FROM overall_scores').fetchone()[0]>=len(rows)
    print('Existing raw reports preserved:',len(rows))
PY
trap - 0
printf 'Overall rating deployed; backup: %s\n' "$backup_dir"
sha256sum "$target/app.py" "$target/web/app.js" "$target/web/index.html" "$target/web/methodology.html" "$target/web/style.css"
