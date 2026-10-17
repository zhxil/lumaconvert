param([switch]$Settings)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Set-Location $root
$run=Get-Content .tmp/latest-ui-run.json | ConvertFrom-Json
$p=Get-Process -Id $run.processId
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class ScrollDriver {
 [StructLayout(LayoutKind.Sequential)]public struct POINT{public int X,Y;}
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr z,int x,int y,int w,int ht,uint flags);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern bool GetCursorPos(out POINT p);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]private static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 public static void Scroll(int d){mouse_event(0x800,0,0,unchecked((uint)d),UIntPtr.Zero);}
}
'@
[ScrollDriver]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
[ScrollDriver]::SetWindowPos($p.MainWindowHandle,[IntPtr](-1),100,100,1550,800,0x40)|Out-Null
[ScrollDriver]::SetForegroundWindow($p.MainWindowHandle)|Out-Null
Start-Sleep -Seconds 1
$before=Get-Content (Join-Path $run.artifacts 'interaction.json')|ConvertFrom-Json
$cursor=[ScrollDriver+POINT]::new(); [ScrollDriver]::GetCursorPos([ref]$cursor)|Out-Null
try {
 [ScrollDriver]::SetCursorPos(800,500)|Out-Null; [ScrollDriver]::Scroll(-360); Start-Sleep -Seconds 1
 $after=Get-Content (Join-Path $run.artifacts 'interaction.json')|ConvertFrom-Json
 if(!$Settings) {
  [ScrollDriver]::SetCursorPos(1450,350)|Out-Null; [ScrollDriver]::Scroll(-360); Start-Sleep -Seconds 1
  $preview=Get-Content (Join-Path $run.artifacts 'interaction.json')|ConvertFrom-Json
 }
} finally {[ScrollDriver]::SetCursorPos($cursor.X,$cursor.Y)|Out-Null}
$result=@{before=$before; after=$after}
if($Settings){$result.settingsWheelScrolled=$after.settingsOffset -gt $before.settingsOffset}
else {$result.queueWheelScrolled=$after.queueOffset -gt $before.queueOffset; $result.preview=$preview; $result.previewWheelScrolled=($preview.previewOffset -gt $before.previewOffset -or $preview.previewScrollable -eq 0)}
$result|ConvertTo-Json -Depth 5|Set-Content -Encoding utf8 (Join-Path $run.artifacts 'mouse-wheel-verification.json')
$result|ConvertTo-Json -Depth 5
[ScrollDriver]::SetWindowPos($p.MainWindowHandle,[IntPtr](-2),0,0,0,0,0x13)|Out-Null
