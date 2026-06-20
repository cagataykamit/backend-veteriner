#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-12C-7 pet read-model rollout / rollback acceptance - deterministic (CI-safe) tests.
.DESCRIPTION
  No live API / SQL Server / token required. Validates only script parse, rollout/rollback sequence
  definitions, flag override invariants, the pet health expectation matrix, presence of the
  DbMigrator backfill command, and doc/secret scanning. Runs in the CI pipeline.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsPetRolloutAcceptanceCommon.ps1")

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

# --- 1. Script parse (self + common) ---
$commonPath = Join-Path $PSScriptRoot "CqrsPetRolloutAcceptanceCommon.ps1"
$selfPath = Join-Path $PSScriptRoot "Test-CqrsPetRolloutAcceptance.ps1"
foreach ($path in @($commonPath, $selfPath)) {
    $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)
    $name = Split-Path $path -Leaf
    Add-Result -Name "ps-parse:$name" -Passed ($null -eq $errors -or $errors.Count -eq 0) `
        -Detail (($errors | ForEach-Object { $_.ToString() }) -join "; ")
}

# --- 2. Rollout sequence definition ---
$rollout = Get-CqrsPetRolloutSequence
$rolloutSteps = @($rollout | ForEach-Object { $_.step })
Add-Result -Name "rollout-sequence-count" -Passed ($rollout.Count -eq 6)
Add-Result -Name "rollout-starts-query-migration" -Passed ($rolloutSteps[0] -eq "query-migration")
Add-Result -Name "rollout-includes-backfill" -Passed ($rolloutSteps -contains "backfill")
Add-Result -Name "rollout-includes-parity-check" -Passed ($rolloutSteps -contains "parity-check")
Add-Result -Name "rollout-includes-health-check" -Passed ($rolloutSteps -contains "health-check")
Add-Result -Name "rollout-includes-pets-enabled" -Passed ($rolloutSteps -contains "pets-enabled")
Add-Result -Name "rollout-ends-smoke" -Passed ($rolloutSteps[-1] -eq "smoke")

# Safety invariant: backfill + parity must come BEFORE enabling PetsEnabled.
$backfillIndex = [array]::IndexOf($rolloutSteps, "backfill")
$parityIndex = [array]::IndexOf($rolloutSteps, "parity-check")
$enableIndex = [array]::IndexOf($rolloutSteps, "pets-enabled")
Add-Result -Name "rollout-backfill-before-enable" -Passed ($backfillIndex -lt $enableIndex)
Add-Result -Name "rollout-parity-before-enable" -Passed ($parityIndex -lt $enableIndex)

# --- 3. Rollback sequence definition ---
$rollback = Get-CqrsPetRollbackSequence
$rollbackSteps = @($rollback | ForEach-Object { $_.step })
Add-Result -Name "rollback-sequence-count" -Passed ($rollback.Count -eq 4)
Add-Result -Name "rollback-starts-pets-disabled" -Passed ($rollbackSteps[0] -eq "pets-disabled")
Add-Result -Name "rollback-includes-restart" -Passed ($rollbackSteps -contains "restart")
Add-Result -Name "rollback-includes-health-check" -Passed ($rollbackSteps -contains "health-check")
Add-Result -Name "rollback-includes-projection-disabled" -Passed ($rollbackSteps -contains "projection-disabled")

# --- 4. Flag override invariants ---
$enableOverride = Get-CqrsPetReadFlagOverrides -Enabled $true
$disableOverride = Get-CqrsPetReadFlagOverrides -Enabled $false
Add-Result -Name "flag-enable-true" -Passed ($enableOverride.QueryReadModels__PetsEnabled -eq "True")
Add-Result -Name "flag-disable-false" -Passed ($disableOverride.QueryReadModels__PetsEnabled -eq "False")

$pauseOverride = Get-CqrsPetProjectionDisabledOverrides -PetsReadEnabled $false
Add-Result -Name "pause-keeps-read-flag-false" -Passed ($pauseOverride.QueryReadModels__PetsEnabled -eq "False")
Add-Result -Name "pause-disables-projection" -Passed ($pauseOverride.PetProjection__Enabled -eq "False")

# Rollback documentation must instruct keeping the projector on.
$rollbackDoc = Get-CqrsPetRollbackDocumentation
Add-Result -Name "rollback-doc-projector-stays-on" -Passed (
    $rollbackDoc.projectorDuringReadRollback -match "stay true")
Add-Result -Name "rollback-doc-backfill-exit-0" -Passed ($rollbackDoc.backfillExitCodes.success -match "0")
Add-Result -Name "rollback-doc-backfill-exit-2" -Passed ($rollbackDoc.backfillExitCodes.parityMismatch -match "2")
Add-Result -Name "rollback-doc-backfill-exit-1" -Passed ($rollbackDoc.backfillExitCodes.exception -match "1")
Add-Result -Name "rollback-doc-no-fallback" -Passed ($rollbackDoc.emptyReadModelNoFallback -match "no Command DB fallback")
Add-Result -Name "rollback-doc-rename-limit" -Passed ($rollbackDoc.renamePropagationLimit -match "ClientFullName")

# --- 5. Health expectation matrix (parity with PetProjectionHealthEvaluator) ---
$healthCases = @(
    @{ name = "proj-on-clean-healthy"; expected = "Healthy"; snapshot = @{
            projectionEnabled = $true; petsReadEnabled = $true
            pendingCount = 0; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 0 } },
    @{ name = "dead-letter-unhealthy"; expected = "Unhealthy"; snapshot = @{
            projectionEnabled = $true; petsReadEnabled = $true
            pendingCount = 0; retryWaitingCount = 0; deadLetterCount = 1; oldestPendingAgeSeconds = 0 } },
    @{ name = "proj-off-read-on-pending-unhealthy"; expected = "Unhealthy"; snapshot = @{
            projectionEnabled = $false; petsReadEnabled = $true
            pendingCount = 3; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 1 } },
    @{ name = "proj-off-read-on-no-pending-degraded"; expected = "Degraded"; snapshot = @{
            projectionEnabled = $false; petsReadEnabled = $true
            pendingCount = 0; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 0 } },
    @{ name = "proj-off-read-off-no-pending-healthy"; expected = "Healthy"; snapshot = @{
            projectionEnabled = $false; petsReadEnabled = $false
            pendingCount = 0; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 0 } },
    @{ name = "proj-off-read-off-old-pending-unhealthy"; expected = "Unhealthy"; snapshot = @{
            projectionEnabled = $false; petsReadEnabled = $false
            pendingCount = 9; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 99 } },
    @{ name = "pending-age-degraded"; expected = "Degraded"; snapshot = @{
            projectionEnabled = $true; petsReadEnabled = $true
            pendingCount = 1; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 15 } },
    @{ name = "pending-age-unhealthy"; expected = "Unhealthy"; snapshot = @{
            projectionEnabled = $true; petsReadEnabled = $true
            pendingCount = 1; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 45 } },
    @{ name = "retry-waiting-degraded"; expected = "Degraded"; snapshot = @{
            projectionEnabled = $true; petsReadEnabled = $true
            pendingCount = 0; retryWaitingCount = 2; deadLetterCount = 0; oldestPendingAgeSeconds = 0 } }
)

foreach ($case in $healthCases) {
    $evaluation = Test-CqrsPetProjectionHealthExpectation -Snapshot $case.snapshot -ExpectedLevel $case.expected
    Add-Result -Name ("health-expectation:" + $case.name) -Passed $evaluation.passed `
        -Detail ("expected=" + $evaluation.expected + " actual=" + $evaluation.actual)
}

# --- 6. DbMigrator backfill command present in source (no build needed) ---
$dbMigratorProgram = Join-Path $repoRoot "src\Backend.Veteriner.DbMigrator\Program.cs"
if (Test-Path -LiteralPath $dbMigratorProgram) {
    $programText = Get-Content -LiteralPath $dbMigratorProgram -Raw
    Add-Result -Name "dbmigrator-backfill-command-exists" -Passed (
        $programText -match "backfill-pet-projections")
    Add-Result -Name "dbmigrator-backfill-tenant-option" -Passed ($programText -match "--tenant")
    Add-Result -Name "dbmigrator-backfill-exit-2-parity" -Passed ($programText -match "return 2")
}
else {
    Add-Result -Name "dbmigrator-program-exists" -Passed $false -Detail "Program.cs not found."
}

# --- 7. Doc verification (12C-7) ---
$docPath = Join-Path $repoRoot "docs\cqrs\cqrs-12c-7-pet-read-model-rollout-acceptance.md"
Add-Result -Name "doc-exists" -Passed (Test-Path -LiteralPath $docPath)
if (Test-Path -LiteralPath $docPath) {
    $docText = Get-Content -LiteralPath $docPath -Raw
    Add-Result -Name "doc-rollout-backfill" -Passed ($docText -match "backfill-pet-projections")
    Add-Result -Name "doc-rollout-enable-flag" -Passed ($docText -match "PetsEnabled")
    Add-Result -Name "doc-rollback-disable" -Passed ($docText -match "PetsEnabled.{0,6}false")
    Add-Result -Name "doc-default-false" -Passed (($docText -match "default") -and ($docText -match "false"))
    Add-Result -Name "doc-backfill-exit-codes" -Passed ($docText -match "exit code 2")
    Add-Result -Name "doc-no-fallback" -Passed ($docText -match "fallback yok")
    Add-Result -Name "doc-rename-propagation" -Passed ($docText -match "ClientFullName")
    Add-Result -Name "doc-no-secret-patterns" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $docText))
}

# --- 8. Acceptance runbook pet section ---
$runbookPath = Join-Path $repoRoot "docs\cqrs\cqrs-acceptance-runbook.md"
if (Test-Path -LiteralPath $runbookPath) {
    $runbookText = Get-Content -LiteralPath $runbookPath -Raw
    Add-Result -Name "runbook-backfill-command" -Passed ($runbookText -match "backfill-pet-projections")
    Add-Result -Name "runbook-pet-test-listed" -Passed ($runbookText -match "Test-CqrsPetRolloutAcceptance")
    Add-Result -Name "runbook-no-secret-patterns" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $runbookText))
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
