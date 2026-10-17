param([Parameter(Mandatory)][int]$TargetProcessId, [Parameter(Mandatory)][string]$ReportPath, [switch]$AllowFailure)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SettingsLayoutWindow {
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
[SettingsLayoutWindow]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$process=Get-Process -Id $TargetProcessId
$window=[Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
function Find-Control([string]$id) {
 $window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
}
$settings=Find-Control 'SettingsItem'
$settings.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
$scale=[SettingsLayoutWindow]::GetDpiForWindow($process.MainWindowHandle)/96.0
$referenceLeft=$null
$cases=foreach($logicalWidth in @(940,1240,1440)) {
 [SettingsLayoutWindow]::SetWindowPos($process.MainWindowHandle,[IntPtr]::Zero,40,40,[int]($logicalWidth*$scale),[int](720*$scale),0x44)|Out-Null
 Start-Sleep -Milliseconds 600
 $frame=$window.Current.BoundingRectangle
 $input=(Find-Control 'OutputBox').Current.BoundingRectangle
 $browse=(Find-Control 'ChooseFolderButton').Current.BoundingRectangle
 $preset=(Find-Control 'DefaultPresetBox').Current.BoundingRectangle
 $contained=$input.Left -ge ($frame.Left+180*$scale) -and $input.Right -le ($frame.Right-16*$scale) -and $browse.Right -le ($frame.Right-16*$scale) -and $preset.Right -le ($frame.Right-16*$scale)
 $leftOffset=$input.Left-$frame.Left
 if($null -eq $referenceLeft){$referenceLeft=$leftOffset}
 $anchored=[Math]::Abs($leftOffset-$referenceLeft) -le 2
 [pscustomobject]@{logicalWidth=$logicalWidth;scale=$scale;windowLeft=$frame.Left;windowRight=$frame.Right;inputLeft=$input.Left;inputRight=$input.Right;browseRight=$browse.Right;presetRight=$preset.Right;contained=$contained;alignedToNavigation=$anchored}
}
$result=@{passed=@($cases|Where-Object {!$_.contained -or !$_.alignedToNavigation}).Count -eq 0;cases=$cases}
$result|ConvertTo-Json -Depth 5|Set-Content -Encoding utf8 $ReportPath
$result|ConvertTo-Json -Depth 5
if(!$result.passed -and !$AllowFailure){throw 'Settings controls overflow the window'}
