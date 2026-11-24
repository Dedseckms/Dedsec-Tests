$ErrorActionPreference='Stop';$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root '.tools/components';New-Item -ItemType Directory -Force $cache | Out-Null
$installer=Join-Path $cache 'PawnIO_setup.exe';$expected='1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032'
if(!(Test-Path -LiteralPath $installer)){Invoke-WebRequest 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $installer}
if((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected){throw 'PawnIO installer SHA256 mismatch'}
$signature=Get-AuthenticodeSignature -LiteralPath $installer
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=namazso.eu'){throw 'PawnIO Authenticode signature is not trusted'}
$source=Join-Path $root '.tools/pawnio-source-2.2.0'
if(!(Test-Path -LiteralPath $source)){& git clone --depth 1 --branch 2.2.0 --recurse-submodules --shallow-submodules https://github.com/namazso/PawnIO.git $source;if($LASTEXITCODE -ne 0){throw 'PawnIO source clone failed'}}
if((& git -C $source rev-parse HEAD) -ne '5cdf470831fdfff3f7f1d06363ca6b230f3bf35a'){throw 'Unexpected PawnIO source commit'}
if((& git -C (Join-Path $source 'PawnPP') rev-parse HEAD) -ne 'e64e4c37b2d8ba0d8ee57205faf8183aee12c438'){throw 'Unexpected PawnPP source commit'}
if((& git -C $source status --porcelain)){throw 'PawnIO source checkout is modified'}
$zipPath=Join-Path $cache 'PawnIO-2.2.0-source.zip'
& {
 $zip=[IO.Compression.ZipArchive]::new([IO.File]::Create($zipPath),[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($file in Get-ChildItem -LiteralPath $source -File -Recurse -Force | Sort-Object FullName){$relative=[IO.Path]::GetRelativePath($source,$file.FullName).Replace('\','/');if($relative -match '(^|/)\.git(/|$)'){continue};$entry=$zip.CreateEntry('PawnIO-2.2.0/'+$relative,[IO.Compression.CompressionLevel]::Optimal);$entry.LastWriteTime=[DateTimeOffset]::new(2026,3,15,0,0,0,[TimeSpan]::Zero);$input=[IO.File]::OpenRead($file.FullName);$output=$entry.Open();try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}}}finally{$zip.Dispose()}
}
$notice=@"
PawnIO official signed installer 2.2.0, unmodified.
Binary: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
SHA256: $expected
Driver source, GPL-2.0-or-later with the README exception:
https://github.com/namazso/PawnIO/tree/5cdf470831fdfff3f7f1d06363ca6b230f3bf35a
Complete driver source including the pinned PawnPP submodule is in PawnIO-2.2.0-source.zip.
COPYING and README are included in that archive. Source/build files are unmodified.
Only the official signature-checked driver is installed; unrestricted mode is not used.
"@
[IO.File]::WriteAllText((Join-Path $cache 'PawnIO-SOURCE.txt'),$notice)
Write-Output 'Offline component verified: PawnIO 2.2.0 SHA256 + Authenticode; source archive included'
