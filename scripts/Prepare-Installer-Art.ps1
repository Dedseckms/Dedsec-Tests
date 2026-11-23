param([string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop';Add-Type -AssemblyName System.Drawing
$source=[Drawing.Image]::FromFile((Join-Path $Root 'src\Dedsec.Tests\Assets\app.png'))
$bmp=[Drawing.Bitmap]::new(240,470);$g=[Drawing.Graphics]::FromImage($bmp);$g.Clear([Drawing.Color]::FromArgb(11,14,18));$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$lime=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(193,250,85));$muted=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(145,163,178));$pen=[Drawing.Pen]::new([Drawing.Color]::FromArgb(45,65,37),1)
for($x=0;$x -lt 240;$x+=24){$g.DrawLine($pen,$x,0,$x,470)};for($y=0;$y -lt 470;$y+=24){$g.DrawLine($pen,0,$y,240,$y)}
$g.DrawImage($source,32,65,176,176);$font=[Drawing.Font]::new('Segoe UI',26,[Drawing.FontStyle]::Bold);$g.DrawString('DEDSEC',$font,$lime,29,280);$g.DrawString('/ TESTS',$font,$lime,29,320);$small=[Drawing.Font]::new('Segoe UI',10);$g.DrawString('HARDWARE. UNDER CONTROL.',$small,$muted,22,424);$bmp.Save((Join-Path $Root 'installer\wizard.bmp'),[Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose();$bmp.Dispose();$font.Dispose();$small.Dispose();$pen.Dispose();$lime.Dispose();$muted.Dispose();$icon=[Drawing.Bitmap]::new($source,64,64);$icon.Save((Join-Path $Root 'installer\small.bmp'),[Drawing.Imaging.ImageFormat]::Bmp);$icon.Dispose();$source.Dispose()
