#Requires -Version 5.1
<#
.SYNOPSIS
  Load test icin 10 klinik-scoped access token dosyasi uretir (tek login + zincirli select-clinic).

.DESCRIPTION
  admin@example.com ile bir kez giris yapar, aktif klinikleri eslestirir ve slot 01-10 icin
  sirayla select-clinic cagirarak clinic-tokens.json olusturur.

.PARAMETER BaseUrl
  API taban URL'si (varsayilan: https://localhost:7173).

.PARAMETER Email
  Giris e-postasi (varsayilan: admin@example.com).

.PARAMETER Password
  Guvenli sifre. Verilmezse interaktif olarak istenir.

.PARAMETER OutputPath
  Cikti JSON yolu. Verilmezse repo kokune gore tests/load/.tokens/clinic-tokens.json.

.PARAMETER Force
  Mevcut cikti dosyasinin uzerine yazilmasina izin verir.

.PARAMETER AllowInsecureLocalhostCertificate
  Yalnizca localhost / 127.0.0.1 / ::1 host'larinda TLS sertifika dogrulamasini atlar.

.EXAMPLE
  .\Prepare-LoadTestTokens.ps1

.EXAMPLE
  .\Prepare-LoadTestTokens.ps1 -AllowInsecureLocalhostCertificate -Force
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "https://localhost:7173",
    [string]$Email = "admin@example.com",
    [SecureString]$Password,
    [string]$OutputPath,
    [switch]$Force,
    [switch]$AllowInsecureLocalhostCertificate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ClinicClaim = "clinic_id"

$defaultClinicName = 'Varsay' + [char]0x0131 + 'lan Klinik'

$ExpectedClinics = @(
    [PSCustomObject]@{ Slot = '01'; Name = $defaultClinicName }
    [PSCustomObject]@{ Slot = '02'; Name = 'LT Klinik 02' }
    [PSCustomObject]@{ Slot = '03'; Name = 'LT Klinik 03' }
    [PSCustomObject]@{ Slot = '04'; Name = 'LT Klinik 04' }
    [PSCustomObject]@{ Slot = '05'; Name = 'LT Klinik 05' }
    [PSCustomObject]@{ Slot = '06'; Name = 'LT Klinik 06' }
    [PSCustomObject]@{ Slot = '07'; Name = 'LT Klinik 07' }
    [PSCustomObject]@{ Slot = '08'; Name = 'LT Klinik 08' }
    [PSCustomObject]@{ Slot = '09'; Name = 'LT Klinik 09' }
    [PSCustomObject]@{ Slot = '10'; Name = 'LT Klinik 10' }
)

$ExpectedClinicNamesBySlot = [ordered]@{}
foreach ($expectedClinic in $ExpectedClinics) {
    $ExpectedClinicNamesBySlot[$expectedClinic.Slot] = $expectedClinic.Name
}

function Test-ClinicNameEquals {
    param(
        [string]$Actual,
        [string]$Expected
    )

    $expectedName = $Expected.Normalize([System.Text.NormalizationForm]::FormC)
    $actualName = ([string]$Actual).Normalize([System.Text.NormalizationForm]::FormC)

    return [string]::Equals(
        $actualName,
        $expectedName,
        [System.StringComparison]::Ordinal
    )
}

function Get-RepositoryRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
}

function Get-PlainTextFromSecureString {
    param([Security.SecureString]$Secure)

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function Test-LocalDevelopmentHost {
    param([uri]$Uri)

    $hostName = $Uri.Host.ToLowerInvariant()
    return $hostName -in @("localhost", "127.0.0.1", "::1", "[::1]")
}

function ConvertFrom-Base64Url {
    param([string]$Segment)

    $padded = $Segment.Replace("-", "+").Replace("_", "/")
    switch ($padded.Length % 4) {
        2 { $padded += "==" }
        3 { $padded += "=" }
    }

    return [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($padded))
}

function Get-JwtPayload {
    param([string]$AccessToken)

    $parts = $AccessToken.Split(".")
    if ($parts.Length -lt 3) {
        throw "JWT uc segmentli degil."
    }

    if ([string]::IsNullOrWhiteSpace($parts[1])) {
        throw "JWT payload segmenti bos."
    }

    try {
        $json = ConvertFrom-Base64Url -Segment $parts[1]
        return $json | ConvertFrom-Json
    }
    catch {
        throw "JWT payload parse edilemedi."
    }
}

function Get-ClinicIdFromJwtPayload {
    param(
        [object]$Payload,
        [string]$Slot,
        [string]$ClinicName
    )

    $property = $Payload.PSObject.Properties[$ClinicClaim]
    if ($null -eq $property) {
        throw "Slot $Slot ($ClinicName): JWT payload $ClinicClaim claim eksik."
    }

    $value = [string]$property.Value
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Slot $Slot ($ClinicName): JWT payload $ClinicClaim claim bos."
    }

    return $value.Trim()
}

function Invoke-VetinityApi {
    param(
        [string]$OperationName,
        [ValidateSet("Get", "Post")]
        [string]$Method,
        [string]$Uri,
        [hashtable]$Headers = @{},
        [object]$Body = $null,
        [bool]$SkipCertificateCheck = $false
    )

    $params = @{
        Method      = $Method
        Uri         = $Uri
        Headers     = $Headers
        ErrorAction = "Stop"
    }

    if ($null -ne $Body) {
        $params["ContentType"] = "application/json; charset=utf-8"
        $params["Body"] = ($Body | ConvertTo-Json -Compress)
    }

    if ($SkipCertificateCheck) {
        $params["SkipCertificateCheck"] = $true
    }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $statusCode = $null

        if ($_.Exception.PSObject.Properties["Response"] -and $null -ne $_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        elseif ($_.Exception.PSObject.Properties["StatusCode"]) {
            $statusCode = [int]$_.Exception.StatusCode
        }

        if ($statusCode -eq 429) {
            throw "429 Too Many Requests ($OperationName)"
        }

        if ($null -ne $statusCode) {
            throw "${OperationName}: HTTP $statusCode"
        }

        throw "${OperationName}: $($_.Exception.Message)"
    }
}

function Resolve-ActiveClinicsBySlot {
    param(
        [object[]]$Clinics
    )

    $activeClinics = @(
        $Clinics | Where-Object {
            $_.PSObject.Properties["isActive"] -and $_.isActive -eq $true
        }
    )

    $resolved = @{}

    foreach ($expected in $ExpectedClinics) {
        $slot = $expected.Slot
        $expectedName = $expected.Name
        $matches = @(
            $activeClinics | Where-Object {
                Test-ClinicNameEquals -Actual $_.name -Expected $expectedName
            }
        )

        if ($matches.Count -eq 0) {
            throw "Aktif klinikleri getir: beklenen klinik bulunamadi (slot $slot / $expectedName)."
        }

        if ($matches.Count -gt 1) {
            throw "Aktif klinikleri getir: ayni isimden birden fazla aktif klinik (slot $slot / $expectedName)."
        }

        $resolved[$slot] = $matches[0]
    }

    return $resolved
}

function Test-TokenBatch {
    param(
        [array]$TokenEntries
    )

    $slotsSeen = New-Object "System.Collections.Generic.HashSet[string]"
    $tokensSeen = New-Object "System.Collections.Generic.HashSet[string]"
    $clinicIdsSeen = New-Object "System.Collections.Generic.HashSet[string]"

    foreach ($entry in $TokenEntries) {
        $slot = [string]$entry.slot
        $accessToken = [string]$entry.accessToken
        $clinicName = $ExpectedClinicNamesBySlot[$slot]

        if (-not $slotsSeen.Add($slot)) {
            throw "Slot $slot ($clinicName): duplicate slot."
        }

        if (-not $tokensSeen.Add($accessToken)) {
            throw "Slot $slot ($clinicName): duplicate access token."
        }

        $payload = Get-JwtPayload -AccessToken $accessToken
        $clinicId = Get-ClinicIdFromJwtPayload -Payload $payload -Slot $slot -ClinicName $clinicName
        $expectedClinicId = [string]$entry.clinicId

        if ($clinicId.ToLowerInvariant() -ne $expectedClinicId.ToLowerInvariant()) {
            throw "Slot $slot ($clinicName): JWT $ClinicClaim eslesmiyor."
        }

        if (-not $clinicIdsSeen.Add($clinicId.ToLowerInvariant())) {
            throw "Slot $slot ($clinicName): duplicate $ClinicClaim."
        }
    }
}

function Write-TokenFileAtomically {
    param(
        [string]$TargetPath,
        [array]$TokenEntries
    )

    $directory = Split-Path -Parent $TargetPath
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $output = @(
        foreach ($entry in $TokenEntries) {
            [ordered]@{
                slot        = $entry.slot
                accessToken = $entry.accessToken
            }
        }
    )

    $json = $output | ConvertTo-Json -Depth 3
    $tempPath = Join-Path $directory ("clinic-tokens." + [Guid]::NewGuid().ToString("N") + ".tmp")

    try {
        [System.IO.File]::WriteAllText($tempPath, $json, [System.Text.UTF8Encoding]::new($false))
        Move-Item -Path $tempPath -Destination $TargetPath -Force
    }
    catch {
        if (Test-Path $tempPath) {
            Remove-Item -Path $tempPath -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}

$repoRoot = Get-RepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "tests\load\.tokens\clinic-tokens.json"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repoRoot $OutputPath
}

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

$normalizedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
$baseUri = [uri]$normalizedBaseUrl

if ($AllowInsecureLocalhostCertificate -and -not (Test-LocalDevelopmentHost -Uri $baseUri)) {
    throw "-AllowInsecureLocalhostCertificate yalnizca localhost, 127.0.0.1 veya ::1 host'larinda kullanilabilir."
}

if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) {
    throw "Cikti dosyasi zaten mevcut: $OutputPath (uzerine yazmak icin -Force kullanin)."
}

if (-not $Password -and -not [string]::IsNullOrWhiteSpace($env:LOADTEST_PASSWORD)) {
    $Password = ConvertTo-SecureString -String $env:LOADTEST_PASSWORD -AsPlainText -Force
}

if (-not $Password) {
    $Password = Read-Host -AsSecureString "Password"
}

if ($null -eq $Password) {
    throw "Password gerekli."
}

$previousCallback = $null
$certificateCallbackChanged = $false
$useSkipCertificateCheck = $false

if ($AllowInsecureLocalhostCertificate.IsPresent) {
    if ($PSVersionTable.PSVersion.Major -ge 7) {
        $useSkipCertificateCheck = $true
    }
    else {
        $previousCallback =
            [System.Net.ServicePointManager]::ServerCertificateValidationCallback

        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {
            $true
        }

        $certificateCallbackChanged = $true
    }
}

$tokenEntries = New-Object "System.Collections.Generic.List[object]"

try {
    $plainPassword = Get-PlainTextFromSecureString -Secure $Password
    try {
        $loginResponse = Invoke-VetinityApi `
            -OperationName "Login" `
            -Method Post `
            -Uri "$normalizedBaseUrl/api/v1/auth/login" `
            -Body @{
                email    = $Email
                password = $plainPassword
            } `
            -SkipCertificateCheck:$useSkipCertificateCheck
    }
    finally {
        $plainPassword = $null
    }

    if ([string]::IsNullOrWhiteSpace($loginResponse.accessToken)) {
        throw "Login: accessToken bos."
    }
    if ([string]::IsNullOrWhiteSpace($loginResponse.refreshToken)) {
        throw "Login: refreshToken bos."
    }

    $accessToken = [string]$loginResponse.accessToken
    $refreshToken = [string]$loginResponse.refreshToken

    $clinicsResponse = Invoke-VetinityApi `
        -OperationName "Aktif klinikleri getir" `
        -Method Get `
        -Uri "$normalizedBaseUrl/api/v1/me/clinics?isActive=true" `
        -Headers @{
            Authorization = "Bearer $accessToken"
            Accept        = "application/json"
        } `
        -SkipCertificateCheck:$useSkipCertificateCheck

    if ($null -eq $clinicsResponse) {
        throw "Aktif klinikleri getir: bos yanit."
    }

    $clinicsBySlot = Resolve-ActiveClinicsBySlot -Clinics @($clinicsResponse)
    $currentRefreshToken = $refreshToken

    foreach ($slot in $ExpectedClinicNamesBySlot.Keys) {
        $clinic = $clinicsBySlot[$slot]
        $clinicName = $ExpectedClinicNamesBySlot[$slot]
        $operationName = "Slot $slot select-clinic"

        $selectResponse = Invoke-VetinityApi `
            -OperationName $operationName `
            -Method Post `
            -Uri "$normalizedBaseUrl/api/v1/auth/select-clinic" `
            -Body @{
                refreshToken = $currentRefreshToken
                clinicId     = $clinic.id
            } `
            -SkipCertificateCheck:$useSkipCertificateCheck

        if ([string]::IsNullOrWhiteSpace($selectResponse.accessToken)) {
            throw "$operationName ($clinicName): accessToken bos."
        }
        if ([string]::IsNullOrWhiteSpace($selectResponse.refreshToken)) {
            throw "$operationName ($clinicName): refreshToken bos."
        }

        $tokenEntries.Add([pscustomobject]@{
                slot        = $slot
                accessToken = [string]$selectResponse.accessToken
                clinicId    = [string]$clinic.id
            })

        $currentRefreshToken = [string]$selectResponse.refreshToken
    }

    Test-TokenBatch -TokenEntries $tokenEntries.ToArray()
    Write-TokenFileAtomically -TargetPath $OutputPath -TokenEntries $tokenEntries.ToArray()

    $slotList = ($ExpectedClinicNamesBySlot.Keys -join ", ")
    Write-Host "10 clinic-scoped access token hazirlandi."
    Write-Host "Cikti: $OutputPath"
    Write-Host "Slotlar: $slotList"
    Write-Host ""
    Write-Host "Ornek k6 komutu:"
    Write-Host ('$env:VETINITY_URL="' + $normalizedBaseUrl + '"')
    Write-Host ('$env:VETINITY_TOKENS_FILE="' + $OutputPath + '"')
    Write-Host '$env:VUS="100"'
    Write-Host '$env:DURATION="3m"'
    Write-Host '& "C:\Program Files\k6\k6.exe" run ".\tests\load\panel-mixed-read.js"'
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
finally {
    if ($certificateCallbackChanged) {
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback =
            $previousCallback
    }
}
