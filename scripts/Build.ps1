param([string]$Dotnet='dotnet',[string]$Iscc='ISCC.exe',[switch]$SkipInstaller)
$ErrorActionPreference='Stop';$root=Split-Path $PSScriptRoot -Parent;Set-Location $root
$version=([xml](Get-Content .\Directory.Build.props -Raw)).Project.PropertyGroup.Version
New-Item -ItemType Directory -Force .\artifacts\release | Out-Null
foreach($arch in @('x64','x86')){
 $target=Join-Path $root "artifacts\win-$arch"
 if(Test-Path -LiteralPath $target){$resolved=(Resolve-Path -LiteralPath $target).Path;$expected=[IO.Path]::GetFullPath((Join-Path $root "artifacts\win-$arch"));if($resolved -ne $expected -or !$resolved.StartsWith((Join-Path $root 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe publish directory'};Remove-Item -LiteralPath $resolved -Recurse -Force}
 & $Dotnet publish .\src\Dedsec.Tests\Dedsec.Tests.csproj -c Release -r "win-$arch" --self-contained true -o $target -p:DebugType=None -p:DebugSymbols=false
 if($LASTEXITCODE -ne 0){throw 'App publish failed'}
 & $Dotnet publish .\src\Dedsec.Tests.Updater\Dedsec.Tests.Updater.csproj -c Release -r "win-$arch" --self-contained true -o $target -p:DebugType=None -p:DebugSymbols=false
 if($LASTEXITCODE -ne 0){throw 'Updater publish failed'}
 & "$PSScriptRoot\Copy-Licenses.ps1" -Target $target
 $zip=Join-Path $root "artifacts\release\DedsecTests-$version-Portable-$arch.zip"
 if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
 [IO.Compression.ZipFile]::CreateFromDirectory($target,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
 if(!$SkipInstaller){& $Iscc /Q "/DAppVersion=$version" "/DArchitecture=$arch" "/DPayloadDir=$target" .\installer\DedsecTests.iss;if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}}
}
