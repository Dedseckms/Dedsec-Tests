# Third-party software

Dedsec Tests source is MIT. Dependencies retain their own licenses:

| Component | Version | License / source |
|---|---|---|
| .NET / WPF | 10 | MIT, https://github.com/dotnet/runtime and https://github.com/dotnet/wpf |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0, https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 |
| PawnIO official signed driver installer | 2.2.0 | Driver: GPL-2.0-or-later with the exception in README, https://github.com/namazso/PawnIO/tree/5cdf470831fdfff3f7f1d06363ca6b230f3bf35a |
| Vortice.Windows | 3.8.3 | MIT, https://github.com/amerkoleci/Vortice.Windows |
| SharpGen.Runtime | transitive NuGet version in packages.lock.json | MIT, https://github.com/SharpGenTools/SharpGenTools |
| HidSharp | transitive | Apache-2.0, https://github.com/IntergatedCircuits/HidSharp |
| BlackSharp.Core / DiskInfoToolkit / RAMSPDToolkit-NDD | transitive | MPL-2.0, https://github.com/Blacktempel/BlackSharp, https://github.com/Blacktempel/DiskInfoToolkit, https://github.com/Blacktempel/RAMSPDToolkit |
| Mono.Posix.NETStandard | 1.0.0 | Microsoft/Mono license metadata preserved with the package in `licenses/` |
| System.Management / System.IO.Ports | 10.x | MIT, https://github.com/dotnet/runtime |

Unmodified third-party libraries are restored from NuGet. Their license files are copied into packaged `licenses/`. LibreHardwareMonitor source, including MPL-covered code, is available at the source link above. No LHM source modifications are made here.

The unmodified official PawnIO installer is bundled offline under `tools/PawnIO_setup.exe`. It is executed only after an explicit user action and consent, with a Windows elevation prompt. The application verifies its pinned SHA-256 before elevation. The build also verifies its Authenticode signature. The unrestricted driver mode is never used. PawnIO is not silently installed or automatically removed, because other applications may use it. Official release: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0 .

`tools/PawnIO-2.2.0-source.zip` contains the complete unmodified driver source, build files, COPYING, README and pinned PawnPP submodule. `tools/PawnIO-SOURCE.txt` records its provenance. PawnIO's original COPYING and README are also preserved in `licenses/`. The driver remains under its own GPL terms and README exception; Dedsec Tests source remains MIT. Generated brand artwork is included in this repository.
