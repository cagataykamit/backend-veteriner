#Requires -Version 5.1
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "CqrsStagedRolloutCommon.ps1")

function Get-CqrsRolloutAcceptanceSequence {
    return @(
        [ordered]@{
            step        = "command-read"
            label       = "Mode A baseline (Command read)"
            phase       = "rollout"
            mode        = "command-read"
            overrides   = Get-CqrsStagedModeEnvironmentOverrides -Mode "command-read"
            readSources = (Get-CqrsStagedModeDefinition -Mode "command-read")
            validation  = "Invoke-CqrsStagedRollout.ps1 -Mode command-read -Apply"
        },
        [ordered]@{
            step        = "appointment-query"
            label       = "Mode B (Appointment query read)"
            phase       = "rollout"
            mode        = "appointment-query"
            overrides   = Get-CqrsStagedModeEnvironmentOverrides -Mode "appointment-query"
            readSources = (Get-CqrsStagedModeDefinition -Mode "appointment-query")
            validation  = "Invoke-CqrsStagedRollout.ps1 -Mode appointment-query -Apply"
        },
        [ordered]@{
            step        = "full-query"
            label       = "Mode C (Full query read)"
            phase       = "rollout"
            mode        = "full-query"
            overrides   = Get-CqrsStagedModeEnvironmentOverrides -Mode "full-query"
            readSources = (Get-CqrsStagedModeDefinition -Mode "full-query")
            validation  = "Invoke-CqrsStagedRollout.ps1 -Mode full-query -Apply"
        },
        [ordered]@{
            step        = "projection-disabled"
            label       = "Projection disabled (outbox pending allowed)"
            phase       = "projection-pause"
            mode        = "full-query"
            overrides   = Get-CqrsProjectionDisabledEnvironmentOverrides -ReadMode "full-query"
            readSources = (Get-CqrsStagedModeDefinition -Mode "full-query")
            validation  = "Invoke-CqrsRolloutAcceptance.ps1 -Step projection-disabled -Apply"
        },
        [ordered]@{
            step        = "projection-reenabled"
            label       = "Projection re-enabled (queue drain + parity)"
            phase       = "projection-resume"
            mode        = "full-query"
            overrides   = Get-CqrsStagedModeEnvironmentOverrides -Mode "full-query"
            readSources = (Get-CqrsStagedModeDefinition -Mode "full-query")
            validation  = "Invoke-CqrsRolloutAcceptance.ps1 -Step projection-reenabled -Apply"
        },
        [ordered]@{
            step        = "rollback-appointment-query"
            label       = "Rollback C -> B"
            phase       = "rollback"
            mode        = "appointment-query"
            overrides   = Get-CqrsStagedModeEnvironmentOverrides -Mode "appointment-query"
            readSources = (Get-CqrsStagedModeDefinition -Mode "appointment-query")
            validation  = "Invoke-CqrsStagedRollout.ps1 -Mode appointment-query -Apply"
        },
        [ordered]@{
            step        = "rollback-command-read"
            label       = "Rollback B -> A"
            phase       = "rollback"
            mode        = "command-read"
            overrides   = Get-CqrsStagedModeEnvironmentOverrides -Mode "command-read"
            readSources = (Get-CqrsStagedModeDefinition -Mode "command-read")
            validation  = "Invoke-CqrsStagedRollout.ps1 -Mode command-read -Apply"
        }
    )
}

function Get-CqrsProjectionDisabledEnvironmentOverrides {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("command-read", "appointment-query", "full-query")]
        [string]$ReadMode
    )

    $readOverrides = Get-CqrsStagedModeEnvironmentOverrides -Mode $ReadMode
    $readOverrides["AppointmentProjection__Enabled"] = "False"
    return $readOverrides
}

function Get-CqrsRolloutReadFlagsEnabled {
    param(
        $AppointmentsReadEnabled,
        $DashboardReadEnabled
    )

    $appointmentsRead = Get-CqrsLoadBooleanValue -Value $AppointmentsReadEnabled
    $dashboardRead = Get-CqrsLoadBooleanValue -Value $DashboardReadEnabled

    if ($null -eq $appointmentsRead -or $null -eq $dashboardRead) {
        return $null
    }

    return ($appointmentsRead -or $dashboardRead)
}

function Get-CqrsRolloutProjectionHealthExpectation {
    param(
        $ProjectionEnabled,
        $AppointmentsReadEnabled,
        $DashboardReadEnabled,
        [int]$PendingCount = 0,
        [int]$RetryWaitingCount = 0,
        [int]$DeadLetterCount = 0
    )

    $projectionOn = Get-CqrsLoadBooleanValue -Value $ProjectionEnabled
    $readFlagsOn = Get-CqrsRolloutReadFlagsEnabled `
        -AppointmentsReadEnabled $AppointmentsReadEnabled `
        -DashboardReadEnabled $DashboardReadEnabled

    if ($null -eq $projectionOn -or $null -eq $readFlagsOn) {
        return $null
    }

    if ($DeadLetterCount -gt 0) {
        return "Unhealthy"
    }

    if (-not $projectionOn -and $readFlagsOn) {
        if ($PendingCount -gt 0 -or $RetryWaitingCount -gt 0) {
            return "Unhealthy"
        }

        return "Degraded"
    }

    if (-not $projectionOn -and -not $readFlagsOn) {
        if ($PendingCount -gt 0 -or $RetryWaitingCount -gt 0) {
            return "Healthy"
        }

        return "Healthy"
    }

    if ($RetryWaitingCount -gt 0) {
        return "Degraded"
    }

    if ($PendingCount -gt 0) {
        return "Degraded"
    }

    return "Healthy"
}

function Test-CqrsRolloutProjectionHealthExpectation {
    param(
        [Parameter(Mandatory = $true)]
        $Snapshot,
        [Parameter(Mandatory = $true)]
        [ValidateSet("Healthy", "Degraded", "Unhealthy")]
        [string]$ExpectedLevel
    )

    $actualLevel = Get-CqrsRolloutProjectionHealthExpectation `
        -ProjectionEnabled $Snapshot.projectionEnabled `
        -AppointmentsReadEnabled $Snapshot.appointmentsReadEnabled `
        -DashboardReadEnabled $Snapshot.dashboardReadEnabled `
        -PendingCount $Snapshot.pendingCount `
        -RetryWaitingCount $Snapshot.retryWaitingCount `
        -DeadLetterCount $Snapshot.deadLetterCount

    return [ordered]@{
        passed       = ($actualLevel -eq $ExpectedLevel)
        expected     = $ExpectedLevel
        actual       = $actualLevel
        snapshot     = $Snapshot
    }
}

function Get-CqrsRolloutHealthSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl
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
            baseUrl                 = $normalizedBaseUrl
            overallStatus           = [string]$health.status
            projectionEntryStatus   = [string]$projectionEntry.status
            projectionEnabled       = $projectionData.projectionEnabled
            claimingEnabled         = $projectionData.claimingEnabled
            appointmentsReadEnabled = $projectionData.appointmentsReadEnabled
            dashboardReadEnabled    = $projectionData.dashboardReadEnabled
            pendingCount            = [int]$projectionData.pendingCount
            retryWaitingCount       = [int]$projectionData.retryWaitingCount
            deadLetterCount         = [int]$projectionData.deadLetterCount
            querySqlStatus          = [string]$health.results.'query-sql'.status
        }
    }
    finally {
        Disable-CqrsLoadLocalhostTlsBypass -TlsState $tlsState
    }
}

function Wait-CqrsRolloutProjectionQueueDrain {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [int]$TimeoutSeconds = 60,
        [int]$PollIntervalMilliseconds = 500
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $attempts = 0

    while ((Get-Date) -lt $deadline) {
        $attempts++
        $snapshot = Get-CqrsRolloutHealthSnapshot -BaseUrl $BaseUrl
        if ($snapshot.pendingCount -eq 0 -and $snapshot.retryWaitingCount -eq 0 -and $snapshot.deadLetterCount -eq 0) {
            return [ordered]@{
                drained  = $true
                attempts = $attempts
                snapshot = $snapshot
            }
        }

        Start-Sleep -Milliseconds $PollIntervalMilliseconds
    }

    return [ordered]@{
        drained  = $false
        attempts = $attempts
        snapshot = (Get-CqrsRolloutHealthSnapshot -BaseUrl $BaseUrl)
    }
}

function Invoke-CqrsRolloutLifecycleSmoke {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$TokenFile,
        [string]$NotesPrefix = "CQRS11E_ROLLOUT"
    )

    $accessToken = Get-CqrsStagedAccessToken -TokenFile $TokenFile
    $tokens = Get-Content -LiteralPath $TokenFile -Raw | ConvertFrom-Json
    $tokenEntry = $tokens | Where-Object { $_.slot -eq "01" } | Select-Object -First 1
    $clinicId = [string]$tokenEntry.clinicId

    if ([string]::IsNullOrWhiteSpace($clinicId)) {
        throw "Token slot 01 clinicId is required for lifecycle smoke."
    }

    $scheduledAtUtc = (Get-CqrsStagedSlotAlignedUtcPlusDays -Days 11).ToString("o")
    $createBody = (@{
            clinicId        = $clinicId
            petId           = [string]$tokenEntry.petId
            scheduledAtUtc  = $scheduledAtUtc
            appointmentType = 0
            notes           = "${NotesPrefix}_CREATE"
        } | ConvertTo-Json -Compress)

    $createResponse = Invoke-CqrsStagedApiRequest `
        -BaseUrl $BaseUrl `
        -Method "POST" `
        -Path "/api/v1/appointments" `
        -AccessToken $accessToken `
        -JsonBody $createBody

    if ($createResponse.statusCode -ne 201) {
        throw "Create appointment smoke failed (status=$($createResponse.statusCode))."
    }

    $appointmentId = [string]($createResponse.body | ConvertFrom-Json)
    $beforeSnapshot = Get-CqrsRolloutHealthSnapshot -BaseUrl $BaseUrl

    return [ordered]@{
        appointmentId   = $appointmentId
        createStatus    = $createResponse.statusCode
        beforeSnapshot  = $beforeSnapshot
    }
}

function Get-CqrsRolloutRollbackDocumentation {
    return [ordered]@{
        readModelRollback = @(
            [ordered]@{
                from = "full-query"
                to   = "appointment-query"
                plan = (Get-CqrsStagedRollbackPlan -FromMode "full-query")
            },
            [ordered]@{
                from = "appointment-query"
                to   = "command-read"
                plan = (Get-CqrsStagedRollbackPlan -FromMode "appointment-query")
            }
        )
        projectionPause = [ordered]@{
            action    = "Set AppointmentProjection__Enabled=false (keep read flags as needed)"
            note      = "Outbox pending may accumulate; health reflects pending when read flags are on."
            recovery  = "Set AppointmentProjection__Enabled=true, restart, wait for queue drain, verify SQL parity."
        }
        projectorDuringReadRollback = "AppointmentProjection__Enabled must stay true during read-flag rollback (Mode C->B->A)."
    }
}
