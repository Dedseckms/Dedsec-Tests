param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$source=[Drawing.Image]::FromFile((Join-Path $Root 'src\Dedsec.Tests\Assets\app.png'))
$stream=[IO.MemoryStream]::new();$writer=[IO.BinaryWriter]::new($stream)
$sizes=@(16,24,32,48,64,128,256);$images=@()
foreach($size in $sizes){$bitmap=[Drawing.Bitmap]::new($source,$size,$size);$png=[IO.MemoryStream]::new();$bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png);$images+=,@($png.ToArray());if($size -eq 256){$bitmap.Save((Join-Path $Root 'server\web\icon.png'),[Drawing.Imaging.ImageFormat]::Png)};$bitmap.Dispose();$png.Dispose()}
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count);$offset=6+16*$sizes.Count
for($i=0;$i -lt $sizes.Count;$i++){$size=$sizes[$i];$encoded=if($size -eq 256){0}else{$size};$writer.Write([byte]$encoded);$writer.Write([byte]$encoded);$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$images[$i].Length);$writer.Write([uint32]$offset);$offset+=$images[$i].Length}
foreach($bytes in $images){$writer.Write([byte[]]$bytes)}
[IO.File]::WriteAllBytes((Join-Path $Root 'src\Dedsec.Tests\Assets\app.ico'),$stream.ToArray());$writer.Dispose();$stream.Dispose();$source.Dispose()
Copy-Item -LiteralPath (Join-Path $Root 'src\Dedsec.Tests\Assets\app.ico') -Destination (Join-Path $Root 'server\web\favicon.ico')
Copy-Item -LiteralPath (Join-Path $Root 'server\web\icon.png') -Destination (Join-Path $Root 'docs\assets\icon.png')
