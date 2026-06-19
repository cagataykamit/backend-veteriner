#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-11E staged rollout / rollback acceptance araci.

.DESCRIPTION
  Varsayilan dry-run. -Apply ile tek bir acceptance adimini dogrular.
  Her adim oncesi ortam degiskenleri + API restart operasyonel sorumlulugundadir.

.PARAMETER BaseUrl
  Hedef API taban URL.

.PARAMETER Step
  command-read | appointment-query | full-query | projection-disabled | projection-reenabled |
  rollback-appointment-query | rollback-command-read

.PARAMETER ShowPlan
  Tum acceptance planini (A->B->C->projection pause/resume->B->A) yazdirir.

.PARAMETER TokenFile
  Klinik token JSON dosyasi.

.PARAMETER CommandDatabase
  Command DB catalog adi.

.PARAMETER QueryDatabase
  Query DB catalog adi.

.PARAMETER ServerInstance
  SQL Server instance.

.PARAMETER Apply
  Secili adim icin dogrulama calistirir.

.PARAMETER PollTimeoutSeconds
  Projection queue drain bekleme suresi.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [ValidateSet(
        "command-read",
        "appointment-query",
        "full-query",
        "projection-disabled",
        "projection-reenabled",
        "rollback-appointment-query",
        "rollback-command-read"
    )]
    [string]$Step = "command-read",

    [string]$TokenFile,

    [string]$CommandDatabase = "VetinityCommandDb_LoadTest",

    [string]$QueryDatabase = "VetinityQueryDb_LoadTest",

    [string]$ServerInstance = "localhost",

    [int]$PollTimeoutSeconds = 60,

    [switch]$ShowPlan,

    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsRolloutAcceptanceCommon.ps1")

$normalizedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
$startedAtUtc = [DateTime]::UtcNow
$report = [ordered]@{
    phase            = "CQRS-11E"
    dryRun           = (-not $Apply.IsPresent)
    startedAtUtc     = $startedAtUtc.ToString("o")
    baseUrl          = $normalizedBaseUrl
    step             = $Step
    hostAllowed      = (Test-CqrsStagedHostAllowed -BaseUrl $normalizedBaseUrl)
    steps            = New-Object System.Collections.Generic.List[object]
    warnings         = New-Object System.Collections.Generic.List[string]
    passed           = $false
}

function Add-Step {
    param(
        [string]$Name,
        [bool]$Passed,
        [object]$Detail = $null
    )

    $report.steps.Add([ordered]@{
            name   = $Name
            passed = $Passed
            detail = $Detail
        }) | Out-Null
}

function Add-Warning {
    param([string]$Message)
    $report.warnings.Add($Message) | Out-Null
}

if ($ShowPlan.IsPresent) {
    Write-Output ([ordered]@{
            acceptanceSequence = Get-CqrsRolloutAcceptanceSequence
            rollbackDocs       = Get-CqrsRolloutRollbackDocumentation
            note               = "Her adimda environment overrides uygula, tek API instance restart et, sonra -Apply ile dogrula."
        })
    return
}

$planEntry = @(Get-CqrsRolloutAcceptanceSequence | Where-Object { $_.step -eq $Step } | Select-Object -First 1)
if ($planEntry.Count -eq 0) {
    throw "Unknown acceptance step '$Step'."
}

$plan = $planEntry[0]
$report.plan = $plan

if (-not $report.hostAllowed) {
    Add-Warning "BaseUrl yalnizca localhost veya CQRS_LOAD_ALLOWED_HOST ile izin verilen host olabilir."
}

if ($CommandDatabase -eq $QueryDatabase) {
    Add-Step -Name "distinct-databases" -Passed $false
    $report.passed = $false
    Write-Output ($report | ConvertTo-Json -Depth 8)
    exit 1
}

Add-Step -Name "distinct-databases" -Passed $true -Detail @{
    commandDatabase = $CommandDatabase
    queryDatabase   = $QueryDatabase
}

if (-not $Apply.IsPresent) {
    Add-Step -Name "dry-run-plan" -Passed $true -Detail @{
        message              = "No service or environment changes were applied."
        requiredOverrides    = $plan.overrides
        recommendedCommands  = @(
            "Set environment overrides from plan.overrides"
            "Restart/deploy exactly one API instance"
            ".\tests\load\tools\Invoke-CqrsRolloutAcceptance.ps1 -BaseUrl $normalizedBaseUrl -Step $Step -Apply"
            ".\tests\load\tools\Invoke-CqrsRolloutAcceptance.ps1 -ShowPlan"
        )
        readSources = @{
            appointmentList     = $plan.readSources.appointmentListSource
            appointmentCalendar = $plan.readSources.appointmentCalendarSource
            dashboard           = $plan.readSources.dashboardAppointmentSource
        }
    }

    $report.passed = $true
    $json = $report | ConvertTo-Json -Depth 8
    if (Test-CqrsStagedOutputContainsSecrets -Text $json) {
        throw "Report output contains secret-like content."
    }
    Write-Output $json
    return
}

$modeSteps = @{
    "command-read"              = "command-read"
    "appointment-query"         = "appointment-query"
    "full-query"                = "full-query"
    "rollback-appointment-query" = "appointment-query"
    "rollback-command-read"     = "command-read"
}

if ($modeSteps.ContainsKey($Step)) {
    $stagedScript = Join-Path $PSScriptRoot "Invoke-CqrsStagedRollout.ps1"
    $stagedJson = & $stagedScript `
        -BaseUrl $normalizedBaseUrl `
        -Mode $modeSteps[$Step] `
        -TokenFile $TokenFile `
        -CommandDatabase $CommandDatabase `
        -QueryDatabase $QueryDatabase `
        -ServerInstance $ServerInstance `
        -PollTimeoutSeconds $PollTimeoutSeconds `
        -Apply | Out-String

    $stagedReport = $stagedJson | ConvertFrom-Json
    foreach ($stagedStep in $stagedReport.steps) {
        Add-Step -Name ("staged:" + $stagedStep.name) -Passed ([bool]$stagedStep.passed) -Detail $stagedStep.detail
    }

    $report.passed = [bool]$stagedReport.passed
    $report.completedAtUtc = [DateTime]::UtcNow.ToString("o")
    $report.durationSeconds = [Math]::Round(([DateTime]::UtcNow - $startedAtUtc).TotalSeconds, 2)

    $json = $report | ConvertTo-Json -Depth 8
    if (Test-CqrsStagedOutputContainsSecrets -Text $json) {
        throw "Report output contains secret-like content."
    }
    Write-Output $json
    if (-not $report.passed) { exit 1 }
    return
}

$instanceProbe = Test-CqrsStagedLocalApiSingleInstance -BaseUrl $normalizedBaseUrl
Add-Step -Name "single-instance-probe" -Passed $instanceProbe.singleInstance -Detail $instanceProbe
if ($instanceProbe.checked -and -not $instanceProbe.singleInstance) {
    throw "Multiple API listeners detected on port 7173."
}

$resolvedTokenFile = Resolve-CqrsLoadTokenFile -TokenFile $TokenFile
$snapshot = Get-CqrsRolloutHealthSnapshot -BaseUrl $normalizedBaseUrl
Add-Step -Name "health-snapshot" -Passed $true -Detail $snapshot

if ($Step -eq "projection-disabled") {
    $projectionOff = -not (Get-CqrsLoadBooleanValue -Value $snapshot.projectionEnabled)
    Add-Step -Name "projection-disabled-flag" -Passed $projectionOff -Detail @{
        expected = $false
        actual   = $snapshot.projectionEnabled
    }
    if (-not $projectionOff) {
        throw "Expected AppointmentProjection__Enabled=false (projectionEnabled=false in health)."
    }

    $lifecycle = Invoke-CqrsRolloutLifecycleSmoke `
        -BaseUrl $normalizedBaseUrl `
        -TokenFile $resolvedTokenFile `
        -NotesPrefix "CQRS11E_PROJ_OFF"
    Add-Step -Name "lifecycle-create-while-projector-off" -Passed ($lifecycle.createStatus -eq 201) -Detail $lifecycle

    $afterSnapshot = Get-CqrsRolloutHealthSnapshot -BaseUrl $normalizedBaseUrl
    $pendingIncreased = ($afterSnapshot.pendingCount -gt $snapshot.pendingCount)
    Add-Step -Name "outbox-pending-accumulates" -Passed $pendingIncreased -Detail @{
        beforePending = $snapshot.pendingCount
        afterPending  = $afterSnapshot.pendingCount
    }
    if (-not $pendingIncreased) {
        throw "Expected pending outbox count to increase while projection is disabled."
    }

    $readFlagsOn = Get-CqrsRolloutReadFlagsEnabled `
        -AppointmentsReadEnabled $afterSnapshot.appointmentsReadEnabled `
        -DashboardReadEnabled $afterSnapshot.dashboardReadEnabled

    if ($readFlagsOn) {
        $healthExpectation = Test-CqrsRolloutProjectionHealthExpectation `
            -Snapshot $afterSnapshot `
            -ExpectedLevel "Unhealthy"
        Add-Step -Name "health-unhealthy-with-read-flags-on" -Passed $healthExpectation.passed -Detail $healthExpectation
        if (-not $healthExpectation.passed) {
            throw "Expected appointment-projection health Unhealthy when read flags on and pending exists."
        }

        $overallUnhealthy = ([string]$afterSnapshot.overallStatus -ne "Healthy")
        Add-Step -Name "ready-not-healthy" -Passed $overallUnhealthy -Detail @{
            overallStatus = $afterSnapshot.overallStatus
        }
    }
    else {
        $healthExpectation = Test-CqrsRolloutProjectionHealthExpectation `
            -Snapshot $afterSnapshot `
            -ExpectedLevel "Healthy"
        Add-Step -Name "health-healthy-with-read-flags-off" -Passed $healthExpectation.passed -Detail $healthExpectation

        $accessToken = Get-CqrsStagedAccessToken -TokenFile $resolvedTokenFile
        $tokens = Get-Content -LiteralPath $resolvedTokenFile -Raw | ConvertFrom-Json
        $clinicId = [string]($tokens | Where-Object { $_.slot -eq "01" } | Select-Object -First 1).clinicId
        $listResponse = Invoke-CqrsStagedApiRequest `
            -BaseUrl $normalizedBaseUrl `
            -Method "GET" `
            -Path "/api/v1/appointments?Page=1&PageSize=5&clinicId=$clinicId" `
            -AccessToken $accessToken
        Add-Step -Name "command-fallback-list-200" -Passed ($listResponse.statusCode -eq 200) -Detail @{
            statusCode = $listResponse.statusCode
            readSource = "Command DB"
        }
    }
}
elseif ($Step -eq "projection-reenabled") {
    $projectionOn = Get-CqrsLoadBooleanValue -Value $snapshot.projectionEnabled
    Add-Step -Name "projection-enabled-flag" -Passed ($projectionOn -eq $true) -Detail @{
        expected = $true
        actual   = $snapshot.projectionEnabled
    }
    if ($projectionOn -ne $true) {
        throw "Expected AppointmentProjection__Enabled=true before drain validation."
    }

    $drain = Wait-CqrsRolloutProjectionQueueDrain `
        -BaseUrl $normalizedBaseUrl `
        -TimeoutSeconds $PollTimeoutSeconds
    Add-Step -Name "projection-queue-drain" -Passed $drain.drained -Detail $drain
    if (-not $drain.drained) {
        throw "Projection queue did not drain after re-enable."
    }

    $parity = Get-CqrsStagedParityReport `
        -ServerInstance $ServerInstance `
        -CommandDatabase $CommandDatabase `
        -QueryDatabase $QueryDatabase
    $parityPassed = $parity.parityMatched -and
        $parity.duplicateAppointmentIds -eq 0 -and
        $parity.invalidGuids -eq 0 -and
        $parity.invalidScheduleRange -eq 0 -and
        $parity.queueClean
    Add-Step -Name "sql-parity-after-drain" -Passed $parityPassed -Detail $parity
    if (-not $parityPassed) {
        throw "SQL parity/integrity checks failed after projection re-enable."
    }

    $finalSnapshot = Get-CqrsRolloutHealthSnapshot -BaseUrl $normalizedBaseUrl
    $healthExpectation = Test-CqrsRolloutProjectionHealthExpectation `
        -Snapshot $finalSnapshot `
        -ExpectedLevel "Healthy"
    Add-Step -Name "health-healthy-after-drain" -Passed $healthExpectation.passed -Detail $healthExpectation
    if (-not $healthExpectation.passed) {
        throw "Expected healthy projection health after queue drain."
    }

    $readyHealthy = ([string]$finalSnapshot.overallStatus -eq "Healthy")
    Add-Step -Name "ready-healthy" -Passed $readyHealthy -Detail @{
        overallStatus = $finalSnapshot.overallStatus
    }
    if (-not $readyHealthy) {
        throw "/health/ready is not Healthy after projection re-enable."
    }
}

$failedSteps = @($report.steps | Where-Object { -not $_.passed })
$report.passed = ($failedSteps.Count -eq 0)
$report.completedAtUtc = [DateTime]::UtcNow.ToString("o")
$report.durationSeconds = [Math]::Round(([DateTime]::UtcNow - $startedAtUtc).TotalSeconds, 2)

$json = $report | ConvertTo-Json -Depth 8
if (Test-CqrsStagedOutputContainsSecrets -Text $json) {
    throw "Report output contains secret-like content."
}

Write-Output $json
if (-not $report.passed) {
    exit 1
}
