param([string]$DotnetPath = 'dotnet', [string]$FallbackPackages = '', [string]$ImageWorker = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.tools/http-cache'
$env:NUGET_SCRATCH = Join-Path $projectRoot '.tools/scratch'
$env:APPDATA = Join-Path $projectRoot '.tools/appdata'
$env:TEMP = Join-Path $projectRoot '.tmp/native-temp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$restoreArgs = @("-p:RestoreConfigFile=$(Join-Path $PSScriptRoot 'NuGet.Config')")
if ($FallbackPackages) { $restoreArgs += "-p:RestoreFallbackFolders=$FallbackPackages" }
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$resultRoot = Join-Path $projectRoot "artifacts/core-$runId"
$testArguments = @($resultRoot, (Join-Path $projectRoot 'bin/shared'), (Join-Path $PSScriptRoot 'fixtures/sample.heic'))
if ($ImageWorker) { $testArguments += [IO.Path]::GetFullPath($ImageWorker) }
& $DotnetPath run --project native/LightConvert.Verify/LightConvert.Verify.csproj -c Release @restoreArgs -- @testArguments
if ($LASTEXITCODE -ne 0) { throw 'C# 集成验证失败' }
@{ artifacts = $resultRoot } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $projectRoot '.tmp/latest-core-run.json')
