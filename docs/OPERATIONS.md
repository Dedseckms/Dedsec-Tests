# Operations

## Runtime and deployment

The registry is a Python stdlib service bound to **127.0.0.1:18240 on server2**, with a separate `dedsec-tests` account. Code is root-owned `/opt/dedsec-tests`; SQLite and backups live in `/var/lib/dedsec-tests` (0700). systemd limits the main service to 160 MiB and 40% of one CPU, with no privileges and a read-only filesystem outside its state directory. No Docker daemon restart, host reboot or production firewall replacement is required.

Caddy on server1 routes only `tests.dedseckms.ru` through a dedicated `dedsec-tests-tunnel.service`. Its separate SSH key is restricted to the backend address/port; the backend forwarding account cannot open a shell, TTY, agent forwarding, remote forwarding or arbitrary destination. It does not replace or restart the existing infrastructure tunnel. Caddy uses a separate imported `90-tests.caddy`, validates configuration, then reloads gracefully. This hostname has no access log; the backend does not log requests, authorization or IPs.

The scripts in `deploy/` are **owner-specific administrative installation scripts**. Review paths and backend LAN address before reusing elsewhere. `backend.sh` installs only the new service/timer and backs up old application code when present. `tunnel-key.sh` generates a key on the frontend; transfer only its public half. `authorize-tunnel.sh` sets the backend's scoped account/SSH policy. `frontend.sh` pins a verified backend host key, starts the new tunnel and adds one Caddy virtual host. A host key must be verified over an already trusted SSH session; never disable StrictHostKeyChecking.

Health: `https://tests.dedseckms.ru/api/health`. Public statistics: `/api/stats`. Service logs should contain lifecycle/errors only, not request data. Logs from unrelated network infrastructure are outside the application's retention guarantees.

## Backups, deletion and recovery

`dedsec-tests-backup.timer` runs daily at 04:50 Europe/Moscow plus a random delay up to ten minutes. The SQLite backup API captures a consistent snapshot; the last seven copies are retained under the private state directory. These are same-server backups; arrange an off-server copy separately if required.

Stop `dedsec-tests`, take a backup of current state, restore a verified SQLite snapshot with ownership `dedsec-tests:dedsec-tests` and mode 0600, and restart. Do not copy a live WAL database as a standalone file. A restored old snapshot can contain subsequently deleted public records; reconcile deletions before bringing it back online. The service does not promise cross-server failover or automatic restoration.

Publication deletion requires the returned bearer deletion token. Only its SHA-256 is stored in SQLite. Tokens remain in local app reports, never in public API responses after creation. An owner who loses the token cannot automatically establish ownership of an anonymous record.

## Release and signed updates

1. Update `Directory.Build.props` version and release text; keep benchmark `suite_version` unchanged only if the workload is genuinely compatible. Change the suite and server whitelist when changing the methodology after a public release.
2. Build with `scripts/Build.ps1`, run the app/server checks and validate the actual packaged binaries. The build directory is recreated only after verifying it is the exact repository `artifacts/win-x64` or `artifacts/win-x86` directory.
3. Use `scripts/Sign-Release.ps1 -PrivateKeyPath <private-key>` to sign the architecture-specific manifests and produce SHA256SUMS. The RSA private key is intentionally ignored under `.local/` on the owner's workstation; **back it up privately**. Do not commit or put it into release assets. The public key is tracked under `src/UpdatePublicKey.pem` and embedded into both binaries.
4. Upload both portable packages, installers, `update-x64.json/.sig`, `update-x86.json/.sig` and SHA256SUMS to the corresponding GitHub tag. Version tags use `vMAJOR.MINOR.PATCH`. The application considers stable and preview releases and selects a strictly newer numeric version with both matching architecture assets.
5. Updates are user-triggered. The client verifies RSA-PSS/SHA-256, architecture, version, allowed GitHub URL, file size and hash before installation. A copied helper runs outside the installation directory, verifies again, rejects archive traversal/symlinks/duplicates and backs up only files being replaced. It does not recursively delete the install directory or user reports. It waits for the old process to exit and requires the new UI to write a readiness marker; failure restores replaced files.

Rollback files are intentionally retained locally under `LocalAppData/DedsecTests/updates`. Remove old staging directories manually when no update helper is running. The initial executable and setup are currently **not Authenticode signed**; manifest signatures do not establish a Windows publisher identity for the initial download.

## Temporary administrative access

Only the new installation requires temporary broad sudo. Remove `/etc/sudoers.d/90-dedsec-tests-setup` after validation. Do not alter existing limited SSO/Cockpit sudo rules. SSH keys, passwords, infrastructure configuration and private server records must never be committed.
