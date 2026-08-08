# Lattice UI automation helper (real-machine verification): shot / click / type / key / window
# usage: powershell -NoProfile -ExecutionPolicy Bypass -File ui.ps1 <cmd> [args]
#   shot <name>       save launcher-root/shot_<name>.png (window capture)
#   click <x> <y>     click at window logical coords (scaled for DPI)
#   type <text>       SendKeys text to foreground window
#   key <keys>        SendKeys single key sequence (e.g. {ENTER} {TAB} ^a)
#   win               print process + window rect
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class Ui32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }
}
'@

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Split-Path -Parent $root

function Get-Launcher {
    Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'Lattice*' } | Select-Object -First 1
}

function Get-LauncherRect {
    $proc = Get-Launcher
    if (-not $proc) { throw "launcher not running" }
    $rect = New-Object Ui32+RECT
    [Ui32]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
    return ,$rect
}

function Shot($name) {
    $rect = Get-LauncherRect
    $w = $rect.R - $rect.L; $h = $rect.B - $rect.T
    if ($w -le 0 -or $h -le 0) { throw "bad window size: $w x $h" }
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.L, $rect.T, 0, 0, $bmp.Size)
    $g.Dispose()
    $path = Join-Path $proj "shot_$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "saved $path ($w x $h)"
}

function Click($x, $y) {
    $rect = Get-LauncherRect
    $proc = Get-Launcher
    [Ui32]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null
    Start-Sleep -Milliseconds 200
    [Ui32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 200
    $physX = $rect.L + [int]($x * ($rect.R - $rect.L) / 900.0)
    $physY = $rect.T + [int]($y * ($rect.B - $rect.T) / 600.0)
    [Ui32]::SetCursorPos($physX, $physY) | Out-Null
    Start-Sleep -Milliseconds 120
    [Ui32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Ui32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Write-Host "click ($x,$y) -> phys ($physX,$physY)"
}

function TypeText($text) {
    $proc = Get-Launcher
    [Ui32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait($text)
    Write-Host "typed: $text"
}

function PressKey($key) {
    $proc = Get-Launcher
    [Ui32]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait($key)
    Write-Host "key: $key"
}

switch ($args[0]) {
    "shot"  { Shot $args[1] }
    "click" { Click ([int]$args[1]) ([int]$args[2]) }
    "type"  { TypeText $args[1] }
    "key"   { PressKey $args[1] }
    "win"   {
        $rect = Get-LauncherRect
        $proc = Get-Launcher
        Write-Host "pid=$($proc.Id) title=$($proc.MainWindowTitle)"
        Write-Host "rect: $($rect.L),$($rect.T) -> $($rect.R),$($rect.B)"
    }
    default { Write-Host "usage: ui.ps1 shot|click|type|key|win" }
}
