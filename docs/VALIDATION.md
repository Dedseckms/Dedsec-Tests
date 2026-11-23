# Validation — 23 November 2026

This is **0.1.0 Preview** for owner review. A successful local test is not evidence that every physical Windows 10/11 machine, sensor or driver works.

## Verified locally

- Windows 11 Pro, Intel i5-1235U, Intel Iris Xe, 16 GB RAM, NVMe SSD: app builds, x64 and x86 runtime smoke; CPU, GPU dispatch/output validation and allocated RAM checks passed.
- Eighteen hardware/client checks passed: cancellation, finite CPU throughput, golden-digest corruption rejection, hardware GPU timestamp timing, privacy projection, high-temperature and missing-sensor states, HRESULT/STOP namespace distinction, RAM patterns/copy, actual 256 MiB unbuffered write/read/data verification/random reads, cancelled-test cleanup, real WMI/Event Log diagnostics, and actual Job Object assignment/child termination after owner exit. The observed diagnostic pass returned 29 findings, five warnings and two unavailable checks; these are local observations, not a machine health certificate.
- Isolated updater fixture tests passed: successful transaction/readiness marker, rollback on failed launch, and rejection of a signed archive containing `../` traversal. Release-manifest verification accepted the actual package and rejected modified metadata/signatures.
- Server: ten HTTP/database tests passed, including consent, privacy field rejection, numeric/duplicate validation, nonce replay rejection, token deletion with cascade, bad origin, backup and idempotent retry with preserved deletion control after a lost response.
- Installers compile in dark branded style. x64 and x86 silent installation and uninstallation in isolated test directories completed with exit 0. Interactive visual review of the installer remains an owner check.
- NuGet vulnerability query for direct/transitive dependencies returned no reported vulnerable packages at the check time. This is limited to the feed and advisories available then.

## Server and website

The site is served by HTTPS at [tests.dedseckms.ru](https://tests.dedseckms.ru). Backend on server2 is loopback-only; a dedicated encrypted tunnel connects Caddy on server1. The main Caddyfile SHA-256 and the pre-existing infrastructure tunnel PID matched their pre-change values after deployment. Existing container identities and selected public service statuses were checked separately; pre-existing failed systemd units were not repaired or hidden.

Initial GitHub CI [45192654187](https://github.com/Dedseckms/Dedsec-Tests/actions/runs/45192654187) passed the registry job and both Windows publish/check jobs. Release files are independently built and checked locally; CI status alone is not installer or physical-device validation.

Disposable clearly-labelled synthetic fixtures verified publication, public listing/detail, responsive cards, two-result comparison and token deletion. They were deleted after QA and are not retained as real benchmark records. Browser QA used the in-app narrow viewport; physical Android/iPhone testing has not been performed.

## Still requiring physical/user validation

- Windows 10 editions and 32-bit hardware, older Intel/AMD CPUs, discrete AMD/NVIDIA GPUs, multiple-GPU laptops, RAID/USB and HDD devices.
- Sensor availability and PawnIO installation/elevation on each hardware family. The program does not install drivers automatically.
- Long-duration thermal/stability behaviour and actual fault diagnosis. The test suite validates behavior and limited current hardware, not the absence of faults.
- Actual future GitHub upgrade between published user versions; installation/rollback mechanics were exercised with isolated fixtures and actual signature checks.
- Owner acceptance before changing the `dedseckms` profile README. That profile is intentionally untouched.
