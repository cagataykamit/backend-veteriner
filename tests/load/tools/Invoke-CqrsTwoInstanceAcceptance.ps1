#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-11D iki instance appointment projection acceptance araci.

.DESCRIPTION
  Varsayilan dry-run. -Apply ile iki API instance uzerinde claim-enabled projection acceptance calistirir.
  Mevcut Invoke-CqrsStagedRollout single-instance guard'ini degistirmez.

.PARAMETER PrimaryBaseUrl
  Birinci API (ornek: https://localhost:7173).

.PARAMETER SecondaryBaseUrl
  Ikinci API (ornek: https://localhost:7174).

.PARAMETER CommandDatabase
  Command DB catalog (varsayilan LoadTest).

.PARAMETER QueryDatabase
  Query DB catalog (varsayilan LoadTest).

.PARAMETER TokenFile
  k6 token dosyasi.

.PARAMETER WorkloadVus
  Toplam k6 VU (primary/secondary arasinda bolunur).

.PARAMETER WorkloadDuration
  k6 sure (ornek: 45s, 2m).

.PARAMETER DrainTimeoutSeconds
  Workload sonrasi projection drain bekleme suresi.

.PARAMETER PrimaryLogPath
  Instance A stdout/log dosyasi (worker id kaniti icin, opsiyonel).

.PARAMETER SecondaryLogPath
  Instance B stdout/log dosyasi (opsiyonel).

.PARAMETER WorkloadRunId
  Deterministic workload run id (rerun slot offset). 0 = otomatik UTC tabanli.

.PARAMETER Reset
  LoadTest DB reset (destructive). -ConfirmReset ile birlikte kullanilmalidir.

.PARAMETER Apply
  Gercek acceptance adimlarini calistirir (health, workload, parity).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PrimaryBaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$SecondaryBaseUrl,

    [string]$CommandDatabase = "VetinityCommandDb_LoadTest",

    [string]$QueryDatabase = "VetinityQueryDb_LoadTest",

    [string]$ServerInstance = "localhost",

    [string]$TokenFile,

    [int]$WorkloadVus = 4,

    [string]$WorkloadDuration = "45s",

    [int]$DrainTimeoutSeconds = 120,

    [string]$PrimaryLogPath,

    [string]$SecondaryLogPath,

    [string]$OutputDirectory,

    [int]$WorkloadRunId = 0,

    [switch]$AllowInsecureLocalhostCertificate,

    [switch]$Reset,

    [switch]$ConfirmReset,

    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsTwoInstanceAcceptanceCommon.ps1")

$normalizedPrimary = $PrimaryBaseUrl.Trim().TrimEnd("/")
$normalizedSecondary = $SecondaryBaseUrl.Trim().TrimEnd("/")
$startedAtUtc = [DateTime]::UtcNow
$resolvedWorkloadRunId = Get-CqrsTwoInstanceWorkloadRunId -WorkloadRunId $WorkloadRunId
$plannedPrimaryIsolation = Get-CqrsTwoInstanceWorkloadIsolationEnv -InstanceLabel "primary" -WorkloadRunId $resolvedWorkloadRunId
$plannedSecondaryIsolation = Get-CqrsTwoInstanceWorkloadIsolationEnv -InstanceLabel "secondary" -WorkloadRunId $resolvedWorkloadRunId

$report = [ordered]@{
    phase              = "CQRS-11D-3"
    dryRun             = (-not $Apply.IsPresent)
    startedAtUtc       = $startedAtUtc.ToString("o")
    primaryBaseUrl     = $normalizedPrimary
    secondaryBaseUrl   = $normalizedSecondary
    commandDatabase    = $CommandDatabase
    queryDatabase      = $QueryDatabase
    expectedFlags      = Get-CqrsTwoInstanceExpectedFlags
    workloadRunId      = $resolvedWorkloadRunId
    workloadIsolationPlan = [ordered]@{
        primary = [ordered]@{
            tokenSlots         = @("01", "02", "03", "04", "05")
            baseDayOffset      = $plannedPrimaryIsolation.baseDayOffset
            slotSequenceOffset = $plannedPrimaryIsolation.slotSequenceOffset
        }
        secondary = [ordered]@{
            tokenSlots         = @("06", "07", "08", "09", "10")
            baseDayOffset      = $plannedSecondaryIsolation.baseDayOffset
            slotSequenceOffset = $plannedSecondaryIsolation.slotSequenceOffset
        }
    }
    steps              = New-Object System.Collections.Generic.List[object]
    warnings           = New-Object System.Collections.Generic.List[string]
    passed             = $false
}

function Add-Step {
    param(
        [string]$Name,
        [bool]$Passed,
        $Detail = $null
    )

    $report.steps.Add([ordered]@{
            name   = $Name
            passed = $Passed
            detail = $Detail
        }) | Out-Null
}

if ($Reset.IsPresent -and -not $ConfirmReset.IsPresent) {
    throw "Destructive reset requires -ConfirmReset."
}

if ($Reset.IsPresent -and $ConfirmReset.IsPresent) {
    if (-not $Apply.IsPresent) {
        $report.warnings.Add("Reset requested in dry-run; no database changes were made.") | Out-Null
    }
    else {
        & (Join-Path $PSScriptRoot "Invoke-CqrsLoadDataReset.ps1") `
            -Method full `
            -ServerInstance $ServerInstance | Out-Null
        Add-Step -Name "loadtest-db-reset" -Passed $true
    }
}

$listenerProbe = Get-CqrsTwoInstanceListenerProbe
Add-Step -Name "two-instance-listeners" -Passed $listenerProbe.bothListening -Detail $listenerProbe

if (-not $Apply.IsPresent) {
    $report.passed = $true
    $report.completedAtUtc = [DateTime]::UtcNow.ToString("o")
    $report.notes = @(
        "Dry-run only. Start two API instances with ClaimingEnabled=true, then re-run with -Apply.",
        "Example: .\tests\load\tools\Invoke-CqrsTwoInstanceAcceptance.ps1 -PrimaryBaseUrl https://localhost:7173 -SecondaryBaseUrl https://localhost:7174 -Apply -AllowInsecureLocalhostCertificate"
    )
    $json = $report | ConvertTo-Json -Depth 8
    if (Test-CqrsStagedOutputContainsSecrets -Text $json) {
        throw "Report output contains secret-like content."
    }
    Write-Output $json
    return
}

foreach ($url in @($normalizedPrimary, $normalizedSecondary)) {
    if (-not (Test-CqrsStagedHostAllowed -BaseUrl $url)) {
        throw "BaseUrl not allowed: $url"
    }
}

if (-not $listenerProbe.bothListening) {
    throw "Both API instances must be listening on ports 7173 and 7174 before -Apply."
}

if (-not $listenerProbe.distinctProcesses) {
    $report.warnings.Add("Primary and secondary listeners may share the same process; expected two distinct API processes.") | Out-Null
}

$primaryHealth = Get-CqrsTwoInstanceHealthSnapshot -BaseUrl $normalizedPrimary -Label "primary"
$secondaryHealth = Get-CqrsTwoInstanceHealthSnapshot -BaseUrl $normalizedSecondary -Label "secondary"

Add-Step -Name "primary-health-ready" -Passed ([string]$primaryHealth.status -eq "Healthy") -Detail $primaryHealth
Add-Step -Name "secondary-health-ready" -Passed ([string]$secondaryHealth.status -eq "Healthy") -Detail $secondaryHealth

if ([string]$primaryHealth.status -ne "Healthy" -or [string]$secondaryHealth.status -ne "Healthy") {
    throw "Both instances must report Healthy on /health/ready."
}

$primaryFlags = Test-CqrsTwoInstanceHealthFlags -Snapshot $primaryHealth
$secondaryFlags = Test-CqrsTwoInstanceHealthFlags -Snapshot $secondaryHealth
Add-Step -Name "primary-flags" -Passed $primaryFlags.passed -Detail $primaryFlags
Add-Step -Name "secondary-flags" -Passed $secondaryFlags.passed -Detail $secondaryFlags

if (-not $primaryFlags.passed -or -not $secondaryFlags.passed) {
    throw "Claim-enabled full-query flags mismatch. Verify environment overrides on both instances."
}

$baselineSnapshot = [ordered]@{
    primary   = $primaryHealth
    secondary = $secondaryHealth
}
Add-Step -Name "queue-baseline-snapshot" -Passed $true -Detail $baselineSnapshot

$processedBaseline = Get-CqrsProcessedProjectionEventsReport `
    -ServerInstance $ServerInstance `
    -CommandDatabase $CommandDatabase `
    -QueryDatabase $QueryDatabase
Add-Step -Name "processed-projection-events-baseline" -Passed $true -Detail $processedBaseline

$workload = Invoke-CqrsTwoInstanceProjectionWorkload `
    -PrimaryBaseUrl $normalizedPrimary `
    -SecondaryBaseUrl $normalizedSecondary `
    -TokenFile $TokenFile `
    -Vus $WorkloadVus `
    -Duration $WorkloadDuration `
    -OutputDirectory $OutputDirectory `
    -WorkloadRunId $resolvedWorkloadRunId

Write-Host ""
Write-Host "Workload isolation summary (runId=$($workload.workloadRunId)):"
foreach ($run in $workload.runs) {
    $slotList = @($run.tokenSlots) -join ", "
    Write-Host ("  {0}: slots [{1}] baseDayOffset={2} slotSequenceOffset={3} tokenFile={4}" -f `
            $run.label, $slotList, $run.baseDayOffset, $run.slotSequenceOffset, $run.tokenFile)
}
Write-Host ""

Add-Step -Name "parallel-projection-lag-workload" -Passed (
    $workload.allJobsCompleted -and $workload.allLifecycleProduced
) -Detail $workload
if (-not $workload.allJobsCompleted) {
    throw "k6 workload failed on one or both instances."
}
if (-not $workload.allLifecycleProduced) {
    throw "k6 lifecycle produced zero appointment operations on one or both instances."
}

$drain = Wait-CqrsProjectionQueueDrain `
    -PrimaryBaseUrl $normalizedPrimary `
    -SecondaryBaseUrl $normalizedSecondary `
    -TimeoutSeconds $DrainTimeoutSeconds

$queueClean = $drain.drained
Add-Step -Name "projection-queue-drain" -Passed $queueClean -Detail $drain
if (-not $queueClean) {
    throw "Projection queue did not drain within $DrainTimeoutSeconds seconds."
}

$parity = Get-CqrsStagedParityReport `
    -ServerInstance $ServerInstance `
    -CommandDatabase $CommandDatabase `
    -QueryDatabase $QueryDatabase

$parityPassed = $parity.queueClean -and
    $parity.parityMatched -and
    $parity.duplicateAppointmentIds -eq 0 -and
    $parity.invalidGuids -eq 0 -and
    $parity.invalidScheduleRange -eq 0

Add-Step -Name "sql-parity" -Passed $parityPassed -Detail $parity
if (-not $parityPassed) {
    throw "SQL parity/integrity checks failed."
}

$processedFinal = Get-CqrsProcessedProjectionEventsReport `
    -ServerInstance $ServerInstance `
    -CommandDatabase $CommandDatabase `
    -QueryDatabase $QueryDatabase

$processedDelta = Get-CqrsProcessedProjectionEventsDeltaReport `
    -BaselineReport $processedBaseline `
    -FinalReport $processedFinal

Add-Step -Name "processed-projection-events" -Passed $processedDelta.passed -Detail $processedDelta
if (-not $processedDelta.passed) {
    throw "ProcessedProjectionEvents delta consistency check failed."
}

$statusReport = Get-CqrsTwoInstanceStatusDistributionReport `
    -ServerInstance $ServerInstance `
    -CommandDatabase $CommandDatabase `
    -QueryDatabase $QueryDatabase

Add-Step -Name "status-distribution" -Passed $statusReport.statusDistributionMatched -Detail $statusReport
if (-not $statusReport.statusDistributionMatched) {
    throw "Command vs Query status distribution mismatch."
}

$logPaths = @()
foreach ($run in $workload.runs) {
    if ($run.logPath) { $logPaths += $run.logPath }
}
if ($PrimaryLogPath) { $logPaths += $PrimaryLogPath }
if ($SecondaryLogPath) { $logPaths += $SecondaryLogPath }

$workerReport = Get-CqrsClaimWorkerParticipationReport -LogPaths $logPaths
$workerParticipation = $workerReport.passed
Add-Step -Name "multi-worker-claim-participation" -Passed $workerParticipation -Detail $workerReport

if (-not $workerParticipation) {
    throw "Claim worker participation check failed. Expected AppointmentProjectionClaimBatchCompleted logs with at least two distinct WorkerId values."
}

$finalPrimary = Get-CqrsTwoInstanceHealthSnapshot -BaseUrl $normalizedPrimary -Label "primary-final"
$finalSecondary = Get-CqrsTwoInstanceHealthSnapshot -BaseUrl $normalizedSecondary -Label "secondary-final"
Add-Step -Name "instances-still-healthy" -Passed (
    [string]$finalPrimary.status -eq "Healthy" -and [string]$finalSecondary.status -eq "Healthy"
) -Detail @{
    primary   = $finalPrimary
    secondary = $finalSecondary
}

$allStepsPassed = Test-CqrsTwoInstanceAllStepsPassed -Steps $report.steps
$report.passed = $allStepsPassed
$report.completedAtUtc = [DateTime]::UtcNow.ToString("o")
$report.summary = [ordered]@{
    instances              = @($normalizedPrimary, $normalizedSecondary)
    commandDatabase        = $CommandDatabase
    queryDatabase          = $QueryDatabase
    workloadVus            = $WorkloadVus
    workloadDuration       = $WorkloadDuration
    workloadRunId          = $resolvedWorkloadRunId
    workloadIsolation      = if ($null -ne $workload) { $workload.workloadIsolation } else { $report.workloadIsolationPlan }
    tokenPartition         = if ($null -ne $workload) { $workload.tokenPartition } else { $null }
    workerIds              = $workerReport.workerIds
    multiWorkerParticipation = $workerParticipation
    claimBatchCompletedCount = $workerReport.claimBatchCompletedCount
    queueClean             = $queueClean
    parityMatched          = $parity.parityMatched
    processedEventsDeltaAligned = $processedDelta.deltasAligned
    processedOutboxDelta   = $processedDelta.processedOutboxDelta
    processedEventsDelta   = $processedDelta.processedEventsDelta
}

$json = $report | ConvertTo-Json -Depth 10
if (Test-CqrsStagedOutputContainsSecrets -Text $json) {
    throw "Report output contains secret-like content."
}

Write-Output $json

if (-not $report.passed) {
    throw "CQRS-11D two-instance acceptance failed."
}
