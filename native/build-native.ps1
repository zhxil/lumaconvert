param([string]$DotnetPath = 'dotnet', [string]$FallbackPackages = '', [string]$ReleaseName = '轻转-v2.2.1-Windows-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot
if (!(Test-Path -LiteralPath (Join-Path $projectRoot 'bin/shared/ffmpeg.exe'))) {
    throw 'Missing FFmpeg. Run: pwsh -File native/setup-ffmpeg.ps1'
}
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.tools/http-cache'
$env:NUGET_SCRATCH = Join-Path $projectRoot '.tools/scratch'
$env:APPDATA = Join-Path $projectRoot '.tools/appdata'
$env:TEMP = Join-Path $projectRoot '.tmp/native-temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
New-Item -ItemType Directory -Force $env:TEMP, $env:NUGET_SCRATCH, $env:APPDATA | Out-Null
$restoreArgs = @("-p:RestoreConfigFile=$(Join-Path $PSScriptRoot 'NuGet.Config')")
if ($FallbackPackages) { $restoreArgs += "-p:RestoreFallbackFolders=$FallbackPackages" }
& $DotnetPath publish native/LightConvert.WinUI/LightConvert.WinUI.csproj -c Release -r win-x64 --self-contained true -o (Join-Path dist $ReleaseName) @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'WinUI 3 发布失败' }
$release = Join-Path $projectRoot (Join-Path 'dist' $ReleaseName)
$worker = Join-Path $projectRoot '.tmp/image-worker-publish'
& $DotnetPath publish native/LightConvert.ImageWorker/LightConvert.ImageWorker.csproj -c Release -r win-x64 --self-contained true -o $worker @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'Image worker publish failed' }
# Both executables share the same .NET runtime and native decoder files.
foreach ($name in @('LightConvert.ImageWorker.exe', 'LightConvert.ImageWorker.dll', 'LightConvert.ImageWorker.deps.json', 'LightConvert.ImageWorker.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path $worker $name) -Destination (Join-Path $release $name)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用说明-WinUI3.txt') -Destination (Join-Path $release '使用说明.txt')
New-Item -ItemType Directory -Force (Join-Path $release 'licenses') | Out-Null
& (Join-Path $projectRoot 'bin/shared/ffmpeg.exe') -L 2>&1 | Out-File -Encoding utf8 (Join-Path $release 'licenses/FFMPEG-LICENSE.txt')
if (Test-Path -LiteralPath (Join-Path $projectRoot 'bin/shared/LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'bin/shared/LICENSE.txt') -Destination (Join-Path $release 'licenses/FFMPEG-GPL-LICENSE.txt')
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $release 'licenses/LumaConvert-MIT-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $env:NUGET_PACKAGES 'magick.net-q8-x64/15.0.0/Notice.txt') -Destination (Join-Path $release 'licenses/Magick.NET-NOTICE.txt')
