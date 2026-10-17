$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
function RoundedPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r) {
 $p=[Drawing.Drawing2D.GraphicsPath]::new(); $d=$r*2
 $p.AddArc($x,$y,$d,$d,180,90); $p.AddArc($x+$w-$d,$y,$d,$d,270,90); $p.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90); $p.AddArc($x,$y+$h-$d,$d,$d,90,90); $p.CloseFigure(); return $p
}
function RenderIcon([int]$size) {
 $im=[Drawing.Bitmap]::new($size,$size); $g=[Drawing.Graphics]::FromImage($im); $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias; $g.ScaleTransform($size/256.0,$size/256.0)
 $bg=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(0,0),[Drawing.Point]::new(256,256),[Drawing.ColorTranslator]::FromHtml('#1747B8'),[Drawing.ColorTranslator]::FromHtml('#3287DE'))
 $path=RoundedPath 4 4 248 248 58; $g.FillPath($bg,$path); $path.Dispose(); $bg.Dispose()
 foreach ($card in @(@(44,75,136,120,18,'#B9DAFF'),@(62,53,140,124,18,'#FFFFFF'),@(74,65,116,86,9,'#3779CB'))) {
  $b=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($card[5])); $p=RoundedPath $card[0] $card[1] $card[2] $card[3] $card[4]; $g.FillPath($b,$p); $p.Dispose(); $b.Dispose()
 }
 $white=[Drawing.SolidBrush]::new([Drawing.Color]::White); $g.FillEllipse($white,150,78,20,20)
 $points=[Drawing.PointF[]]@([Drawing.PointF]::new(80,142),[Drawing.PointF]::new(112,105),[Drawing.PointF]::new(136,128),[Drawing.PointF]::new(150,114),[Drawing.PointF]::new(184,142)); $g.FillPolygon($white,$points)
 $teal=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#0B9B91')); $g.FillEllipse($teal,140,138,86,86)
 $pen=[Drawing.Pen]::new([Drawing.Color]::White,10); $pen.StartCap=$pen.EndCap=[Drawing.Drawing2D.LineCap]::Round; $g.DrawLine($pen,160,181,204,181); $g.DrawLine($pen,189,166,204,181); $g.DrawLine($pen,204,181,189,196)
 $g.Dispose(); $pen.Dispose(); $white.Dispose(); $teal.Dispose(); return $im
}
$assets=Join-Path $PSScriptRoot 'LightConvert.WinUI/Assets'
$large=RenderIcon 1024; $large.Save((Join-Path $assets 'LightConvert.png'),[Drawing.Imaging.ImageFormat]::Png); $large.Dispose()
$sizes=@(16,20,24,32,40,48,64,128,256); $payloads=@()
foreach ($size in $sizes) { $image=RenderIcon $size; $stream=[IO.MemoryStream]::new(); $image.Save($stream,[Drawing.Imaging.ImageFormat]::Png); $payloads+=,@($stream.ToArray()); $stream.Dispose(); $image.Dispose() }
$writer=[IO.BinaryWriter]::new([IO.File]::Create((Join-Path $assets 'LightConvert.ico')))
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count); $offset=6+16*$sizes.Count
for($i=0;$i -lt $sizes.Count;$i++) { $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}; $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$payloads[$i].Length); $writer.Write([uint32]$offset); $offset+=$payloads[$i].Length }
foreach($bytes in $payloads){$writer.Write([byte[]]$bytes)}
$writer.Dispose()
