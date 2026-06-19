#Requires -Version 5.1
Set-StrictMode -Version Latest

# CQRS-12B-7 - Client read-model rollout / rollback acceptance helpers.
#
# Pure, deterministic functions. No live API / SQL / token required (CI-safe).
# Health expectation logic mirrors ClientProjectionHealthEvaluator (C#) exactly:
#  - Client side has a single read flag: QueryReadModels:ClientsEnabled (no appointment/dashboard split).
#  - No claim/lease.

. (Join-Path $PSScriptRoot "CqrsStagedRolloutCommon.ps1")

function Get-CqrsClientReadFlagOverrides {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Enabled
    )

    return [ordered]@{
        QueryReadModels__ClientsEnabled = (& { if ($Enabled) { "True" } else { "False" } })
    }
}

function Get-CqrsClientProjectionDisabledOverrides {
    # Stops the projector only, leaving the read flag unchanged (planned pause / rollback step).
    param(
        [Parameter(Mandatory = $true)]
        [bool]$ClientsReadEnabled
    )

    $overrides = Get-CqrsClientReadFlagOverrides -Enabled $ClientsReadEnabled
    $overrides["ClientProjection__Enabled"] = "False"
    return $overrides
}

function Get-CqrsClientRolloutSequence {
    # Safe order that runs BEFORE enabling ClientsEnabled.
    return @(
        [ordered]@{
            step       = "query-migration"
            label      = "Query DB migrate (ClientReadModels schema)"
            phase      = "rollout"
            command    = "dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query"
            validation = "ClientReadModels table exists; no pending migration."
        },
        [ordered]@{
            step       = "backfill"
            label      = "Client read-model backfill (idempotent)"
            phase      = "rollout"
            command    = "dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections"
            validation = "Backfill success; parity in-sync (exit code 2 on mismatch)."
        },
        [ordered]@{
            step       = "parity-check"
            label      = "Parity verification (Command Clients == Query ClientReadModels)"
            phase      = "rollout"
            command    = "IClientReadModelParityReader / SQL COUNT_BIG"
            validation = "CommandCount == QueryCount (InSync == true)."
        },
        [ordered]@{
            step       = "health-check"
            label      = "Health verification (/health/ready -> client-projection)"
            phase      = "rollout"
            command    = "GET /health/ready"
            validation = "overall Healthy; client-projection entry Healthy; deadLetter=0."
        },
        [ordered]@{
            step       = "clients-enabled"
            label      = "ClientsEnabled=true rollout"
            phase      = "rollout"
            command    = "QueryReadModels__ClientsEnabled=true -> restart"
            overrides  = (Get-CqrsClientReadFlagOverrides -Enabled $true)
            validation = "Startup log ClientsQueryReadEnabled=True; read path reads Query DB."
        },
        [ordered]@{
            step       = "smoke"
            label      = "Smoke (list/search read-model + tenant isolation)"
            phase      = "rollout"
            command    = "GET /api/v1/clients (list/search)"
            validation = "Only requested tenant rows; read-model parity preserved."
        }
    )
}

function Get-CqrsClientRollbackSequence {
    # Read path rollback (fastest first). Projector STAYS ON (golden rule).
    return @(
        [ordered]@{
            step       = "clients-disabled"
            label      = "ClientsEnabled=false (read path returns to Command DB)"
            phase      = "rollback"
            command    = "QueryReadModels__ClientsEnabled=false"
            overrides  = (Get-CqrsClientReadFlagOverrides -Enabled $false)
            validation = "Read path Command DB; behavior identical to pre-12B."
        },
        [ordered]@{
            step       = "restart"
            label      = "API restart (flags are IOptions startup binding)"
            phase      = "rollback"
            command    = "Single instance restart/deploy"
            validation = "Startup log ClientsQueryReadEnabled=False."
        },
        [ordered]@{
            step       = "health-check"
            label      = "Health verification"
            phase      = "rollback"
            command    = "GET /health/ready"
            validation = "overall Healthy; client-projection entry healthy."
        },
        [ordered]@{
            step       = "projection-disabled"
            label      = "(Optional) ClientProjection:Enabled=false - planned pause"
            phase      = "rollback-optional"
            command    = "ClientProjection__Enabled=false"
            overrides  = (Get-CqrsClientProjectionDisabledOverrides -ClientsReadEnabled $false)
            validation = "Planned pause only; with read flag on, pending backlog turns health Degraded/Unhealthy."
        }
    )
}

function Get-CqrsClientProjectionHealthExpectation {
    # Same priority order as ClientProjectionHealthEvaluator (C#).
    param(
        $ProjectionEnabled,
        $ClientsReadEnabled,
        [int]$PendingCount = 0,
        [int]$RetryWaitingCount = 0,
        [int]$DeadLetterCount = 0,
        [double]$OldestPendingAgeSeconds = 0,
        [double]$DegradedAfterSeconds = 10,
        [double]$UnhealthyAfterSeconds = 30,
        [bool]$QueryReachable = $true,
        [bool]$QueryHasPendingMigrations = $false,
        [bool]$DeadLetterIsUnhealthy = $true
    )

    $projectionOn = Get-CqrsLoadBooleanValue -Value $ProjectionEnabled
    $readOn = Get-CqrsLoadBooleanValue -Value $ClientsReadEnabled

    if ($null -eq $projectionOn -or $null -eq $readOn) {
        return $null
    }

    if (-not $QueryReachable) {
        return "Unhealthy"
    }

    if ($QueryHasPendingMigrations) {
        return "Unhealthy"
    }

    if ($DeadLetterCount -gt 0 -and $DeadLetterIsUnhealthy) {
        return "Unhealthy"
    }

    if (-not $projectionOn -and $readOn) {
        if ($PendingCount -gt 0 -or $RetryWaitingCount -gt 0) {
            return "Unhealthy"
        }

        return "Degraded"
    }

    if ($PendingCount -gt 0) {
        if ($OldestPendingAgeSeconds -ge $UnhealthyAfterSeconds) {
            return "Unhealthy"
        }

        if ($OldestPendingAgeSeconds -ge $DegradedAfterSeconds) {
            return "Degraded"
        }
    }

    if ($RetryWaitingCount -gt 0) {
        return "Degraded"
    }

    return "Healthy"
}

function Test-CqrsClientProjectionHealthExpectation {
    param(
        [Parameter(Mandatory = $true)]
        $Snapshot,
        [Parameter(Mandatory = $true)]
        [ValidateSet("Healthy", "Degraded", "Unhealthy")]
        [string]$ExpectedLevel
    )

    $params = @{
        ProjectionEnabled  = $Snapshot.projectionEnabled
        ClientsReadEnabled = $Snapshot.clientsReadEnabled
        PendingCount       = [int]$Snapshot.pendingCount
        RetryWaitingCount  = [int]$Snapshot.retryWaitingCount
        DeadLetterCount    = [int]$Snapshot.deadLetterCount
    }

    if ($Snapshot.Keys -contains "oldestPendingAgeSeconds") {
        $params.OldestPendingAgeSeconds = [double]$Snapshot.oldestPendingAgeSeconds
    }

    $actual = Get-CqrsClientProjectionHealthExpectation @params

    return [ordered]@{
        passed   = ($actual -eq $ExpectedLevel)
        expected = $ExpectedLevel
        actual   = $actual
        snapshot = $Snapshot
    }
}

function Get-CqrsClientRollbackDocumentation {
    return [ordered]@{
        readModelRollback = [ordered]@{
            action = "QueryReadModels__ClientsEnabled=false"
            effect = "Read path returns to Command DB immediately; no automatic fallback (flag toggle)."
        }
        projectionPause = [ordered]@{
            action   = "ClientProjection__Enabled=false (optional, planned pause)"
            note     = "With read flag on, pending backlog turns health Degraded/Unhealthy."
            recovery = "ClientProjection__Enabled=true, restart, queue drain, verify parity."
        }
        projectorDuringReadRollback = "ClientProjection__Enabled must stay true during read-flag rollback."
    }
}
