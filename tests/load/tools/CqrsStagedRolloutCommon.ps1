#Requires -Version 5.1
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "CqrsLoadCommon.ps1")

function ConvertTo-CqrsStagedPreflightMode {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("command-read", "appointment-query", "full-query")]
        [string]$Mode
    )

    switch ($Mode) {
        "command-read" { return "command" }
        "appointment-query" { return "appointment-query" }
        "full-query" { return "full-query" }
    }
}

function Get-CqrsStagedModeDefinition {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("command-read", "appointment-query", "full-query")]
        [string]$Mode
    )

    switch ($Mode) {
        "command-read" {
            return [ordered]@{
                mode                        = $Mode
                label                       = "Mode A - Command read"
                appointmentsReadEnabled     = $false
                dashboardReadEnabled        = $false
                appointmentProjectionEnabled = $true
                appointmentListSource       = "Command DB"
                appointmentCalendarSource   = "Command DB"
                dashboardAppointmentSource  = "Command DB"
            }
        }
        "appointment-query" {
            return [ordered]@{
                mode                        = $Mode
                label                       = "Mode B - Appointment query read"
                appointmentsReadEnabled     = $true
                dashboardReadEnabled        = $false
                appointmentProjectionEnabled = $true
                appointmentListSource       = "Query DB"
                appointmentCalendarSource   = "Query DB"
                dashboardAppointmentSource  = "Command DB"
            }
        }
        "full-query" {
            return [ordered]@{
                mode                        = $Mode
                label                       = "Mode C - Full query read"
                appointmentsReadEnabled     = $true
                dashboardReadEnabled        = $true
                appointmentProjectionEnabled = $true
                appointmentListSource       = "Query DB"
                appointmentCalendarSource   = "Query DB"
                dashboardAppointmentSource  = "Query DB"
            }
        }
    }
}

function Get-CqrsStagedModeEnvironmentOverrides {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("command-read", "appointment-query", "full-query")]
        [string]$Mode
    )

    $definition = Get-CqrsStagedModeDefinition -Mode $Mode
    return [ordered]@{
        "QueryReadModels__AppointmentsEnabled"          = [string]$definition.appointmentsReadEnabled
        "QueryReadModels__DashboardAppointmentsEnabled"   = [string]$definition.dashboardReadEnabled
        "AppointmentProjection__Enabled"                  = [string]$definition.appointmentProjectionEnabled
    }
}

function Get-CqrsStagedRolloutSequence {
    return @(
        "command-read",
        "appointment-query",
        "full-query",
        "appointment-query",
        "command-read"
    )
}

function Get-CqrsStagedRollbackPlan {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("full-query", "appointment-query", "command-read")]
        [string]$FromMode
    )

    switch ($FromMode) {
        "full-query" {
            return [ordered]@{
                fromMode = $FromMode
                toMode   = "appointment-query"
                steps    = @(
                    [ordered]@{
                        action = "Set environment overrides"
                        overrides = Get-CqrsStagedModeEnvironmentOverrides -Mode "appointment-query"
                    },
                    [ordered]@{
                        action = "Restart/deploy single API instance (projector stays enabled)"
                        note   = "Dashboard returns to Command DB; appointment list/calendar remain on Query DB."
                    },
                    [ordered]@{
                        action = "Run staged rollout validation"
                        command = ".\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl {url} -Mode appointment-query -Apply"
                    }
                )
            }
        }
        "appointment-query" {
            return [ordered]@{
                fromMode = $FromMode
                toMode   = "command-read"
                steps    = @(
                    [ordered]@{
                        action = "Set environment overrides"
                        overrides = Get-CqrsStagedModeEnvironmentOverrides -Mode "command-read"
                    },
                    [ordered]@{
                        action = "Restart/deploy single API instance (projector stays enabled)"
                        note   = "All appointment reads return to Command DB; Query projection continues updating in background."
                    },
                    [ordered]@{
                        action = "Run staged rollout validation"
                        command = ".\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl {url} -Mode command-read -Apply"
                    }
                )
            }
        }
        default {
            throw "Rollback plan is only defined for full-query or appointment-query source modes."
        }
    }
}

function Get-CqrsStagedDatabaseCatalog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString
    )

    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        return "(not-configured)"
    }

    if ($ConnectionString -match '(?i)Database=([^;]+)') {
        return $Matches[1].Trim()
    }

    if ($ConnectionString -match '(?i)Initial Catalog=([^;]+)') {
        return $Matches[1].Trim()
    }

    return "(unparseable)"
}

function Test-CqrsStagedHostAllowed {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl
    )

    return (Test-CqrsLoadLocalOrAllowedHost -BaseUrl $BaseUrl)
}

function Test-CqrsStagedOutputContainsSecrets {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $patterns = @(
        'Password\s*=',
        'Pwd\s*=',
        'Jwt__Key',
        'SecretKey',
        'ApiKey\s*=',
        'Bearer\s+eyJ'
    )

    foreach ($pattern in $patterns) {
        if ($Text -match $pattern) {
            return $true
        }
    }

    return $false
}

function Invoke-CqrsStagedSqlScalar {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerInstance,
        [Parameter(Mandatory = $true)]
        [string]$Database,
        [Parameter(Mandatory = $true)]
        [string]$Query
    )

    $result = sqlcmd -S $ServerInstance -d $Database -Q $Query -h -1 -W 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed for database '$Database'."
    }

    $line = ($result | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    return [string]$line
}

function Get-CqrsStagedParityReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerInstance,
        [Parameter(Mandatory = $true)]
        [string]$CommandDatabase,
        [Parameter(Mandatory = $true)]
        [string]$QueryDatabase
    )

    if ($CommandDatabase -eq $QueryDatabase) {
        throw "Command and Query database catalogs must differ (both='$CommandDatabase')."
    }

    $appointmentCount = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $CommandDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Appointments;")
    $readModelCount = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM AppointmentReadModels;")
    $duplicateAppointmentIds = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM (SELECT AppointmentId FROM AppointmentReadModels GROUP BY AppointmentId HAVING COUNT(*) > 1) d;")
    $invalidGuids = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM AppointmentReadModels WHERE TRY_CONVERT(uniqueidentifier, AppointmentId) IS NULL;")
    $invalidScheduleRange = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $QueryDatabase `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM AppointmentReadModels WHERE ScheduledEndUtc < ScheduledAtUtc;")
    $pendingOutbox = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $CommandDatabase `
        -Query @"
SET NOCOUNT ON;
SELECT COUNT(*)
FROM OutboxMessages
WHERE Type IN (
    'appointment.created.v1',
    'appointment.updated.v1',
    'appointment.rescheduled.v1',
    'appointment.cancelled.v1',
    'appointment.completed.v1'
)
AND ProcessedAtUtc IS NULL
AND DeadLetterAtUtc IS NULL
AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= SYSUTCDATETIME());
"@)
    $retryWaiting = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $CommandDatabase `
        -Query @"
SET NOCOUNT ON;
SELECT COUNT(*)
FROM OutboxMessages
WHERE Type IN (
    'appointment.created.v1',
    'appointment.updated.v1',
    'appointment.rescheduled.v1',
    'appointment.cancelled.v1',
    'appointment.completed.v1'
)
AND ProcessedAtUtc IS NULL
AND DeadLetterAtUtc IS NULL
AND NextAttemptAtUtc > SYSUTCDATETIME();
"@)
    $deadLetter = [int](Invoke-CqrsStagedSqlScalar `
        -ServerInstance $ServerInstance `
        -Database $CommandDatabase `
        -Query @"
SET NOCOUNT ON;
SELECT COUNT(*)
FROM OutboxMessages
WHERE Type IN (
    'appointment.created.v1',
    'appointment.updated.v1',
    'appointment.rescheduled.v1',
    'appointment.cancelled.v1',
    'appointment.completed.v1'
)
AND DeadLetterAtUtc IS NOT NULL;
"@)

    return [ordered]@{
        commandDatabase          = $CommandDatabase
        queryDatabase            = $QueryDatabase
        appointmentCount         = $appointmentCount
        appointmentReadModelCount = $readModelCount
        parityMatched            = ($appointmentCount -eq $readModelCount)
        duplicateAppointmentIds  = $duplicateAppointmentIds
        invalidGuids             = $invalidGuids
        invalidScheduleRange     = $invalidScheduleRange
        pendingOutbox            = $pendingOutbox
        retryWaitingOutbox       = $retryWaiting
        deadLetterOutbox         = $deadLetter
        queueClean               = ($pendingOutbox -eq 0 -and $retryWaiting -eq 0 -and $deadLetter -eq 0)
    }
}

function Get-CqrsStagedAccessToken {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TokenFile
    )

    $resolvedTokenFile = Resolve-CqrsLoadTokenFile -TokenFile $TokenFile
    if (-not (Test-Path -LiteralPath $resolvedTokenFile)) {
        throw "Token file not found: $resolvedTokenFile"
    }

    $tokens = Get-Content -LiteralPath $resolvedTokenFile -Raw | ConvertFrom-Json
    $entry = $tokens | Where-Object { $_.slot -eq "01" } | Select-Object -First 1
    if (-not $entry) {
        throw "Token slot 01 not found in $resolvedTokenFile"
    }

    return [string]$entry.accessToken
}

function Invoke-CqrsStagedApiRequest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,
        [Parameter(Mandatory = $true)]
        [string]$Method,
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$AccessToken,
        [string]$JsonBody = $null
    )

    $responseFile = [System.IO.Path]::GetTempFileName()
    $bodyFile = $null

    try {
        $curlArgs = @(
            "-k", "-s",
            "-X", $Method,
            "$($BaseUrl.Trim().TrimEnd('/'))$Path",
            "-H", "Authorization: Bearer $AccessToken",
            "-H", "Accept: application/json",
            "-o", $responseFile,
            "-w", "%{http_code}"
        )

        if (-not [string]::IsNullOrWhiteSpace($JsonBody)) {
            $bodyFile = [System.IO.Path]::GetTempFileName()
            Set-Content -LiteralPath $bodyFile -Value $JsonBody -Encoding UTF8 -NoNewline
            $curlArgs += @("-H", "Content-Type: application/json", "--data-binary", "@$bodyFile")
        }

        $statusCode = & curl.exe @curlArgs 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "curl failed for $Method $Path"
        }

        $body = Get-Content -LiteralPath $responseFile -Raw -ErrorAction SilentlyContinue
        return [ordered]@{
            statusCode = [int]$statusCode
            body       = [string]$body
        }
    }
    finally {
        if ($null -ne $bodyFile -and (Test-Path -LiteralPath $bodyFile)) {
            Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $responseFile) {
            Remove-Item -LiteralPath $responseFile -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-CqrsStagedSlotAlignedUtcPlusDays {
    param([int]$Days)

    $date = [DateTime]::UtcNow.Date.AddDays($Days)
    while ($date.DayOfWeek -eq [DayOfWeek]::Saturday -or $date.DayOfWeek -eq [DayOfWeek]::Sunday) {
        $date = $date.AddDays(1)
    }

    return $date.AddHours(9)
}

function Test-CqrsStagedLocalApiSingleInstance {
    param(
        [string]$BaseUrl = "https://localhost:7173"
    )

    if (-not (Test-CqrsStagedHostAllowed -BaseUrl $BaseUrl)) {
        return [ordered]@{
            checked = $false
            reason  = "Single-instance probe only runs for localhost or CQRS_LOAD_ALLOWED_HOST."
        }
    }

    $listeners = @(Get-NetTCPConnection -LocalPort 7173 -State Listen -ErrorAction SilentlyContinue)
    $processIds = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique | Where-Object { $_ -gt 0 })

    return [ordered]@{
        checked       = $true
        listenerCount = $listeners.Count
        processCount  = $processIds.Count
        processIds    = $processIds
        singleInstance = ($processIds.Count -le 1)
    }
}
