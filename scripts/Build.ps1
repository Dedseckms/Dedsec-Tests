param([string]$Dotnet='dotnet',[string]$Iscc='ISCC.exe',[switch]$SkipInstaller)
$ErrorActionPreference='Stop';$root=Split-Path $PSScriptRoot -Parent;Set-Location $root
$version=([xml](Get-Content .\Directory.Build.props -Raw)).Project.PropertyGroup.Version
$buildRoot=Join-Path $root ('artifacts\build-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
$release=Join-Path $buildRoot 'release';New-Item -ItemType Directory -Force $release | Out-Null
foreach($arch in @('x64','x86')){
 $target=Join-Path $buildRoot "win-$arch"
 & $Dotnet publish .\src\Dedsec.Tests\Dedsec.Tests.csproj -c Release -r "win-$arch" --self-contained true -o $target -p:DebugType=None -p:DebugSymbols=false
 if($LASTEXITCODE -ne 0){throw 'App publish failed'}
 & $Dotnet publish .\src\Dedsec.Tests.Updater\Dedsec.Tests.Updater.csproj -c Release -r "win-$arch" --self-contained true -o $target -p:DebugType=None -p:DebugSymbols=false
 if($LASTEXITCODE -ne 0){throw 'Updater publish failed'}
 & "$PSScriptRoot\Copy-Licenses.ps1" -Target $target
 $zip=Join-Path $release "DedsecTests-$version-Portable-$arch.zip"
 [IO.Compression.ZipFile]::CreateFromDirectory($target,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
 if(!$SkipInstaller){& $Iscc /Q "/O$release" "/DAppVersion=$version" "/DArchitecture=$arch" "/DPayloadDir=$target" .\installer\DedsecTests.iss;if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}}
}
New-Item -ItemType Directory -Force (Join-Path $root '.local')|Out-Null
[IO.File]::WriteAllText((Join-Path $root '.local\final-release-path.txt'),$release)
Write-Output "RELEASE_DIRECTORY=$release"
