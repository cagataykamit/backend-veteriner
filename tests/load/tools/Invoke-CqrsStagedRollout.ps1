#Requires -Version 5.1
<#
.SYNOPSIS
  CQRS-11A staged rollout dogrulama araci (Mode A/B/C ve rollback planlama).

.DESCRIPTION
  Varsayilan dry-run. -Apply verilmeden hicbir servis/config degistirmez.
  Gercek staging deployment mekanizmasi repo disindaysa yalnizca dogrulama ve plan uretir.

.PARAMETER BaseUrl
  Hedef API taban URL.

.PARAMETER Mode
  command-read | appointment-query | full-query

.PARAMETER TokenFile
  Klinik token JSON dosyasi.

.PARAMETER CommandDatabase
  Command DB catalog adi (parity/queue SQL kontrolleri icin).

.PARAMETER QueryDatabase
  Query DB catalog adi.

.PARAMETER ServerInstance
  SQL Server instance (varsayilan localhost).

.PARAMETER Apply
  Dogrulama adimlarini calistirir. Ortam degiskeni degistirmez; staging deploy/restart operasyonel sorumlulugundadir.

.PARAMETER ShowSequence
  A -> B -> C -> B -> A rollout planini yazdirir.

.PARAMETER ShowRollbackFrom
  full-query veya appointment-query kaynagindan rollback planini yazdirir.

.PARAMETER SkipEndpointSmoke
  HTTP endpoint smoke adimlarini atlar.

.PARAMETER SkipParity
  SQL parity adimlarini atlar.

.PARAMETER PollTimeoutSeconds
  Projection queue drain bekleme suresi (varsayilan 30).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateSet("command-read", "appointment-query", "full-query")]
    [string]$Mode,

    [string]$TokenFile,

    [string]$CommandDatabase = "VetinityCommandDb_LoadTest",

    [string]$QueryDatabase = "VetinityQueryDb_LoadTest",

    [string]$ServerInstance = "localhost",

    [switch]$Apply,

    [switch]$ShowSequence,

    [ValidateSet("full-query", "appointment-query")]
    [string]$ShowRollbackFrom,

    [switch]$SkipEndpointSmoke,

    [switch]$SkipParity,

    [int]$PollTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsStagedRolloutCommon.ps1")

$normalizedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
$definition = Get-CqrsStagedModeDefinition -Mode $Mode
$preflightMode = ConvertTo-CqrsStagedPreflightMode -Mode $Mode
$environmentOverrides = Get-CqrsStagedModeEnvironmentOverrides -Mode $Mode
$startedAtUtc = [DateTime]::UtcNow
$report = [ordered]@{
    phase            = "CQRS-11A"
    dryRun           = (-not $Apply.IsPresent)
    startedAtUtc     = $startedAtUtc.ToString("o")
    baseUrl          = $normalizedBaseUrl
    mode             = $Mode
    modeDefinition   = $definition
    environmentPlan  = $environmentOverrides
    hostAllowed      = (Test-CqrsStagedHostAllowed -BaseUrl $normalizedBaseUrl)
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

function Add-Warning {
    param([string]$Message)
    $report.warnings.Add($Message) | Out-Null
}

if ($ShowSequence.IsPresent) {
    $sequencePlan = Get-CqrsStagedRolloutSequence | ForEach-Object {
        [ordered]@{
            mode      = $_
            overrides = Get-CqrsStagedModeEnvironmentOverrides -Mode $_
        }
    }

    Write-Output ([ordered]@{
            rolloutSequence = $sequencePlan
            note            = "Her adimda tek API instance restart/deploy + validation calistirilmalidir."
        })
    return
}

if (-not [string]::IsNullOrWhiteSpace($ShowRollbackFrom)) {
    Write-Output (Get-CqrsStagedRollbackPlan -FromMode $ShowRollbackFrom)
    return
}

if (-not $report.hostAllowed) {
    Add-Warning "BaseUrl yalnizca localhost veya CQRS_LOAD_ALLOWED_HOST ile izin verilen host olabilir."
}

if ($CommandDatabase -eq $QueryDatabase) {
    Add-Step -Name "distinct-databases" -Passed $false -Detail @{
        commandDatabase = $CommandDatabase
        queryDatabase   = $QueryDatabase
    }
    $report.passed = $false
    $json = $report | ConvertTo-Json -Depth 8
    if (Test-CqrsStagedOutputContainsSecrets -Text $json) {
        throw "Report output contains secret-like content."
    }
    Write-Output $json
    exit 1
}

Add-Step -Name "distinct-databases" -Passed $true -Detail @{
    commandDatabase = $CommandDatabase
    queryDatabase   = $QueryDatabase
}

if (-not $Apply.IsPresent) {
    Add-Step -Name "dry-run-plan" -Passed $true -Detail @{
        message = "No service or environment changes were applied."
        requiredEnvironmentOverrides = $environmentOverrides
        recommendedCommands = @(
            "Set staging environment overrides shown in environmentPlan"
            "Restart/deploy exactly one API instance (AppointmentProjection__Enabled=true)"
            ".\tests\load\tools\Test-CqrsLoadPreflight.ps1 -BaseUrl $normalizedBaseUrl -Mode $preflightMode"
            ".\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl $normalizedBaseUrl -Mode $Mode -Apply"
            ".\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -ShowSequence"
            ".\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -ShowRollbackFrom full-query"
        )
    }

    $report.passed = $true
    $json = $report | ConvertTo-Json -Depth 8
    if (Test-CqrsStagedOutputContainsSecrets -Text $json) {
        throw "Report output contains secret-like content."
    }
    Write-Output $json
    return
}

$instanceProbe = Test-CqrsStagedLocalApiSingleInstance -BaseUrl $normalizedBaseUrl
Add-Step -Name "single-instance-probe" -Passed $instanceProbe.singleInstance -Detail $instanceProbe
if ($instanceProbe.checked -and -not $instanceProbe.singleInstance) {
    throw "Multiple API listeners detected on port 7173. Abort rollout until instance count = 1 (CQRS-11D claim/lease)."
}

$tlsState = Enable-CqrsLoadLocalhostTlsBypass
try {
    $health = Invoke-CqrsLoadHealthReady `
        -BaseUrl $normalizedBaseUrl `
        -SkipCertificateCheck:($tlsState.UseSkipCertificateCheck -or $true)

    Add-Step -Name "health-ready" -Passed ([string]$health.status -eq "Healthy") -Detail @{
        status = $health.status
    }

    if ([string]$health.status -ne "Healthy") {
        throw "Health is not Healthy."
    }

    $projectionEntry = $health.results.'appointment-projection'
    $projectionData = $projectionEntry.data

    $flagChecks = @(
        @{ name = "projection-enabled"; actual = $projectionData.projectionEnabled; expected = $true },
        @{ name = "appointments-read-flag"; actual = $projectionData.appointmentsReadEnabled; expected = $definition.appointmentsReadEnabled },
        @{ name = "dashboard-read-flag"; actual = $projectionData.dashboardReadEnabled; expected = $definition.dashboardReadEnabled }
    )

    foreach ($check in $flagChecks) {
        $ok = Test-CqrsLoadBooleanMatch -Actual $check.actual -Expected $check.expected
        Add-Step -Name $check.name -Passed $ok -Detail @{
            expected = $check.expected
            actual   = $check.actual
        }
        if (-not $ok) {
            throw "Mode '$Mode' flag mismatch at $($check.name)."
        }
    }

    $pendingCount = [int]$projectionData.pendingCount
    $retryWaitingCount = [int]$projectionData.retryWaitingCount
    $deadLetterCount = [int]$projectionData.deadLetterCount
    $queueClean = ($pendingCount -eq 0 -and $retryWaitingCount -eq 0 -and $deadLetterCount -eq 0)
    Add-Step -Name "projection-queue-clean" -Passed $queueClean -Detail @{
        pendingCount      = $pendingCount
        retryWaitingCount = $retryWaitingCount
        deadLetterCount   = $deadLetterCount
    }
    if (-not $queueClean) {
        throw "Projection queue is not clean."
    }

    $querySqlEntry = $health.results.'query-sql'
    $queryHealthy = ([string]$querySqlEntry.status -eq "Healthy")
    Add-Step -Name "query-sql-health" -Passed $queryHealthy -Detail @{
        status      = $querySqlEntry.status
        description = $querySqlEntry.description
    }

    if (-not $SkipParity.IsPresent) {
        $parity = Get-CqrsStagedParityReport `
            -ServerInstance $ServerInstance `
            -CommandDatabase $CommandDatabase `
            -QueryDatabase $QueryDatabase

        $parityPassed = $parity.queueClean -and
            $parity.duplicateAppointmentIds -eq 0 -and
            $parity.invalidGuids -eq 0 -and
            $parity.invalidScheduleRange -eq 0

        if ($Mode -ne "command-read") {
            $parityPassed = $parityPassed -and $parity.parityMatched
        }

        Add-Step -Name "sql-parity" -Passed $parityPassed -Detail $parity
        if (-not $parityPassed) {
            throw "SQL parity/integrity checks failed."
        }
    }

    if (-not $SkipEndpointSmoke.IsPresent) {
        $resolvedTokenFile = Resolve-CqrsLoadTokenFile -TokenFile $TokenFile
        $accessToken = Get-CqrsStagedAccessToken -TokenFile $resolvedTokenFile
        $tokens = Get-Content -LiteralPath $resolvedTokenFile -Raw | ConvertFrom-Json
        $tokenEntry = $tokens | Where-Object { $_.slot -eq "01" } | Select-Object -First 1
        $clinicId = [string]$tokenEntry.clinicId

        if ([string]::IsNullOrWhiteSpace($clinicId)) {
            throw "Token slot 01 clinicId is required for endpoint smoke."
        }

        $fromUtc = (Get-CqrsStagedSlotAlignedUtcPlusDays -Days 1).ToString("o")
        $toUtc = (Get-CqrsStagedSlotAlignedUtcPlusDays -Days 14).ToString("o")
        $scheduledAtUtc = (Get-CqrsStagedSlotAlignedUtcPlusDays -Days 7).ToString("o")

        $smokeCalls = @(
            @{ name = "appointments-list"; method = "GET"; path = "/api/v1/appointments?Page=1&PageSize=5&clinicId=$clinicId"; body = $null; expect = 200 },
            @{ name = "appointments-calendar"; method = "GET"; path = "/api/v1/appointments/calendar?fromUtc=$fromUtc&toUtc=$toUtc&clinicId=$clinicId"; body = $null; expect = 200 },
            @{ name = "dashboard-summary"; method = "GET"; path = "/api/v1/dashboard/summary?clinicId=$clinicId"; body = $null; expect = 200 }
        )

        foreach ($call in $smokeCalls) {
            $response = Invoke-CqrsStagedApiRequest `
                -BaseUrl $normalizedBaseUrl `
                -Method $call.method `
                -Path $call.path `
                -AccessToken $accessToken `
                -JsonBody $call.body
            $ok = ($response.statusCode -eq $call.expect)
            Add-Step -Name ("endpoint-" + $call.name) -Passed $ok -Detail @{
                statusCode = $response.statusCode
                readSource = switch ($call.name) {
                    "appointments-list" { $definition.appointmentListSource }
                    "appointments-calendar" { $definition.appointmentCalendarSource }
                    "dashboard-summary" { $definition.dashboardAppointmentSource }
                    default { "n/a" }
                }
            }
            if (-not $ok) {
                throw "Endpoint smoke failed: $($call.name)"
            }
        }

        $createBody = (@{
                clinicId       = $clinicId
                petId          = [string]$tokenEntry.petId
                scheduledAtUtc = $scheduledAtUtc
                appointmentType = 0
                notes          = "CQRS11A_ROLLOUT_SMOKE"
            } | ConvertTo-Json -Compress)

        $createResponse = Invoke-CqrsStagedApiRequest `
            -BaseUrl $normalizedBaseUrl `
            -Method "POST" `
            -Path "/api/v1/appointments" `
            -AccessToken $accessToken `
            -JsonBody $createBody

        $createPassed = ($createResponse.statusCode -eq 201)
        Add-Step -Name "endpoint-create" -Passed $createPassed -Detail @{ statusCode = $createResponse.statusCode }
        if (-not $createPassed) {
            throw "Create appointment smoke failed."
        }

        $appointmentId = [string]($createResponse.body | ConvertFrom-Json)
        $rescheduleAtUtc = (Get-CqrsStagedSlotAlignedUtcPlusDays -Days 9).ToString("o")
        $rescheduleBody = (@{ scheduledAtUtc = $rescheduleAtUtc } | ConvertTo-Json -Compress)
        $rescheduleResponse = Invoke-CqrsStagedApiRequest `
            -BaseUrl $normalizedBaseUrl `
            -Method "POST" `
            -Path "/api/v1/appointments/$appointmentId/reschedule" `
            -AccessToken $accessToken `
            -JsonBody $rescheduleBody
        Add-Step -Name "endpoint-reschedule" -Passed ($rescheduleResponse.statusCode -eq 204) -Detail @{
            statusCode = $rescheduleResponse.statusCode
        }
        if ($rescheduleResponse.statusCode -ne 204) {
            throw "Reschedule smoke failed."
        }

        $deadline = (Get-Date).AddSeconds($PollTimeoutSeconds)
        $projected = $false
        while ((Get-Date) -lt $deadline) {
            $healthPoll = Invoke-CqrsLoadHealthReady -BaseUrl $normalizedBaseUrl -SkipCertificateCheck:$true
            $pending = [int]$healthPoll.results.'appointment-projection'.data.pendingCount
            if ($pending -eq 0) {
                $projected = $true
                break
            }
            Start-Sleep -Milliseconds 500
        }

        Add-Step -Name "projection-drain-after-lifecycle" -Passed $projected -Detail @{
            timeoutSeconds = $PollTimeoutSeconds
        }
        if (-not $projected) {
            throw "Projection queue did not drain after lifecycle smoke."
        }

        $detailResponse = Invoke-CqrsStagedApiRequest `
            -BaseUrl $normalizedBaseUrl `
            -Method "GET" `
            -Path "/api/v1/appointments/$appointmentId" `
            -AccessToken $accessToken
        Add-Step -Name "endpoint-detail" -Passed ($detailResponse.statusCode -eq 200) -Detail @{
            statusCode = $detailResponse.statusCode
        }

        $cancelBody = (@{ reason = "CQRS11A rollback smoke" } | ConvertTo-Json -Compress)
        $cancelResponse = Invoke-CqrsStagedApiRequest `
            -BaseUrl $normalizedBaseUrl `
            -Method "POST" `
            -Path "/api/v1/appointments/$appointmentId/cancel" `
            -AccessToken $accessToken `
            -JsonBody $cancelBody
        Add-Step -Name "endpoint-cancel" -Passed ($cancelResponse.statusCode -eq 204) -Detail @{
            statusCode = $cancelResponse.statusCode
        }
        if ($cancelResponse.statusCode -ne 204) {
            throw "Cancel smoke failed."
        }
    }

    $queryFailureNote = [ordered]@{
        automaticCommandFallback = $false
        expectedBehavior         = @(
            "Query DB outage => /health/ready query-sql Unhealthy"
            "Mode B/C read endpoints fail without silent Command fallback"
            "Operational rollback => Mode A or B flags + restart + verify Command reads"
            "After Query DB repair => rebuild/parity => re-enable Mode B/C"
        )
    }
    Add-Step -Name "query-db-failure-policy" -Passed $true -Detail $queryFailureNote
}
finally {
    Disable-CqrsLoadLocalhostTlsBypass -TlsState $tlsState
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
if (-not $report.passed) {
    exit 1
}
