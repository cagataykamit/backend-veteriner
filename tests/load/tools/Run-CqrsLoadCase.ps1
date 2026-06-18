#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS load test kosusunu calistirir (preflight + k6 + metadata + summary export).

.PARAMETER Mode
  command | appointment-query | full-query (yalnizca preflight dogrulamasi; API flag'leri disaridan ayarlanir).

.PARAMETER Workload
  read | write-mix | projection-lag

.PARAMETER Vus
  k6 virtual user sayisi.

.PARAMETER Duration
  k6 sure (ornek: 5m, 30s).

.PARAMETER BaseUrl
  API taban URL.

.PARAMETER TokenFile
  Token JSON dosyasi.

.PARAMETER OutputDirectory
  Sonuc klasoru. Varsayilan: tests/load/results
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("command", "appointment-query", "full-query")]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [ValidateSet("read", "write-mix", "projection-lag")]
    [string]$Workload,

    [Parameter(Mandatory = $true)]
    [int]$Vus,

    [Parameter(Mandatory = $true)]
    [string]$Duration,

    [string]$BaseUrl = "https://localhost:7173",

    [string]$TokenFile,

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsLoadCommon.ps1")

$repoRoot = Get-CqrsLoadRepositoryRoot
$normalizedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
$resolvedTokenFile = Resolve-CqrsLoadTokenFile -TokenFile $TokenFile

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "tests\load\results"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$scriptPath = Get-CqrsLoadWorkloadScript -Workload $Workload
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Workload script bulunamadi: $scriptPath"
}

$startUtc = [DateTime]::UtcNow
$timestamp = $startUtc.ToString("yyyyMMdd-HHmmss")
$runId = "$Mode-$Workload-$timestamp"
$runDirectory = Join-Path $OutputDirectory $runId
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

$summaryPath = Join-Path $runDirectory "k6-summary.json"
$metadataPath = Join-Path $runDirectory "run-metadata.json"
$k6LogPath = Join-Path $runDirectory "k6-console.log"

function Resolve-EffectiveK6TokenPlan {
    param(
        [string]$SourceTokenFile,
        [int]$RequestedVus,
        [string]$RunDirectory,
        [string]$Workload
    )

    $tokens = Get-Content -LiteralPath $SourceTokenFile -Raw | ConvertFrom-Json
    if (-not $tokens) {
        throw "Token dosyasi bos: $SourceTokenFile"
    }

    $tokenCount = @($tokens).Count
    if ($RequestedVus % $tokenCount -eq 0 -or $Workload -eq "projection-lag") {
        return @{
            TokenFile = $SourceTokenFile
            Vus       = $RequestedVus
            SingleSlotSmoke = $false
        }
    }

    if ($RequestedVus -eq 1) {
        $slot01 = @($tokens | Where-Object { [string]$_.slot -eq "01" })
        $selected = if ($slot01.Count -gt 0) { $slot01[0] } else { $tokens[0] }
        $tempTokenFile = Join-Path $RunDirectory "smoke-single-token.json"
        $entry = [ordered]@{
            slot        = [string]$selected.slot
            accessToken = [string]$selected.accessToken
        }
        $json = "[" + (($entry | ConvertTo-Json -Compress)) + "]"
        [System.IO.File]::WriteAllText($tempTokenFile, $json, [System.Text.UTF8Encoding]::new($false))
        return @{
            TokenFile = $tempTokenFile
            Vus       = 1
            SingleSlotSmoke = $true
        }
    }

    throw "VUS ($RequestedVus) token sayisi ($tokenCount) ile tam bolunmelidir."
}

$tokenPlan = Resolve-EffectiveK6TokenPlan `
    -SourceTokenFile $resolvedTokenFile `
    -RequestedVus $Vus `
    -RunDirectory $runDirectory `
    -Workload $Workload
$effectiveTokenFile = $tokenPlan.TokenFile
$effectiveVus = $tokenPlan.Vus

& (Join-Path $PSScriptRoot "Test-CqrsLoadPreflight.ps1") `
    -BaseUrl $normalizedBaseUrl `
    -Mode $Mode `
    -TokenFile $resolvedTokenFile | Out-Null

$metadata = [ordered]@{
    runId        = $runId
    mode         = $Mode
    workload     = $Workload
    vus          = $effectiveVus
    duration     = $Duration
    baseUrl      = $normalizedBaseUrl
    tokenFile    = $effectiveTokenFile
    sourceTokenFile = $resolvedTokenFile
    singleSlotSmoke = $tokenPlan.SingleSlotSmoke
    script       = $scriptPath
    startUtc     = $startUtc.ToString("o")
    gitCommit    = Get-CqrsLoadGitCommitHash
    dotnetEnvironment = $env:DOTNET_ENVIRONMENT
}

$metadata | ConvertTo-Json -Depth 5 | Set-Content -Path $metadataPath -Encoding UTF8

$k6Args = @(
    "run",
    $scriptPath,
    "--summary-export", $summaryPath,
    "-e", "VETINITY_URL=$normalizedBaseUrl",
    "-e", "VETINITY_TOKENS_FILE=$effectiveTokenFile",
    "-e", "VUS=$effectiveVus",
    "-e", "DURATION=$Duration"
)

if ($Workload -eq "projection-lag") {
    $k6Args += @(
        "-e", "PROJECTION_LAG_VUS=$effectiveVus",
        "-e", "PROJECTION_LAG_DURATION=$Duration"
    )
}

Push-Location $repoRoot
$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & k6 @k6Args 2>&1 | Tee-Object -FilePath $k6LogPath
    $exitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorAction
    Pop-Location
}

$endUtc = [DateTime]::UtcNow
$metadataCompleted = [ordered]@{
    runId        = $runId
    mode         = $Mode
    workload     = $Workload
    vus          = $effectiveVus
    duration     = $Duration
    baseUrl      = $normalizedBaseUrl
    tokenFile    = $effectiveTokenFile
    sourceTokenFile = $resolvedTokenFile
    singleSlotSmoke = $tokenPlan.SingleSlotSmoke
    script       = $scriptPath
    startUtc     = $startUtc.ToString("o")
    endUtc       = $endUtc.ToString("o")
    gitCommit    = Get-CqrsLoadGitCommitHash
    dotnetEnvironment = $env:DOTNET_ENVIRONMENT
    exitCode     = $exitCode
    summaryPath  = $summaryPath
    k6LogPath    = $k6LogPath
}

$metadataCompleted | ConvertTo-Json -Depth 5 | Set-Content -Path $metadataPath -Encoding UTF8

Write-Output ([ordered]@{
        runId       = $runId
        runDirectory = $runDirectory
        summaryPath = $summaryPath
        metadataPath = $metadataPath
        exitCode    = $exitCode
        startUtc    = $startUtc.ToString("o")
        endUtc      = $endUtc.ToString("o")
    })

exit $exitCode
