#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-12D-10 shared + payments search lookup rollout acceptance (CI-safe + optional live smoke).

.DESCRIPTION
  Default (no switches): deterministic CI-safe checks — script parse, rollout/rollback sequences,
  flag invariants, DbMigrator backfill commands, doc presence, secret scanning.

  -Apply: validates environment flag snapshot + /health/ready preconditions + minimum HTTP smoke
  for clinical lists, appointments, and payment list/report/export surfaces. Does NOT mutate env
  or restart API (operator responsibility).

.PARAMETER BaseUrl
  Target API base URL (live smoke only). Default https://localhost:7173

.PARAMETER TokenFile
  Load-test token JSON (slot 01 clinicId required for smoke). Default resolved by CqrsLoadCommon.

.PARAMETER Step
  Live validation step when -Apply is set.

.PARAMETER SearchTerm
  Benign search term for smoke requests. Default "smoke". Avoid PII in this parameter.

.PARAMETER ShowPlan
  Print rollout / rollback / precondition plan as JSON.

.PARAMETER Help
  Show usage and exit 0.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1 -Help

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1 -ShowPlan

.EXAMPLE
  $env:QueryReadModels__SharedSearchLookupEnabled = "True"
  $env:QueryReadModels__PaymentsSearchLookupEnabled = "False"
  powershell -NoProfile -ExecutionPolicy Bypass -File tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1 `
    -Apply -Step smoke-clinical -BaseUrl https://localhost:7173 -TokenFile tests/load/tokens/load-test-tokens.json
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://localhost:7173",

    [string]$TokenFile,

    [ValidateSet(
        "precondition-health",
        "shared-search-enabled",
        "smoke-clinical",
        "smoke-appointments",
        "payments-search-enabled",
        "smoke-payments-list",
        "smoke-payments-report",
        "smoke-payments-export-csv",
        "smoke-payments-export-xlsx",
        "smoke-payments-all",
        "rollback-payments-search",
        "rollback-shared-search"
    )]
    [string]$Step = "precondition-health",

    [string]$SearchTerm = "smoke",

    [switch]$ShowPlan,

    [switch]$Apply,

    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsSharedSearchLookupRolloutAcceptanceCommon.ps1")

if ($Help.IsPresent) {
    Get-Help -Name $MyInvocation.MyCommand.Path -Full | Out-String | Write-Output
    exit 0
}

if ($ShowPlan.IsPresent) {
    $plan = Get-CqrsSharedSearchLookupRolloutDocumentation
    $json = $plan | ConvertTo-Json -Depth 8
    if (Test-CqrsStagedOutputContainsSecrets -Text $json) {
        throw "Plan output contains secret-like content."
    }
    Write-Output $json
    exit 0
}

if ($Apply.IsPresent) {
    $normalizedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
    $startedAtUtc = [DateTime]::UtcNow
    $report = [ordered]@{
        phase            = "CQRS-12D-10"
        dryRun           = $false
        startedAtUtc     = $startedAtUtc.ToString("o")
        baseUrl          = $normalizedBaseUrl
        step             = $Step
        hostAllowed      = (Test-CqrsStagedHostAllowed -BaseUrl $normalizedBaseUrl)
        flagSnapshot     = Get-CqrsSharedSearchLookupFlagSnapshotFromEnvironment
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

    if (-not $report.hostAllowed) {
        $report.warnings.Add("BaseUrl must be localhost or CQRS_LOAD_ALLOWED_HOST.") | Out-Null
    }

    $healthSnapshot = Get-CqrsSharedSearchLookupHealthSnapshot -BaseUrl $normalizedBaseUrl
    Add-Step -Name "health-snapshot" -Passed $true -Detail @{
        overallStatus     = $healthSnapshot.overallStatus
        clientProjection  = $healthSnapshot.clientProjection.status
        petProjection     = $healthSnapshot.petProjection.status
        querySqlStatus    = $healthSnapshot.querySqlStatus
    }

    switch ($Step) {
        "precondition-health" {
            $healthEval = Test-CqrsSharedSearchLookupPreconditionHealth -Snapshot $healthSnapshot
            Add-Step -Name "precondition-health" -Passed $healthEval.passed -Detail $healthEval
            if (-not $healthEval.passed) {
                throw "Precondition health check failed (client/pet projection or overall not Healthy)."
            }
        }
        "shared-search-enabled" {
            $flagEval = Test-CqrsSharedSearchLookupFlagExpectation -ExpectSharedEnabled $true -ExpectPaymentsEnabled $false
            Add-Step -Name "flag-shared-enabled" -Passed $flagEval.passed -Detail $flagEval
            if (-not $flagEval.passed) {
                throw "Expected QueryReadModels__SharedSearchLookupEnabled=true and PaymentsSearchLookupEnabled=false in environment."
            }

            $clinical = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "clinical" -SearchTerm $SearchTerm
            Add-Step -Name "smoke-clinical" -Passed $clinical.passed -Detail $clinical

            $appointments = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "appointments" -SearchTerm $SearchTerm
            Add-Step -Name "smoke-appointments" -Passed $appointments.passed -Detail $appointments

            if (-not $clinical.passed -or -not $appointments.passed) {
                throw "Clinical or appointments smoke failed after shared flag enable."
            }
        }
        "smoke-clinical" {
            $clinical = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "clinical" -SearchTerm $SearchTerm
            Add-Step -Name "smoke-clinical" -Passed $clinical.passed -Detail $clinical
            if (-not $clinical.passed) { throw "Clinical smoke failed." }
        }
        "smoke-appointments" {
            $appointments = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "appointments" -SearchTerm $SearchTerm
            Add-Step -Name "smoke-appointments" -Passed $appointments.passed -Detail $appointments
            if (-not $appointments.passed) { throw "Appointments smoke failed." }
        }
        "payments-search-enabled" {
            $flagEval = Test-CqrsSharedSearchLookupFlagExpectation -ExpectSharedEnabled $true -ExpectPaymentsEnabled $true
            Add-Step -Name "flag-payments-enabled" -Passed $flagEval.passed -Detail $flagEval
            if (-not $flagEval.passed) {
                throw "Expected both SharedSearchLookupEnabled=true and PaymentsSearchLookupEnabled=true."
            }

            $payments = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "payments" -SearchTerm $SearchTerm
            Add-Step -Name "smoke-payments-all" -Passed $payments.passed -Detail $payments
            if (-not $payments.passed) { throw "Payment smoke failed after payments flag enable." }
        }
        "smoke-payments-list" {
            $result = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "payments" -SearchTerm $SearchTerm
            $listOnly = @($result.items | Where-Object { $_.name -eq "payments-list" })
            $passed = ($listOnly.Count -eq 1 -and $listOnly[0].passed)
            Add-Step -Name "smoke-payments-list" -Passed $passed -Detail $listOnly
            if (-not $passed) { throw "Payment list smoke failed." }
        }
        "smoke-payments-report" {
            $result = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "payments" -SearchTerm $SearchTerm
            $item = @($result.items | Where-Object { $_.name -eq "payments-report" })
            $passed = ($item.Count -eq 1 -and $item[0].passed)
            Add-Step -Name "smoke-payments-report" -Passed $passed -Detail $item
            if (-not $passed) { throw "Payment report smoke failed." }
        }
        "smoke-payments-export-csv" {
            $result = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "payments" -SearchTerm $SearchTerm
            $item = @($result.items | Where-Object { $_.name -eq "payments-export-csv" })
            $passed = ($item.Count -eq 1 -and $item[0].passed)
            Add-Step -Name "smoke-payments-export-csv" -Passed $passed -Detail $item
            if (-not $passed) { throw "Payment CSV export smoke failed." }
        }
        "smoke-payments-export-xlsx" {
            $result = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "payments" -SearchTerm $SearchTerm
            $item = @($result.items | Where-Object { $_.name -eq "payments-export-xlsx" })
            $passed = ($item.Count -eq 1 -and $item[0].passed)
            Add-Step -Name "smoke-payments-export-xlsx" -Passed $passed -Detail $item
            if (-not $passed) { throw "Payment XLSX export smoke failed." }
        }
        "smoke-payments-all" {
            $payments = Invoke-CqrsSharedSearchLookupSmokeGroup `
                -BaseUrl $normalizedBaseUrl -TokenFile $TokenFile -Group "payments" -SearchTerm $SearchTerm
            Add-Step -Name "smoke-payments-all" -Passed $payments.passed -Detail $payments
            if (-not $payments.passed) { throw "Payment smoke group failed." }
        }
        "rollback-payments-search" {
            $flagEval = Test-CqrsSharedSearchLookupFlagExpectation -ExpectSharedEnabled $true -ExpectPaymentsEnabled $false
            Add-Step -Name "flag-payments-disabled" -Passed $flagEval.passed -Detail $flagEval
            if (-not $flagEval.passed) {
                throw "Expected PaymentsSearchLookupEnabled=false (partial payment rollback)."
            }
        }
        "rollback-shared-search" {
            $flagEval = Test-CqrsSharedSearchLookupFlagExpectation -ExpectSharedEnabled $false -ExpectPaymentsEnabled $false
            Add-Step -Name "flag-shared-disabled" -Passed $flagEval.passed -Detail $flagEval
            if (-not $flagEval.passed) {
                throw "Expected both search lookup flags false (full rollback to production default)."
            }
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
    if (-not $report.passed) { exit 1 }
    exit 0
}

# --- CI-safe deterministic acceptance (default) ---
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

$commonPath = Join-Path $PSScriptRoot "CqrsSharedSearchLookupRolloutAcceptanceCommon.ps1"
$selfPath = Join-Path $PSScriptRoot "Test-CqrsSharedSearchLookupRolloutAcceptance.ps1"
foreach ($path in @($commonPath, $selfPath)) {
    $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)
    $name = Split-Path $path -Leaf
    Add-Result -Name "ps-parse:$name" -Passed ($null -eq $errors -or $errors.Count -eq 0) `
        -Detail (($errors | ForEach-Object { $_.ToString() }) -join "; ")
}

$preconditions = Get-CqrsSharedSearchLookupPreconditionSequence
$preconditionSteps = @($preconditions | ForEach-Object { $_.step })
Add-Result -Name "precondition-count" -Passed ($preconditions.Count -eq 7)
Add-Result -Name "precondition-starts-command-migration" -Passed ($preconditionSteps[0] -eq "command-migration")
Add-Result -Name "precondition-includes-backfill-client" -Passed ($preconditionSteps -contains "backfill-client")
Add-Result -Name "precondition-includes-backfill-pet" -Passed ($preconditionSteps -contains "backfill-pet")
Add-Result -Name "precondition-includes-parity-client" -Passed ($preconditionSteps -contains "parity-client")
Add-Result -Name "precondition-includes-parity-pet" -Passed ($preconditionSteps -contains "parity-pet")
Add-Result -Name "precondition-ends-health" -Passed ($preconditionSteps[-1] -eq "health-check")

$backfillClientIndex = [array]::IndexOf($preconditionSteps, "backfill-client")
$backfillPetIndex = [array]::IndexOf($preconditionSteps, "backfill-pet")
$healthIndex = [array]::IndexOf($preconditionSteps, "health-check")
Add-Result -Name "precondition-backfills-before-health" -Passed (
    $backfillClientIndex -lt $healthIndex -and $backfillPetIndex -lt $healthIndex)

$rollout = Get-CqrsSharedSearchLookupRolloutSequence
$rolloutSteps = @($rollout | ForEach-Object { $_.step })
Add-Result -Name "rollout-sequence-count" -Passed ($rollout.Count -eq 8)
Add-Result -Name "rollout-starts-shared-enabled" -Passed ($rolloutSteps[0] -eq "shared-search-enabled")
Add-Result -Name "rollout-includes-clinical-smoke" -Passed ($rolloutSteps -contains "smoke-clinical")
Add-Result -Name "rollout-includes-appointments-smoke" -Passed ($rolloutSteps -contains "smoke-appointments")
Add-Result -Name "rollout-includes-payments-enabled" -Passed ($rolloutSteps -contains "payments-search-enabled")
Add-Result -Name "rollout-includes-export-xlsx" -Passed ($rolloutSteps -contains "smoke-payments-export-xlsx")

$sharedEnableIndex = [array]::IndexOf($rolloutSteps, "shared-search-enabled")
$paymentsEnableIndex = [array]::IndexOf($rolloutSteps, "payments-search-enabled")
$clinicalSmokeIndex = [array]::IndexOf($rolloutSteps, "smoke-clinical")
Add-Result -Name "rollout-shared-before-payments" -Passed ($sharedEnableIndex -lt $paymentsEnableIndex)
Add-Result -Name "rollout-clinical-smoke-after-shared-enable" -Passed ($clinicalSmokeIndex -gt $sharedEnableIndex)

$rollback = Get-CqrsSharedSearchLookupRollbackSequence
$rollbackSteps = @($rollback | ForEach-Object { $_.step })
Add-Result -Name "rollback-payments-before-shared" -Passed ($rollbackSteps[0] -eq "payments-search-disabled")
Add-Result -Name "rollback-shared-second" -Passed ($rollbackSteps[1] -eq "shared-search-disabled")

$sharedOnly = Get-CqrsSharedSearchLookupFlagOverrides -SharedSearchLookupEnabled $true -PaymentsSearchLookupEnabled $false
$bothOn = Get-CqrsSharedSearchLookupFlagOverrides -SharedSearchLookupEnabled $true -PaymentsSearchLookupEnabled $true
$bothOff = Get-CqrsSharedSearchLookupFlagOverrides -SharedSearchLookupEnabled $false -PaymentsSearchLookupEnabled $false
Add-Result -Name "flag-shared-only" -Passed (
    $sharedOnly.QueryReadModels__SharedSearchLookupEnabled -eq "True" -and
    $sharedOnly.QueryReadModels__PaymentsSearchLookupEnabled -eq "False")
Add-Result -Name "flag-both-on" -Passed (
    $bothOn.QueryReadModels__SharedSearchLookupEnabled -eq "True" -and
    $bothOn.QueryReadModels__PaymentsSearchLookupEnabled -eq "True")
Add-Result -Name "flag-both-off" -Passed (
    $bothOff.QueryReadModels__SharedSearchLookupEnabled -eq "False" -and
    $bothOff.QueryReadModels__PaymentsSearchLookupEnabled -eq "False")

$matrix = Get-CqrsSharedSearchLookupFlagCombinationMatrix
Add-Result -Name "flag-matrix-count" -Passed ($matrix.Count -eq 4)
Add-Result -Name "flag-matrix-production-default-row" -Passed (
    @($matrix | Where-Object { $_.productionDefault -eq $true }).Count -eq 1)

$smokeRequests = Get-CqrsSharedSearchLookupSmokeRequests -ClinicId "00000000-0000-0000-0000-000000000001"
Add-Result -Name "smoke-endpoint-count" -Passed ($smokeRequests.Count -eq 11)
Add-Result -Name "smoke-includes-export-xlsx" -Passed (
    @($smokeRequests | Where-Object { $_.name -eq "payments-export-xlsx" }).Count -eq 1)

$dbMigratorProgram = Join-Path $repoRoot "src\Backend.Veteriner.DbMigrator\Program.cs"
if (Test-Path -LiteralPath $dbMigratorProgram) {
    $programText = Get-Content -LiteralPath $dbMigratorProgram -Raw
    Add-Result -Name "dbmigrator-backfill-client" -Passed ($programText -match "backfill-client-projections")
    Add-Result -Name "dbmigrator-backfill-pet" -Passed ($programText -match "backfill-pet-projections")
    Add-Result -Name "dbmigrator-migrate-query" -Passed ($programText -match "migrate-query")
}
else {
    Add-Result -Name "dbmigrator-program-exists" -Passed $false
}

$docPath = Join-Path $repoRoot "docs\cqrs\cqrs-12d-10-shared-search-lookup-rollout-acceptance.md"
Add-Result -Name "doc-exists" -Passed (Test-Path -LiteralPath $docPath)
if (Test-Path -LiteralPath $docPath) {
    $docText = Get-Content -LiteralPath $docPath -Raw
    Add-Result -Name "doc-shared-flag" -Passed ($docText -match "SharedSearchLookupEnabled")
    Add-Result -Name "doc-payments-flag" -Passed ($docText -match "PaymentsSearchLookupEnabled")
    Add-Result -Name "doc-no-fallback" -Passed ($docText -match "fallback" -and $docText -match "yok|No automatic")
    Add-Result -Name "doc-default-false" -Passed (($docText -match "default") -and ($docText -match "false"))
    Add-Result -Name "doc-rollout-order" -Passed ($docText -match "SharedSearchLookupEnabled=true" -and $docText -match "PaymentsSearchLookupEnabled=true")
    Add-Result -Name "doc-no-secret-patterns" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $docText))
}

$failed = @($results | Where-Object { -not $_.passed })
$summary = [ordered]@{
    passed = ($failed.Count -eq 0)
    total  = $results.Count
    failed = $failed.Count
    items  = $results
}

$summary | ConvertTo-Json -Depth 5
if ($failed.Count -gt 0) {
    exit 1
}
