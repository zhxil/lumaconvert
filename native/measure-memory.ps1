param([Parameter(Mandatory)][string]$Executable, [string]$Fixtures = '测试媒体', [string]$Label = 'measure')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$run = Join-Path $root ('artifacts/memory-' + $Label + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $run | Out-Null
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class ProcessTree {
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Entry {
 public uint size,usage,pid; public UIntPtr heap; public uint module,threads,parent; public int priority; public uint flags;
 [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string exe;
 }
 [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32First(IntPtr h,ref Entry e);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32Next(IntPtr h,ref Entry e);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 public static int[] Children(int root) {
 var h=CreateToolhelp32Snapshot(2,0); var e=new Entry(); e.size=(uint)Marshal.SizeOf(e);
 var parents=new Dictionary<int,int>();
 try { if(Process32First(h,ref e)) do {parents[(int)e.pid]=(int)e.parent;} while(Process32Next(h,ref e)); } finally {CloseHandle(h);}
 var ids=new HashSet<int>{root}; bool changed;
 do {changed=false; foreach(var p in parents) if(ids.Contains(p.Value)&&ids.Add(p.Key)) changed=true;} while(changed);
 var a=new int[ids.Count]; ids.CopyTo(a); return a;
 }
}
'@
$argsList = @('--smoke-test','--fixtures', ('"'+[IO.Path]::GetFullPath((Join-Path $root $Fixtures))+'"'), '--output', ('"'+(Join-Path $run 'output')+'"'), '--report', ('"'+(Join-Path $run 'ui-verification.json')+'"'), '--keep-open')
$p = Start-Process -FilePath ([IO.Path]::GetFullPath((Join-Path $root $Executable))) -ArgumentList $argsList -WindowStyle Hidden -PassThru
$samples = [Collections.Generic.List[object]]::new()
$watch = [Diagnostics.Stopwatch]::StartNew()
$doneAt = $null
try {
 while (!$p.HasExited -and $watch.Elapsed.TotalSeconds -lt 180) {
  $private=0L; $working=0L; $mainPrivate=0L; $mainWorking=0L; $count=0
  foreach ($processId in [ProcessTree]::Children($p.Id)) {
   try { $q=Get-Process -Id $processId -ErrorAction Stop; $private+=$q.PrivateMemorySize64; $working+=$q.WorkingSet64; $count++; if($processId -eq $p.Id){$mainPrivate=$q.PrivateMemorySize64;$mainWorking=$q.WorkingSet64}; $q.Dispose() } catch {}
  }
  $done=Test-Path -LiteralPath (Join-Path $run 'ui-verification.json')
  if($done -and $null -eq $doneAt){$doneAt=$watch.Elapsed.TotalSeconds}
  $samples.Add([pscustomobject]@{seconds=$watch.Elapsed.TotalSeconds; treePrivate=$private; treeWorking=$working; mainPrivate=$mainPrivate; mainWorking=$mainWorking; processes=$count; completed=$done})
  if($null -ne $doneAt -and $watch.Elapsed.TotalSeconds-$doneAt -gt 8){break}
  Start-Sleep -Milliseconds 150
 }
 $samples | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $run 'samples.json')
 if (!$doneAt) { throw "Application did not complete verification: $run" }
 $verification=Get-Content -LiteralPath (Join-Path $run 'ui-verification.json') -Raw | ConvertFrom-Json
 if ($verification.completed -ne $verification.count -or $verification.errors.Count -ne 0) { throw "Conversion verification failed: $run" }
 $idle=@($samples | Where-Object completed | Select-Object -Last 20)
 $summary=[ordered]@{executable=$Executable; samplingMs=150; peakTreePrivate=($samples | Measure-Object treePrivate -Maximum).Maximum; peakTreeWorking=($samples | Measure-Object treeWorking -Maximum).Maximum; settledMainPrivate=($idle | Measure-Object mainPrivate -Average).Average; settledMainWorking=($idle | Measure-Object mainWorking -Average).Average; report=(Join-Path $run 'ui-verification.json')}
 $summary | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $run 'memory.json')
 $summary | ConvertTo-Json
} finally { if(!$p.HasExited){Stop-Process -Id $p.Id}; $p.Dispose() }
