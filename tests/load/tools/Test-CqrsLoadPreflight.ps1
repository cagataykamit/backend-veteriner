#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS load test oncesi ortam ve mode dogrulamasi.

.PARAMETER BaseUrl
  API taban URL (ornek: https://localhost:7173).

.PARAMETER Mode
  command | appointment-query | full-query

.PARAMETER TokenFile
  Klinik token JSON dosyasi. Varsayilan: tests/load/.tokens/clinic-tokens.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateSet("command", "appointment-query", "full-query")]
    [string]$Mode,

    [string]$TokenFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsLoadCommon.ps1")

function Assert-Preflight {
    param(
        [string]$Message
    )

    throw "Preflight failed: $Message"
}

$normalizedBaseUrl = $BaseUrl.Trim().TrimEnd("/")

if (-not (Test-CqrsLoadLocalOrAllowedHost -BaseUrl $normalizedBaseUrl)) {
    Assert-Preflight "BaseUrl yalnizca localhost/127.0.0.1 veya CQRS_LOAD_ALLOWED_HOST ile izin verilen host olabilir."
}

$resolvedTokenFile = Resolve-CqrsLoadTokenFile -TokenFile $TokenFile
if (-not (Test-Path -LiteralPath $resolvedTokenFile)) {
    Assert-Preflight "Token dosyasi bulunamadi: $resolvedTokenFile"
}

$tlsState = Enable-CqrsLoadLocalhostTlsBypass
try {
    $health = Invoke-CqrsLoadHealthReady `
        -BaseUrl $normalizedBaseUrl `
        -SkipCertificateCheck:($tlsState.UseSkipCertificateCheck -or $true)

    if ($null -eq $health -or $null -eq $health.results) {
        Assert-Preflight "/health/ready yaniti gecersiz."
    }

    if ([string]$health.status -ne "Healthy") {
        Assert-Preflight "/health/ready status Healthy degil (status=$($health.status))."
    }

    $projectionEntry = $health.results.'appointment-projection'
    if ($null -eq $projectionEntry) {
        Assert-Preflight "appointment-projection health entry eksik."
    }

    $projectionData = $projectionEntry.data
    if ($null -eq $projectionData) {
        Assert-Preflight "appointment-projection data eksik."
    }

    if (-not (Test-CqrsLoadBooleanMatch -Actual $projectionData.projectionEnabled -Expected $true)) {
        Assert-Preflight "projectionEnabled=true bekleniyor."
    }

    $pendingCount = [int]$projectionData.pendingCount
    $retryWaitingCount = [int]$projectionData.retryWaitingCount
    $deadLetterCount = [int]$projectionData.deadLetterCount

    if ($pendingCount -ne 0) {
        Assert-Preflight "pendingCount=0 bekleniyor (actual=$pendingCount)."
    }

    if ($retryWaitingCount -ne 0) {
        Assert-Preflight "retryWaitingCount=0 bekleniyor (actual=$retryWaitingCount)."
    }

    if ($deadLetterCount -ne 0) {
        Assert-Preflight "deadLetterCount=0 bekleniyor (actual=$deadLetterCount)."
    }

    $querySqlEntry = $health.results.'query-sql'
    if ($null -eq $querySqlEntry) {
        Assert-Preflight "query-sql health entry eksik."
    }

    if ([string]$querySqlEntry.status -ne "Healthy") {
        Assert-Preflight "Query DB reachable degil (query-sql status=$($querySqlEntry.status))."
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$querySqlEntry.description) -and
        [string]$querySqlEntry.description -match "bekleyen migration") {
        Assert-Preflight "Query DB pending migration var."
    }

    $expected = Get-CqrsLoadModeExpectations -Mode $Mode

    if (-not (Test-CqrsLoadBooleanMatch `
            -Actual $projectionData.appointmentsReadEnabled `
            -Expected $expected.appointmentsReadEnabled)) {
        Assert-Preflight "Mode '$Mode' icin appointmentsReadEnabled=$($expected.appointmentsReadEnabled) bekleniyor."
    }

    if (-not (Test-CqrsLoadBooleanMatch `
            -Actual $projectionData.dashboardReadEnabled `
            -Expected $expected.dashboardReadEnabled)) {
        Assert-Preflight "Mode '$Mode' icin dashboardReadEnabled=$($expected.dashboardReadEnabled) bekleniyor."
    }
}
finally {
    Disable-CqrsLoadLocalhostTlsBypass -TlsState $tlsState
}

Write-Output ([ordered]@{
        ok       = $true
        baseUrl  = $normalizedBaseUrl
        mode     = $Mode
        tokenFile = $resolvedTokenFile
        flags    = Get-CqrsLoadModeExpectations -Mode $Mode
        projection = @{
            pendingCount      = 0
            retryWaitingCount = 0
            deadLetterCount   = 0
            projectionEnabled = $true
        }
    })
