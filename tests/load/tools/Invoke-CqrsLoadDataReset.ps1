#Requires -Version 5.1
<#
.SYNOPSIS
  Load-test command/query DB veri esitligi icin deterministik hazirlik ve sayim raporu.

.PARAMETER Method
  rebuild-only: yalnizca rebuild-appointment-projections
  full: migrate + migrate-query + loadtest-seed small + rebuild

.PARAMETER BatchSize
  rebuild batch size (varsayilan 1000).

.PARAMETER ServerInstance
  SQL Server instance (varsayilan localhost).

.PARAMETER SkipMigrator
  DbMigrator adimlarini atla; yalnizca sayim raporu.
#>
[CmdletBinding()]
param(
    [ValidateSet("rebuild-only", "full")]
    [string]$Method = "rebuild-only",

    [int]$BatchSize = 1000,

    [string]$ServerInstance = "localhost",

    [switch]$SkipMigrator
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "CqrsLoadCommon.ps1")

$CommandDb = "VetinityCommandDb_LoadTest"
$QueryDb = "VetinityQueryDb_LoadTest"
$repoRoot = Get-CqrsLoadRepositoryRoot

function Invoke-SqlScalar {
    param(
        [string]$Database,
        [string]$Query
    )

    $result = sqlcmd -S $ServerInstance -d $Database -Q $Query -h -1 -W 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed for $Database"
    }

    $line = ($result | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    return [string]$line
}

function Get-EntityCounts {
    return [ordered]@{
        Tenants               = [int](Invoke-SqlScalar -Database $CommandDb -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Tenants;")
        Clinics               = [int](Invoke-SqlScalar -Database $CommandDb -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Clinics;")
        Clients               = [int](Invoke-SqlScalar -Database $CommandDb -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Clients;")
        Pets                  = [int](Invoke-SqlScalar -Database $CommandDb -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Pets;")
        Appointments          = [int](Invoke-SqlScalar -Database $CommandDb -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Appointments;")
        AppointmentReadModels = [int](Invoke-SqlScalar -Database $QueryDb -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM AppointmentReadModels;")
        K6LoadTestAppointments = [int](Invoke-SqlScalar -Database $CommandDb -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM Appointments WHERE Notes LIKE 'K6_LOAD_TEST%';")
    }
}

function Remove-K6LoadTestAppointments {
    $pending = [int](Invoke-SqlScalar -Database $CommandDb -Query @"
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
AND DeadLetterAtUtc IS NULL;
"@)

    if ($pending -gt 0) {
        throw "K6 cleanup oncesi appointment outbox pending/retry > 0 ($pending). Projector durdurulup kuyruk bosaltilmali."
    }

    sqlcmd -S $ServerInstance -d $CommandDb -Q @"
SET NOCOUNT ON;
DELETE FROM Appointments WHERE Notes LIKE 'K6_LOAD_TEST%';
"@ | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "K6_LOAD_TEST appointment cleanup failed."
    }
}

$env:DOTNET_ENVIRONMENT = "LoadTest"
$env:ASPNETCORE_ENVIRONMENT = "LoadTest"

# appsettings.json bos connection string birlestirmesini guvenli sekilde override eder (secret loglanmaz).
if (-not $env:ConnectionStrings__DefaultConnection) {
    $env:ConnectionStrings__DefaultConnection =
        "Server=localhost;Database=VetinityCommandDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
}
if (-not $env:ConnectionStrings__QueryConnection) {
    $env:ConnectionStrings__QueryConnection =
        "Server=localhost;Database=VetinityQueryDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
}
if (-not $env:Jwt__Key) {
    $env:Jwt__Key = "loadtest-local-jwt-signing-key-32chars-min"
}

if (-not $SkipMigrator) {
    Push-Location $repoRoot
    try {
        if ($Method -eq "full") {
            dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate
            if ($LASTEXITCODE -ne 0) { throw "migrate failed." }

            dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
            if ($LASTEXITCODE -ne 0) { throw "migrate-query failed." }

            dotnet run --project src/Backend.Veteriner.DbMigrator -- seed
            if ($LASTEXITCODE -ne 0) { throw "seed failed." }

            dotnet run --project src/Backend.Veteriner.DbMigrator -- loadtest-seed small
            if ($LASTEXITCODE -ne 0) { throw "loadtest-seed failed." }
        }

        Remove-K6LoadTestAppointments

        dotnet run --project src/Backend.Veteriner.DbMigrator -- rebuild-appointment-projections --batch-size $BatchSize
        if ($LASTEXITCODE -ne 0) { throw "rebuild-appointment-projections failed." }
    }
    finally {
        Pop-Location
    }
}

$counts = Get-EntityCounts

Write-Output ([ordered]@{
        method      = $Method
        commandDb   = $CommandDb
        queryDb     = $QueryDb
        server      = $ServerInstance
        counts      = $counts
        parityOk    = ($counts.Appointments -eq $counts.AppointmentReadModels)
        timestampUtc = [DateTime]::UtcNow.ToString("o")
    })
