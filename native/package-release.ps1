param([string]$ReleaseName = '轻转-v2.2.1-Windows-x64')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$release=Join-Path $root (Join-Path 'dist' $ReleaseName)
foreach($file in @('LightConvert.exe','LightConvert.ImageWorker.exe','tools/ffmpeg.exe','tools/ffprobe.exe','System.Console.dll')) {
 if(!(Test-Path -LiteralPath (Join-Path $release $file))){throw "Missing release file: $file"}
}
$archive=$release+'.zip'
if(Test-Path -LiteralPath $archive){throw 'Archive already exists; use a new release name.'}
Compress-Archive -LiteralPath $release -DestinationPath $archive -CompressionLevel Optimal
$hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -Encoding utf8 ($archive+'.sha256.txt')
Get-Item -LiteralPath $archive | Select-Object FullName,Length
