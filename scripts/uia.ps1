# Lattice UIA driver: enumerate / invoke / read elements via UIAutomation
# usage: powershell -NoProfile -ExecutionPolicy Bypass -File uia.ps1 <cmd> [args]
#   tree [limit]             dump all elements (index | type | name | rect)
#   texts [limit]            dump only Text elements (page state reader)
#   inv <substr>             InvokePattern on element whose Name contains substr
#   sel <substr>             SelectionItemPattern select (list items / tabs)
#   find <substr>            print matching elements
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$proc = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'Lattice*' } | Select-Object -First 1
if (-not $proc) { Write-Host "NO_PROCESS"; exit 1 }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
if ($null -eq $root) { Write-Host "NO_UIA_ROOT"; exit 1 }

$all = @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition))

function Rect-Str($r) {
    if ($r.IsEmpty) { return "(empty)" }
    "{0},{1} {2}x{3}" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height
}

switch ($args[0]) {
    "tree" {
        $limit = if ($args.Count -gt 1) { [int]$args[1] } else { 500 }
        for ($i = 0; $i -lt $all.Count -and $i -lt $limit; $i++) {
            $el = $all[$i]
            $ct = ($el.Current.ControlType.ProgrammaticName -replace 'ControlType\.', '')
            $n = $el.Current.Name
            if ($n -or $ct -in @('Button', 'Edit', 'ListItem', 'TabItem', 'CheckBox', 'Slider')) {
                Write-Host ("{0,4} | {1,-10} | {2} | {3}" -f $i, $ct, $n, (Rect-Str $el.Current.BoundingRectangle))
            }
        }
    }
    "texts" {
        $limit = if ($args.Count -gt 1) { [int]$args[1] } else { 200 }
        for ($i = 0; $i -lt $all.Count -and $i -lt $limit; $i++) {
            $el = $all[$i]
            $n = $el.Current.Name
            if ($n) {
                $ct = ($el.Current.ControlType.ProgrammaticName -replace 'ControlType\.', '')
                Write-Host ("{0,4} | {1,-10} | {2} | {3}" -f $i, $ct, $n, (Rect-Str $el.Current.BoundingRectangle))
            }
        }
    }
    "inv" {
        $q = $args[1]
        foreach ($el in $all) {
            if ($el.Current.Name -like "*$q*") {
                $cur = $el
                while ($cur) {
                    try {
                        $pat = $cur.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                        $pat.Invoke()
                        Write-Host ("INVOKED: " + $el.Current.Name)
                        exit 0
                    } catch { }
                    $cur = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($cur)
                }
                Write-Host ("no invoke chain: " + $el.Current.Name)
            }
        }
        Write-Host "NOT_FOUND: $q"
    }
    "sel" {
        $q = $args[1]
        foreach ($el in $all) {
            if ($el.Current.Name -like "*$q*") {
                $cur = $el
                while ($cur) {
                    try {
                        $pat = $cur.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                        $pat.Select()
                        Write-Host ("SELECTED: " + $el.Current.Name)
                        exit 0
                    } catch { }
                    $cur = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($cur)
                }
                Write-Host ("no select chain: " + $el.Current.Name)
            }
        }
        Write-Host "NOT_FOUND: $q"
    }
    "invn" {
        $idx = [int]$args[1]
        $el = $all[$idx]
        $cur = $el
        while ($cur) {
            try {
                $pat = $cur.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $pat.Invoke()
                Write-Host ("INVOKED #" + $idx + ": " + $el.Current.Name)
                exit 0
            } catch { }
            $cur = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($cur)
        }
        Write-Host "no invoke at #$idx"
    }
    "find" {
        $q = $args[1]
        for ($i = 0; $i -lt $all.Count; $i++) {
            $el = $all[$i]
            $n = $el.Current.Name
            if ($n -and $n -like "*$q*") {
                $ct = ($el.Current.ControlType.ProgrammaticName -replace 'ControlType\.', '')
                Write-Host ("{0,4} | {1,-10} | {2} | {3}" -f $i, $ct, $n, (Rect-Str $el.Current.BoundingRectangle))
            }
        }
    }
    default { Write-Host "usage: uia.ps1 tree|texts|inv|sel|find" }
}
