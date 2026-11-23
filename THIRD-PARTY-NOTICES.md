# Third-party software

Dedsec Tests source is MIT. Dependencies retain their own licenses:

| Component | Version | License / source |
|---|---|---|
| .NET / WPF | 10 | MIT, https://github.com/dotnet/runtime and https://github.com/dotnet/wpf |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0, https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 |
| Vortice.Windows | 3.8.3 | MIT, https://github.com/amerkoleci/Vortice.Windows |
| SharpGen.Runtime | transitive NuGet version in packages.lock.json | MIT, https://github.com/SharpGenTools/SharpGenTools |
| HidSharp | transitive | Apache-2.0, https://github.com/IntergatedCircuits/HidSharp |
| BlackSharp.Core / DiskInfoToolkit / RAMSPDToolkit-NDD | transitive | MPL-2.0, https://github.com/Blacktempel/BlackSharp, https://github.com/Blacktempel/DiskInfoToolkit, https://github.com/Blacktempel/RAMSPDToolkit |
| Mono.Posix.NETStandard | 1.0.0 | Microsoft/Mono license metadata preserved with the package in `licenses/` |
| System.Management / System.IO.Ports | 10.x | MIT, https://github.com/dotnet/runtime |

Unmodified third-party binaries are restored from NuGet. Their license files are copied into packaged `licenses/`. LibreHardwareMonitor source, including MPL-covered code, is available at the source link above. No LHM source modifications are made here. Sensor drivers are not installed by Dedsec Tests. PawnIO is optional, separately installed software: https://pawnio.eu/ . Generated brand artwork is included in this repository.
