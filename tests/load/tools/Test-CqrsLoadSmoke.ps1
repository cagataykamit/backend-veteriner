#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS load test smoke kontrolleri (syntax, preflight, kisa k6 kosulari, gitignore).
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://localhost:7173",
    [string]$TokenFile,
    [switch]$SkipK6,
    [switch]$SkipPreflightNegative
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsLoadCommon.ps1")

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

$jsScripts = @(
    "cqrs-read.js",
    "appointment-projection-lag.js",
    "panel-appointment-write-mix.js",
    "panel-mixed-read.js",
    "dashboard-smoke.js"
)

foreach ($scriptName in $jsScripts) {
    $path = Join-Path $repoRoot "tests\load\$scriptName"
    $output = node --check $path 2>&1
    Add-Result -Name "js-syntax:$scriptName" -Passed ($LASTEXITCODE -eq 0) -Detail ([string]$output)
}

$psScripts = Get-ChildItem -Path (Join-Path $repoRoot "tests\load\tools") -Filter "*.ps1" -File
foreach ($psScript in $psScripts) {
    $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile(
        $psScript.FullName,
        [ref]$null,
        [ref]$errors
    )
    $passed = ($null -eq $errors -or $errors.Count -eq 0)
    $detail = if ($passed) { "" } else { ($errors | ForEach-Object { $_.ToString() }) -join "; " }
    Add-Result -Name "ps-parse:$($psScript.Name)" -Passed $passed -Detail $detail
}

$resolvedTokenFile = Resolve-CqrsLoadTokenFile -TokenFile $TokenFile
if (-not (Test-Path -LiteralPath $resolvedTokenFile)) {
    Add-Result -Name "token-file-exists" -Passed $false -Detail "Missing: $resolvedTokenFile"
}
else {
    Add-Result -Name "token-file-exists" -Passed $true
}

if (-not $SkipPreflightNegative) {
    try {
        & (Join-Path $PSScriptRoot "Test-CqrsLoadPreflight.ps1") `
            -BaseUrl "https://production.example" `
            -Mode "command" `
            -TokenFile $resolvedTokenFile
        Add-Result -Name "preflight-negative-baseurl" -Passed $false -Detail "Expected failure did not occur."
    }
    catch {
        Add-Result -Name "preflight-negative-baseurl" -Passed $true
    }

    try {
        & (Join-Path $PSScriptRoot "Test-CqrsLoadPreflight.ps1") `
            -BaseUrl $BaseUrl `
            -Mode "full-query" `
            -TokenFile $resolvedTokenFile
        Add-Result -Name "preflight-negative-wrong-mode" -Passed $false -Detail "Expected mode mismatch failure did not occur."
    }
    catch {
        Add-Result -Name "preflight-negative-wrong-mode" -Passed $true
    }
}

if (-not $SkipK6) {
    $smokeCases = @(
        @{ Mode = "command"; Workload = "read"; Vus = 1; Duration = "15s" },
        @{ Mode = "command"; Workload = "write-mix"; Vus = 1; Duration = "30s" },
        @{ Mode = "full-query"; Workload = "projection-lag"; Vus = 1; Duration = "30s" }
    )

    foreach ($case in $smokeCases) {
        $name = "k6-smoke:$($case.Mode)/$($case.Workload)"
        try {
            & (Join-Path $PSScriptRoot "Run-CqrsLoadCase.ps1") `
                -Mode $case.Mode `
                -Workload $case.Workload `
                -Vus $case.Vus `
                -Duration $case.Duration `
                -BaseUrl $BaseUrl `
                -TokenFile $resolvedTokenFile | Out-Null
            Add-Result -Name $name -Passed ($LASTEXITCODE -eq 0) -Detail "exit=$LASTEXITCODE"
        }
        catch {
            Add-Result -Name $name -Passed $false -Detail $_.Exception.Message
        }
    }
}

$gitStatus = git -C $repoRoot status --short 2>&1
$resultsTracked = ($gitStatus | Where-Object { $_ -match "tests/load/results|tests/load/\.tokens" })
Add-Result -Name "gitignore-results-tokens" -Passed ($resultsTracked.Count -eq 0) `
    -Detail (($resultsTracked | ForEach-Object { [string]$_ }) -join "; ")

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
