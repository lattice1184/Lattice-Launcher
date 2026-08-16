# track-download.ps1 - Download progress tracker for Lattice vs Verse benchmark
# Usage: powershell -ExecutionPolicy Bypass -File track-download.ps1 -Dir <target dir> -Out <csv>
# Monitors total size of a directory every 250ms, records speed curve, auto-finishes
# when size is stable for -StableSec seconds after growing beyond -MinBytes.
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [string]$Out = "",
    [double]$StableSec = 5.0,
    [long]$MinBytes = 1MB
)

if (-not (Test-Path $Dir)) { Write-Host "ERROR: dir not found: $Dir"; exit 1 }
if (-not $Out) { $Out = "track-" + (Get-Date -Format "HHmmss") + ".csv" }

function Get-DirSize([string]$path) {
    $sum = [long]0
    try {
        foreach ($f in [System.IO.Directory]::EnumerateFiles($path, "*", [System.IO.SearchOption]::AllDirectories)) {
            try { $sum += (Get-Item -LiteralPath $f -ErrorAction Stop).Length } catch { }
        }
    } catch { }
    return $sum
}

$baseline = Get-DirSize $Dir
Write-Host ("Baseline: " + [math]::Round($baseline / 1MB, 2) + " MB")
Write-Host "Tracking... (Ctrl+C to stop; auto-finish after stable " + $StableSec + "s and >" + [math]::Round($MinBytes / 1MB, 1) + "MB growth)"

$rows = New-Object System.Collections.Generic.List[string]
$start = [DateTime]::Now
$prevT = $start
$prevBytes = $baseline
$stableCount = 0
$peak = 0.0
$grew = $false
$sampleCount = 0

try {
    while ($true) {
        Start-Sleep -Milliseconds 250
        $now = [DateTime]::Now
        $bytes = Get-DirSize $Dir
        $elapsed = ($now - $start).TotalSeconds
        $dt = ($now - $prevT).TotalSeconds
        if ($dt -gt 0) {
            $delta = $bytes - $prevBytes
            $mbps = ($delta / 1MB) / $dt
            if ($mbps -gt $peak) { $peak = $mbps }
            if ($delta -gt 0) { $grew = $true }
            $grewMB = ($bytes - $baseline) / 1MB
            $rows.Add(([math]::Round($elapsed, 2).ToString() + "," + [math]::Round($grewMB, 2).ToString() + "," + [math]::Round($mbps, 2).ToString()))
            $sampleCount++
            $status = ("t=" + [math]::Round($elapsed, 1) + "s  size=" + [math]::Round($grewMB, 1) + "MB  speed=" + [math]::Round($mbps, 2) + "MB/s  peak=" + [math]::Round($peak, 2) + "MB/s")
            Write-Host ("`r" + $status + "        ") -NoNewline
            if ($delta -lt 10240) { $stableCount++ } else { $stableCount = 0 }
        }
        $prevT = $now
        $prevBytes = $bytes
        $grown = ($bytes - $baseline)
        if ($grew -and $grown -ge $MinBytes -and $stableCount -ge ([int]($StableSec / 0.25))) { break }
    }
}
finally {
    Write-Host ""
    $grown = ($prevBytes - $baseline)
    $elapsed = ($prevT - $start).TotalSeconds
    $avg = ($grown / 1MB) / [math]::Max($elapsed, 0.001)
    Write-Host "=== Summary ==="
    Write-Host ("Elapsed: " + [math]::Round($elapsed, 1) + " s")
    Write-Host ("Growth:  " + [math]::Round($grown / 1MB, 2) + " MB")
    Write-Host ("Average: " + [math]::Round($avg, 2) + " MB/s")
    Write-Host ("Peak:    " + [math]::Round($peak, 2) + " MB/s")
    $rows.Insert(0, "sec,sizeMB,speedMBps")
    [System.IO.File]::WriteAllLines((Join-Path (Get-Location) $Out), $rows)
    Write-Host ("CSV saved: " + $Out + " (" + $sampleCount + " samples)")
}
