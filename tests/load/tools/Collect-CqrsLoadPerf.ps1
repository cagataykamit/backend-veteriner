#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS load test kosusu sirasinda API ve SQL Server kaynak kullanimi ornekler.

.PARAMETER DurationSeconds
  Ornekleme suresi.

.PARAMETER IntervalSeconds
  Ornekleme araligi.

.PARAMETER OutputPath
  JSON cikti dosyasi.

.PARAMETER ApiProcessName
  API process adi (varsayilan Backend.Veteriner.Api).
#>
[CmdletBinding()]
param(
    [int]$DurationSeconds = 60,
    [int]$IntervalSeconds = 2,
    [string]$OutputPath,
    [string]$ApiProcessName = "Backend.Veteriner.Api"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsLoadCommon.ps1")

function Get-ProcessCpuPercent {
    param(
        [System.Diagnostics.Process]$Process,
        [double]$ElapsedSeconds
    )

    if ($null -eq $Process -or $ElapsedSeconds -le 0) {
        return 0
    }

    try {
        $cpu = $Process.TotalProcessorTime.TotalMilliseconds
        return [math]::Round(($cpu / ($ElapsedSeconds * 1000 * [Environment]::ProcessorCount)) * 100, 2)
    }
    catch {
        return 0
    }
}

function Get-CounterSafe {
    param([string]$Path)

    try {
        $sample = Get-Counter -Counter $Path -ErrorAction Stop
        return [double]$sample.CounterSamples[0].CookedValue
    }
    catch {
        return $null
    }
}

$repoRoot = Get-CqrsLoadRepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "tests\load\results\perf-sample.json"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repoRoot $OutputPath
}

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$startUtc = [DateTime]::UtcNow
$apiProcess = Get-Process -Name $ApiProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
$sqlProcess = Get-Process -Name "sqlservr" -ErrorAction SilentlyContinue | Select-Object -First 1

$apiCpuStart = if ($apiProcess) { $apiProcess.TotalProcessorTime } else { $null }
$sqlCpuStart = if ($sqlProcess) { $sqlProcess.TotalProcessorTime } else { $null }

$samples = New-Object System.Collections.Generic.List[object]
$deadline = (Get-Date).AddSeconds($DurationSeconds)

while ((Get-Date) -lt $deadline) {
    $sampleUtc = [DateTime]::UtcNow

    $api = Get-Process -Name $ApiProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    $sql = Get-Process -Name "sqlservr" -ErrorAction SilentlyContinue | Select-Object -First 1

    $samples.Add([ordered]@{
            sampleUtc          = $sampleUtc.ToString("o")
            apiWorkingSetMb    = if ($api) { [math]::Round($api.WorkingSet64 / 1MB, 2) } else { $null }
            apiThreadCount     = if ($api) { $api.Threads.Count } else { $null }
            sqlWorkingSetMb    = if ($sql) { [math]::Round($sql.WorkingSet64 / 1MB, 2) } else { $null }
            availableMemoryMb  = Get-CounterSafe -Path "\Memory\Available MBytes"
            diskQueueLength    = Get-CounterSafe -Path "\PhysicalDisk(_Total)\Current Disk Queue Length"
        }) | Out-Null

    Start-Sleep -Seconds $IntervalSeconds
}

$endUtc = [DateTime]::UtcNow
$elapsed = ($endUtc - $startUtc).TotalSeconds

$apiEnd = Get-Process -Name $ApiProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
$sqlEnd = Get-Process -Name "sqlservr" -ErrorAction SilentlyContinue | Select-Object -First 1

function Get-Percentile {
    param([double[]]$Values, [double]$Percentile)

    if ($null -eq $Values -or $Values.Count -eq 0) {
        return $null
    }

    $sorted = $Values | Sort-Object
    $index = [math]::Ceiling(($Percentile / 100.0) * $sorted.Count) - 1
    if ($index -lt 0) { $index = 0 }
    if ($index -ge $sorted.Count) { $index = $sorted.Count - 1 }
    return [math]::Round($sorted[$index], 2)
}

$apiWs = @($samples | ForEach-Object { $_.apiWorkingSetMb } | Where-Object { $null -ne $_ })
$sqlWs = @($samples | ForEach-Object { $_.sqlWorkingSetMb } | Where-Object { $null -ne $_ })
$mem = @($samples | ForEach-Object { $_.availableMemoryMb } | Where-Object { $null -ne $_ })
$diskQ = @($samples | ForEach-Object { $_.diskQueueLength } | Where-Object { $null -ne $_ })

$summary = [ordered]@{
    startUtc = $startUtc.ToString("o")
    endUtc   = $endUtc.ToString("o")
    durationSeconds = [math]::Round($elapsed, 2)
    api = [ordered]@{
        processFound = ($null -ne $apiEnd)
        cpuAvgPercent = if ($apiEnd -and $apiCpuStart) {
            $deltaMs = ($apiEnd.TotalProcessorTime - $apiCpuStart).TotalMilliseconds
            [math]::Round(($deltaMs / ($elapsed * 1000 * [Environment]::ProcessorCount)) * 100, 2)
        } else { $null }
        workingSetAvgMb = if ($apiWs.Count -gt 0) { Get-Percentile -Values @($apiWs) -Percentile 50 } else { $null }
        workingSetP95Mb = if ($apiWs.Count -gt 0) { Get-Percentile -Values @($apiWs) -Percentile 95 } else { $null }
        workingSetMaxMb = if ($apiWs.Count -gt 0) { [math]::Round((($apiWs | Measure-Object -Maximum).Maximum), 2) } else { $null }
        threadCountLast = if ($apiEnd) { $apiEnd.Threads.Count } else { $null }
    }
    sqlServer = [ordered]@{
        processFound = ($null -ne $sqlEnd)
        cpuAvgPercent = if ($sqlEnd -and $sqlCpuStart) {
            $deltaMs = ($sqlEnd.TotalProcessorTime - $sqlCpuStart).TotalMilliseconds
            [math]::Round(($deltaMs / ($elapsed * 1000 * [Environment]::ProcessorCount)) * 100, 2)
        } else { $null }
        workingSetAvgMb = if ($sqlWs.Count -gt 0) { Get-Percentile -Values @($sqlWs) -Percentile 50 } else { $null }
        workingSetP95Mb = if ($sqlWs.Count -gt 0) { Get-Percentile -Values @($sqlWs) -Percentile 95 } else { $null }
        workingSetMaxMb = if ($sqlWs.Count -gt 0) { [math]::Round((($sqlWs | Measure-Object -Maximum).Maximum), 2) } else { $null }
    }
    system = [ordered]@{
        availableMemoryAvgMb = if ($mem.Count -gt 0) { Get-Percentile -Values @($mem) -Percentile 50 } else { $null }
        availableMemoryMinMb = if ($mem.Count -gt 0) { [math]::Round((($mem | Measure-Object -Minimum).Minimum), 2) } else { $null }
        diskQueueAvg = if ($diskQ.Count -gt 0) { Get-Percentile -Values @($diskQ) -Percentile 50 } else { $null }
        diskQueueP95 = if ($diskQ.Count -gt 0) { Get-Percentile -Values @($diskQ) -Percentile 95 } else { $null }
        diskQueueMax = if ($diskQ.Count -gt 0) { [math]::Round((($diskQ | Measure-Object -Maximum).Maximum), 2) } else { $null }
    }
    samples = $samples
}

$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputPath -Encoding UTF8
Write-Output $summary
