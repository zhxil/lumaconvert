param([switch]$KeepOpen, [string]$Fixtures = '', [switch]$WheelTest, [switch]$ShowSettings)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'dist/轻转-v2.3.0-Windows-x64/LightConvert.exe'
$core = Get-Content (Join-Path $projectRoot '.tmp/latest-core-run.json') | ConvertFrom-Json
$fixturePath = Join-Path $core.artifacts 'fixtures'
if ($Fixtures) { $fixturePath = [IO.Path]::GetFullPath($Fixtures) }
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$resultRoot = Join-Path $projectRoot "artifacts/winui-$runId"
New-Item -ItemType Directory -Force $resultRoot | Out-Null
$arguments = @('--smoke-test', '--fixtures', ('"' + $fixturePath + '"'), '--output', ('"' + (Join-Path $resultRoot 'output') + '"'), '--report', ('"' + (Join-Path $resultRoot 'ui-verification.json') + '"'))
if ($KeepOpen) { $arguments += '--keep-open' }
if ($WheelTest) { $arguments += '--wheel-test' }
if ($ShowSettings) { $arguments += '--show-settings' }
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
@{ processId = $process.Id; artifacts = $resultRoot } | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $projectRoot '.tmp/latest-ui-run.json')
Write-Output "Process: $($process.Id)"
Write-Output "Report: $resultRoot"
