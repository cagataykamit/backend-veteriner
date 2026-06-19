#Requires -Version 5.1
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "CqrsStagedRolloutCommon.ps1")

function Get-CqrsTwoInstanceExpectedFlags {
    return [ordered]@{
        projectionEnabled        = $true
        claimingEnabled          = $true
        appointmentsReadEnabled  = $true
        dashboardReadEnabled     = $true
    }
}

function Test-CqrsTwoInstanceAllStepsPassed {
    param(
        $Steps
    )

    if ($null -eq $Steps) {
        return $true
    }

    $failedSteps = @($Steps | Where-Object { -not $_.passed })
    return ($failedSteps.Count -eq 0)
}

function Get-CqrsTwoInstanceHealthSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [string]$Label = "instance"
    )

    $normalizedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
    $tlsState = Enable-CqrsLoadLocalhostTlsBypass
    try {
        $health = Invoke-CqrsLoadHealthReady `
            -BaseUrl $normalizedBaseUrl `
            -SkipCertificateCheck:($tlsState.UseSkipCertificateCheck -or $true)

        $projectionEntry = $health.results.'appointment-projection'
        $projectionData = $projectionEntry.data

        return [ordered]@{
            label               = $Label
            baseUrl             = $normalizedBaseUrl
            status              = [string]$health.status
            projectionEnabled   = $projectionData.projectionEnabled
            claimingEnabled     = $projectionData.claimingEnabled
            appointmentsReadEnabled = $projectionData.appointmentsReadEnabled
            dashboardReadEnabled    = $projectionData.dashboardReadEnabled
            pendingCount        = [int]$projectionData.pendingCount
            retryWaitingCount   = [int]$projectionData.retryWaitingCount
            deadLetterCount     = [int]$projectionData.deadLetterCount
            querySqlStatus      = [string]$health.results.'query-sql'.status
        }
    }
    finally {
        Disable-CqrsLoadLocalhostTlsBypass -TlsState $tlsState
    }
}

function Test-CqrsTwoInstanceHealthFlags {
    param(
        [Parameter(Mandatory = $true)]
        $Snapshot
    )

    $expected = Get-CqrsTwoInstanceExpectedFlags
    $checks = @(
        @{ name = "projection-enabled"; actual = $Snapshot.projectionEnabled; expected = $expected.projectionEnabled },
        @{ name = "claiming-enabled"; actual = $Snapshot.claimingEnabled; expected = $expected.claimingEnabled },
        @{ name = "appointments-read-enabled"; actual = $Snapshot.appointmentsReadEnabled; expected = $expected.appointmentsReadEnabled },
        @{ name = "dashboard-read-enabled"; actual = $Snapshot.dashboardReadEnabled; expected = $expected.dashboardReadEnabled }
    )

    $results = New-Object System.Collections.Generic.List[object]
    $allPassed = $true
    foreach ($check in $checks) {
        $ok = Test-CqrsLoadBooleanMatch -Actual $check.actual -Expected $check.expected
        if (-not $ok) { $allPassed = $false }
        $results.Add([ordered]@{
                name     = $check.name
                passed   = $ok
                expected = $check.expected
                actual   = $check.actual
            }) | Out-Null
    }

    return [ordered]@{
        passed = $allPassed
        checks = $results
    }
}

function Get-CqrsTwoInstanceListenerProbe {
    param(
        [int]$PrimaryHttpsPort = 7173,
        [int]$SecondaryHttpsPort = 7174
    )

    $primaryListeners = @(Get-NetTCPConnection -LocalPort $PrimaryHttpsPort -State Listen -ErrorAction SilentlyContinue)
    $secondaryListeners = @(Get-NetTCPConnection -LocalPort $SecondaryHttpsPort -State Listen -ErrorAction SilentlyContinue)

    $primaryPids = @($primaryListeners | Select-Object -ExpandProperty OwningProcess -Unique | Where-Object { $_ -gt 0 })
    $secondaryPids = @($secondaryListeners | Select-Object -ExpandProperty OwningProcess -Unique | Where-Object { $_ -gt 0 })

    return [ordered]@{
        primaryPort          = $PrimaryHttpsPort
        secondaryPort        = $SecondaryHttpsPort
        primaryListenerCount = $primaryListeners.Count
        secondaryListenerCount = $secondaryListeners.Count
        primaryProcessIds    = $primaryPids
        secondaryProcessIds  = $secondaryPids
        bothListening        = ($primaryPids.Count -ge 1 -and $secondaryPids.Count -ge 1)
        distinctProcesses    = (@($primaryPids + $secondaryPids | Select-Object -Unique).Count -ge 2)
    }
}

function Wait-CqrsProjectionQueueDrain {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrimaryBaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$SecondaryBaseUrl,
        [int]$TimeoutSeconds = 120,
        [int]$PollIntervalSeconds = 2
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $attempts = 0

    while ([DateTime]::UtcNow -lt $deadline) {
        $attempts++
        $primary = Get-CqrsTwoInstanceHealthSnapshot -BaseUrl $PrimaryBaseUrl -Label "primary"
        $secondary = Get-CqrsTwoInstanceHealthSnapshot -BaseUrl $SecondaryBaseUrl -Label "secondary"

        $primaryClean = ($primary.pendingCount -eq 0 -and $primary.retryWaitingCount -eq 0 -and $primary.deadLetterCount -eq 0)
        $secondaryHealthy = ([string]$primary.status -eq "Healthy" -and [string]$secondary.status -eq "Healthy")

        if ($primaryClean -and $secondaryHealthy) {
            return [ordered]@{
                drained       = $true
                attempts      = $attempts
                primary       = $primary
                secondary     = $secondary
                elapsedSeconds = $TimeoutSeconds - ($deadline - [DateTime]::UtcNow).TotalSeconds
            }
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    $finalPrimary = Get-CqrsTwoInstanceHealthSnapshot -BaseUrl $PrimaryBaseUrl -Label "primary"
    $finalSecondary = Get-CqrsTwoInstanceHealthSnapshot -BaseUrl $SecondaryBaseUrl -Label "secondary"

    return [ordered]@{
        drained   = $false
        attempts  = $attempts
        primary   = $finalPrimary
        secondary = $finalSecondary
    }
}

function Get-CqrsProcessedProjectionEventsReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerInstance,
        [Parameter(Mandatory = $true)]
        [string]$CommandDatabase,
        [Parameter(Mandatory = $true)]
        [string]$QueryDatabase,
        [string]$ConsumerName = "appointment-read-model-v1"
    )

    $processedOutbox = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $CommandDatabase `
        -Query @"
SET NOCOUNT ON;
SELECT COUNT(*)
FROM OutboxMessages
WHERE Type IN (
    'appointment.created.v1',
    'appointment.updated.v1',
    'appointment.rescheduled.v1',
    'appointment.cancelled.v1',
    'appointment.completed.v1'
)
AND ProcessedAtUtc IS NOT NULL;
"@)

    $processedEvents = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM ProcessedProjectionEvents WHERE ConsumerName = N'$ConsumerName';")

    $duplicateProcessedKeys = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $QueryDatabase `
        -Query @"
SET NOCOUNT ON;
SELECT COUNT(*)
FROM (
    SELECT EventId, ConsumerName
    FROM ProcessedProjectionEvents
    WHERE ConsumerName = N'$ConsumerName'
    GROUP BY EventId, ConsumerName
    HAVING COUNT(*) > 1
) d;
"@)

    return [ordered]@{
        consumerName           = $ConsumerName
        processedOutboxCount   = $processedOutbox
        processedEventsCount   = $processedEvents
        duplicateProcessedKeys = $duplicateProcessedKeys
        countsAligned          = ($processedOutbox -eq $processedEvents)
        noDuplicateKeys        = ($duplicateProcessedKeys -eq 0)
    }
}

function Get-CqrsProcessedProjectionEventsDeltaReport {
    param(
        [Parameter(Mandatory = $true)]
        $BaselineReport,
        [Parameter(Mandatory = $true)]
        $FinalReport
    )

    $processedOutboxDelta = [int]$FinalReport.processedOutboxCount - [int]$BaselineReport.processedOutboxCount
    $processedEventsDelta = [int]$FinalReport.processedEventsCount - [int]$BaselineReport.processedEventsCount

    return [ordered]@{
        baseline               = $BaselineReport
        final                  = $FinalReport
        processedOutboxDelta   = $processedOutboxDelta
        processedEventsDelta   = $processedEventsDelta
        deltasAligned          = ($processedOutboxDelta -eq $processedEventsDelta)
        deltasPositive         = ($processedOutboxDelta -gt 0 -and $processedEventsDelta -gt 0)
        noDuplicateKeys        = [bool]$FinalReport.noDuplicateKeys
        passed                 = (
            $FinalReport.noDuplicateKeys -and
            $processedOutboxDelta -eq $processedEventsDelta -and
            $processedOutboxDelta -gt 0
        )
    }
}

function Get-CqrsTwoInstanceStatusDistributionReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerInstance,
        [Parameter(Mandatory = $true)]
        [string]$CommandDatabase,
        [Parameter(Mandatory = $true)]
        [string]$QueryDatabase
    )

    $commandScheduled = [int](Invoke-CqrsStagedSqlScalar -ServerInstance $ServerInstance -Database $CommandDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Appointments WHERE Status = 0;")
    $commandCancelled = [int](Invoke-CqrsStagedSqlScalar -ServerInstance $ServerInstance -Database $CommandDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Appointments WHERE Status = 2;")
    $commandCompleted = [int](Invoke-CqrsStagedSqlScalar -ServerInstance $ServerInstance -Database $CommandDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Appointments WHERE Status = 1;")

    $queryScheduled = [int](Invoke-CqrsStagedSqlScalar -ServerInstance $ServerInstance -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM AppointmentReadModels WHERE Status = 0;")
    $queryCancelled = [int](Invoke-CqrsStagedSqlScalar -ServerInstance $ServerInstance -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM AppointmentReadModels WHERE Status = 2;")
    $queryCompleted = [int](Invoke-CqrsStagedSqlScalar -ServerInstance $ServerInstance -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM AppointmentReadModels WHERE Status = 1;")

    $dailyStatsTotal = [int](Invoke-CqrsStagedSqlScalar -ServerInstance $ServerInstance -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT ISNULL(SUM(TotalCount), 0) FROM ClinicDailyAppointmentStatsReadModels;")

    $readModelTotal = [int](Invoke-CqrsStagedSqlScalar -ServerInstance $ServerInstance -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM AppointmentReadModels;")

    return [ordered]@{
        command = @{
            scheduled = $commandScheduled
            completed = $commandCompleted
            cancelled = $commandCancelled
        }
        queryReadModel = @{
            scheduled = $queryScheduled
            completed = $queryCompleted
            cancelled = $queryCancelled
        }
        statusDistributionMatched = (
            $commandScheduled -eq $queryScheduled -and
            $commandCancelled -eq $queryCancelled -and
            $commandCompleted -eq $queryCompleted
        )
        dailyStatsTotalCountSum = $dailyStatsTotal
        readModelTotalCount     = $readModelTotal
        dailyStatsNotOverCounting = ($dailyStatsTotal -ge $readModelTotal)
    }
}

function ConvertFrom-CqrsTokenFileEntries {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TokenFilePath
    )

    $raw = Get-Content -LiteralPath $TokenFilePath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return @()
    }

    $parsed = $raw | ConvertFrom-Json
    if ($null -eq $parsed) {
        return @()
    }

    if ($parsed -is [System.Array]) {
        return @($parsed)
    }

    $slotProperty = $parsed.PSObject.Properties["slot"]
    $accessTokenProperty = $parsed.PSObject.Properties["accessToken"]
    if ($null -ne $slotProperty -and $null -ne $accessTokenProperty) {
        $slots = @($slotProperty.Value)
        $accessTokens = @($accessTokenProperty.Value)
        if ($slots.Count -gt 1 -and $accessTokens.Count -eq $slots.Count) {
            $entries = New-Object System.Collections.Generic.List[object]
            for ($index = 0; $index -lt $slots.Count; $index++) {
                $entries.Add([PSCustomObject]@{
                        slot        = [string]$slots[$index]
                        accessToken = [string]$accessTokens[$index]
                    }) | Out-Null
            }
            return $entries.ToArray()
        }
    }

    return @($parsed)
}

function Normalize-CqrsTokenSlot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Slot
    )

    $normalized = [string]$Slot.Trim()
    if ($normalized -match '^\d$') {
        return "0$normalized"
    }

    return $normalized
}

function Get-CqrsTwoInstanceWorkloadRunId {
    param(
        [int]$WorkloadRunId = 0
    )

    if ($WorkloadRunId -gt 0) {
        return $WorkloadRunId
    }

    return ([int]([DateTime]::UtcNow.Ticks % 9999)) + 1
}

function Get-CqrsTwoInstanceWorkloadIsolationEnv {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("primary", "secondary")]
        [string]$InstanceLabel,
        [Parameter(Mandatory = $true)]
        [int]$WorkloadRunId
    )

    $slotBand = $WorkloadRunId % 100
    $sharedSlotSequenceOffset = $slotBand * 3
    $primaryBaseDayOffset = 120 + ($WorkloadRunId % 365)
    $secondaryBaseDayOffset = $primaryBaseDayOffset + 14

    if ($InstanceLabel -eq "primary") {
        return [ordered]@{
            instanceLabel            = $InstanceLabel
            workloadRunId            = $WorkloadRunId
            baseDayOffset            = $primaryBaseDayOffset
            slotSequenceOffset       = $sharedSlotSequenceOffset
            vetinityBaseDayOffset    = $primaryBaseDayOffset
            vetinitySlotSequenceOffset = $sharedSlotSequenceOffset
            vetinityWorkloadRunId    = $WorkloadRunId
        }
    }

    return [ordered]@{
        instanceLabel            = $InstanceLabel
        workloadRunId            = $WorkloadRunId
        baseDayOffset            = $secondaryBaseDayOffset
        slotSequenceOffset       = $sharedSlotSequenceOffset
        vetinityBaseDayOffset    = $secondaryBaseDayOffset
        vetinitySlotSequenceOffset = $sharedSlotSequenceOffset
        vetinityWorkloadRunId    = $WorkloadRunId
    }
}

function New-CqrsPartitionedTokenFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceTokenFile,
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,
        [string[]]$PrimarySlots = @("01", "02", "03", "04", "05"),
        [string[]]$SecondarySlots = @("06", "07", "08", "09", "10"),
        [string]$FilePrefix = "two-instance-tokens"
    )

    $resolvedSource = Resolve-CqrsLoadTokenFile -TokenFile $SourceTokenFile
    $tokens = @(ConvertFrom-CqrsTokenFileEntries -TokenFilePath $resolvedSource)
    if ($tokens.Count -eq 0) {
        throw "Token file is empty: $resolvedSource"
    }

    $primarySlotSet = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($slot in $PrimarySlots) {
        [void]$primarySlotSet.Add((Normalize-CqrsTokenSlot -Slot $slot))
    }

    $secondarySlotSet = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($slot in $SecondarySlots) {
        [void]$secondarySlotSet.Add((Normalize-CqrsTokenSlot -Slot $slot))
    }

    $overlap = @($primarySlotSet | Where-Object { $secondarySlotSet.Contains($_) })
    if ($overlap.Count -gt 0) {
        throw "Primary and secondary token slot ranges overlap: $($overlap -join ', ')"
    }

    $primaryTokens = @(
        foreach ($entry in $tokens) {
            $slot = Normalize-CqrsTokenSlot -Slot ([string]$entry.slot)
            if ($primarySlotSet.Contains($slot)) {
                [ordered]@{
                    slot        = $slot
                    accessToken = [string]$entry.accessToken
                }
            }
        }
    )

    $secondaryTokens = @(
        foreach ($entry in $tokens) {
            $slot = Normalize-CqrsTokenSlot -Slot ([string]$entry.slot)
            if ($secondarySlotSet.Contains($slot)) {
                [ordered]@{
                    slot        = $slot
                    accessToken = [string]$entry.accessToken
                }
            }
        }
    )

    if ($primaryTokens.Count -eq 0) {
        throw "No primary token entries found for slots: $($PrimarySlots -join ', ')"
    }
    if ($secondaryTokens.Count -eq 0) {
        throw "No secondary token entries found for slots: $($SecondarySlots -join ', ')"
    }

    if (-not (Test-Path -LiteralPath $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }

    $primaryPath = Join-Path $OutputDirectory "$FilePrefix-primary.json"
    $secondaryPath = Join-Path $OutputDirectory "$FilePrefix-secondary.json"

    $primaryJson = $primaryTokens | ConvertTo-Json -Depth 3
    $secondaryJson = $secondaryTokens | ConvertTo-Json -Depth 3
    [System.IO.File]::WriteAllText($primaryPath, $primaryJson, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($secondaryPath, $secondaryJson, [System.Text.UTF8Encoding]::new($false))

    return [ordered]@{
        sourceTokenFile = $resolvedSource
        primary = [ordered]@{
            tokenFile = [System.IO.Path]::GetFullPath($primaryPath)
            slots     = @($primaryTokens | ForEach-Object { [string]$_.slot })
            tokenCount = $primaryTokens.Count
        }
        secondary = [ordered]@{
            tokenFile = [System.IO.Path]::GetFullPath($secondaryPath)
            slots     = @($secondaryTokens | ForEach-Object { [string]$_.slot })
            tokenCount = $secondaryTokens.Count
        }
    }
}

function Get-CqrsK6SummaryMetricCount {
    param(
        $Summary,
        [Parameter(Mandatory = $true)]
        [string]$MetricName
    )

    if ($null -eq $Summary -or $null -eq $Summary.metrics) {
        return 0
    }

    $metrics = $Summary.metrics
    $metric = $null

    if ($metrics -is [System.Collections.IDictionary]) {
        if (-not $metrics.Contains($MetricName)) {
            return 0
        }
        $metric = $metrics[$MetricName]
    }
    elseif ($metrics.PSObject.Properties.Name -contains $MetricName) {
        $metric = $metrics.$MetricName
    }
    else {
        return 0
    }

    if ($null -eq $metric) {
        return 0
    }

    if ($metric -is [System.Collections.IDictionary]) {
        if ($metric.Contains('values')) {
            $values = $metric['values']
            if ($null -ne $values) {
                if ($values -is [System.Collections.IDictionary] -and $values.Contains('count')) {
                    return [long]$values['count']
                }
                if ($values.PSObject.Properties.Name -contains 'count') {
                    return [long]$values.count
                }
            }
        }

        if ($metric.Contains('count')) {
            return [long]$metric['count']
        }

        return 0
    }

    $propertyNames = $metric.PSObject.Properties.Name

    if ($propertyNames -contains 'values') {
        $values = $metric.values
        if ($null -ne $values) {
            if ($values -is [System.Collections.IDictionary] -and $values.Contains('count')) {
                return [long]$values['count']
            }
            if ($values.PSObject.Properties.Name -contains 'count') {
                return [long]$values.count
            }
        }
    }

    if ($propertyNames -contains 'count') {
        return [long]$metric.count
    }

    return 0
}

function Get-CqrsK6ProjectionLagLifecycleSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SummaryPath
    )

    if (-not (Test-Path -LiteralPath $SummaryPath)) {
        return [ordered]@{
            summaryPath                  = $SummaryPath
            summaryExists                = $false
            lifecycleAttemptsCount       = 0
            lifecycleCompletedCount      = 0
            scheduleNullSkippedCount     = 0
            createCompletedCount         = 0
            rescheduleCompletedCount     = 0
            cancelCompletedCount         = 0
            createLagSampleCount         = 0
            rescheduleLagSampleCount     = 0
            cancelLagSampleCount         = 0
            passed                       = $false
        }
    }

    $summary = Get-Content -LiteralPath $SummaryPath -Raw | ConvertFrom-Json

    $lifecycleAttemptsCount = Get-CqrsK6SummaryMetricCount -Summary $summary -MetricName "appointment_projection_lifecycle_attempts"
    $lifecycleCompletedCount = Get-CqrsK6SummaryMetricCount -Summary $summary -MetricName "appointment_projection_lifecycle_completed"
    $scheduleNullSkippedCount = Get-CqrsK6SummaryMetricCount -Summary $summary -MetricName "appointment_projection_schedule_null_skipped"
    $createCompletedCount = Get-CqrsK6SummaryMetricCount -Summary $summary -MetricName "appointment_projection_create_completed"
    $rescheduleCompletedCount = Get-CqrsK6SummaryMetricCount -Summary $summary -MetricName "appointment_projection_reschedule_completed"
    $cancelCompletedCount = Get-CqrsK6SummaryMetricCount -Summary $summary -MetricName "appointment_projection_cancel_completed"
    $createLagSampleCount = Get-CqrsK6SummaryMetricCount -Summary $summary -MetricName "appointment_projection_create_lag_ms"
    $rescheduleLagSampleCount = Get-CqrsK6SummaryMetricCount -Summary $summary -MetricName "appointment_projection_reschedule_lag_ms"
    $cancelLagSampleCount = Get-CqrsK6SummaryMetricCount -Summary $summary -MetricName "appointment_projection_cancel_lag_ms"

    $passed = Test-CqrsK6ProjectionLagLifecycleSummary -Summary ([ordered]@{
            lifecycleAttemptsCount   = $lifecycleAttemptsCount
            lifecycleCompletedCount  = $lifecycleCompletedCount
            createCompletedCount     = $createCompletedCount
            rescheduleCompletedCount   = $rescheduleCompletedCount
            cancelCompletedCount       = $cancelCompletedCount
            scheduleNullSkippedCount   = $scheduleNullSkippedCount
        })

    return [ordered]@{
        summaryPath                  = $SummaryPath
        summaryExists                = $true
        lifecycleAttemptsCount       = $lifecycleAttemptsCount
        lifecycleCompletedCount      = $lifecycleCompletedCount
        scheduleNullSkippedCount     = $scheduleNullSkippedCount
        createCompletedCount         = $createCompletedCount
        rescheduleCompletedCount     = $rescheduleCompletedCount
        cancelCompletedCount         = $cancelCompletedCount
        createLagSampleCount         = $createLagSampleCount
        rescheduleLagSampleCount     = $rescheduleLagSampleCount
        cancelLagSampleCount         = $cancelLagSampleCount
        passed                       = $passed
    }
}

function Test-CqrsK6ProjectionLagLifecycleSummary {
    param(
        [Parameter(Mandatory = $true)]
        $Summary
    )

    return (
        [long]$Summary.lifecycleCompletedCount -gt 0 -and
        [long]$Summary.lifecycleAttemptsCount -gt 0 -and
        [long]$Summary.createCompletedCount -gt 0 -and
        [long]$Summary.rescheduleCompletedCount -gt 0 -and
        [long]$Summary.cancelCompletedCount -gt 0 -and
        [long]$Summary.scheduleNullSkippedCount -eq 0
    )
}

function Get-CqrsClaimWorkerParticipationReport {
    param(
        [string[]]$LogPaths
    )

    $completedPattern = 'AppointmentProjectionClaimBatchCompleted WorkerId=([^\s]+)'
    $startedPattern = 'AppointmentProjectionClaimBatchStarted'

    $workerIds = New-Object System.Collections.Generic.HashSet[string]
    $claimBatchStartedCount = 0
    $claimBatchCompletedCount = 0
    $scannedPaths = New-Object System.Collections.Generic.List[string]

    foreach ($path in $LogPaths) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) {
            continue
        }

        $scannedPaths.Add($path) | Out-Null

        $startedMatches = Select-String -LiteralPath $path -Pattern $startedPattern -AllMatches
        if ($null -ne $startedMatches) {
            $claimBatchStartedCount += @($startedMatches).Count
        }

        $completedMatches = Select-String -LiteralPath $path -Pattern $completedPattern -AllMatches
        if ($null -ne $completedMatches) {
            foreach ($match in @($completedMatches)) {
                $claimBatchCompletedCount++
                $id = $match.Matches[0].Groups[1].Value.Trim()
                if (-not [string]::IsNullOrWhiteSpace($id)) {
                    [void]$workerIds.Add($id)
                }
            }
        }
    }

    $sortedWorkerIds = @($workerIds | Sort-Object)

    return [ordered]@{
        workerIds                = $sortedWorkerIds
        workerIdCount            = $sortedWorkerIds.Count
        claimBatchStartedCount   = $claimBatchStartedCount
        claimBatchCompletedCount = $claimBatchCompletedCount
        hasCompletedLogs         = ($claimBatchCompletedCount -gt 0)
        multiWorkerParticipation = ($sortedWorkerIds.Count -ge 2)
        logPathsScanned          = @($scannedPaths)
        passed                   = (
            $claimBatchCompletedCount -gt 0 -and
            $sortedWorkerIds.Count -ge 2
        )
    }
}

function Get-CqrsClaimWorkerIdsFromLogs {
    param(
        [string[]]$LogPaths
    )

    $workerIds = New-Object System.Collections.Generic.HashSet[string]
    $pattern = 'AppointmentProjectionClaimBatchCompleted WorkerId=([^\s]+)'

    foreach ($path in $LogPaths) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) {
            continue
        }

        $matches = Select-String -LiteralPath $path -Pattern $pattern -AllMatches
        if ($null -ne $matches) {
            foreach ($match in @($matches)) {
                $id = $match.Matches[0].Groups[1].Value.Trim()
                if (-not [string]::IsNullOrWhiteSpace($id)) {
                    [void]$workerIds.Add($id)
                }
            }
        }
    }

    return @($workerIds | Sort-Object)
}

function Invoke-CqrsTwoInstanceProjectionWorkload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrimaryBaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$SecondaryBaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$TokenFile,
        [int]$Vus = 4,
        [string]$Duration = "45s",
        [string]$OutputDirectory,
        [int]$WorkloadRunId = 0
    )

    $repoRoot = Get-CqrsLoadRepositoryRoot
    $scriptPath = Get-CqrsLoadWorkloadScript -Workload "projection-lag"
    $resolvedRunId = Get-CqrsTwoInstanceWorkloadRunId -WorkloadRunId $WorkloadRunId

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $repoRoot "tests\load\results\two-instance-acceptance"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory = Join-Path $repoRoot $OutputDirectory
    }

    $OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (-not (Test-Path $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }

    $timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
    $tokenPartition = New-CqrsPartitionedTokenFiles `
        -SourceTokenFile $TokenFile `
        -OutputDirectory $OutputDirectory `
        -FilePrefix "two-instance-tokens-$timestamp"

    $primaryIsolation = Get-CqrsTwoInstanceWorkloadIsolationEnv -InstanceLabel "primary" -WorkloadRunId $resolvedRunId
    $secondaryIsolation = Get-CqrsTwoInstanceWorkloadIsolationEnv -InstanceLabel "secondary" -WorkloadRunId $resolvedRunId

    $halfVus = [Math]::Max(1, [int][Math]::Floor($Vus / 2))
    $remainderVus = $Vus - $halfVus

    $runs = @(
        @{
            label              = "primary"
            baseUrl            = $PrimaryBaseUrl.Trim().TrimEnd("/")
            vus                = $halfVus
            tokenFile          = $tokenPartition.primary.tokenFile
            tokenSlots         = $tokenPartition.primary.slots
            isolation          = $primaryIsolation
            summary            = Join-Path $OutputDirectory "k6-primary-$timestamp.json"
            log                = Join-Path $OutputDirectory "k6-primary-$timestamp.log"
        },
        @{
            label              = "secondary"
            baseUrl            = $SecondaryBaseUrl.Trim().TrimEnd("/")
            vus                = $remainderVus
            tokenFile          = $tokenPartition.secondary.tokenFile
            tokenSlots         = $tokenPartition.secondary.slots
            isolation          = $secondaryIsolation
            summary            = Join-Path $OutputDirectory "k6-secondary-$timestamp.json"
            log                = Join-Path $OutputDirectory "k6-secondary-$timestamp.log"
        }
    )

    $jobs = @()
    foreach ($run in $runs) {
        $jobs += Start-Job -ArgumentList @(
            $repoRoot,
            $scriptPath,
            $run.baseUrl,
            $run.tokenFile,
            $run.vus,
            $Duration,
            $run.summary,
            $run.log,
            $run.isolation.vetinityBaseDayOffset,
            $run.isolation.vetinitySlotSequenceOffset,
            $run.isolation.vetinityWorkloadRunId
        ) -ScriptBlock {
            param(
                $RepoRoot,
                $ScriptPath,
                $BaseUrl,
                $TokenFile,
                $Vus,
                $Duration,
                $SummaryPath,
                $LogPath,
                $BaseDayOffset,
                $SlotSequenceOffset,
                $WorkloadRunId
            )
            Set-Location $RepoRoot
            $k6Args = @(
                "run",
                $ScriptPath,
                "--summary-export", $SummaryPath,
                "-e", "VETINITY_URL=$BaseUrl",
                "-e", "VETINITY_TOKENS_FILE=$TokenFile",
                "-e", "VUS=$Vus",
                "-e", "DURATION=$Duration",
                "-e", "PROJECTION_LAG_VUS=$Vus",
                "-e", "PROJECTION_LAG_DURATION=$Duration",
                "-e", "VETINITY_BASE_DAY_OFFSET=$BaseDayOffset",
                "-e", "VETINITY_SLOT_SEQUENCE_OFFSET=$SlotSequenceOffset",
                "-e", "VETINITY_WORKLOAD_RUN_ID=$WorkloadRunId"
            )
            & k6 @k6Args 2>&1 | Tee-Object -FilePath $LogPath
            if ($LASTEXITCODE -ne 0) {
                throw "k6 exited with code $LASTEXITCODE for $BaseUrl"
            }
        }
    }

    $failedStates = @()
    $jobResults = New-Object System.Collections.Generic.List[object]
    foreach ($job in $jobs) {
        Wait-Job -Job $job | Out-Null
        if ($job.State -ne "Completed") {
            $failedStates += $job.State
        }
        Receive-Job -Job $job -ErrorAction SilentlyContinue | Out-Null
        Remove-Job -Job $job -Force
    }

    $lifecycleFailures = New-Object System.Collections.Generic.List[string]
    foreach ($run in $runs) {
        $lifecycleSummary = Get-CqrsK6ProjectionLagLifecycleSummary -SummaryPath $run.summary
        if (-not $lifecycleSummary.passed) {
            $lifecycleFailures.Add(
                ("{0}: lifecycleAttempts={1} lifecycleCompleted={2} createCompleted={3} rescheduleCompleted={4} cancelCompleted={5}" -f `
                    $run.label,
                    $lifecycleSummary.lifecycleAttemptsCount,
                    $lifecycleSummary.lifecycleCompletedCount,
                    $lifecycleSummary.createCompletedCount,
                    $lifecycleSummary.rescheduleCompletedCount,
                    $lifecycleSummary.cancelCompletedCount)
            ) | Out-Null
        }

        $jobResults.Add([ordered]@{
                label                = $run.label
                baseUrl              = $run.baseUrl
                vus                  = $run.vus
                tokenFile            = $run.tokenFile
                tokenSlots           = $run.tokenSlots
                baseDayOffset        = $run.isolation.baseDayOffset
                slotSequenceOffset   = $run.isolation.slotSequenceOffset
                workloadRunId        = $run.isolation.workloadRunId
                summaryPath          = $run.summary
                logPath              = $run.log
                summaryExists        = $lifecycleSummary.summaryExists
                lifecycleSummary     = $lifecycleSummary
            }) | Out-Null
    }

    $allLifecycleProduced = ($lifecycleFailures.Count -eq 0)
    if ($failedStates.Count -eq 0 -and -not $allLifecycleProduced) {
        throw ("k6 lifecycle produced zero appointment operations on one or both instances. " + ($lifecycleFailures -join "; "))
    }

    return [ordered]@{
        outputDirectory  = $OutputDirectory
        duration         = $Duration
        totalVus         = $Vus
        workloadRunId    = $resolvedRunId
        tokenPartition   = $tokenPartition
        workloadIsolation = [ordered]@{
            primary   = $primaryIsolation
            secondary = $secondaryIsolation
        }
        runs             = $jobResults
        allJobsCompleted = ($failedStates.Count -eq 0)
        allLifecycleProduced = $allLifecycleProduced
        lifecycleFailures = @($lifecycleFailures)
        failedJobStates  = $failedStates
    }
}
