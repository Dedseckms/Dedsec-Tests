# Validation — 24 November 2026

This is **0.3.1 Preview** for owner review before 1.0.0. A successful local test is not evidence that every physical Windows 10/11 machine, sensor or driver works.

## 0.3.1 update workflow and partition editor

- The production HTTP transport was exercised against the actual GitHub API, redirected signed metadata and a complete published portable ZIP. Signature, size and SHA-256 matched. The reported user TLS failure was not reproduced on this computer; the new UI preserves the inner error, adds a local log and manual browser download without disabling certificate validation.
- Isolated transport checks cover a failed connection, interrupted body followed by a clean retry, oversize refusal, cancellation during a blocked read, file-handle release, TLS explanations and compatible-version discovery. Transfer percentages use bytes actually read. Installer progress comes from the real extraction/copy/readiness transaction and reaches 100 only after readiness is confirmed. Ready, rollback and signed-traversal fixtures passed with the production helper.
- Visual boundary checks cover Windows minimum rounding, maximum capacity, MiB alignment and invalid precision input. The modal opens before the potentially lengthy native bounds request. Closing cancels waiting only; the Windows service can finish its analysis in the background. Native writes still revalidate bounds and identity.
- A separate offscreen application probe closes the actual owned editor with its X button and verifies that the main window remains visible, including restoration from minimized tray mode. Its settings are isolated under capture-local. It does not change partitions.

## 0.3.0 disk manager and consolidated workspace

- Seven unique navigation destinations. Monitoring owns the live charts and raw sensors; diagnostics owns system findings, Windows events, read-only SFC/DISM checks and error-code explanations. Reports show saved run results. Captures exercise the actual application at 1200 × 820 and 920 × 640.
- Actual Windows inventory and firmware enumeration were read on the development computer. Real DISM CheckHealth completed with exit 0. No physical partition, EFI directory or firmware entry was changed by developer validation.
- The native storage harness creates and attaches its own expandable 512 MiB VHD. Before each request it verifies the image-to-disk mapping, size and non-system status. It checks GPT initialization, creation, NTFS/exFAT formatting, real shrink/extend, reuse of a deleted adjacent partition, letter changes and SHA-256-verified EFI directory cleanup. Negative cases include stale disk/layout/GPT identity, noncontiguous growth, protected EFI operations, traversal, changed directory preview, application-containing volumes and folder mount points. It detaches the VHD in a finally block.
- Disk writes require a separate typed target and consent. The native worker rechecks identity and layout. EFI cleanup only accepts FAT/FAT32 GPT EFI volumes, rejects reparse points and preserves Microsoft/Boot. Local journals and backups never enter the public benchmark payload.
- The automated UI check renders an actual object-backed dark disk selector and verifies that the model is displayed instead of the CLR type name. Publication consent and live-table scroll preservation remain covered. Fourteen isolated registry tests and the real .NET publisher integration remain separate from storage validation.
- A separate application probe renders the actual agreement with isolated capture settings and an update-readiness marker. It verifies that readiness is signalled while consent remains unchecked, then declines and closes the dialog. The updater must not roll back an otherwise started application because the user is still reading the new agreement.
- Firmware-entry deletion is an experimental preview feature: enumeration was tested; deleting an entry on a physical machine was not. BCD export does not constitute a complete UEFI NVRAM backup. Shrink/grow uses Windows-supported bounds; there is no partition mover or automatic preservation of files on a partition the user chooses to delete.

Earlier evidence below records the preceding previews; it is not a claim that every check was repeated for 0.3.0.

## 0.1.2 publication, live tables and measured display

- Reproduced the reported `Transfer encoding unsupported` error with the original .NET `PostAsJsonAsync` against the actual production Python handler in an isolated database. The new production publisher sends an explicit Content-Length, confirms ownership, reads the stored result back and compares the configuration/measurements with the sent JSON. Retry returns the same ID. Twenty checks passed in that integration run, including cleanup; the isolated registry was empty afterwards.
- The production .NET publisher also passed through the public HTTPS/Caddy/tunnel/backend route, with exact raw-value readback, idempotent retry and token deletion. This was a clearly labelled disposable synthetic transport fixture; it was deleted, not retained as a hardware benchmark.
- Eighteen checks passed with actual WPF application resources: values change in the existing DataGrid while scroll offset and selected PID persist. Dark table/selection/scrollbar resources were rendered and visually inspected (`artifacts/ui-dark-grid.png`). This is local UI evidence, not physical testing of every display/DPI.
- Twenty-six checks passed with current physical CPU/GPU/RAM/file-disk operations, including independent SHA checks and GPU hardware timestamps. Ten backend tests and JavaScript DT/unit-conversion checks passed. These counts overlap; do not add them as independent tests.
- DT points and compact units are computed only for valid successful measured throughput. Skipped/non-finite/untimed values, error counts and latency cannot become throughput scores. JSON values and API units remain unchanged.

## 0.1.1 NVMe temperature correction

Owner reported a constant 84 °C warning on a Kingston NV1. Source inspection of [LibreHardwareMonitor 0.9.6 StorageDevice](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Hardware/Storage/StorageDevice.cs) confirmed that NVMe warning and critical temperature **limits** are exposed as `SensorType.Temperature`, indices 10/11. Dedsec Tests 0.1.0 incorrectly included those limits in live temperature aggregation. Version 0.1.1 maps them to `TemperatureLimit`, keeps their values visible with explicit labels, and excludes them from live temperature alerts, monitoring availability, graphs, peak reports and thermal stops. Actual temperature readings, including a real 84 °C reading, remain included. The overview now names the hottest measured sensor. This fixes the aggregation error; the reported physical NV1 still needs an owner check after updating.

Thirteen focused client checks passed, including five regression checks for threshold exclusion, preserved raw limit values, absence of false overheating alerts, unknown monitoring when only limits exist, genuine hot SSD readings and CPU index isolation (some checks cover multiple assertions).

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
