param([Parameter(Mandatory)][string]$Target)
$ErrorActionPreference='Stop';$root=Split-Path $PSScriptRoot -Parent
$assets=Get-Content (Join-Path $root 'src\Dedsec.Tests\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$packageRoot=@($assets.packageFolders.Keys)[0];$folder=Join-Path $Target 'licenses';New-Item -ItemType Directory -Force $folder | Out-Null
Copy-Item -Path (Join-Path $root 'licenses\*') -Destination $folder -Recurse -Force
foreach($library in $assets.libraries.Values){if($library.type -ne 'package'){continue};$path=Join-Path $packageRoot $library.path;if(!(Test-Path -LiteralPath $path)){continue};$dest=Join-Path $folder ($library.path.Replace('/','-'));New-Item -ItemType Directory -Force $dest | Out-Null
 foreach($file in Get-ChildItem -LiteralPath $path -File){if($file.Name -match '(?i)license|copying|notice|\.nuspec$'){Copy-Item -LiteralPath $file.FullName -Destination $dest}}
 [IO.File]::WriteAllText((Join-Path $dest 'SOURCE.txt'),"NuGet: https://www.nuget.org/packages/$($library.path)`nPackage metadata and license are preserved alongside this file.`n")
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE-RU.txt') -Destination $Target
