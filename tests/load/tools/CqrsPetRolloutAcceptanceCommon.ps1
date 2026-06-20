#Requires -Version 5.1
Set-StrictMode -Version Latest

# CQRS-12C-7 - Pet read-model rollout / rollback acceptance helpers.
#
# Pure, deterministic functions. No live API / SQL / token required (CI-safe).
# Health expectation logic mirrors PetProjectionHealthEvaluator (C#) exactly:
#  - Pet side has a single read flag: QueryReadModels:PetsEnabled.
#  - No claim/lease.

. (Join-Path $PSScriptRoot "CqrsStagedRolloutCommon.ps1")

function Get-CqrsPetReadFlagOverrides {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Enabled
    )

    return [ordered]@{
        QueryReadModels__PetsEnabled = (& { if ($Enabled) { "True" } else { "False" } })
    }
}

function Get-CqrsPetProjectionDisabledOverrides {
    # Stops the projector only, leaving the read flag unchanged (planned pause / rollback step).
    param(
        [Parameter(Mandatory = $true)]
        [bool]$PetsReadEnabled
    )

    $overrides = Get-CqrsPetReadFlagOverrides -Enabled $PetsReadEnabled
    $overrides["PetProjection__Enabled"] = "False"
    return $overrides
}

function Get-CqrsPetRolloutSequence {
    # Safe order that runs BEFORE enabling PetsEnabled.
    return @(
        [ordered]@{
            step       = "query-migration"
            label      = "Query DB migrate (PetReadModels schema)"
            phase      = "rollout"
            command    = "dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query"
            validation = "PetReadModels table exists; no pending migration."
        },
        [ordered]@{
            step       = "backfill"
            label      = "Pet read-model backfill (idempotent)"
            phase      = "rollout"
            command    = "dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections"
            validation = "Backfill success (exit 0); parity in-sync; exit code 2 on mismatch."
        },
        [ordered]@{
            step       = "parity-check"
            label      = "Parity verification (Command Pets == Query PetReadModels)"
            phase      = "rollout"
            command    = "IPetReadModelParityReader / SQL COUNT_BIG"
            validation = "CommandCount == QueryCount (InSync == true)."
        },
        [ordered]@{
            step       = "health-check"
            label      = "Health verification (/health/ready -> pet-projection)"
            phase      = "rollout"
            command    = "GET /health/ready"
            validation = "overall Healthy; pet-projection Healthy; deadLetter=0; PetProjection enabled before read flag."
        },
        [ordered]@{
            step       = "pets-enabled"
            label      = "PetsEnabled=true rollout"
            phase      = "rollout"
            command    = "QueryReadModels__PetsEnabled=true -> restart"
            overrides  = (Get-CqrsPetReadFlagOverrides -Enabled $true)
            validation = "Startup log PetsQueryReadEnabled=True; read path reads Query DB."
        },
        [ordered]@{
            step       = "smoke"
            label      = "Smoke (list/search read-model + tenant isolation)"
            phase      = "rollout"
            command    = "GET /api/v1/pets (list/search)"
            validation = "Only requested tenant rows; read-model parity preserved."
        }
    )
}

function Get-CqrsPetRollbackSequence {
    # Read path rollback (fastest first). Projector STAYS ON (golden rule).
    return @(
        [ordered]@{
            step       = "pets-disabled"
            label      = "PetsEnabled=false (read path returns to Command DB)"
            phase      = "rollback"
            command    = "QueryReadModels__PetsEnabled=false"
            overrides  = (Get-CqrsPetReadFlagOverrides -Enabled $false)
            validation = "Read path Command DB; behavior identical to pre-12C."
        },
        [ordered]@{
            step       = "restart"
            label      = "API restart (flags are IOptions startup binding)"
            phase      = "rollback"
            command    = "Single instance restart/deploy"
            validation = "Startup log PetsQueryReadEnabled=False."
        },
        [ordered]@{
            step       = "health-check"
            label      = "Health verification"
            phase      = "rollback"
            command    = "GET /health/ready"
            validation = "overall Healthy; pet-projection entry healthy."
        },
        [ordered]@{
            step       = "projection-disabled"
            label      = "(Optional) PetProjection:Enabled=false - planned pause"
            phase      = "rollback-optional"
            command    = "PetProjection__Enabled=false"
            overrides  = (Get-CqrsPetProjectionDisabledOverrides -PetsReadEnabled $false)
            validation = "Planned pause only; with read flag on, pending backlog turns health Degraded/Unhealthy."
        }
    )
}

function Get-CqrsPetProjectionHealthExpectation {
    # Same priority order as PetProjectionHealthEvaluator (C#).
    param(
        $ProjectionEnabled,
        $PetsReadEnabled,
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
    $readOn = Get-CqrsLoadBooleanValue -Value $PetsReadEnabled

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

function Test-CqrsPetProjectionHealthExpectation {
    param(
        [Parameter(Mandatory = $true)]
        $Snapshot,
        [Parameter(Mandatory = $true)]
        [ValidateSet("Healthy", "Degraded", "Unhealthy")]
        [string]$ExpectedLevel
    )

    $params = @{
        ProjectionEnabled = $Snapshot.projectionEnabled
        PetsReadEnabled   = $Snapshot.petsReadEnabled
        PendingCount      = [int]$Snapshot.pendingCount
        RetryWaitingCount = [int]$Snapshot.retryWaitingCount
        DeadLetterCount   = [int]$Snapshot.deadLetterCount
    }

    if ($Snapshot.Keys -contains "oldestPendingAgeSeconds") {
        $params.OldestPendingAgeSeconds = [double]$Snapshot.oldestPendingAgeSeconds
    }

    $actual = Get-CqrsPetProjectionHealthExpectation @params

    return [ordered]@{
        passed   = ($actual -eq $ExpectedLevel)
        expected = $ExpectedLevel
        actual   = $actual
        snapshot = $Snapshot
    }
}

function Get-CqrsPetRollbackDocumentation {
    return [ordered]@{
        readModelRollback = [ordered]@{
            action = "QueryReadModels__PetsEnabled=false"
            effect = "Read path returns to Command DB immediately; no automatic fallback (flag toggle)."
        }
        projectionPause = [ordered]@{
            action   = "PetProjection__Enabled=false (optional, planned pause)"
            note     = "With read flag on, pending backlog turns health Degraded/Unhealthy."
            recovery = "PetProjection__Enabled=true, restart, queue drain, verify parity."
        }
        projectorDuringReadRollback = "PetProjection__Enabled must stay true during read-flag rollback."
        backfillExitCodes           = [ordered]@{
            success         = "0 - backfill completed; parity in-sync"
            parityMismatch  = "2 - parity not in-sync after backfill; do not enable PetsEnabled"
            exception       = "1 - unhandled failure"
        }
        emptyReadModelNoFallback    = "PetsEnabled=true with empty/incomplete PetReadModels returns incomplete list; no Command DB fallback."
        renamePropagationLimit      = "ClientFullName, SpeciesName, ColorName, BreedRefName update only via pet create/update events or backfill; no cross-entity rename propagation."
    }
}
