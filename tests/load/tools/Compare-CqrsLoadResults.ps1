#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS load test k6 summary dosyalarini karsilastirir.

.PARAMETER ResultsDirectory
  tests/load/results veya belirli bir kosu klasoru.

.PARAMETER OutputPath
  Opsiyonel CSV cikti yolu.
#>
[CmdletBinding()]
param(
    [string]$ResultsDirectory,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsLoadCommon.ps1")

function Get-SummaryFiles {
    param([string]$Root)

    if (-not (Test-Path -LiteralPath $Root)) {
        return @()
    }

    $directSummary = Join-Path $Root "k6-summary.json"
    if (Test-Path -LiteralPath $directSummary) {
        return @($directSummary)
    }

    return Get-ChildItem -Path $Root -Recurse -Filter "k6-summary.json" -File |
        Sort-Object FullName |
        ForEach-Object { $_.FullName }
}

function Read-RunMetadata {
    param([string]$SummaryFile)

    $runDir = Split-Path -Parent $SummaryFile
    $metadataPath = Join-Path $runDir "run-metadata.json"
    if (-not (Test-Path -LiteralPath $metadataPath)) {
        return $null
    }

    return Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
}

function Test-MetricHasProperty {
    param(
        $Metric,
        [string]$PropertyName
    )

    return ($null -ne $Metric -and ($Metric.PSObject.Properties.Name -contains $PropertyName))
}

function Get-MetricValue {
    param(
        $Summary,
        [string]$MetricName,
        [string]$Stat = "p(95)"
    )

    if ($null -eq $Summary -or $null -eq $Summary.metrics) {
        return $null
    }

    $metrics = $Summary.metrics
    if (-not ($metrics.PSObject.Properties.Name -contains $MetricName)) {
        return $null
    }

    $metric = $metrics.$MetricName

    if (Test-MetricHasProperty -Metric $metric -PropertyName "values") {
        $values = $metric.values
        if ($null -ne $values -and (Test-MetricHasProperty -Metric $values -PropertyName $Stat)) {
            return [double]$values.$Stat
        }

        if ($null -ne $values -and (Test-MetricHasProperty -Metric $values -PropertyName "avg")) {
            return [double]$values.avg
        }
    }

    if (Test-MetricHasProperty -Metric $metric -PropertyName $Stat) {
        return [double]$metric.$Stat
    }

    if (Test-MetricHasProperty -Metric $metric -PropertyName "value") {
        return [double]$metric.value
    }

    return $null
}

function Get-MetricCount {
    param(
        $Summary,
        [string]$MetricName
    )

    if ($null -eq $Summary -or $null -eq $Summary.metrics) {
        return $null
    }

    $metrics = $Summary.metrics
    if (-not ($metrics.PSObject.Properties.Name -contains $MetricName)) {
        return $null
    }

    $metric = $metrics.$MetricName

    if (Test-MetricHasProperty -Metric $metric -PropertyName "values") {
        $values = $metric.values
        if ($null -ne $values -and (Test-MetricHasProperty -Metric $values -PropertyName "count")) {
            return [long]$values.count
        }
    }

    if (Test-MetricHasProperty -Metric $metric -PropertyName "count") {
        return [long]$metric.count
    }

    return $null
}

function Format-Metric {
    param($Value)

    if ($null -eq $Value) {
        return "N/A"
    }

    return ([math]::Round([double]$Value, 2)).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Delta {
    param($Left, $Right)

    if ($null -eq $Left -or $null -eq $Right) {
        return "N/A"
    }

    $delta = [double]$Right - [double]$Left
    return ([math]::Round($delta, 2)).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}

$repoRoot = Get-CqrsLoadRepositoryRoot
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $repoRoot "tests\load\results"
}
elseif (-not [System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $repoRoot $ResultsDirectory
}

$ResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory)
$summaryFiles = Get-SummaryFiles -Root $ResultsDirectory

$rows = New-Object System.Collections.Generic.List[object]

foreach ($summaryFile in $summaryFiles) {
    $summary = Get-Content -LiteralPath $summaryFile -Raw | ConvertFrom-Json
    $metadata = Read-RunMetadata -SummaryFile $summaryFile

    $mode = if ($metadata) { [string]$metadata.mode } else { "unknown" }
    $workload = if ($metadata) { [string]$metadata.workload } else { "unknown" }

    $httpP50 = Get-MetricValue -Summary $summary -MetricName "http_req_duration" -Stat "p(50)"
    $httpP95 = Get-MetricValue -Summary $summary -MetricName "http_req_duration" -Stat "p(95)"
    $httpP99 = Get-MetricValue -Summary $summary -MetricName "http_req_duration" -Stat "p(99)"
    $httpMax = Get-MetricValue -Summary $summary -MetricName "http_req_duration" -Stat "max"

    $httpFailedRate = Get-MetricValue -Summary $summary -MetricName "http_req_failed" -Stat "rate"
    if ($null -eq $httpFailedRate) {
        $httpFailedRate = Get-MetricValue -Summary $summary -MetricName "http_req_failed" -Stat "value"
    }

    $iterDuration = Get-MetricValue -Summary $summary -MetricName "iteration_duration" -Stat "avg"
    $iterCount = Get-MetricCount -Summary $summary -MetricName "iterations"
    $requestsPerSec = $null
    $hasTiming = $false
    if ($null -ne $metadata) {
        $metaProps = $metadata.PSObject.Properties.Name
        $hasTiming = ($metaProps -contains "startUtc") -and ($metaProps -contains "endUtc") `
            -and $metadata.startUtc -and $metadata.endUtc
    }

    if ($null -ne $iterCount -and $hasTiming) {
        $elapsed = ([datetime]$metadata.endUtc - [datetime]$metadata.startUtc).TotalSeconds
        if ($elapsed -gt 0) {
            $requestsPerSec = [double]$iterCount / $elapsed
        }
    }

    $row = [ordered]@{
        Mode                         = $mode
        Workload                     = $workload
        Vus                          = if ($metadata) { [string]$metadata.vus } else { "" }
        Duration                     = if ($metadata) { [string]$metadata.duration } else { "" }
        RequestsPerSec               = Format-Metric $requestsPerSec
        HttpP50                      = Format-Metric $httpP50
        HttpP95                      = Format-Metric $httpP95
        HttpP99                      = Format-Metric $httpP99
        HttpMax                      = Format-Metric $httpMax
        FailureRate                  = Format-Metric $httpFailedRate
        ListP95                      = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_list_duration")
        CalendarP95                  = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_calendar_duration")
        DashboardP95                 = Format-Metric (Get-MetricValue -Summary $summary -MetricName "dashboard_duration")
        CreateP95                      = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_create_duration")
        RescheduleP95                  = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_reschedule_duration")
        CancelP95                      = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_cancel_duration")
        ProjectionCreateP50            = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_create_lag_ms" -Stat "p(50)")
        ProjectionCreateP95            = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_create_lag_ms")
        ProjectionCreateP99            = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_create_lag_ms" -Stat "p(99)")
        ProjectionCreateMax            = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_create_lag_ms" -Stat "max")
        ProjectionRescheduleP50        = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_reschedule_lag_ms" -Stat "p(50)")
        ProjectionRescheduleP95        = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_reschedule_lag_ms")
        ProjectionRescheduleP99        = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_reschedule_lag_ms" -Stat "p(99)")
        ProjectionRescheduleMax        = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_reschedule_lag_ms" -Stat "max")
        ProjectionCancelP50            = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_cancel_lag_ms" -Stat "p(50)")
        ProjectionCancelP95            = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_cancel_lag_ms")
        ProjectionCancelP99            = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_cancel_lag_ms" -Stat "p(99)")
        ProjectionCancelMax            = Format-Metric (Get-MetricValue -Summary $summary -MetricName "appointment_projection_cancel_lag_ms" -Stat "max")
        ProjectionTimeoutCount         = Format-Metric (Get-MetricCount -Summary $summary -MetricName "appointment_projection_timeout_rate")
        CleanupFailures                = Format-Metric (Get-MetricCount -Summary $summary -MetricName "appointment_cleanup_failures")
        SummaryFile                    = $summaryFile
    }

    $rows.Add([PSCustomObject]$row) | Out-Null
}

$comparison = [ordered]@{}
function Find-Row {
    param([string]$ModeName, [string]$WorkloadName)

    $candidates = @($rows | Where-Object {
        $_.Mode -eq $ModeName -and $_.Workload -eq $WorkloadName
    })

    if ($candidates.Count -eq 0) {
        return $null
    }

    $preferred = @($candidates | Where-Object {
        $_.Vus -eq "100" -and $_.Duration -eq "5m"
    }) | Sort-Object SummaryFile -Descending | Select-Object -First 1

    if ($preferred) {
        return $preferred
    }

    return $candidates | Sort-Object {
        $vus = 0
        if (-not [string]::IsNullOrWhiteSpace($_.Vus)) {
            [void][int]::TryParse($_.Vus, [ref]$vus)
        }
        $vus
    } -Descending | Select-Object -First 1
}

function Parse-MetricOrNull {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text) -or $Text -eq "N/A") {
        return $null
    }

    return [double]$Text
}

$rowARead = Find-Row -ModeName "command" -WorkloadName "read"
$rowBRead = Find-Row -ModeName "appointment-query" -WorkloadName "read"
$rowCRead = Find-Row -ModeName "full-query" -WorkloadName "read"
$rowAWrite = Find-Row -ModeName "command" -WorkloadName "write-mix"
$rowCWrite = Find-Row -ModeName "full-query" -WorkloadName "write-mix"
$rowCLag = Find-Row -ModeName "full-query" -WorkloadName "projection-lag"

$comparison["B_vs_A_list_p95_delta"] = Format-Delta `
    (Parse-MetricOrNull $rowARead.ListP95) `
    (Parse-MetricOrNull $rowBRead.ListP95)

$comparison["B_vs_A_calendar_p95_delta"] = Format-Delta `
    (Parse-MetricOrNull $rowARead.CalendarP95) `
    (Parse-MetricOrNull $rowBRead.CalendarP95)

$comparison["C_vs_B_dashboard_p95_delta"] = Format-Delta `
    (Parse-MetricOrNull $rowBRead.DashboardP95) `
    (Parse-MetricOrNull $rowCRead.DashboardP95)

$comparison["C_vs_A_http_p95_delta"] = Format-Delta `
    (Parse-MetricOrNull $rowARead.HttpP95) `
    (Parse-MetricOrNull $rowCRead.HttpP95)

$comparison["C_vs_A_http_p99_delta"] = Format-Delta `
    (Parse-MetricOrNull $rowARead.HttpP99) `
    (Parse-MetricOrNull $rowCRead.HttpP99)

$comparison["A_vs_C_create_p95_delta"] = Format-Delta `
    (Parse-MetricOrNull $rowAWrite.CreateP95) `
    (Parse-MetricOrNull $rowCWrite.CreateP95)

$comparison["A_vs_C_reschedule_p95_delta"] = Format-Delta `
    (Parse-MetricOrNull $rowAWrite.RescheduleP95) `
    (Parse-MetricOrNull $rowCWrite.RescheduleP95)

$comparison["A_vs_C_cancel_p95_delta"] = Format-Delta `
    (Parse-MetricOrNull $rowAWrite.CancelP95) `
    (Parse-MetricOrNull $rowCWrite.CancelP95)

$lagTargetsMet = "N/A"
if ($rowCLag) {
    $createOk = (Parse-MetricOrNull $rowCLag.ProjectionCreateP95) -lt 2000
    $rescheduleOk = (Parse-MetricOrNull $rowCLag.ProjectionRescheduleP95) -lt 2000
    $cancelOk = (Parse-MetricOrNull $rowCLag.ProjectionCancelP95) -lt 2000
    $timeoutOk = ($rowCLag.ProjectionTimeoutCount -eq "0" -or $rowCLag.ProjectionTimeoutCount -eq "N/A")

    if ($null -ne $createOk -and $null -ne $rescheduleOk -and $null -ne $cancelOk) {
        $lagTargetsMet = ($createOk -and $rescheduleOk -and $cancelOk -and $timeoutOk)
    }
}

$comparison["projection_lag_targets_met"] = [string]$lagTargetsMet

$result = [ordered]@{
    resultsDirectory = $ResultsDirectory
    rowCount         = $rows.Count
    rows             = $rows
    comparisons      = $comparison
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
        $OutputPath = Join-Path $repoRoot $OutputPath
    }

    $rows | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8
    $result["csvPath"] = $OutputPath
}

$result | ConvertTo-Json -Depth 6
