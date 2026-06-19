# CQRS-11D-3: İki Instance Acceptance

Bu belge, appointment projection **claim/lease** altyapısının aynı Command DB ve Query DB üzerinde **iki API instance** ile güvenli çalıştığını doğrulamak için acceptance runbook'udur.

CQRS-11D-2A/2B/2C tamamlandıktan sonra çalıştırılır. Production/staging `Invoke-CqrsStagedRollout.ps1` single-instance guard'ı **değiştirilmez**; bu senaryo için ayrı araç kullanılır.

## Amaç

```text
Instance A + Instance B
  -> aynı VetinityCommandDb_LoadTest
  -> aynı VetinityQueryDb_LoadTest
  -> AppointmentProjection:ClaimingEnabled=true
  -> paralel claim + Query dedup-first apply
  -> parity + queue clean + ProcessedProjectionEvents tutarlılığı
```

## Ön koşullar

1. Command ve Query DB migration'ları uygulanmış olmalı (DbMigrator veya `dotnet ef database update`).
2. LoadTest token dosyası: `tests/load/.tokens/clinic-tokens.json` (`Prepare-LoadTestTokens.ps1`).
3. k6 yüklü (`k6 version`).
4. İki ayrı terminalde API instance'ları.

## Instance A

```powershell
$env:DOTNET_ENVIRONMENT="LoadTest"
$env:ASPNETCORE_ENVIRONMENT="LoadTest"
$env:ConnectionStrings__DefaultConnection="Server=localhost;Database=VetinityCommandDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
$env:ConnectionStrings__QueryConnection="Server=localhost;Database=VetinityQueryDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
$env:RateLimiting__Enabled="false"
$env:QueryReadModels__AppointmentsEnabled="true"
$env:QueryReadModels__DashboardAppointmentsEnabled="true"
$env:AppointmentProjection__Enabled="true"
$env:AppointmentProjection__ClaimingEnabled="true"
$env:AppointmentProjection__ClaimBatchSize="1"
$env:AppointmentProjection__LeaseDurationSeconds="60"
# Jwt signing key: appointment-cqrs-load-test.md ile ayni LoadTest degeri (Prepare-LoadTestTokens ile uyumlu)

dotnet run --project src/Backend.Veteriner.Api --no-launch-profile --urls "https://localhost:7173;http://localhost:5018"
```

## Instance B

```powershell
$env:DOTNET_ENVIRONMENT="LoadTest"
$env:ASPNETCORE_ENVIRONMENT="LoadTest"
$env:ConnectionStrings__DefaultConnection="Server=localhost;Database=VetinityCommandDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
$env:ConnectionStrings__QueryConnection="Server=localhost;Database=VetinityQueryDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
$env:RateLimiting__Enabled="false"
$env:QueryReadModels__AppointmentsEnabled="true"
$env:QueryReadModels__DashboardAppointmentsEnabled="true"
$env:AppointmentProjection__Enabled="true"
$env:AppointmentProjection__ClaimingEnabled="true"
$env:AppointmentProjection__ClaimBatchSize="1"
$env:AppointmentProjection__LeaseDurationSeconds="60"
# Jwt signing key: appointment-cqrs-load-test.md ile ayni LoadTest degeri

dotnet run --project src/Backend.Veteriner.Api --no-launch-profile --urls "https://localhost:7174;http://localhost:5019"
```

Stdout'u worker kanıtı için dosyaya yönlendirmek önerilir:

```powershell
dotnet run ... 2>&1 | Tee-Object -FilePath tests/load/results/instance-a.log
```

## Acceptance script

Dry-run (plan):

```powershell
.\tests\load\tools\Invoke-CqrsTwoInstanceAcceptance.ps1 `
  -PrimaryBaseUrl https://localhost:7173 `
  -SecondaryBaseUrl https://localhost:7174
```

Gerçek acceptance:

```powershell
.\tests\load\tools\Invoke-CqrsTwoInstanceAcceptance.ps1 `
  -PrimaryBaseUrl https://localhost:7173 `
  -SecondaryBaseUrl https://localhost:7174 `
  -Apply `
  -AllowInsecureLocalhostCertificate `
  -WorkloadVus 6 `
  -WorkloadDuration 90s `
  -PrimaryLogPath tests/load/results/instance-a.log `
  -SecondaryLogPath tests/load/results/instance-b.log
```

Destructive reset (opsiyonel, varsayılan kapalı):

```powershell
.\tests\load\tools\Invoke-CqrsTwoInstanceAcceptance.ps1 `
  -PrimaryBaseUrl https://localhost:7173 `
  -SecondaryBaseUrl https://localhost:7174 `
  -Reset -ConfirmReset -Apply
```

## Script kontrolleri

| Adım | Açıklama |
|------|----------|
| Listener probe | 7173 ve 7174 portlarında iki ayrı process |
| Health ready | Her iki instance Healthy |
| Flags | projection + claiming + query read flags |
| Queue baseline | Başlangıç pending/retry/dead-letter snapshot |
| Workload | Paralel `appointment-projection-lag.js` (A/B) |
| Drain | Queue pending/retry/dead-letter = 0 |
| SQL parity | Command vs Query read model sayımı |
| ProcessedProjectionEvents | Outbox processed == dedup count, duplicate PK yok |
| Status distribution | Command/Query status dağılımı |
| Worker participation | En az 2 farklı `WorkerId` log kanıtı |

## Workload eşiği

k6 `appointment-projection-lag.js` threshold'ları gevşetilmez:

```text
http_req_failed = 0
checks = 100%
create/reschedule/cancel failure = 0
projection timeout = 0
```

İki worker claim kanıtı için `WorkloadVus` ve `WorkloadDuration` artırılabilir; kodda yapay sleep eklenmez.

## Lease / instance stop (manuel)

Tam otomasyon zorunlu değil. Manuel adımlar:

1. İki instance açık, workload devam ederken Instance A'yı durdurun.
2. `LeaseDurationSeconds` (60s) sonrası Instance B pending satırları reclaim edip işlemeli.
3. Acceptance script'i tekrar çalıştırın veya drain + parity kontrol edin.

## Otomasyon testi

```powershell
.\tests\load\tools\Test-CqrsTwoInstanceAcceptance.ps1
```

## İlgili dosyalar

| Dosya | Rol |
|-------|-----|
| `tests/load/tools/Invoke-CqrsTwoInstanceAcceptance.ps1` | Ana acceptance |
| `tests/load/tools/CqrsTwoInstanceAcceptanceCommon.ps1` | Paylaşılan helper'lar |
| `tests/load/tools/Test-CqrsTwoInstanceAcceptance.ps1` | Script parse/dry-run testleri |
| `tests/load/appointment-projection-lag.js` | Workload |

## Notlar

- `ClaimingEnabled` production default **false** kalır; yalnızca LoadTest acceptance'ta env override ile açılır.
- Command DB claim token Query DB commit'i fence etmez; asıl fence `ProcessedProjectionEvents (EventId, ConsumerName)` PK'dır (11D-2C).
- `Invoke-CqrsStagedRollout.ps1` port 7173 single-instance probe'u bilinçli olarak korunur.
