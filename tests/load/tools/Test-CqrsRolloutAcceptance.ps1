#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-11E staged rollout / rollback acceptance otomasyon testleri.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://localhost:7173"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsRolloutAcceptanceCommon.ps1")

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

$invokePath = Join-Path $PSScriptRoot "Invoke-CqrsRolloutAcceptance.ps1"
$commonPath = Join-Path $PSScriptRoot "CqrsRolloutAcceptanceCommon.ps1"
$docPath = Join-Path $repoRoot "docs\cqrs\cqrs-11e-rollout-rollback-acceptance.md"

foreach ($path in @($invokePath, $commonPath)) {
    $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)
    $name = Split-Path $path -Leaf
    Add-Result -Name "ps-parse:$name" -Passed ($null -eq $errors -or $errors.Count -eq 0) `
        -Detail (($errors | ForEach-Object { $_.ToString() }) -join "; ")
}

Add-Result -Name "doc-exists" -Passed (Test-Path -LiteralPath $docPath)

$sequence = Get-CqrsRolloutAcceptanceSequence
Add-Result -Name "acceptance-sequence-count" -Passed ($sequence.Count -eq 7)
Add-Result -Name "acceptance-sequence-starts-mode-a" -Passed ($sequence[0].step -eq "command-read")
Add-Result -Name "acceptance-sequence-ends-mode-a" -Passed ($sequence[-1].step -eq "rollback-command-read")
Add-Result -Name "acceptance-sequence-includes-projection-pause" -Passed (
    @($sequence | Where-Object { $_.step -eq "projection-disabled" }).Count -eq 1 -and
    @($sequence | Where-Object { $_.step -eq "projection-reenabled" }).Count -eq 1
)

$projectionOffOverrides = Get-CqrsProjectionDisabledEnvironmentOverrides -ReadMode "full-query"
Add-Result -Name "projection-disabled-overrides" -Passed (
    $projectionOffOverrides.AppointmentProjection__Enabled -eq "False" -and
    $projectionOffOverrides.QueryReadModels__AppointmentsEnabled -eq "True" -and
    $projectionOffOverrides.QueryReadModels__DashboardAppointmentsEnabled -eq "True"
)

$healthCases = @(
    @{
        name     = "proj-off-read-on-pending-unhealthy"
        snapshot = @{
            projectionEnabled       = $false
            appointmentsReadEnabled = $true
            dashboardReadEnabled    = $false
            pendingCount            = 2
            retryWaitingCount       = 0
            deadLetterCount         = 0
        }
        expected = "Unhealthy"
    },
    @{
        name     = "proj-off-read-on-no-pending-degraded"
        snapshot = @{
            projectionEnabled       = $false
            appointmentsReadEnabled = $true
            dashboardReadEnabled    = $false
            pendingCount            = 0
            retryWaitingCount       = 0
            deadLetterCount         = 0
        }
        expected = "Degraded"
    },
    @{
        name     = "proj-off-read-off-pending-healthy"
        snapshot = @{
            projectionEnabled       = $false
            appointmentsReadEnabled = $false
            dashboardReadEnabled    = $false
            pendingCount            = 5
            retryWaitingCount       = 0
            deadLetterCount         = 0
        }
        expected = "Healthy"
    },
    @{
        name     = "proj-on-clean-healthy"
        snapshot = @{
            projectionEnabled       = $true
            appointmentsReadEnabled = $true
            dashboardReadEnabled    = $true
            pendingCount            = 0
            retryWaitingCount       = 0
            deadLetterCount         = 0
        }
        expected = "Healthy"
    }
)

foreach ($case in $healthCases) {
    $evaluation = Test-CqrsRolloutProjectionHealthExpectation `
        -Snapshot $case.snapshot `
        -ExpectedLevel $case.expected
    Add-Result -Name ("health-expectation:" + $case.name) -Passed $evaluation.passed
}

$rollbackDocs = Get-CqrsRolloutRollbackDocumentation
Add-Result -Name "rollback-docs-read-model" -Passed ($rollbackDocs.readModelRollback.Count -eq 2)
Add-Result -Name "rollback-docs-projector-stays-on" -Passed (
    $rollbackDocs.projectorDuringReadRollback -match "stay true"
)

try {
    $dryRunJson = & $invokePath -BaseUrl $BaseUrl -Step "command-read" | Out-String
    $dryRun = $dryRunJson | ConvertFrom-Json
    Add-Result -Name "dry-run-default" -Passed ($dryRun.dryRun -eq $true -and $dryRun.phase -eq "CQRS-11E")
    Add-Result -Name "dry-run-no-secret-leak" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $dryRunJson))
}
catch {
    Add-Result -Name "dry-run-default" -Passed $false -Detail $_.Exception.Message
}

$planJson = & $invokePath -BaseUrl $BaseUrl -ShowPlan | Out-String
Add-Result -Name "show-plan" -Passed ($planJson -match "acceptanceSequence")

foreach ($mode in @("command-read", "appointment-query", "full-query")) {
    $definition = Get-CqrsStagedModeDefinition -Mode $mode
    $passed = $true
    switch ($mode) {
        "command-read" {
            $passed = (-not $definition.appointmentsReadEnabled) -and (-not $definition.dashboardReadEnabled)
        }
        "appointment-query" {
            $passed = $definition.appointmentsReadEnabled -and (-not $definition.dashboardReadEnabled)
        }
        "full-query" {
            $passed = $definition.appointmentsReadEnabled -and $definition.dashboardReadEnabled
        }
    }
    Add-Result -Name ("read-source:$mode") -Passed $passed
}

if (Test-Path -LiteralPath $docPath) {
    $docText = Get-Content -LiteralPath $docPath -Raw
    Add-Result -Name "doc-rollback-steps" -Passed ($docText -match "rollback-command-read")
    Add-Result -Name "doc-projection-pause" -Passed ($docText -match "projection-disabled")
    Add-Result -Name "doc-no-secret-patterns" -Passed (-not (Test-CqrsStagedOutputContainsSecrets -Text $docText))
    Add-Result -Name "doc-command-fallback" -Passed ($docText -match "Command DB")
    Add-Result -Name "doc-detail-always-command" -Passed ($docText -match "GetAppointmentById|detail.*Command")
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
