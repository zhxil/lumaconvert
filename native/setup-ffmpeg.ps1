param([string]$ArchivePath = '', [string]$ExpectedSha256 = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $root ('.tools/ffmpeg-setup/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $cache | Out-Null
$name = 'ffmpeg-n8.1-latest-win64-gpl-shared-8.1.zip'
$source = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/$name"
if (!$ArchivePath) {
    $checksums = (Invoke-WebRequest 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256').Content
    if ($checksums -is [byte[]]) { $checksums = [Text.Encoding]::UTF8.GetString($checksums) }
    $entry = @($checksums -split "`n" | Where-Object { $_.Trim() -match ('^[0-9a-fA-F]{64}\s+\*?(?:\./)?' + [regex]::Escape($name) + '$') })
    if ($entry.Count -ne 1) { throw 'Could not find the exact archive in upstream checksums.sha256.' }
    $ExpectedSha256 = ($entry[0].Trim() -split '\s+')[0]
    $ArchivePath = Join-Path $cache $name
    Invoke-WebRequest -Uri $source -OutFile $ArchivePath
} elseif (!$ExpectedSha256) { throw 'A local archive requires -ExpectedSha256 from the upstream checksum file.' }
$hash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
if ($hash -ne $ExpectedSha256) { throw 'FFmpeg archive checksum mismatch. Nothing was installed.' }
Expand-Archive -LiteralPath $ArchivePath -DestinationPath (Join-Path $cache 'unpacked')
$folder = Get-ChildItem -LiteralPath (Join-Path $cache 'unpacked') -Directory | Select-Object -First 1
$tools = @('ffmpeg.exe','ffprobe.exe','avcodec-62.dll','avdevice-62.dll','avfilter-11.dll','avformat-62.dll','avutil-60.dll','swresample-6.dll','swscale-9.dll')
foreach ($file in $tools) {
    if (!(Test-Path -LiteralPath (Join-Path $folder.FullName "bin/$file"))) { throw "Missing FFmpeg component: $file" }
}
$destination = Join-Path $root 'bin/shared'
New-Item -ItemType Directory -Force $destination | Out-Null
foreach ($file in $tools) { Copy-Item -LiteralPath (Join-Path $folder.FullName "bin/$file") -Destination $destination }
Copy-Item -LiteralPath (Join-Path $folder.FullName 'LICENSE.txt') -Destination $destination
@{ source = $source; sha256 = $hash; installed = [DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $destination 'provenance.json')
Write-Output "FFmpeg installed and verified: $destination"
