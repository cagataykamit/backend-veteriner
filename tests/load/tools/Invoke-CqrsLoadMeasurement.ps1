#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-10 olcum matrisi orchestrator (API mode switch + k6 + perf + parity).
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://localhost:7173",
    [string]$TokenFile,
    [switch]$SkipSoak
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsLoadCommon.ps1")

$repoRoot = Get-CqrsLoadRepositoryRoot
$resolvedTokenFile = Resolve-CqrsLoadTokenFile -TokenFile $TokenFile
$resultsRoot = Join-Path $repoRoot "tests\load\results"
$matrixLog = Join-Path $resultsRoot "matrix-run.log"

if (-not (Test-Path $resultsRoot)) {
    New-Item -ItemType Directory -Path $resultsRoot -Force | Out-Null
}

function Write-MatrixLog {
    param([string]$Message)
    $line = "[{0}] {1}" -f ([DateTime]::UtcNow.ToString("o")), $Message
    Add-Content -Path $matrixLog -Value $line -Encoding UTF8
    Write-Host $line
}

function Set-LoadTestBaseEnvironment {
    $env:DOTNET_ENVIRONMENT = "LoadTest"
    $env:ASPNETCORE_ENVIRONMENT = "LoadTest"
    $env:ConnectionStrings__DefaultConnection =
        "Server=localhost;Database=VetinityCommandDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
    $env:ConnectionStrings__QueryConnection =
        "Server=localhost;Database=VetinityQueryDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
    $env:Jwt__Key = "loadtest-local-jwt-signing-key-32chars-min"
    $env:RateLimiting__Enabled = "false"
}

function Stop-ApiListener {
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $connections = Get-NetTCPConnection -LocalPort 7173 -ErrorAction SilentlyContinue
        foreach ($conn in $connections) {
            $procId = $conn.OwningProcess
            if ($procId -gt 0) {
                Write-MatrixLog "Stopping process on port 7173 (PID=$procId)"
                Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
            }
        }
        Start-Sleep -Seconds 2
        $stillListening = Get-NetTCPConnection -LocalPort 7173 -State Listen -ErrorAction SilentlyContinue
        if (-not $stillListening) {
            return
        }
    }

    throw "Port 7173 is still in use after API stop attempts."
}

function Start-ApiForMode {
    param(
        [bool]$AppointmentsEnabled,
        [bool]$DashboardEnabled
    )

    Stop-ApiListener
    Set-LoadTestBaseEnvironment
    $env:QueryReadModels__AppointmentsEnabled = [string]$AppointmentsEnabled
    $env:QueryReadModels__DashboardAppointmentsEnabled = [string]$DashboardEnabled
    $env:AppointmentProjection__Enabled = "true"

    $apiProject = Join-Path $repoRoot "src\Backend.Veteriner.Api\Backend.Veteriner.Api.csproj"
    $argList = @(
        "run",
        "--project", $apiProject,
        "--no-launch-profile",
        "--no-build",
        "--urls", "https://localhost:7173;http://localhost:5018"
    )

    $script:ApiProcess = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $argList `
        -WorkingDirectory $repoRoot `
        -PassThru `
        -WindowStyle Hidden

    Write-MatrixLog "API started PID=$($script:ApiProcess.Id) appointments=$AppointmentsEnabled dashboard=$DashboardEnabled"

    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        try {
            $health = Invoke-CqrsLoadHealthReady -BaseUrl $BaseUrl -SkipCertificateCheck:$true
            if ($health.status -eq "Healthy") {
                return $health
            }
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }

    throw "API health did not become Healthy within 60s."
}

function Get-EntityCounts {
    $commandDb = "VetinityCommandDb_LoadTest"
    $queryDb = "VetinityQueryDb_LoadTest"

    function Invoke-Count {
        param([string]$Database, [string]$Query)
        $line = sqlcmd -S localhost -d $Database -Q $Query -h -1 -W 2>&1
        if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed for $Database" }
        return [int](($line | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1))
    }

    return [ordered]@{
        Tenants               = Invoke-Count $commandDb "SET NOCOUNT ON; SELECT COUNT(*) FROM Tenants;"
        Clinics               = Invoke-Count $commandDb "SET NOCOUNT ON; SELECT COUNT(*) FROM Clinics;"
        Clients               = Invoke-Count $commandDb "SET NOCOUNT ON; SELECT COUNT(*) FROM Clients;"
        Pets                  = Invoke-Count $commandDb "SET NOCOUNT ON; SELECT COUNT(*) FROM Pets;"
        Appointments          = Invoke-Count $commandDb "SET NOCOUNT ON; SELECT COUNT(*) FROM Appointments;"
        AppointmentReadModels = Invoke-Count $queryDb "SET NOCOUNT ON; SELECT COUNT(*) FROM AppointmentReadModels;"
        K6LoadTestNotes       = Invoke-Count $commandDb "SET NOCOUNT ON; SELECT COUNT(*) FROM Appointments WHERE Notes LIKE 'K6_LOAD_TEST%';"
    }
}

function Assert-DataParity {
    param([string]$Label)

    $counts = Get-EntityCounts
    Write-MatrixLog "$Label counts: Tenants=$($counts.Tenants) Clinics=$($counts.Clinics) Clients=$($counts.Clients) Pets=$($counts.Pets) Appointments=$($counts.Appointments) ReadModels=$($counts.AppointmentReadModels) K6Notes=$($counts.K6LoadTestNotes)"

    if ($counts.Appointments -ne $counts.AppointmentReadModels) {
        throw "Parity failed before '$Label': Appointments=$($counts.Appointments) ReadModels=$($counts.AppointmentReadModels)"
    }

    return $counts
}

function Invoke-LoadCaseWithPerf {
    param(
        [string]$Mode,
        [string]$Workload,
        [int]$Vus,
        [string]$Duration
    )

    $perfPath = Join-Path $resultsRoot ("perf-$Mode-$Workload-$Vus-$Duration.json")
    $durationSeconds = 120
    if ($Duration -match '^(\d+)m$') { $durationSeconds = [int]$Matches[1] * 60 }
    elseif ($Duration -match '^(\d+)s$') { $durationSeconds = [int]$Matches[1] }

    $perfJob = Start-Job -ScriptBlock {
        param($ScriptPath, $PerfPath, $DurationSeconds)
        & $ScriptPath -DurationSeconds ($DurationSeconds + 30) -IntervalSeconds 5 -OutputPath $PerfPath
    } -ArgumentList (Join-Path $PSScriptRoot "Collect-CqrsLoadPerf.ps1"), $perfPath, $durationSeconds

    try {
        & (Join-Path $PSScriptRoot "Run-CqrsLoadCase.ps1") `
            -Mode $Mode `
            -Workload $Workload `
            -Vus $Vus `
            -Duration $Duration `
            -BaseUrl $BaseUrl `
            -TokenFile $resolvedTokenFile | Out-Null
        $exit = $LASTEXITCODE
    }
    finally {
        Wait-Job $perfJob -Timeout ($durationSeconds + 60) | Out-Null
        Receive-Job $perfJob -ErrorAction SilentlyContinue | Out-Null
        Remove-Job $perfJob -Force -ErrorAction SilentlyContinue
    }

    if ($exit -ne 0) {
        if ($exit -eq 99) {
            Write-MatrixLog "WARN thresholds not met: $Mode/$Workload VUS=$Vus Duration=$Duration exit=99"
        }
        else {
            throw "Load case failed: $Mode/$Workload VUS=$Vus Duration=$Duration exit=$exit"
        }
    }
}

function Test-AuthenticatedToken {
    $tokens = Get-Content -LiteralPath $resolvedTokenFile -Raw | ConvertFrom-Json
    $token = [string]$tokens[0].accessToken
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Token file first slot accessToken empty."
    }

    $status = & curl.exe -k -s -o NUL -w "%{http_code}" `
        -H "Authorization: Bearer $token" `
        -H "Accept: application/json" `
        "$BaseUrl/api/v1/dashboard/summary"

    if ([string]$status -ne "200") {
        throw "Authenticated dashboard probe failed with HTTP $status"
    }

    Write-MatrixLog "Authenticated dashboard probe HTTP 200"
}

Set-LoadTestBaseEnvironment
Push-Location $repoRoot
try {
    Write-MatrixLog "Building API"
    dotnet build src/Backend.Veteriner.Api -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "API build failed." }

    $null = Start-ApiForMode -AppointmentsEnabled $false -DashboardEnabled $false
    & (Join-Path $PSScriptRoot "Test-CqrsLoadPreflight.ps1") -BaseUrl $BaseUrl -Mode command -TokenFile $resolvedTokenFile | Out-Null

    Write-MatrixLog "Regenerating tokens"
    $securePassword = ConvertTo-SecureString "123456" -AsPlainText -Force
    & (Join-Path $PSScriptRoot "Prepare-LoadTestTokens.ps1") `
        -AllowInsecureLocalhostCertificate `
        -Force `
        -Password $securePassword | Out-Null
    $securePassword = $null

    Test-AuthenticatedToken

    Write-MatrixLog "Running smoke (command read + write-mix)"
    & (Join-Path $PSScriptRoot "Run-CqrsLoadCase.ps1") -Mode command -Workload read -Vus 1 -Duration 15s -BaseUrl $BaseUrl -TokenFile $resolvedTokenFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Smoke read failed." }
    & (Join-Path $PSScriptRoot "Run-CqrsLoadCase.ps1") -Mode command -Workload write-mix -Vus 1 -Duration 30s -BaseUrl $BaseUrl -TokenFile $resolvedTokenFile | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Smoke write-mix failed." }

    $null = Assert-DataParity -Label "pre-matrix"

    # MODE A
    $null = Start-ApiForMode -AppointmentsEnabled $false -DashboardEnabled $false
    & (Join-Path $PSScriptRoot "Test-CqrsLoadPreflight.ps1") -BaseUrl $BaseUrl -Mode command -TokenFile $resolvedTokenFile | Out-Null
    $null = Assert-DataParity -Label "mode-A-start"
    Invoke-LoadCaseWithPerf -Mode command -Workload read -Vus 50 -Duration 2m
    Invoke-LoadCaseWithPerf -Mode command -Workload read -Vus 100 -Duration 5m
    Invoke-LoadCaseWithPerf -Mode command -Workload write-mix -Vus 100 -Duration 5m
    & (Join-Path $PSScriptRoot "Invoke-CqrsLoadDataReset.ps1") -Method rebuild-only | Out-Null
    $null = Assert-DataParity -Label "mode-A-post-reset"

    # MODE B
    $null = Start-ApiForMode -AppointmentsEnabled $true -DashboardEnabled $false
    & (Join-Path $PSScriptRoot "Test-CqrsLoadPreflight.ps1") -BaseUrl $BaseUrl -Mode appointment-query -TokenFile $resolvedTokenFile | Out-Null
    $null = Assert-DataParity -Label "mode-B-start"
    Invoke-LoadCaseWithPerf -Mode appointment-query -Workload read -Vus 50 -Duration 2m
    Invoke-LoadCaseWithPerf -Mode appointment-query -Workload read -Vus 100 -Duration 5m

    # MODE C
    $null = Start-ApiForMode -AppointmentsEnabled $true -DashboardEnabled $true
    & (Join-Path $PSScriptRoot "Test-CqrsLoadPreflight.ps1") -BaseUrl $BaseUrl -Mode full-query -TokenFile $resolvedTokenFile | Out-Null
    $null = Assert-DataParity -Label "mode-C-start"
    Invoke-LoadCaseWithPerf -Mode full-query -Workload read -Vus 50 -Duration 2m
    Invoke-LoadCaseWithPerf -Mode full-query -Workload read -Vus 100 -Duration 5m
    Invoke-LoadCaseWithPerf -Mode full-query -Workload write-mix -Vus 100 -Duration 5m
    $null = Assert-DataParity -Label "mode-C-post-write-mix"
    Invoke-LoadCaseWithPerf -Mode full-query -Workload projection-lag -Vus 2 -Duration 5m
    if (-not $SkipSoak) {
        Invoke-LoadCaseWithPerf -Mode full-query -Workload write-mix -Vus 100 -Duration 15m
    }

    Write-MatrixLog "Exporting SQL top queries"
    sqlcmd -S localhost -d VetinityCommandDb_LoadTest -i (Join-Path $repoRoot "tests\load\sql\cqrs-top-queries.sql") `
        -v DatabaseName=VetinityCommandDb_LoadTest `
        -o (Join-Path $resultsRoot "command-top-queries.txt") -W | Out-Null
    sqlcmd -S localhost -d VetinityQueryDb_LoadTest -i (Join-Path $repoRoot "tests\load\sql\cqrs-top-queries.sql") `
        -v DatabaseName=VetinityQueryDb_LoadTest `
        -o (Join-Path $resultsRoot "query-top-queries.txt") -W | Out-Null

    & (Join-Path $PSScriptRoot "Compare-CqrsLoadResults.ps1") `
        -ResultsDirectory $resultsRoot `
        -OutputPath (Join-Path $resultsRoot "comparison.csv") | Out-Null

    Write-MatrixLog "Matrix completed successfully"
}
finally {
    Pop-Location
    if ($null -ne $script:ApiProcess -and -not $script:ApiProcess.HasExited) {
        Stop-Process -Id $script:ApiProcess.Id -Force -ErrorAction SilentlyContinue
    }
}
