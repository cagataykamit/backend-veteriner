# CQRS-13A — Projection hardening + dashboard finance projection design

**Tür:** Reliability hardening (Client/Pet claim/lease opt-in) + dashboard finance read-model **design
foundation** (docs only for 13B–13D).

**Production read davranışı değişmedi:** `ClaimingEnabled=false` (default), mevcut FIFO processor yolu
korunur. Read flag default'ları (`ClientsEnabled`, `PetsEnabled`, vb.) değişmedi.

---

## 1. Bu fazda yapılanlar (13A)

### Client/Pet projection claim/lease opt-in

| Bileşen | Değişiklik |
|---|---|
| `ClientProjectionProcessor` | `ClaimingEnabled=true` → atomik claim/lease batch; `false` → mevcut FIFO |
| `PetProjectionProcessor` | Aynı pattern |
| `SqlClientOutboxClaimRepository` | Client event tipleri için UPDLOCK/READPAST claim SQL |
| `SqlPetOutboxClaimRepository` | Pet event tipleri için aynı |
| `ClientProjectionOptions` / `PetProjectionOptions` | `ClaimingEnabled`, `LeaseDurationSeconds`, `ClaimBatchSize` |
| `ClientProjectionHealthCheck` / `PetProjectionHealthCheck` | Health data: `claimingEnabled` |
| `appsettings.*` | Explicit `ClientProjection` section + Pet claiming defaults |

**Migration yok:** Mevcut `OutboxMessages` claim/lease kolonları (`ClaimedBy`, `ClaimToken`,
`ClaimedAtUtc`, `LeaseExpiresAtUtc`) kullanılır.

### Idempotency / stale guard (değişmedi)

- `ProcessedProjectionEvents (EventId, ConsumerName)` dedup korunur.
- Stale guard: `OccurredAtUtc` vs `LastEventOccurredAtUtc` — claim path aynı `ApplyTransactionallyAsync`
  kullanır.

### Config default'ları (production davranışı korunur)

| Ayar | Default |
|---|---|
| `ClientProjection:ClaimingEnabled` | `false` |
| `PetProjection:ClaimingEnabled` | `false` |
| `ClientProjection:Enabled` | `true` (code default; appsettings'te explicit) |
| `PetProjection:Enabled` | `false` |

---

## 2. Claim path özeti

```text
ProcessBatchAsync
  ClaimingEnabled=false  →  ProcessFifoBatchAsync (12B/12C davranışı, bit-for-bit)
  ClaimingEnabled=true   →  ProcessClaimBatchAsync
                              → ClaimNextBatchAsync (SQL atomik claim + lease)
                              → ApplyTransactionallyAsync (ProcessedProjectionEvents + upsert)
                              → MarkProcessedAsync / MarkRetryAsync / MarkDeadLetterAsync
```

Appointment projection ile farklar:

- Client/Pet **per-aggregate sequence yok**; claim SQL FIFO (`CreatedAtUtc`, `Id`) sıralar.
- Metadata mismatch dead-letter yok (appointment'a özgü); unknown event type → dead-letter.

---

## 3. Rollback

Claiming kapatma: `ClientProjection__ClaimingEnabled=false` / `PetProjection__ClaimingEnabled=false` +
restart → FIFO path geri döner. Read-model şeması / backfill / parity etkilenmez.

---

## 4. Dashboard finance projection design (13B–13D sınırları)

Bu fazda **yalnızca tasarım notu**. Implementation 13B–13D'de.

### 4.1 Neden payment event emission gerekli?

`GetDashboardFinanceSummaryQueryHandler` bugün Command DB'den canlı aggregate okur:

- 6 SQL SUM/COUNT (today/week/month paid totals)
- 7 günlük tüm payment satırı taraması (`PaymentsPaidAtAmountInWindowSpec` → in-memory bucket)
- Recent payments + client/pet name hydration

Dashboard appointment dilimi zaten `ClinicDailyAppointmentStatsReadModel` ile projeksiyonlu.
Finance dilimi için **Payment integration event emission** (write-path outbox) gerekir — Payment
aggregate bugün outbox event **yaymıyor** (CQRS-13 audit doğrulandı).

### 4.2 Olası read-model: `ClinicDailyPaymentStatsReadModel`

Appointment `ClinicDailyAppointmentStatsReadModel` desenine paralel:

| Alan (öneri) | Açıklama |
|---|---|
| `TenantId`, `ClinicId`, `LocalDate` | PK bileşenleri (Istanbul gün bucket) |
| `PaidTotalAmount`, `PaidCount` | Günlük ödenen toplam / adet |
| `Currency` | Tek para birimi varsayımı veya ayrı satır (TBD 13B design) |
| `LastProjectedAtUtc` | Projection wall-clock |

Projection consumer: payment created/updated/cancelled (veya paid) event'lerinden günlük istatistik
upsert.

### 4.3 Faz sınırları

| Faz | Kapsam | Bu fazda? |
|---|---|---|
| **13A** | Client/Pet claim/lease + finance design doc | ✅ |
| **13B** | Payment integration event emission + `ClinicDailyPaymentStatsReadModel` entity + Query DB migration + idempotency | ❌ |
| **13C** | Payment projection processor + backfill + health + parity | ❌ |
| **13D** | `DashboardFinanceReadEnabled` flag + `GetDashboardFinanceSummaryQueryHandler` routing + rollout | ❌ |

### 4.4 13B–13D dışında kalan (bilinçli)

- Payment list/report/export Command DB aggregate okuması (12D search lookup ayrı)
- Operational alerts (vaccination stats) projeksiyonu
- Examination read-model
- Otomatik Command DB fallback (yok)

### 4.5 Risk notları (finance projection için)

- Write-path genişlemesi: Payment outbox event atomik + idempotent olmalı.
- Stale read-model + flag true → dashboard finance özeti eksik/yanlış olabilir; fallback yok.
- Rollback: `DashboardFinanceReadEnabled=false` + restart (13D).

---

## 5. Testler

| Test grubu | Kapsam |
|---|---|
| `ClientProjectionClaimPathIntegrationTests` | Claim success, duplicate idempotency, stale guard, retry |
| `PetProjectionClaimPathIntegrationTests` | Aynı |
| `ClientOutboxClaimRepositoryIntegrationTests` | Two-worker no duplicate claim, lease columns |
| `PetOutboxClaimRepositoryIntegrationTests` | Aynı |
| Mevcut `ClientProjectionIntegrationTests` / `PetProjectionIntegrationTests` | FIFO regression (`ClaimingEnabled=false`) |

Manuel:

```powershell
dotnet test --no-restore --filter "ClientProjection"
dotnet test --no-restore --filter "PetProjection"
dotnet test --no-restore --filter "Projection"
dotnet test --no-restore
```

---

## 6. İlgili dokümanlar

- [`cqrs-13-next-read-model-target-audit.md`](cqrs-13-next-read-model-target-audit.md)
- [`cqrs-12b-7-client-read-model-rollout-acceptance.md`](cqrs-12b-7-client-read-model-rollout-acceptance.md)
- Appointment claim pattern: `AppointmentProjectionProcessor`, `SqlAppointmentOutboxClaimRepository`
