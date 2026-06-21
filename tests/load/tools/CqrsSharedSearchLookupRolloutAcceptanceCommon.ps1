#Requires -Version 5.1
Set-StrictMode -Version Latest

# CQRS-12D-10 — Shared + payments search lookup rollout / rollback acceptance helpers.
#
# Pure sequence/flag helpers are CI-safe. Live smoke helpers require BaseUrl + token file.

. (Join-Path $PSScriptRoot "CqrsStagedRolloutCommon.ps1")
. (Join-Path $PSScriptRoot "CqrsClientRolloutAcceptanceCommon.ps1")
. (Join-Path $PSScriptRoot "CqrsPetRolloutAcceptanceCommon.ps1")

function Get-CqrsSharedSearchLookupFlagOverrides {
    param(
        [bool]$SharedSearchLookupEnabled = $false,
        [bool]$PaymentsSearchLookupEnabled = $false
    )

    return [ordered]@{
        QueryReadModels__SharedSearchLookupEnabled  = (& { if ($SharedSearchLookupEnabled) { "True" } else { "False" } })
        QueryReadModels__PaymentsSearchLookupEnabled = (& { if ($PaymentsSearchLookupEnabled) { "True" } else { "False" } })
    }
}

function Get-CqrsSharedSearchLookupFlagSnapshotFromEnvironment {
    $shared = Get-CqrsLoadBooleanValue -Value $env:QueryReadModels__SharedSearchLookupEnabled
    $payments = Get-CqrsLoadBooleanValue -Value $env:QueryReadModels__PaymentsSearchLookupEnabled

    return [ordered]@{
        source                      = "process environment (QueryReadModels__*)"
        SharedSearchLookupEnabled   = $shared
        PaymentsSearchLookupEnabled = $payments
        note                        = "Runtime API flags bind at startup; set env vars and restart before -Apply validation."
    }
}

function Get-CqrsSharedSearchLookupPreconditionSequence {
    # Must complete before ANY search lookup flag is enabled.
    return @(
        [ordered]@{
            step       = "command-migration"
            label      = "Command DB migrate"
            phase      = "precondition"
            command    = "dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate"
            validation = "Command schema current; no pending migration."
        },
        [ordered]@{
            step       = "query-migration"
            label      = "Query DB migrate (ClientReadModels + PetReadModels)"
            phase      = "precondition"
            command    = "dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query"
            validation = "ClientReadModels + PetReadModels exist; no pending migration."
        },
        [ordered]@{
            step       = "backfill-client"
            label      = "Client read-model backfill (idempotent)"
            phase      = "precondition"
            command    = "dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections"
            validation = "Backfill success; parity in-sync (exit code 2 on mismatch)."
        },
        [ordered]@{
            step       = "backfill-pet"
            label      = "Pet read-model backfill (idempotent)"
            phase      = "precondition"
            command    = "dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections"
            validation = "Backfill success; parity in-sync (exit code 2 on mismatch)."
        },
        [ordered]@{
            step       = "parity-client"
            label      = "Client parity (Command Clients == Query ClientReadModels)"
            phase      = "precondition"
            command    = "IClientReadModelParityReader / SQL COUNT_BIG"
            validation = "CommandCount == QueryCount (InSync == true)."
        },
        [ordered]@{
            step       = "parity-pet"
            label      = "Pet parity (Command Pets == Query PetReadModels)"
            phase      = "precondition"
            command    = "IPetReadModelParityReader / SQL COUNT_BIG"
            validation = "CommandCount == QueryCount (InSync == true)."
        },
        [ordered]@{
            step       = "health-check"
            label      = "Projection health (/health/ready)"
            phase      = "precondition"
            command    = "GET /health/ready"
            validation = "overall Healthy; client-projection + pet-projection Healthy; deadLetter=0."
        }
    )
}

function Get-CqrsSharedSearchLookupRolloutSequence {
    return @(
        [ordered]@{
            step       = "shared-search-enabled"
            label      = "SharedSearchLookupEnabled=true (clinical + appointments Command-path lookup)"
            phase      = "rollout"
            command    = "QueryReadModels__SharedSearchLookupEnabled=true -> single instance restart"
            overrides  = (Get-CqrsSharedSearchLookupFlagOverrides -SharedSearchLookupEnabled $true -PaymentsSearchLookupEnabled $false)
            validation = "Startup log SharedSearchLookupEnabled=True; clinical + appointments smoke pass."
        },
        [ordered]@{
            step       = "smoke-clinical"
            label      = "Clinical list search smoke (Strategy A pet-id lookup)"
            phase      = "rollout"
            command    = "GET examinations/treatments/vaccinations/hospitalizations/lab-results/prescriptions ?search="
            validation = "HTTP 200; tenant-scoped rows only (manual parity spot-check recommended)."
        },
        [ordered]@{
            step       = "smoke-appointments"
            label      = "Appointments list search smoke (Command DB path when AppointmentsEnabled=false)"
            phase      = "rollout"
            command    = "GET /api/v1/appointments?search="
            validation = "HTTP 200; AppointmentsEnabled=true ise SharedSearchLookupEnabled bu yolda etkisizdir."
        },
        [ordered]@{
            step       = "payments-search-enabled"
            label      = "PaymentsSearchLookupEnabled=true (list + report + export Strategy B lookup)"
            phase      = "rollout"
            command    = "QueryReadModels__PaymentsSearchLookupEnabled=true -> single instance restart"
            overrides  = (Get-CqrsSharedSearchLookupFlagOverrides -SharedSearchLookupEnabled $true -PaymentsSearchLookupEnabled $true)
            validation = "Startup log PaymentsSearchLookupEnabled=True; payment list/report/export smoke pass."
        },
        [ordered]@{
            step       = "smoke-payments-list"
            label      = "Payment list search smoke (separate searchClientIds + searchPetIds)"
            phase      = "rollout"
            command    = "GET /api/v1/payments?search="
            validation = "HTTP 200; no Command DB fallback when read-models empty."
        },
        [ordered]@{
            step       = "smoke-payments-report"
            label      = "Payment report search smoke"
            phase      = "rollout"
            command    = "GET /api/v1/reports/payments?search="
            validation = "HTTP 200; total/totalAmount manual parity spot-check recommended."
        },
        [ordered]@{
            step       = "smoke-payments-export-csv"
            label      = "Payment CSV export search smoke"
            phase      = "rollout"
            command    = "GET /api/v1/reports/payments/export?search="
            validation = "HTTP 200; row/column shape unchanged; MaxExportRows enforced."
        },
        [ordered]@{
            step       = "smoke-payments-export-xlsx"
            label      = "Payment XLSX export search smoke"
            phase      = "rollout"
            command    = "GET /api/v1/reports/payments/export-xlsx?search="
            validation = "HTTP 200; row/column shape unchanged; MaxExportRows enforced."
        }
    )
}

function Get-CqrsSharedSearchLookupRollbackSequence {
    return @(
        [ordered]@{
            step       = "payments-search-disabled"
            label      = "PaymentsSearchLookupEnabled=false (partial rollback - financial surfaces first)"
            phase      = "rollback"
            command    = "QueryReadModels__PaymentsSearchLookupEnabled=false -> restart"
            overrides  = (Get-CqrsSharedSearchLookupFlagOverrides -SharedSearchLookupEnabled $true -PaymentsSearchLookupEnabled $false)
            validation = "Payment list/report/export lookup Command DB; SharedSearchLookupEnabled unchanged."
        },
        [ordered]@{
            step       = "shared-search-disabled"
            label      = "SharedSearchLookupEnabled=false (clinical + appointments lookup Command DB)"
            phase      = "rollback"
            command    = "QueryReadModels__SharedSearchLookupEnabled=false -> restart"
            overrides  = (Get-CqrsSharedSearchLookupFlagOverrides -SharedSearchLookupEnabled $false -PaymentsSearchLookupEnabled $false)
            validation = "All search lookup paths Command DB; production default behavior restored."
        },
        [ordered]@{
            step       = "restart"
            label      = "API restart (flags are IOptions startup binding)"
            phase      = "rollback"
            command    = "Single instance restart/deploy after each flag change"
            validation = "Startup log shows expected flag values."
        },
        [ordered]@{
            step       = "health-check"
            label      = "Health verification after rollback"
            phase      = "rollback"
            command    = "GET /health/ready"
            validation = "overall Healthy; client-projection + pet-projection healthy."
        }
    )
}

function Get-CqrsSharedSearchLookupFlagCombinationMatrix {
    return @(
        [ordered]@{
            sharedSearchLookupEnabled   = $false
            paymentsSearchLookupEnabled = $false
            clinicalLookup              = "Command DB (ListSearchPetIds / client+pet specs)"
            appointmentsLookup          = "Command DB (AppointmentsEnabled=false path)"
            paymentsLookup              = "Command DB (ClientsByTenantTextSearchSpec + PetsByTenantTextFieldsSearchSpec)"
            productionDefault           = $true
        },
        [ordered]@{
            sharedSearchLookupEnabled   = $true
            paymentsSearchLookupEnabled = $false
            clinicalLookup              = "Query DB PetReadModels (ResolvePetIdsByTextSearchAsync)"
            appointmentsLookup          = "Query DB pet lookup when AppointmentsEnabled=false"
            paymentsLookup              = "Command DB (unchanged)"
            productionDefault           = $false
        },
        [ordered]@{
            sharedSearchLookupEnabled   = $false
            paymentsSearchLookupEnabled = $true
            clinicalLookup              = "Command DB"
            appointmentsLookup          = "Command DB"
            paymentsLookup              = "Query DB ClientReadModels + PetReadModels (Strategy B, separate id sets)"
            productionDefault           = $false
            note                        = "Not recommended alone: enable Shared first for staged parity; payment-only rollback supported."
        },
        [ordered]@{
            sharedSearchLookupEnabled   = $true
            paymentsSearchLookupEnabled = $true
            clinicalLookup              = "Query DB PetReadModels"
            appointmentsLookup          = "Query DB pet lookup (Command path only)"
            paymentsLookup              = "Query DB ClientReadModels + PetReadModels"
            productionDefault           = $false
            note                        = "Full 12D rollout target state after preconditions + smoke."
        }
    )
}

function Get-CqrsSharedSearchLookupRolloutDocumentation {
    return [ordered]@{
        preconditions = Get-CqrsSharedSearchLookupPreconditionSequence
        rollout       = Get-CqrsSharedSearchLookupRolloutSequence
        rollback      = Get-CqrsSharedSearchLookupRollbackSequence
        flagMatrix    = Get-CqrsSharedSearchLookupFlagCombinationMatrix
        noFallback    = "Query DB empty/stale + flag true => silent missing search results. No automatic Command DB fallback."
        rollbackNote  = "Fastest rollback: set flag false + restart. Payment flag may be disabled independently."
        blastRadius   = @(
            "SharedSearchLookupEnabled: examinations, treatments, vaccinations, hospitalizations, lab-results, prescriptions, appointments (Command DB list path)."
            "PaymentsSearchLookupEnabled: payment list, payment report, CSV export, XLSX export."
        )
    }
}

function Get-CqrsSharedSearchLookupHealthSnapshot {
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

        $clientEntry = $health.results.'client-projection'
        $petEntry = $health.results.'pet-projection'

        return [ordered]@{
            baseUrl       = $normalizedBaseUrl
            overallStatus = [string]$health.status
            clientProjection = @{
                status = [string]$clientEntry.status
                data   = $clientEntry.data
            }
            petProjection = @{
                status = [string]$petEntry.status
                data   = $petEntry.data
            }
            querySqlStatus = [string]$health.results.'query-sql'.status
        }
    }
    finally {
        Disable-CqrsLoadLocalhostTlsBypass -TlsState $tlsState
    }
}

function Test-CqrsSharedSearchLookupPreconditionHealth {
    param(
        [Parameter(Mandatory = $true)]
        $Snapshot
    )

    $clientData = $Snapshot.clientProjection.data
    $petData = $Snapshot.petProjection.data

    $clientExpectation = Test-CqrsClientProjectionHealthExpectation `
        -Snapshot @{
            projectionEnabled = $clientData.projectionEnabled
            clientsReadEnabled = $clientData.clientsReadEnabled
            pendingCount = [int]$clientData.pendingCount
            retryWaitingCount = [int]$clientData.retryWaitingCount
            deadLetterCount = [int]$clientData.deadLetterCount
            oldestPendingAgeSeconds = [double]$clientData.oldestPendingAgeSeconds
        } `
        -ExpectedLevel "Healthy"

    $petExpectation = Test-CqrsPetProjectionHealthExpectation `
        -Snapshot @{
            projectionEnabled = $petData.projectionEnabled
            petsReadEnabled = $petData.petsReadEnabled
            pendingCount = [int]$petData.pendingCount
            retryWaitingCount = [int]$petData.retryWaitingCount
            deadLetterCount = [int]$petData.deadLetterCount
            oldestPendingAgeSeconds = [double]$petData.oldestPendingAgeSeconds
        } `
        -ExpectedLevel "Healthy"

    $overallHealthy = ([string]$Snapshot.overallStatus -eq "Healthy")

    return [ordered]@{
        passed           = ($clientExpectation.passed -and $petExpectation.passed -and $overallHealthy)
        overallHealthy   = $overallHealthy
        clientExpectation = $clientExpectation
        petExpectation   = $petExpectation
    }
}

function Get-CqrsSharedSearchLookupSmokeRequests {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ClinicId,
        [string]$SearchTerm = "smoke",
        [int]$ReportDaysBack = 30
    )

    $encodedSearch = [System.Uri]::EscapeDataString($SearchTerm)
    $toUtc = (Get-Date).ToUniversalTime()
    $fromUtc = $toUtc.AddDays(-1 * $ReportDaysBack)
    $fromIso = $fromUtc.ToString("o")
    $toIso = $toUtc.ToString("o")
    $reportQuery = ("fromUtc={0}&toUtc={1}&clinicId={2}&search={3}" -f `
        [System.Uri]::EscapeDataString($fromIso), `
        [System.Uri]::EscapeDataString($toIso), `
        $ClinicId, `
        $encodedSearch)
    $listQuery = ("Page=1&PageSize=5&clinicId={0}&search={1}" -f $ClinicId, $encodedSearch)

    return @(
        @{ group = "clinical"; name = "examinations-list"; method = "GET"; path = "/api/v1/examinations?$listQuery"; expect = 200 },
        @{ group = "clinical"; name = "treatments-list"; method = "GET"; path = "/api/v1/treatments?$listQuery"; expect = 200 },
        @{ group = "clinical"; name = "vaccinations-list"; method = "GET"; path = "/api/v1/vaccinations?$listQuery"; expect = 200 },
        @{ group = "clinical"; name = "hospitalizations-list"; method = "GET"; path = "/api/v1/hospitalizations?$listQuery"; expect = 200 },
        @{ group = "clinical"; name = "lab-results-list"; method = "GET"; path = "/api/v1/lab-results?$listQuery"; expect = 200 },
        @{ group = "clinical"; name = "prescriptions-list"; method = "GET"; path = "/api/v1/prescriptions?$listQuery"; expect = 200 },
        @{ group = "appointments"; name = "appointments-list"; method = "GET"; path = "/api/v1/appointments?$listQuery"; expect = 200 },
        @{ group = "payments"; name = "payments-list"; method = "GET"; path = "/api/v1/payments?$listQuery"; expect = 200 },
        @{ group = "payments"; name = "payments-report"; method = "GET"; path = "/api/v1/reports/payments?$reportQuery"; expect = 200 },
        @{ group = "payments"; name = "payments-export-csv"; method = "GET"; path = "/api/v1/reports/payments/export?$reportQuery"; expect = 200; accept = "text/csv" },
        @{ group = "payments"; name = "payments-export-xlsx"; method = "GET"; path = "/api/v1/reports/payments/export-xlsx?$reportQuery"; expect = 200; accept = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
    )
}

function Invoke-CqrsSharedSearchLookupSmokeGroup {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$TokenFile,
        [Parameter(Mandatory = $true)]
        [ValidateSet("clinical", "appointments", "payments", "all")]
        [string]$Group,
        [string]$SearchTerm = "smoke"
    )

    $resolvedTokenFile = Resolve-CqrsLoadTokenFile -TokenFile $TokenFile
    $accessToken = Get-CqrsStagedAccessToken -TokenFile $resolvedTokenFile
    $tokens = Get-Content -LiteralPath $resolvedTokenFile -Raw | ConvertFrom-Json
    $tokenEntry = $tokens | Where-Object { $_.slot -eq "01" } | Select-Object -First 1
    $clinicId = [string]$tokenEntry.clinicId

    if ([string]::IsNullOrWhiteSpace($clinicId)) {
        throw "Token slot 01 clinicId is required for smoke requests."
    }

    $requests = Get-CqrsSharedSearchLookupSmokeRequests -ClinicId $clinicId -SearchTerm $SearchTerm
    if ($Group -ne "all") {
        $requests = @($requests | Where-Object { $_.group -eq $Group })
    }

    $results = New-Object System.Collections.Generic.List[object]
    foreach ($request in $requests) {
        $response = Invoke-CqrsStagedApiRequest `
            -BaseUrl $BaseUrl `
            -Method $request.method `
            -Path $request.path `
            -AccessToken $accessToken

        $passed = ([int]$response.statusCode -eq [int]$request.expect)
        $results.Add([ordered]@{
                name       = $request.name
                group      = $request.group
                method     = $request.method
                path       = $request.path
                statusCode = $response.statusCode
                expected   = $request.expect
                passed     = $passed
            }) | Out-Null
    }

    $failed = @($results | Where-Object { -not $_.passed })
    return [ordered]@{
        group   = $Group
        passed  = ($failed.Count -eq 0)
        total   = $results.Count
        failed  = $failed.Count
        items   = $results
    }
}

function Test-CqrsSharedSearchLookupFlagExpectation {
    param(
        [bool]$ExpectSharedEnabled,
        [bool]$ExpectPaymentsEnabled
    )

    $snapshot = Get-CqrsSharedSearchLookupFlagSnapshotFromEnvironment
    $sharedOk = ($snapshot.SharedSearchLookupEnabled -eq $ExpectSharedEnabled)
    $paymentsOk = ($snapshot.PaymentsSearchLookupEnabled -eq $ExpectPaymentsEnabled)

    return [ordered]@{
        passed = ($sharedOk -and $paymentsOk)
        expected = @{
            SharedSearchLookupEnabled   = $ExpectSharedEnabled
            PaymentsSearchLookupEnabled = $ExpectPaymentsEnabled
        }
        actual = @{
            SharedSearchLookupEnabled   = $snapshot.SharedSearchLookupEnabled
            PaymentsSearchLookupEnabled = $snapshot.PaymentsSearchLookupEnabled
        }
    }
}
