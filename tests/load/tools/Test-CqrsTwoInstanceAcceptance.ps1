#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-11D iki instance acceptance script otomasyon testleri (parse, dry-run, dokuman).
#>
[CmdletBinding()]
param(
    [string]$PrimaryBaseUrl = "https://localhost:7173",
    [string]$SecondaryBaseUrl = "https://localhost:7174"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsTwoInstanceAcceptanceCommon.ps1")

$repoRoot = Get-CqrsLoadRepositoryRoot
$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail = ""
    )

    $results.Add([ordered]@{
            name   = $Name
            passed = $Passed
            detail = $Detail
        }) | Out-Null
}

$invokePath = Join-Path $PSScriptRoot "Invoke-CqrsTwoInstanceAcceptance.ps1"
$commonPath = Join-Path $PSScriptRoot "CqrsTwoInstanceAcceptanceCommon.ps1"
$docPath = Join-Path $repoRoot "docs\cqrs\cqrs-11d-two-instance-acceptance.md"

foreach ($path in @($invokePath, $commonPath)) {
    $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)
    $name = Split-Path $path -Leaf
    Add-Result -Name "ps-parse:$name" -Passed ($null -eq $errors -or $errors.Count -eq 0) `
        -Detail (($errors | ForEach-Object { $_.ToString() }) -join "; ")
}

Add-Result -Name "doc-exists" -Passed (Test-Path -LiteralPath $docPath)

$expected = Get-CqrsTwoInstanceExpectedFlags
Add-Result -Name "expected-flags-claim-enabled" -Passed (
    $expected.claimingEnabled -eq $true -and
    $expected.projectionEnabled -eq $true -and
    $expected.appointmentsReadEnabled -eq $true -and
    $expected.dashboardReadEnabled -eq $true
)

try {
    $dryRunJson = & $invokePath `
        -PrimaryBaseUrl $PrimaryBaseUrl `
        -SecondaryBaseUrl $SecondaryBaseUrl | Out-String
    $dryRun = $dryRunJson | ConvertFrom-Json
    Add-Result -Name "dry-run-default" -Passed ($dryRun.dryRun -eq $true)
    Add-Result -Name "dry-run-no-secret-leak" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $dryRunJson))
}
catch {
    Add-Result -Name "dry-run-default" -Passed $false -Detail $_.Exception.Message
}

try {
    & $invokePath `
        -PrimaryBaseUrl $PrimaryBaseUrl `
        -SecondaryBaseUrl $SecondaryBaseUrl `
        -Reset | Out-Null
    Add-Result -Name "reset-without-confirm-fails" -Passed $false -Detail "Expected exception was not thrown."
}
catch {
    Add-Result -Name "reset-without-confirm-fails" -Passed $true
}

$processed = Get-CqrsProcessedProjectionEventsReport `
    -ServerInstance "localhost" `
    -CommandDatabase "VetinityCommandDb_LoadTest" `
    -QueryDatabase "VetinityQueryDb_LoadTest"
Add-Result -Name "processed-events-report-shape" -Passed (
    $null -ne $processed.processedOutboxCount -and
    $null -ne $processed.processedEventsCount -and
    $null -ne $processed.duplicateProcessedKeys
)

if (Test-Path -LiteralPath $docPath) {
    $docText = Get-Content -LiteralPath $docPath -Raw
    Add-Result -Name "doc-mentions-two-instances" -Passed ($docText -match "7173" -and $docText -match "7174")
    Add-Result -Name "doc-no-secret-patterns" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $docText))
}

$commonSource = Get-Content -LiteralPath $commonPath -Raw
Add-Result -Name "workload-passes-isolation-env-vars" -Passed (
    $commonSource -match 'VETINITY_BASE_DAY_OFFSET=' -and
    $commonSource -match 'VETINITY_SLOT_SEQUENCE_OFFSET=' -and
    $commonSource -match 'VETINITY_WORKLOAD_RUN_ID=' -and
    $commonSource -match 'New-CqrsPartitionedTokenFiles' -and
    $commonSource -match 'Get-CqrsProcessedProjectionEventsDeltaReport' -and
    $commonSource -match 'Get-CqrsClaimWorkerParticipationReport' -and
    $commonSource -match 'Get-CqrsK6ProjectionLagLifecycleSummary' -and
    $commonSource -match 'Test-CqrsTwoInstanceAllStepsPassed'
)

$lagSource = Get-Content -LiteralPath (Join-Path $repoRoot "tests\load\appointment-projection-lag.js") -Raw
Add-Result -Name "projection-lag-reads-isolation-env" -Passed (
    $lagSource -match 'VETINITY_BASE_DAY_OFFSET' -and
    $lagSource -match 'VETINITY_SLOT_SEQUENCE_OFFSET'
)
Add-Result -Name "projection-lag-lifecycle-counters" -Passed (
    $lagSource -match 'appointment_projection_lifecycle_attempts' -and
    $lagSource -match 'appointment_projection_lifecycle_completed' -and
    $lagSource -match 'appointment_projection_create_completed' -and
    $lagSource -match 'appointment_projection_reschedule_completed' -and
    $lagSource -match 'appointment_projection_cancel_completed' -and
    $lagSource -match 'appointment_projection_schedule_null_skipped' -and
    $lagSource -match 'appointment_projection_lifecycle_attempts.*\["count>0"\]' -and
    $lagSource -match 'appointment_projection_lifecycle_completed.*\["count>0"\]' -and
    $lagSource -match 'appointment_projection_create_completed.*\["count>0"\]' -and
    $lagSource -match 'appointment_projection_reschedule_completed.*\["count>0"\]' -and
    $lagSource -match 'appointment_projection_cancel_completed.*\["count>0"\]'
)

Add-Result -Name "projection-lag-trend-no-count-threshold" -Passed (
    -not ($lagSource -match 'appointment_projection_create_lag_ms:\s*\["count>0"') -and
    -not ($lagSource -match 'appointment_projection_reschedule_lag_ms:\s*\["count>0"') -and
    -not ($lagSource -match 'appointment_projection_cancel_lag_ms:\s*\["count>0"')
)

$primaryIsolation = Get-CqrsTwoInstanceWorkloadIsolationEnv -InstanceLabel "primary" -WorkloadRunId 42
$secondaryIsolation = Get-CqrsTwoInstanceWorkloadIsolationEnv -InstanceLabel "secondary" -WorkloadRunId 42
Add-Result -Name "workload-offset-primary-secondary-differ" -Passed (
    $primaryIsolation.baseDayOffset -ne $secondaryIsolation.baseDayOffset -and
    $primaryIsolation.slotSequenceOffset -eq $secondaryIsolation.slotSequenceOffset -and
    $primaryIsolation.slotSequenceOffset -eq 126 -and
    $primaryIsolation.baseDayOffset -eq 162 -and
    $secondaryIsolation.baseDayOffset -eq 176
)

$tokenTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("vetinity-two-instance-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tokenTempDir -Force | Out-Null
try {
    $mockTokens = @(
        foreach ($slot in 1..10) {
            $slotText = "{0:D2}" -f $slot
            [ordered]@{
                slot        = $slotText
                accessToken = "mock.$slotText.token"
            }
        }
    )
    $mockSourcePath = Join-Path $tokenTempDir "clinic-tokens-source.json"
    $mockJson = $mockTokens | ConvertTo-Json -Depth 3
    [System.IO.File]::WriteAllText($mockSourcePath, $mockJson, [System.Text.UTF8Encoding]::new($false))

    $partition = New-CqrsPartitionedTokenFiles `
        -SourceTokenFile $mockSourcePath `
        -OutputDirectory $tokenTempDir `
        -FilePrefix "partition-test"

    $primarySlots = @($partition.primary.slots)
    $secondarySlots = @($partition.secondary.slots)
    $overlap = @($primarySlots | Where-Object { $secondarySlots -contains $_ })

    Add-Result -Name "token-partition-slot-counts" -Passed (
        $partition.primary.tokenCount -eq 5 -and
        $partition.secondary.tokenCount -eq 5
    ) -Detail "primary=$($primarySlots -join ',') secondary=$($secondarySlots -join ',')"

    Add-Result -Name "token-partition-no-overlap" -Passed ($overlap.Count -eq 0)

    Add-Result -Name "token-partition-primary-slots" -Passed (
        ($primarySlots -join ",") -eq "01,02,03,04,05"
    )

    Add-Result -Name "token-partition-secondary-slots" -Passed (
        ($secondarySlots -join ",") -eq "06,07,08,09,10"
    )

    $primaryJson = ConvertFrom-CqrsTokenFileEntries -TokenFilePath $partition.primary.tokenFile
    $secondaryJson = ConvertFrom-CqrsTokenFileEntries -TokenFilePath $partition.secondary.tokenFile
    Add-Result -Name "token-partition-json-schema" -Passed (
        ($primaryJson | ForEach-Object { $_.slot -and $_.accessToken }) -notcontains $false -and
        ($secondaryJson | ForEach-Object { $_.slot -and $_.accessToken }) -notcontains $false
    )
}
finally {
    if (Test-Path -LiteralPath $tokenTempDir) {
        Remove-Item -LiteralPath $tokenTempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    $dryRunPlanJson = & $invokePath `
        -PrimaryBaseUrl $PrimaryBaseUrl `
        -SecondaryBaseUrl $SecondaryBaseUrl | Out-String
    $dryRunPlan = $dryRunPlanJson | ConvertFrom-Json
    Add-Result -Name "dry-run-workload-isolation-plan" -Passed (
        $null -ne $dryRunPlan.workloadIsolationPlan.primary.tokenSlots -and
        $null -ne $dryRunPlan.workloadIsolationPlan.secondary.tokenSlots -and
        ($dryRunPlan.workloadIsolationPlan.primary.tokenSlots -join ",") -eq "01,02,03,04,05" -and
        ($dryRunPlan.workloadIsolationPlan.secondary.tokenSlots -join ",") -eq "06,07,08,09,10"
    )
}
catch {
    Add-Result -Name "dry-run-workload-isolation-plan" -Passed $false -Detail $_.Exception.Message
}

Add-Result -Name "lifecycle-zero-summary-fails" -Passed (-not (Test-CqrsK6ProjectionLagLifecycleSummary -Summary ([ordered]@{
            lifecycleAttemptsCount   = 0
            lifecycleCompletedCount  = 0
            createCompletedCount     = 0
            rescheduleCompletedCount = 0
            cancelCompletedCount     = 0
            scheduleNullSkippedCount = 0
        })))

$positiveLifecycleSummary = [ordered]@{
    lifecycleAttemptsCount   = 3
    lifecycleCompletedCount  = 2
    createCompletedCount     = 2
    rescheduleCompletedCount = 2
    cancelCompletedCount     = 2
    scheduleNullSkippedCount = 0
}
Add-Result -Name "lifecycle-positive-summary-passes" -Passed (
    Test-CqrsK6ProjectionLagLifecycleSummary -Summary $positiveLifecycleSummary
)

Add-Result -Name "lifecycle-schedule-null-skipped-fails" -Passed (-not (
    Test-CqrsK6ProjectionLagLifecycleSummary -Summary ([ordered]@{
        lifecycleAttemptsCount   = 3
        lifecycleCompletedCount  = 2
        createCompletedCount     = 2
        rescheduleCompletedCount = 2
        cancelCompletedCount     = 2
        scheduleNullSkippedCount = 1
    })
))

$k6TopLevelCountSummary = [ordered]@{
    metrics = [ordered]@{
        appointment_projection_lifecycle_attempts = [ordered]@{
            count = 150
            rate  = 2.5
        }
        appointment_projection_lifecycle_completed = [ordered]@{
            count = 150
        }
        appointment_projection_create_completed = [ordered]@{
            count = 150
        }
        appointment_projection_reschedule_completed = [ordered]@{
            count = 150
        }
        appointment_projection_cancel_completed = [ordered]@{
            count = 150
        }
        appointment_projection_schedule_null_skipped = [ordered]@{
            count = 0
        }
    }
}
Add-Result -Name "k6-summary-count-top-level" -Passed (
    (Get-CqrsK6SummaryMetricCount -Summary $k6TopLevelCountSummary -MetricName "appointment_projection_lifecycle_attempts") -eq 150 -and
    (Get-CqrsK6SummaryMetricCount -Summary $k6TopLevelCountSummary -MetricName "appointment_projection_schedule_null_skipped") -eq 0
)

$k6LegacyValuesCountSummary = [ordered]@{
    metrics = [ordered]@{
        appointment_projection_lifecycle_attempts = [ordered]@{
            values = [ordered]@{
                count = 42
            }
        }
        appointment_projection_lifecycle_completed = [ordered]@{
            values = [ordered]@{
                count = 40
            }
        }
    }
}
Add-Result -Name "k6-summary-count-legacy-values" -Passed (
    (Get-CqrsK6SummaryMetricCount -Summary $k6LegacyValuesCountSummary -MetricName "appointment_projection_lifecycle_attempts") -eq 42 -and
    (Get-CqrsK6SummaryMetricCount -Summary $k6LegacyValuesCountSummary -MetricName "appointment_projection_lifecycle_completed") -eq 40
)

$k6MissingMetricSummary = [ordered]@{
    metrics = [ordered]@{}
}
Add-Result -Name "k6-summary-metric-missing-returns-zero" -Passed (
    (Get-CqrsK6SummaryMetricCount -Summary $k6MissingMetricSummary -MetricName "appointment_projection_lifecycle_attempts") -eq 0 -and
    -not (Test-CqrsK6ProjectionLagLifecycleSummary -Summary ([ordered]@{
            lifecycleAttemptsCount   = (Get-CqrsK6SummaryMetricCount -Summary $k6MissingMetricSummary -MetricName "appointment_projection_lifecycle_attempts")
            lifecycleCompletedCount  = (Get-CqrsK6SummaryMetricCount -Summary $k6MissingMetricSummary -MetricName "appointment_projection_lifecycle_completed")
            createCompletedCount     = 0
            rescheduleCompletedCount = 0
            cancelCompletedCount     = 0
            scheduleNullSkippedCount = 0
        }))
)

$k6TopLevelLifecycleParsed = Test-CqrsK6ProjectionLagLifecycleSummary -Summary ([ordered]@{
    lifecycleAttemptsCount   = (Get-CqrsK6SummaryMetricCount -Summary $k6TopLevelCountSummary -MetricName "appointment_projection_lifecycle_attempts")
    lifecycleCompletedCount  = (Get-CqrsK6SummaryMetricCount -Summary $k6TopLevelCountSummary -MetricName "appointment_projection_lifecycle_completed")
    createCompletedCount     = (Get-CqrsK6SummaryMetricCount -Summary $k6TopLevelCountSummary -MetricName "appointment_projection_create_completed")
    rescheduleCompletedCount = (Get-CqrsK6SummaryMetricCount -Summary $k6TopLevelCountSummary -MetricName "appointment_projection_reschedule_completed")
    cancelCompletedCount     = (Get-CqrsK6SummaryMetricCount -Summary $k6TopLevelCountSummary -MetricName "appointment_projection_cancel_completed")
    scheduleNullSkippedCount = (Get-CqrsK6SummaryMetricCount -Summary $k6TopLevelCountSummary -MetricName "appointment_projection_schedule_null_skipped")
})
Add-Result -Name "k6-top-level-summary-lifecycle-passes" -Passed $k6TopLevelLifecycleParsed

$workerLogTempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("vetinity-worker-participation-test-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $workerLogTempDir -Force | Out-Null
try {
    $singleMatchLogPath = Join-Path $workerLogTempDir "single-match.log"
    $singleMatchLog = @(
        "AppointmentProjectionClaimBatchStarted WorkerId=worker-a"
        "AppointmentProjectionClaimBatchCompleted WorkerId=worker-a"
    ) -join "`n"
    [System.IO.File]::WriteAllText($singleMatchLogPath, $singleMatchLog, [System.Text.UTF8Encoding]::new($false))

    $singleWorkerReport = $null
    $singleWorkerException = $null
    try {
        $singleWorkerReport = Get-CqrsClaimWorkerParticipationReport -LogPaths @($singleMatchLogPath)
    }
    catch {
        $singleWorkerException = $_.Exception.Message
    }

    Add-Result -Name "worker-participation-single-match-no-strictmode-crash" -Passed (
        $null -eq $singleWorkerException -and
        $singleWorkerReport.claimBatchStartedCount -eq 1 -and
        $singleWorkerReport.claimBatchCompletedCount -eq 1 -and
        $singleWorkerReport.workerIdCount -eq 1
    ) -Detail $singleWorkerException

    Add-Result -Name "worker-participation-single-worker-controlled-fail" -Passed (
        $null -ne $singleWorkerReport -and
        -not $singleWorkerReport.passed -and
        $singleWorkerReport.hasCompletedLogs -and
        -not $singleWorkerReport.multiWorkerParticipation
    )

    $twoWorkerLogPath = Join-Path $workerLogTempDir "two-workers.log"
    $twoWorkerLog = @(
        "AppointmentProjectionClaimBatchStarted WorkerId=worker-a"
        "AppointmentProjectionClaimBatchStarted WorkerId=worker-b"
        "AppointmentProjectionClaimBatchCompleted WorkerId=worker-a"
        "AppointmentProjectionClaimBatchCompleted WorkerId=worker-b"
    ) -join "`n"
    [System.IO.File]::WriteAllText($twoWorkerLogPath, $twoWorkerLog, [System.Text.UTF8Encoding]::new($false))

    $twoWorkerReport = Get-CqrsClaimWorkerParticipationReport -LogPaths @($twoWorkerLogPath)
    Add-Result -Name "worker-participation-two-workers-pass" -Passed (
        $twoWorkerReport.passed -and
        $twoWorkerReport.workerIdCount -eq 2 -and
        $twoWorkerReport.claimBatchStartedCount -eq 2 -and
        $twoWorkerReport.claimBatchCompletedCount -eq 2 -and
        $twoWorkerReport.multiWorkerParticipation
    )

    $emptyLogPath = Join-Path $workerLogTempDir "empty.log"
    [System.IO.File]::WriteAllText($emptyLogPath, "", [System.Text.UTF8Encoding]::new($false))
    $emptyReport = Get-CqrsClaimWorkerParticipationReport -LogPaths @($emptyLogPath)
    Add-Result -Name "worker-participation-empty-log-zero-matches" -Passed (
        -not $emptyReport.passed -and
        $emptyReport.claimBatchStartedCount -eq 0 -and
        $emptyReport.claimBatchCompletedCount -eq 0 -and
        $emptyReport.workerIdCount -eq 0
    )
}
finally {
    if (Test-Path -LiteralPath $workerLogTempDir) {
        Remove-Item -LiteralPath $workerLogTempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$processedBaselineMock = [ordered]@{
    processedOutboxCount   = 50000
    processedEventsCount   = 0
    duplicateProcessedKeys = 0
    noDuplicateKeys        = $true
}
$processedFinalMock = [ordered]@{
    processedOutboxCount   = 50012
    processedEventsCount   = 12
    duplicateProcessedKeys = 0
    noDuplicateKeys        = $true
}
$processedDeltaMock = Get-CqrsProcessedProjectionEventsDeltaReport `
    -BaselineReport $processedBaselineMock `
    -FinalReport $processedFinalMock
Add-Result -Name "processed-events-delta-aligned" -Passed (
    $processedDeltaMock.deltasAligned -and
    $processedDeltaMock.deltasPositive -and
    $processedDeltaMock.processedOutboxDelta -eq 12 -and
    $processedDeltaMock.processedEventsDelta -eq 12 -and
    $processedDeltaMock.passed
)

$processedDeltaMismatchMock = Get-CqrsProcessedProjectionEventsDeltaReport `
    -BaselineReport $processedBaselineMock `
    -FinalReport ([ordered]@{
        processedOutboxCount   = 50012
        processedEventsCount   = 10
        duplicateProcessedKeys = 0
        noDuplicateKeys        = $true
    })
Add-Result -Name "processed-events-delta-mismatch-fails" -Passed (-not $processedDeltaMismatchMock.passed)

$allPassedSteps = @(
    [ordered]@{ name = "step-a"; passed = $true },
    [ordered]@{ name = "step-b"; passed = $true }
)
Add-Result -Name "report-all-steps-passed-true" -Passed (
    Test-CqrsTwoInstanceAllStepsPassed -Steps $allPassedSteps
)

$oneFailedStep = @(
    [ordered]@{ name = "step-a"; passed = $true },
    [ordered]@{ name = "step-b"; passed = $false }
)
Add-Result -Name "report-one-failed-step-false" -Passed (-not (
    Test-CqrsTwoInstanceAllStepsPassed -Steps $oneFailedStep
))

$singlePassedStep = [ordered]@{ name = "only-step"; passed = $true }
Add-Result -Name "report-single-step-scalar-no-crash" -Passed (
    Test-CqrsTwoInstanceAllStepsPassed -Steps $singlePassedStep
)

Add-Result -Name "report-null-steps-no-crash" -Passed (
    Test-CqrsTwoInstanceAllStepsPassed -Steps $null
)

$invokeSource = Get-Content -LiteralPath $invokePath -Raw
Add-Result -Name "invoke-no-linq-any" -Passed (
    -not ($invokeSource -match '\.Any\s*\(')
)

$failed = @($results | Where-Object { -not $_.passed })
$summary = [ordered]@{
    total  = $results.Count
    passed = $results.Count - $failed.Count
    failed = $failed.Count
    results = $results
}

$json = $summary | ConvertTo-Json -Depth 6
Write-Output $json

if ($failed.Count -gt 0) {
    throw "Test-CqrsTwoInstanceAcceptance failed ($($failed.Count)/$($results.Count))."
}
