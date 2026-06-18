#Requires -Version 5.1
<#
.SYNOPSIS
  Tek appointment ile reschedule projection diagnostic (API + SQL timestamp zinciri).

.NOTES
  Create/reschedule/cancel contract: tests/load/panel-appointment-write-mix.js ile ayni flat JSON.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://localhost:7173",
    [string]$TokenFile,
    [int]$PollTimeoutSeconds = 30,
    [int]$PollIntervalMs = 200
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsLoadCommon.ps1")

$resolvedTokenFile = Resolve-CqrsLoadTokenFile -TokenFile $TokenFile
$commandDb = "VetinityCommandDb_LoadTest"
$queryDb = "VetinityQueryDb_LoadTest"
$server = "localhost"
$apiRoot = $BaseUrl.Trim().TrimEnd("/")
$APPOINTMENT_TYPE_EXAMINATION = 0

function Invoke-SqlScalar {
    param([string]$Database, [string]$Query)
    $result = sqlcmd -S $server -d $Database -Q $Query -h -1 -W 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $Database" }
    return ($result | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
}

function Invoke-SqlRows {
    param([string]$Database, [string]$Query)
    return @(sqlcmd -S $server -d $Database -Q $Query -W -s "|" 2>&1)
}

function Get-AccessToken {
    $tokens = Get-Content -LiteralPath $resolvedTokenFile -Raw | ConvertFrom-Json
    $entry = $tokens | Where-Object { $_.slot -eq "01" } | Select-Object -First 1
    if (-not $entry) { throw "Token slot 01 bulunamadi." }
    return [string]$entry.accessToken
}

function ConvertTo-JsonBody {
    param([hashtable]$Payload)
    return ($Payload | ConvertTo-Json -Depth 10 -Compress)
}

function Get-ProblemDetailsSummary {
    param(
        [int]$StatusCode,
        [string]$Body
    )

    $summary = [ordered]@{
        statusCode = $StatusCode
        code       = $null
        title      = $null
        detail     = $null
        errors     = @()
    }

    if ([string]::IsNullOrWhiteSpace($Body)) {
        return $summary
    }

    try {
        $problem = $Body | ConvertFrom-Json
        if ($problem.PSObject.Properties.Name -contains "title") {
            $summary.title = [string]$problem.title
        }
        if ($problem.PSObject.Properties.Name -contains "detail") {
            $detail = [string]$problem.detail
            if ($detail.Length -gt 240) {
                $detail = $detail.Substring(0, 240)
            }
            $summary.detail = $detail
        }
        if ($problem.extensions -and ($problem.extensions.PSObject.Properties.Name -contains "code")) {
            $summary.code = [string]$problem.extensions.code
        }
        if ($problem.errors) {
            foreach ($prop in $problem.errors.PSObject.Properties) {
                $summary.errors += [string]$prop.Name
            }
        }
    }
    catch {
        $summary.detail = "Non-JSON error body"
    }

    return $summary
}

function Invoke-ApiRequest {
    param(
        [string]$Method,
        [string]$Path,
        [string]$AccessToken,
        [string]$JsonBody = $null
    )

    $responseFile = [System.IO.Path]::GetTempFileName()
    $bodyFile = $null

    try {
        $curlArgs = @(
            "-k", "-s",
            "-X", $Method,
            "$apiRoot$Path",
            "-H", "Authorization: Bearer $AccessToken",
            "-H", "Accept: application/json",
            "-o", $responseFile,
            "-w", "%{http_code}"
        )

        if ($JsonBody) {
            $bodyFile = [System.IO.Path]::GetTempFileName()
            $utf8NoBom = New-Object System.Text.UTF8Encoding $false
            [System.IO.File]::WriteAllText($bodyFile, $JsonBody, $utf8NoBom)
            $curlArgs += @(
                "-H", "Content-Type: application/json; charset=utf-8",
                "--data-binary", "@$bodyFile"
            )
        }

        $statusRaw = & curl.exe @curlArgs 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "curl failed: $Method $Path (exit=$LASTEXITCODE)"
        }

        $statusCode = [int]$statusRaw
        $body = ""
        if (Test-Path -LiteralPath $responseFile) {
            $body = [System.IO.File]::ReadAllText($responseFile)
        }

        return @{
            StatusCode = $statusCode
            Body       = $body
        }
    }
    finally {
        if (Test-Path -LiteralPath $responseFile) {
            Remove-Item -LiteralPath $responseFile -Force -ErrorAction SilentlyContinue
        }
        if ($bodyFile -and (Test-Path -LiteralPath $bodyFile)) {
            Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
        }
    }
}

function Parse-CreatedAppointmentId {
    param(
        [int]$StatusCode,
        [string]$Body
    )

    if ($StatusCode -ne 201) {
        $problem = Get-ProblemDetailsSummary -StatusCode $StatusCode -Body $Body
        throw "Appointment create failed with HTTP $StatusCode (code=$($problem.code); errors=$([string]::Join(',', $problem.errors)))."
    }

    $rawBody = $Body.Trim()
    if ([string]::IsNullOrWhiteSpace($rawBody)) {
        throw "Appointment create succeeded but response did not contain the expected appointment ID."
    }

    try {
        $parsed = $rawBody | ConvertFrom-Json
        if ($parsed -is [string] -and -not [string]::IsNullOrWhiteSpace($parsed)) {
            return [guid]$parsed
        }
        if ($parsed -and ($parsed.PSObject.Properties.Name -contains "id") -and $parsed.id) {
            return [guid]$parsed.id
        }
    }
    catch {
        # plain JSON string body
    }

    $unquoted = $rawBody.Trim().Trim('"')
    try {
        return [guid]$unquoted
    }
    catch {
        throw "Appointment create succeeded but response did not contain the expected appointment ID."
    }
}

function ConvertTo-IstanbulDayOfWeek {
    param([DateTime]$UtcMidnight)
    $shifted = $UtcMidnight.AddMinutes(180)
    return [int]$shifted.DayOfWeek
}

function Get-WeekdaySlotCapacity {
    param([int]$DayOfWeek)
    if ($DayOfWeek -eq 0) { return 0 }

    $workEndLocalMinutes = if ($DayOfWeek -eq 6) { 14 * 60 } else { 18 * 60 }
    $lastStartLocalMinutes = $workEndLocalMinutes - 30
    if ($lastStartLocalMinutes -lt (9 * 60)) { return 0 }

    return [int][math]::Floor(($lastStartLocalMinutes - (9 * 60)) / 15) + 1
}

function Convert-IstanbulLocalMinutesToUtc {
    param(
        [DateTime]$DayCursorUtcMidnight,
        [int]$LocalMinutes
    )

    $localHour = [math]::Floor($LocalMinutes / 60)
    $localMinute = $LocalMinutes % 60
    $utcHour = $localHour - 3
    $scheduled = $DayCursorUtcMidnight

    if ($utcHour -lt 0) {
        $scheduled = $scheduled.AddDays(-1)
        $utcHour += 24
    }

    return $scheduled.AddHours($utcHour).AddMinutes($localMinute)
}

function Build-ScheduledAtUtcFromLinearSlot {
    param(
        [int]$LinearSlot,
        [int]$DayOffsetSeed
    )

    $remaining = $LinearSlot
    $dayOffset = 120 + $DayOffsetSeed
    $nowMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $maxFutureMs = $nowMs + (2 * 365 * 24 * 60 * 60 * 1000)

    for ($guard = 0; $guard -lt 5000; $guard++) {
        $cursor = [DateTime]::UtcNow.Date.AddDays($dayOffset)
        $capacity = Get-WeekdaySlotCapacity -DayOfWeek (ConvertTo-IstanbulDayOfWeek -UtcMidnight $cursor)

        if ($capacity -eq 0) {
            $dayOffset += 1
            continue
        }

        if ($remaining -lt $capacity) {
            $localMinutes = (9 * 60) + ($remaining * 15)
            $scheduled = Convert-IstanbulLocalMinutesToUtc -DayCursorUtcMidnight $cursor -LocalMinutes $localMinutes
            $scheduledMs = [DateTimeOffset]::new($scheduled, [TimeSpan]::Zero).ToUnixTimeMilliseconds()

            if ($scheduledMs -gt $nowMs -and $scheduledMs -le $maxFutureMs) {
                return $scheduled.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            }

            return $null
        }

        $remaining -= $capacity
        $dayOffset += 1
    }

    return $null
}

function Build-DiagnosticWriteSchedule {
    param(
        [int]$UniqueSeed
    )

    $blockIndex = 900 + ($UniqueSeed % 500)
    $createLinearSlot = $blockIndex * 4
    $rescheduleLinearSlot = $createLinearSlot + 3
    $dayOffsetSeed = 80 + ($UniqueSeed % 40)

    $createIso = Build-ScheduledAtUtcFromLinearSlot -LinearSlot $createLinearSlot -DayOffsetSeed $dayOffsetSeed
    $rescheduleIso = Build-ScheduledAtUtcFromLinearSlot -LinearSlot $rescheduleLinearSlot -DayOffsetSeed $dayOffsetSeed

    if (-not $createIso -or -not $rescheduleIso) {
        throw "Diagnostic schedule generation failed (collision-free slot not found)."
    }

    return @{
        createScheduledAtUtc     = $createIso
        rescheduleScheduledAtUtc = $rescheduleIso
        blockIndex               = $blockIndex
        dayOffsetSeed            = $dayOffsetSeed
    }
}

function To-EpochMs {
    param($Iso)
    if ($null -eq $Iso) { return $null }

    if ($Iso -is [DateTime]) {
        return [DateTimeOffset]::new($Iso.ToUniversalTime(), [TimeSpan]::Zero).ToUnixTimeMilliseconds()
    }

    $text = [string]$Iso
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }

    $normalized = $text.Trim()
    if ($normalized -notmatch 'Z|[+-]\d{2}:\d{2}$') {
        $normalized = $normalized + "Z"
    }

    try {
        return [DateTimeOffset]::Parse(
            $normalized,
            [Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AssumeUniversal
        ).ToUnixTimeMilliseconds()
    }
    catch {
        return [DateTimeOffset]::Parse($text).ToUnixTimeMilliseconds()
    }
}

function ScheduledAtMatches {
    param(
        [string]$ActualIso,
        [string]$ExpectedIso
    )

    $actualMs = To-EpochMs $ActualIso
    $expectedMs = To-EpochMs $ExpectedIso
    if ($null -eq $actualMs -or $null -eq $expectedMs) {
        return $false
    }
    return ([math]::Abs($actualMs - $expectedMs) -lt 1000)
}

function Get-QueryAppointmentViaList {
    param(
        [string]$AccessToken,
        [guid]$AppointmentId,
        [guid]$PetId,
        [string]$Search,
        [string]$DateFromUtc,
        [string]$DateToUtc
    )

    $qs = "page=1&pageSize=50&petId=$PetId&search=$([uri]::EscapeDataString($Search))&dateFromUtc=$([uri]::EscapeDataString($DateFromUtc))&dateToUtc=$([uri]::EscapeDataString($DateToUtc))"
    $response = Invoke-ApiRequest -Method "GET" -Path "/api/v1/appointments?$qs" -AccessToken $AccessToken
    if ($response.StatusCode -ne 200) {
        return $null
    }

    $json = $response.Body | ConvertFrom-Json
    if (-not $json -or -not ($json.PSObject.Properties.Name -contains "items")) {
        return $null
    }

    return ($json.items | Where-Object { $_.id -eq $AppointmentId } | Select-Object -First 1)
}

function Wait-QueryProjection {
    param(
        [string]$AccessToken,
        [guid]$AppointmentId,
        [guid]$PetId,
        [string]$Search,
        [string]$DateFromUtc,
        [string]$DateToUtc,
        [scriptblock]$Predicate
    )

    $started = [DateTime]::UtcNow
    $deadline = $started.AddSeconds($PollTimeoutSeconds)

    while ([DateTime]::UtcNow -lt $deadline) {
        $item = Get-QueryAppointmentViaList -AccessToken $AccessToken -AppointmentId $AppointmentId `
            -PetId $PetId -Search $Search -DateFromUtc $DateFromUtc -DateToUtc $DateToUtc
        if ($item -and (& $Predicate $item)) {
            $lagMs = [int]([DateTime]::UtcNow - $started).TotalMilliseconds
            return @{ Ok = $true; Item = $item; LagMs = $lagMs }
        }
        Start-Sleep -Milliseconds $PollIntervalMs
    }

    return @{ Ok = $false; Item = $null; LagMs = [int]($PollTimeoutSeconds * 1000) }
}

$accessToken = Get-AccessToken

$petResponse = Invoke-ApiRequest -Method "GET" -Path "/api/v1/pets?page=1&pageSize=5" -AccessToken $accessToken
if ($petResponse.StatusCode -ne 200) {
    $problem = Get-ProblemDetailsSummary -StatusCode $petResponse.StatusCode -Body $petResponse.Body
    throw "Pet listesi alinamadi (HTTP $($petResponse.StatusCode); code=$($problem.code))."
}

$petJson = $petResponse.Body | ConvertFrom-Json
if (-not $petJson -or -not ($petJson.PSObject.Properties.Name -contains "items") -or @($petJson.items).Count -eq 0) {
    throw "Pet listesi bos."
}

$runTag = "diag-" + [DateTime]::UtcNow.ToString("yyyyMMddHHmmss")
$searchTag = $runTag
$uniqueSeed = [int]([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() % 1000000)
$petIndex = $uniqueSeed % @($petJson.items).Count
$petId = [guid]$petJson.items[$petIndex].id

$schedule = Build-DiagnosticWriteSchedule -UniqueSeed $uniqueSeed
$createIso = $schedule.createScheduledAtUtc
$rescheduleIso = $schedule.rescheduleScheduledAtUtc
$note = "K6_LOAD_TEST $searchTag vu=diag"

$from = ([DateTimeOffset]::Parse($createIso, [Globalization.CultureInfo]::InvariantCulture)).UtcDateTime.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
$to = ([DateTimeOffset]::Parse($createIso, [Globalization.CultureInfo]::InvariantCulture)).UtcDateTime.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

$timeline = [ordered]@{}
$createJson = ConvertTo-JsonBody -Payload @{
    petId           = $petId.ToString()
    scheduledAtUtc  = $createIso
    appointmentType = $APPOINTMENT_TYPE_EXAMINATION
    notes           = $note
}

$createResponse = Invoke-ApiRequest -Method "POST" -Path "/api/v1/appointments" -AccessToken $accessToken -JsonBody $createJson
$timeline["createHttpCompletedUtc"] = [DateTime]::UtcNow.ToString("o")
$timeline["createHttpStatus"] = $createResponse.StatusCode
$timeline["createRequestShape"] = "flat-json-no-cmd-wrapper"
$timeline["scheduleBlockIndex"] = $schedule.blockIndex
$timeline["scheduleDayOffsetSeed"] = $schedule.dayOffsetSeed

if ($createResponse.StatusCode -ne 201) {
    $timeline["createProblem"] = Get-ProblemDetailsSummary -StatusCode $createResponse.StatusCode -Body $createResponse.Body
    throw "Appointment create failed with HTTP $($createResponse.StatusCode)."
}

$appointmentId = Parse-CreatedAppointmentId -StatusCode $createResponse.StatusCode -Body $createResponse.Body

$createPoll = Wait-QueryProjection -AccessToken $accessToken -AppointmentId $appointmentId -PetId $petId `
    -Search $searchTag -DateFromUtc $from -DateToUtc $to -Predicate { param($i) $true }
$timeline["createPollOk"] = $createPoll.Ok
$timeline["createProjectionLagMs"] = $createPoll.LagMs
$timeline["createQueryVisibleUtc"] = [DateTime]::UtcNow.ToString("o")

$rescheduleJson = ConvertTo-JsonBody -Payload @{
    scheduledAtUtc = $rescheduleIso
}
$rescheduleResponse = Invoke-ApiRequest -Method "POST" -Path "/api/v1/appointments/$appointmentId/reschedule" `
    -AccessToken $accessToken -JsonBody $rescheduleJson
$timeline["rescheduleHttpStatus"] = $rescheduleResponse.StatusCode
$timeline["rescheduleHttpCompletedUtc"] = [DateTime]::UtcNow.ToString("o")

if ($rescheduleResponse.StatusCode -ne 204) {
    $timeline["rescheduleProblem"] = Get-ProblemDetailsSummary -StatusCode $rescheduleResponse.StatusCode -Body $rescheduleResponse.Body
}

Start-Sleep -Milliseconds 500
$outboxRows = Invoke-SqlRows -Database $commandDb -Query @"
SET NOCOUNT ON;
SELECT TOP 1 Type,
       CONVERT(varchar(33), CreatedAtUtc, 126),
       CONVERT(varchar(33), ProcessedAtUtc, 126),
       RetryCount,
       ISNULL(LEFT(LastError, 120), ''),
       CONVERT(varchar(33), DeadLetterAtUtc, 126)
FROM OutboxMessages
WHERE Payload LIKE '%$($appointmentId.ToString())%'
  AND Type = 'appointment.rescheduled.v1'
ORDER BY CreatedAtUtc DESC;
"@

$resFrom = ([DateTimeOffset]::Parse($rescheduleIso, [Globalization.CultureInfo]::InvariantCulture)).UtcDateTime.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
$resTo = ([DateTimeOffset]::Parse($rescheduleIso, [Globalization.CultureInfo]::InvariantCulture)).UtcDateTime.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

$resPoll = Wait-QueryProjection -AccessToken $accessToken -AppointmentId $appointmentId -PetId $petId `
    -Search $searchTag -DateFromUtc $resFrom -DateToUtc $resTo -Predicate {
        param($i)
        ScheduledAtMatches -ActualIso $i.scheduledAtUtc -ExpectedIso $rescheduleIso
    }

$timeline["reschedulePollOk"] = $resPoll.Ok
$timeline["rescheduleProjectionLagMs"] = $resPoll.LagMs
$timeline["rescheduleQueryMatchedUtc"] = [DateTime]::UtcNow.ToString("o")
if ($resPoll.Item) {
    $timeline["queryScheduledAtUtc"] = $resPoll.Item.scheduledAtUtc
    $timeline["expectedScheduledAtUtc"] = $rescheduleIso
    $timeline["epochDeltaMs"] = (To-EpochMs $resPoll.Item.scheduledAtUtc) - (To-EpochMs $rescheduleIso)
}

$queryRow = Invoke-SqlRows -Database $queryDb -Query @"
SET NOCOUNT ON;
SELECT CONVERT(varchar(33), ScheduledAtUtc, 126),
       CONVERT(varchar(33), ScheduledEndUtc, 126),
       CONVERT(varchar(36), LastEventId),
       CONVERT(varchar(33), LastProjectedAtUtc, 126),
       Status
FROM AppointmentReadModels WHERE AppointmentId = '$($appointmentId.ToString())';
"@

$cmdScheduled = Invoke-SqlScalar -Database $commandDb -Query "SET NOCOUNT ON; SELECT CONVERT(varchar(33), ScheduledAtUtc, 126) FROM Appointments WHERE Id = '$($appointmentId.ToString())';"

$verdict = "unknown"
if ($resPoll.Ok) {
    $verdict = "ok-projection-and-poll"
}
elseif ($timeline["rescheduleHttpStatus"] -ne 204) {
    $verdict = "A-command-http-failed"
}
elseif ($outboxRows.Count -lt 2) {
    $verdict = "A-no-outbox-event"
}
elseif (($outboxRows[1] -split "\|")[2].Trim() -eq "") {
    $verdict = "B-outbox-not-processed"
}
else {
    $queryScheduled = ($queryRow[1] -split "\|")[0].Trim()
    if (ScheduledAtMatches -ActualIso $queryScheduled -ExpectedIso $rescheduleIso) {
        $verdict = "D-k6-poll-script-logic"
    }
    else {
        $verdict = "C-query-db-stale-after-processed"
    }
}

[ordered]@{
    appointmentId            = $appointmentId
    petId                    = $petId
    createScheduledAtUtc     = $createIso
    rescheduleScheduledAtUtc = $rescheduleIso
    timeline                 = $timeline
    commandScheduledAtUtc    = $cmdScheduled
    outboxReschedule         = $outboxRows
    queryReadModel           = $queryRow
    verdict                  = $verdict
    projectionIsReal         = ($verdict -in @("B-outbox-not-processed", "C-query-db-stale-after-processed"))
    scriptIsCause            = ($verdict -eq "D-k6-poll-script-logic")
} | ConvertTo-Json -Depth 6
