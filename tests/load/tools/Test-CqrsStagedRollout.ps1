#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-11A/11B staged rollout ve operations readiness otomasyon testleri.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://localhost:7173"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsStagedRolloutCommon.ps1")

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

$scriptPath = Join-Path $PSScriptRoot "Invoke-CqrsStagedRollout.ps1"
$errors = $null
$null = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath,
    [ref]$null,
    [ref]$errors
)
$parsePassed = ($null -eq $errors -or $errors.Count -eq 0)
Add-Result -Name "ps-parse:Invoke-CqrsStagedRollout.ps1" -Passed $parsePassed `
    -Detail (($errors | ForEach-Object { $_.ToString() }) -join "; ")

$commonErrors = $null
$commonPath = Join-Path $PSScriptRoot "CqrsStagedRolloutCommon.ps1"
$null = [System.Management.Automation.Language.Parser]::ParseFile(
    $commonPath,
    [ref]$null,
    [ref]$commonErrors
)
Add-Result -Name "ps-parse:CqrsStagedRolloutCommon.ps1" -Passed ($null -eq $commonErrors -or $commonErrors.Count -eq 0)

foreach ($mode in @("command-read", "appointment-query", "full-query")) {
    $definition = Get-CqrsStagedModeDefinition -Mode $mode
    $passed = $true
    $detail = ""

    switch ($mode) {
        "command-read" {
            $passed = (-not $definition.appointmentsReadEnabled) -and (-not $definition.dashboardReadEnabled) -and $definition.appointmentProjectionEnabled
        }
        "appointment-query" {
            $passed = $definition.appointmentsReadEnabled -and (-not $definition.dashboardReadEnabled) -and $definition.appointmentProjectionEnabled
        }
        "full-query" {
            $passed = $definition.appointmentsReadEnabled -and $definition.dashboardReadEnabled -and $definition.appointmentProjectionEnabled
        }
    }

    Add-Result -Name "mode-definition:$mode" -Passed $passed -Detail $detail
}

$modeAOverrides = Get-CqrsStagedModeEnvironmentOverrides -Mode "command-read"
Add-Result -Name "mode-a-safe-default" -Passed (
    $modeAOverrides.QueryReadModels__AppointmentsEnabled -eq "False" -and
    $modeAOverrides.QueryReadModels__DashboardAppointmentsEnabled -eq "False" -and
    $modeAOverrides.AppointmentProjection__Enabled -eq "True"
)

$rollbackCtoB = Get-CqrsStagedRollbackPlan -FromMode "full-query"
Add-Result -Name "rollback-plan:c-to-b" -Passed ($rollbackCtoB.toMode -eq "appointment-query")

$rollbackBtoA = Get-CqrsStagedRollbackPlan -FromMode "appointment-query"
Add-Result -Name "rollback-plan:b-to-a" -Passed ($rollbackBtoA.toMode -eq "command-read")

$sequence = Get-CqrsStagedRolloutSequence
Add-Result -Name "rollout-sequence" -Passed (
    $sequence.Count -eq 5 -and
    $sequence[0] -eq "command-read" -and
    $sequence[-1] -eq "command-read"
)

try {
    $null = Get-CqrsStagedParityReport `
        -ServerInstance "localhost" `
        -CommandDatabase "SameDb" `
        -QueryDatabase "SameDb"
    Add-Result -Name "parity-same-catalog-fails" -Passed $false -Detail "Expected exception was not thrown."
}
catch {
    Add-Result -Name "parity-same-catalog-fails" -Passed $true
}

$dryRunJson = & $scriptPath `
    -BaseUrl $BaseUrl `
    -Mode "command-read" `
    -CommandDatabase "VetinityCommandDb_LoadTest" `
    -QueryDatabase "VetinityQueryDb_LoadTest" | Out-String

$dryRun = $dryRunJson | ConvertFrom-Json
Add-Result -Name "dry-run-default" -Passed ($dryRun.dryRun -eq $true)
Add-Result -Name "dry-run-no-secret-leak" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $dryRunJson))

$sequenceJson = & $scriptPath -BaseUrl $BaseUrl -Mode "command-read" -ShowSequence | Out-String
Add-Result -Name "show-sequence" -Passed ($sequenceJson -match "rolloutSequence")

try {
    & (Join-Path $PSScriptRoot "Test-CqrsLoadPreflight.ps1") `
        -BaseUrl "https://production.example" `
        -Mode "command" `
        -TokenFile (Join-Path $repoRoot "tests\load\.tokens\clinic-tokens.json")
    Add-Result -Name "preflight-invalid-host-fails" -Passed $false
}
catch {
    Add-Result -Name "preflight-invalid-host-fails" -Passed $true
}

if (Test-Path -LiteralPath (Join-Path $repoRoot "tests\load\.tokens\clinic-tokens.json")) {
    try {
        & (Join-Path $PSScriptRoot "Test-CqrsLoadPreflight.ps1") `
            -BaseUrl $BaseUrl `
            -Mode "full-query" `
            -TokenFile (Join-Path $repoRoot "tests\load\.tokens\clinic-tokens.json")
        Add-Result -Name "preflight-mode-mismatch-fails" -Passed $false -Detail "API may already be in full-query mode."
    }
    catch {
        Add-Result -Name "preflight-mode-mismatch-fails" -Passed $true
    }
}
else {
    Add-Result -Name "preflight-mode-mismatch-fails" -Passed $true -Detail "Skipped: token file missing."
}

# CQRS-11B operations readiness (read-only)
$runbookPath = Join-Path $repoRoot "docs\cqrs\cqrs-11b-operations-runbook.md"
Add-Result -Name "runbook-exists" -Passed (Test-Path -LiteralPath $runbookPath)

$requiredScripts = @(
    "Invoke-CqrsStagedRollout.ps1",
    "Test-CqrsLoadPreflight.ps1",
    "Invoke-CqrsLoadDataReset.ps1",
    "CqrsStagedRolloutCommon.ps1"
)
foreach ($scriptName in $requiredScripts) {
    $path = Join-Path $PSScriptRoot $scriptName
    Add-Result -Name "script-exists:$scriptName" -Passed (Test-Path -LiteralPath $path)
}

$stagingConfigPath = Join-Path $repoRoot "src\Backend.Veteriner.Api\appsettings.Staging.json"
$stagingConfigOk = $false
if (Test-Path -LiteralPath $stagingConfigPath) {
    $stagingJson = Get-Content -LiteralPath $stagingConfigPath -Raw | ConvertFrom-Json
    $stagingConfigOk = (
        $stagingJson.QueryReadModels.AppointmentsEnabled -eq $false -and
        $stagingJson.QueryReadModels.DashboardAppointmentsEnabled -eq $false -and
        $stagingJson.AppointmentProjection.Enabled -eq $true
    )
}
Add-Result -Name "staging-config-mode-a-default" -Passed $stagingConfigOk

$startupLoggerPath = Join-Path $repoRoot "src\Backend.Veteriner.Api\Configuration\CqrsStartupConfigurationLogger.cs"
Add-Result -Name "startup-cqrs-logger-exists" -Passed (Test-Path -LiteralPath $startupLoggerPath)

if (Test-Path -LiteralPath $runbookPath) {
    $runbookText = Get-Content -LiteralPath $runbookPath -Raw
    Add-Result -Name "runbook-no-secret-patterns" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $runbookText))
    Add-Result -Name "runbook-single-instance-warning" -Passed ($runbookText -match "tek aktif projector instance|tek instance")
    Add-Result -Name "runbook-projector-stays-enabled-on-rollback" -Passed (
        $runbookText -match "AppointmentProjection__Enabled=true kalmalı" -or
        $runbookText -match "projector açık"
    )
    Add-Result -Name "runbook-no-automatic-fallback" -Passed ($runbookText -match "otomatik Command fallback yok")
    Add-Result -Name "runbook-rebuild-command-documented" -Passed (
        $runbookText -match "rebuild-appointment-projections"
    )
    Add-Result -Name "runbook-dead-letter-no-supported-replay" -Passed (
        $runbookText -match "desteklenmiş manuel replay" -or
        $runbookText -match "Desteklenmiş appointment replay"
    )
}

$gitignorePath = Join-Path $repoRoot ".gitignore"
if (Test-Path -LiteralPath $gitignorePath) {
    $gitignoreText = Get-Content -LiteralPath $gitignorePath -Raw
    Add-Result -Name "gitignore-tokens" -Passed ($gitignoreText -match "tests/load/\.tokens")
    Add-Result -Name "gitignore-results" -Passed ($gitignoreText -match "tests/load/results")
}

$dbMigratorHelp = dotnet run --project (Join-Path $repoRoot "src\Backend.Veteriner.DbMigrator") -- help 2>&1 | Out-String
Add-Result -Name "dbmigrator-rebuild-command-exists" -Passed ($dbMigratorHelp -match "rebuild-appointment-projections")

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
