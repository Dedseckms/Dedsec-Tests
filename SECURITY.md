# Security and privacy

Do not post credentials, deletion tokens, memory dumps or full local event logs in public issues. The public API accepts a strict field allowlist, but hardware labels are client-reported strings; the server cannot independently establish that clients are truthful. Public benchmark records are unverified observations, not attested performance claims.

For a security issue, use GitHub's private vulnerability reporting for this repository if enabled. Otherwise open an issue containing only a request for a private contact, without exploit secrets or user data.

Updates require an RSA-PSS/SHA-256 signed manifest and matching package hash and size. The public key is embedded in the app and updater. Keep the private signing key outside Git. Losing it requires a separately trusted key-rotation release; replacing the public key alone cannot update already-installed apps. Hash manifests do not provide Windows Authenticode publisher verification: the initial setup is currently unsigned.
