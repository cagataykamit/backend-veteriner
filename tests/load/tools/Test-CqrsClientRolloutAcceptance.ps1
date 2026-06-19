#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-12B-7 client read-model rollout / rollback acceptance - deterministic (CI-safe) tests.
.DESCRIPTION
  No live API / SQL Server / token required. Validates only script parse, rollout/rollback sequence
  definitions, flag override invariants, the client health expectation matrix, presence of the
  DbMigrator backfill command, and doc/secret scanning. Runs in the CI pipeline.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsClientRolloutAcceptanceCommon.ps1")

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
$commonPath = Join-Path $PSScriptRoot "CqrsClientRolloutAcceptanceCommon.ps1"
$selfPath = Join-Path $PSScriptRoot "Test-CqrsClientRolloutAcceptance.ps1"
foreach ($path in @($commonPath, $selfPath)) {
    $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)
    $name = Split-Path $path -Leaf
    Add-Result -Name "ps-parse:$name" -Passed ($null -eq $errors -or $errors.Count -eq 0) `
        -Detail (($errors | ForEach-Object { $_.ToString() }) -join "; ")
}

# --- 2. Rollout sequence definition ---
$rollout = Get-CqrsClientRolloutSequence
$rolloutSteps = @($rollout | ForEach-Object { $_.step })
Add-Result -Name "rollout-sequence-count" -Passed ($rollout.Count -eq 6)
Add-Result -Name "rollout-starts-query-migration" -Passed ($rolloutSteps[0] -eq "query-migration")
Add-Result -Name "rollout-includes-backfill" -Passed ($rolloutSteps -contains "backfill")
Add-Result -Name "rollout-includes-parity-check" -Passed ($rolloutSteps -contains "parity-check")
Add-Result -Name "rollout-includes-health-check" -Passed ($rolloutSteps -contains "health-check")
Add-Result -Name "rollout-includes-clients-enabled" -Passed ($rolloutSteps -contains "clients-enabled")
Add-Result -Name "rollout-ends-smoke" -Passed ($rolloutSteps[-1] -eq "smoke")

# Safety invariant: backfill + parity must come BEFORE enabling ClientsEnabled.
$backfillIndex = [array]::IndexOf($rolloutSteps, "backfill")
$parityIndex = [array]::IndexOf($rolloutSteps, "parity-check")
$enableIndex = [array]::IndexOf($rolloutSteps, "clients-enabled")
Add-Result -Name "rollout-backfill-before-enable" -Passed ($backfillIndex -lt $enableIndex)
Add-Result -Name "rollout-parity-before-enable" -Passed ($parityIndex -lt $enableIndex)

# --- 3. Rollback sequence definition ---
$rollback = Get-CqrsClientRollbackSequence
$rollbackSteps = @($rollback | ForEach-Object { $_.step })
Add-Result -Name "rollback-sequence-count" -Passed ($rollback.Count -eq 4)
Add-Result -Name "rollback-starts-clients-disabled" -Passed ($rollbackSteps[0] -eq "clients-disabled")
Add-Result -Name "rollback-includes-restart" -Passed ($rollbackSteps -contains "restart")
Add-Result -Name "rollback-includes-health-check" -Passed ($rollbackSteps -contains "health-check")
Add-Result -Name "rollback-includes-projection-disabled" -Passed ($rollbackSteps -contains "projection-disabled")

# --- 4. Flag override invariants ---
$enableOverride = Get-CqrsClientReadFlagOverrides -Enabled $true
$disableOverride = Get-CqrsClientReadFlagOverrides -Enabled $false
Add-Result -Name "flag-enable-true" -Passed ($enableOverride.QueryReadModels__ClientsEnabled -eq "True")
Add-Result -Name "flag-disable-false" -Passed ($disableOverride.QueryReadModels__ClientsEnabled -eq "False")

$pauseOverride = Get-CqrsClientProjectionDisabledOverrides -ClientsReadEnabled $false
Add-Result -Name "pause-keeps-read-flag-false" -Passed ($pauseOverride.QueryReadModels__ClientsEnabled -eq "False")
Add-Result -Name "pause-disables-projection" -Passed ($pauseOverride.ClientProjection__Enabled -eq "False")

# Rollback documentation must instruct keeping the projector on.
$rollbackDoc = Get-CqrsClientRollbackDocumentation
Add-Result -Name "rollback-doc-projector-stays-on" -Passed (
    $rollbackDoc.projectorDuringReadRollback -match "stay true")

# --- 5. Health expectation matrix (parity with ClientProjectionHealthEvaluator) ---
$healthCases = @(
    @{ name = "proj-on-clean-healthy"; expected = "Healthy"; snapshot = @{
            projectionEnabled = $true; clientsReadEnabled = $true
            pendingCount = 0; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 0 } },
    @{ name = "dead-letter-unhealthy"; expected = "Unhealthy"; snapshot = @{
            projectionEnabled = $true; clientsReadEnabled = $true
            pendingCount = 0; retryWaitingCount = 0; deadLetterCount = 1; oldestPendingAgeSeconds = 0 } },
    @{ name = "proj-off-read-on-pending-unhealthy"; expected = "Unhealthy"; snapshot = @{
            projectionEnabled = $false; clientsReadEnabled = $true
            pendingCount = 3; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 1 } },
    @{ name = "proj-off-read-on-no-pending-degraded"; expected = "Degraded"; snapshot = @{
            projectionEnabled = $false; clientsReadEnabled = $true
            pendingCount = 0; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 0 } },
    @{ name = "proj-off-read-off-no-pending-healthy"; expected = "Healthy"; snapshot = @{
            projectionEnabled = $false; clientsReadEnabled = $false
            pendingCount = 0; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 0 } },
    @{ name = "proj-off-read-off-old-pending-unhealthy"; expected = "Unhealthy"; snapshot = @{
            projectionEnabled = $false; clientsReadEnabled = $false
            pendingCount = 9; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 99 } },
    @{ name = "pending-age-degraded"; expected = "Degraded"; snapshot = @{
            projectionEnabled = $true; clientsReadEnabled = $true
            pendingCount = 1; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 15 } },
    @{ name = "pending-age-unhealthy"; expected = "Unhealthy"; snapshot = @{
            projectionEnabled = $true; clientsReadEnabled = $true
            pendingCount = 1; retryWaitingCount = 0; deadLetterCount = 0; oldestPendingAgeSeconds = 45 } },
    @{ name = "retry-waiting-degraded"; expected = "Degraded"; snapshot = @{
            projectionEnabled = $true; clientsReadEnabled = $true
            pendingCount = 0; retryWaitingCount = 2; deadLetterCount = 0; oldestPendingAgeSeconds = 0 } }
)

foreach ($case in $healthCases) {
    $evaluation = Test-CqrsClientProjectionHealthExpectation -Snapshot $case.snapshot -ExpectedLevel $case.expected
    Add-Result -Name ("health-expectation:" + $case.name) -Passed $evaluation.passed `
        -Detail ("expected=" + $evaluation.expected + " actual=" + $evaluation.actual)
}

# --- 6. DbMigrator backfill command present in source (no build needed) ---
$dbMigratorProgram = Join-Path $repoRoot "src\Backend.Veteriner.DbMigrator\Program.cs"
if (Test-Path -LiteralPath $dbMigratorProgram) {
    $programText = Get-Content -LiteralPath $dbMigratorProgram -Raw
    Add-Result -Name "dbmigrator-backfill-command-exists" -Passed (
        $programText -match "backfill-client-projections")
    Add-Result -Name "dbmigrator-backfill-tenant-option" -Passed ($programText -match "--tenant")
}
else {
    Add-Result -Name "dbmigrator-program-exists" -Passed $false -Detail "Program.cs not found."
}

# --- 7. Doc verification (12B-7) ---
$docPath = Join-Path $repoRoot "docs\cqrs\cqrs-12b-7-client-read-model-rollout-acceptance.md"
Add-Result -Name "doc-exists" -Passed (Test-Path -LiteralPath $docPath)
if (Test-Path -LiteralPath $docPath) {
    $docText = Get-Content -LiteralPath $docPath -Raw
    Add-Result -Name "doc-rollout-backfill" -Passed ($docText -match "backfill-client-projections")
    Add-Result -Name "doc-rollout-enable-flag" -Passed ($docText -match "ClientsEnabled")
    Add-Result -Name "doc-rollback-disable" -Passed ($docText -match "ClientsEnabled.{0,6}false")
    Add-Result -Name "doc-default-false" -Passed (($docText -match "default") -and ($docText -match "false"))
    Add-Result -Name "doc-no-secret-patterns" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $docText))
}

# --- 8. Acceptance runbook client section ---
$runbookPath = Join-Path $repoRoot "docs\cqrs\cqrs-acceptance-runbook.md"
if (Test-Path -LiteralPath $runbookPath) {
    $runbookText = Get-Content -LiteralPath $runbookPath -Raw
    Add-Result -Name "runbook-backfill-command" -Passed ($runbookText -match "backfill-client-projections")
    Add-Result -Name "runbook-client-test-listed" -Passed ($runbookText -match "Test-CqrsClientRolloutAcceptance")
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
