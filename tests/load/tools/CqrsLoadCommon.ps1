#Requires -Version 5.1
Set-StrictMode -Version Latest

function Get-CqrsLoadRepositoryRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
}

function Test-CqrsLoadLocalOrAllowedHost {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl
    )

    $uri = [uri]$BaseUrl.Trim().TrimEnd("/")
    $hostName = $uri.Host.ToLowerInvariant()

    if ($hostName -in @("localhost", "127.0.0.1", "::1", "[::1]")) {
        return $true
    }

    $allowed = $env:CQRS_LOAD_ALLOWED_HOST
    if (-not [string]::IsNullOrWhiteSpace($allowed)) {
        $allowedHosts = $allowed.Split(",") | ForEach-Object { $_.Trim().ToLowerInvariant() }
        return $hostName -in $allowedHosts
    }

    return $false
}

function Enable-CqrsLoadLocalhostTlsBypass {
    if ($PSVersionTable.PSVersion.Major -ge 7) {
        return @{
            UseSkipCertificateCheck = $true
            CallbackChanged       = $false
            PreviousCallback      = $null
        }
    }

    $previousCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

    return @{
        UseSkipCertificateCheck = $false
        CallbackChanged         = $true
        PreviousCallback        = $previousCallback
    }
}

function Disable-CqrsLoadLocalhostTlsBypass {
    param($TlsState)

    if ($null -eq $TlsState) {
        return
    }

    if ($TlsState.CallbackChanged) {
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback =
            $TlsState.PreviousCallback
    }
}

function Invoke-CqrsLoadHealthReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [bool]$SkipCertificateCheck = $false
    )

    $uri = "$($BaseUrl.Trim().TrimEnd('/'))/health/ready"

    if ($SkipCertificateCheck -or (Test-CqrsLoadLocalOrAllowedHost -BaseUrl $BaseUrl)) {
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($null -ne $curl) {
            $raw = & curl.exe -k -s $uri 2>$null
            if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($raw)) {
                throw "curl /health/ready failed."
            }
            return ($raw | ConvertFrom-Json)
        }
    }

    $params = @{
        Method      = "Get"
        Uri         = $uri
        ErrorAction = "Stop"
    }

    if ($SkipCertificateCheck) {
        $params["SkipCertificateCheck"] = $true
    }

    return Invoke-RestMethod @params
}

function Get-CqrsLoadModeExpectations {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("command", "appointment-query", "full-query")]
        [string]$Mode
    )

    switch ($Mode) {
        "command" {
            return @{
                appointmentsReadEnabled = $false
                dashboardReadEnabled    = $false
            }
        }
        "appointment-query" {
            return @{
                appointmentsReadEnabled = $true
                dashboardReadEnabled    = $false
            }
        }
        "full-query" {
            return @{
                appointmentsReadEnabled = $true
                dashboardReadEnabled    = $true
            }
        }
    }
}

function Test-CqrsLoadBooleanMatch {
    param(
        $Actual,
        [bool]$Expected
    )

    if ($null -eq $Actual) {
        return $false
    }

    if ($Actual -is [bool]) {
        return $Actual -eq $Expected
    }

    $normalized = [string]$Actual
    if ($normalized -eq "True" -or $normalized -eq "true" -or $normalized -eq "1") {
        return $Expected
    }

    if ($normalized -eq "False" -or $normalized -eq "false" -or $normalized -eq "0") {
        return -not $Expected
    }

    return $false
}

function Get-CqrsLoadGitCommitHash {
    $repoRoot = Get-CqrsLoadRepositoryRoot
    Push-Location $repoRoot
    try {
        $hash = git rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($hash)) {
            return "unknown"
        }
        return [string]$hash.Trim()
    }
    finally {
        Pop-Location
    }
}

function Resolve-CqrsLoadTokenFile {
    param(
        [string]$TokenFile
    )

    if ([string]::IsNullOrWhiteSpace($TokenFile)) {
        $TokenFile = Join-Path (Get-CqrsLoadRepositoryRoot) "tests\load\.tokens\clinic-tokens.json"
    }
    elseif (-not [System.IO.Path]::IsPathRooted($TokenFile)) {
        $TokenFile = Join-Path (Get-CqrsLoadRepositoryRoot) $TokenFile
    }

    return [System.IO.Path]::GetFullPath($TokenFile)
}

function Get-CqrsLoadWorkloadScript {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("read", "write-mix", "projection-lag")]
        [string]$Workload
    )

    $loadRoot = Join-Path (Get-CqrsLoadRepositoryRoot) "tests\load"

    switch ($Workload) {
        "read" { return Join-Path $loadRoot "cqrs-read.js" }
        "write-mix" { return Join-Path $loadRoot "panel-appointment-write-mix.js" }
        "projection-lag" { return Join-Path $loadRoot "appointment-projection-lag.js" }
    }
}
