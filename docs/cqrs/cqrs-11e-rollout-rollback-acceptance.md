# CQRS-11E — Staged rollout / rollback acceptance

Appointment read-model rollout ve rollback senaryolarının güvenli olduğunu kanıtlamak için acceptance planı.

**Ön koşul:** CQRS-11D iki instance projection acceptance tamamlanmış olmalı (claim/lease, queue drain, SQL parity).

**Kapsam:** staging veya staging-benzeri localhost/LoadTest. Production deploy veya secret commit yok.

## Feature flags

| Config key | Env override | Etki |
|------------|--------------|------|
| `QueryReadModels:AppointmentsEnabled` | `QueryReadModels__AppointmentsEnabled` | List + calendar → Query DB |
| `QueryReadModels:DashboardAppointmentsEnabled` | `QueryReadModels__DashboardAppointmentsEnabled` | Dashboard appointment metrikleri → Query DB |
| `AppointmentProjection:Enabled` | `AppointmentProjection__Enabled` | Outbox → Query read model projector |
| `AppointmentProjection:ClaimingEnabled` | `AppointmentProjection__ClaimingEnabled` | Multi-instance claim/lease (CQRS-11D) |

Tüm flag değişiklikleri **process restart** gerektirir (`IOptions` startup binding).

## Read path davranışı

| Endpoint | Flag | Kaynak |
|----------|------|--------|
| `GET /api/v1/appointments` | `AppointmentsEnabled=true` | Query DB (`AppointmentReadModels`) |
| `GET /api/v1/appointments` | `AppointmentsEnabled=false` | Command DB (`Appointments`) |
| `GET /api/v1/appointments/calendar` | `AppointmentsEnabled=true` | Query DB |
| `GET /api/v1/appointments/calendar` | `AppointmentsEnabled=false` | Command DB |
| `GET /api/v1/dashboard/summary` | `DashboardAppointmentsEnabled=true` | Query DB (appointment kısmı) |
| `GET /api/v1/dashboard/summary` | `DashboardAppointmentsEnabled=false` | Command DB (appointment kısmı) |
| `GET /api/v1/appointments/{id}` (`GetAppointmentById`) | *(flag yok)* | **Her zaman Command DB** |

Command fallback yalnızca read flag `false` iken devreye girer. Query DB outage sırasında **otomatik Command fallback yok** (Mode B/C read endpoint'leri fail eder).

## Health (`AppointmentProjectionHealthCheck`)

`/health/ready` → `appointment-projection` entry:

| Durum | Beklenen health |
|-------|-----------------|
| Projector ON, queue temiz | Healthy |
| Projector ON, pending age > threshold | Degraded / Unhealthy |
| Projector OFF, read flags OFF, pending birikiyor | **Healthy** (beklenen pause davranışı) |
| Projector OFF, read flags ON, pending > 0 | **Unhealthy** |
| Projector OFF, read flags ON, pending = 0 | Degraded |

`claimingEnabled` health data'da raporlanır; CQRS-11D acceptance ile doğrulanır.

## Acceptance sequence

```text
Mode A (command-read)
  -> Mode B (appointment-query)
  -> Mode C (full-query)
  -> Projection disabled (outbox pending birikir)
  -> Projection re-enabled (queue drain + parity)
  -> Rollback C -> B
  -> Rollback B -> A
```

### Tooling

| Script | Amaç |
|--------|------|
| `Invoke-CqrsRolloutAcceptance.ps1` | Adım adım acceptance (dry-run default, `-Apply` doğrulama) |
| `Test-CqrsRolloutAcceptance.ps1` | Script/plan/health expectation otomasyon |
| `Invoke-CqrsStagedRollout.ps1` | Mode A/B/C smoke + parity (mevcut CQRS-11A) |
| `CqrsRolloutAcceptanceCommon.ps1` | Paylaşılan helper'lar |

### Plan görüntüleme

```powershell
.\tests\load\tools\Invoke-CqrsRolloutAcceptance.ps1 -BaseUrl https://localhost:7173 -ShowPlan
```

### Adım doğrulama (API zaten hedef konfigürasyonda)

```powershell
# Mode A
.\tests\load\tools\Invoke-CqrsRolloutAcceptance.ps1 -BaseUrl https://localhost:7173 -Step command-read -Apply

# Mode B
.\tests\load\tools\Invoke-CqrsRolloutAcceptance.ps1 -BaseUrl https://localhost:7173 -Step appointment-query -Apply

# Mode C
.\tests\load\tools\Invoke-CqrsRolloutAcceptance.ps1 -BaseUrl https://localhost:7173 -Step full-query -Apply
```

## Rollback senaryosu

### 1. Read model flag kapatma (operasyonel rollback)

**C → B** (dashboard read kapat, appointment read açık):

```text
QueryReadModels__AppointmentsEnabled=true
QueryReadModels__DashboardAppointmentsEnabled=false
AppointmentProjection__Enabled=true   # projector AÇIK kalmalı
```

**B → A** (tüm read flag'ler kapalı):

```text
QueryReadModels__AppointmentsEnabled=false
QueryReadModels__DashboardAppointmentsEnabled=false
AppointmentProjection__Enabled=true
```

Her adımda: tek API instance restart → `Invoke-CqrsRolloutAcceptance.ps1 -Step rollback-command-read -Apply` (veya ilgili step).

Rollback sırasında appointment list/calendar/dashboard sırasıyla Query→Command ve dashboard Query→Command'a döner. Projector arka planda Query DB'yi güncel tutar; tekrar B/C açıldığında rebuild gerekmez (queue temizse).

### 2. Projection flag kapatma (planlı pause)

```text
AppointmentProjection__Enabled=false
# read flags istenen modda kalabilir (genelde full-query veya command-read)
```

Doğrulama:

```powershell
.\tests\load\tools\Invoke-CqrsRolloutAcceptance.ps1 `
  -BaseUrl https://localhost:7173 `
  -Step projection-disabled `
  -Apply
```

Beklenenler:

- Yeni appointment create → outbox `pending` artar
- Read flags ON ise `/health/ready` Unhealthy
- Read flags OFF ise endpoint'ler Command DB'den 200 döner, health Healthy kalabilir

### 3. Projection tekrar açma

```text
AppointmentProjection__Enabled=true
# read flags önceki değere döndürülür
```

Doğrulama:

```powershell
.\tests\load\tools\Invoke-CqrsRolloutAcceptance.ps1 `
  -BaseUrl https://localhost:7173 `
  -Step projection-reenabled `
  -Apply `
  -PollTimeoutSeconds 120
```

Beklenenler:

- Queue drain (`pending=retry=dead-letter=0`)
- SQL parity (`Appointments` count = `AppointmentReadModels` count)
- `/health/ready` Healthy

### 4. Queue drain / parity doğrulama

`Get-CqrsStagedParityReport` (PowerShell) veya:

```powershell
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 `
  -BaseUrl https://localhost:7173 `
  -Mode full-query `
  -CommandDatabase VetinityCommandDb_LoadTest `
  -QueryDatabase VetinityQueryDb_LoadTest `
  -Apply
```

## Localhost LoadTest örneği

```powershell
$env:ASPNETCORE_ENVIRONMENT = "LoadTest"
# ConnectionStrings + auth signing key env ile (commit etme)

# Otomasyon (API gerekmez)
.\tests\load\tools\Test-CqrsRolloutAcceptance.ps1
.\tests\load\tools\Test-CqrsStagedRollout.ps1

# Canlı acceptance (API ayakta, adım adım restart)
.\tests\load\tools\Invoke-CqrsRolloutAcceptance.ps1 -BaseUrl https://localhost:7173 -ShowPlan
```

## Acceptance kriterleri (CQRS-11E)

- [ ] Mode A/B/C: flag'ler health'te doğru, endpoint smoke 200
- [ ] Mode B: dashboard Command, list/calendar Query
- [ ] Mode A: tüm appointment read Command DB
- [ ] Projection OFF: pending birikir, health doğru yansır
- [ ] Projection ON: queue drain, parity temiz
- [ ] Rollback C→B→A: veri kaybı yok, projector açık
- [ ] Script output'ta secret yok

## İlgili dokümanlar

- [`cqrs-11a-staging-rollout.md`](cqrs-11a-staging-rollout.md)
- [`cqrs-11b-operations-runbook.md`](cqrs-11b-operations-runbook.md)
- [`cqrs-11d-two-instance-acceptance.md`](cqrs-11d-two-instance-acceptance.md)
- [`appointment-projection-operations.md`](appointment-projection-operations.md)
