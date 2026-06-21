# CQRS-13B — Payment finance read-model foundation

**Tür:** Dashboard finance projection altyapısı — payment outbox event emission + Query DB daily stats read-model schema.

**Production read davranışı değişmedi:** Dashboard handler hâlâ Command DB aggregate okur. Read-model tablo boş kalır (projection processor yok). Feature flag / routing yok.

---

## 1. Bu fazda yapılanlar (13B)

### Payment integration event emission

| Write path | Event type | Koşul |
|---|---|---|
| `CreatePaymentCommandHandler` | `payment.created.v1` | Başarılı create + `SaveChanges` öncesi outbox enqueue |
| `UpdatePaymentCommandHandler` | `payment.updated.v1` | Başarılı update + `SaveChanges` öncesi outbox enqueue |

**Delete / cancel / refund event yok:** Domain'de bu write path'ler bulunmuyor (`Payment` aggregate yalnızca create + update).

Outbox mesajları generic `OutboxProcessor` tarafından **tüketilmez** (`OutboxMessageQueryFilters.ProjectionIntegrationEventTypeValues`).

### Event contract

**Envelope (tüm tipler):**

| Alan | Tip | Açıklama |
|---|---|---|
| `EventId` | `Guid` | Idempotency anahtarı (ProcessedProjectionEvents, 13C) |
| `OccurredAtUtc` | `DateTime` | Event wall-clock (UTC) |
| `Current` | `PaymentProjectionSnapshot` | Finance projection snapshot |

**Snapshot (`PaymentProjectionSnapshot`):**

| Alan | Tip | Açıklama |
|---|---|---|
| `PaymentId` | `Guid` | Aggregate kimliği |
| `TenantId` | `Guid` | Kiracı |
| `ClinicId` | `Guid` | Klinik |
| `ClientId` | `Guid` | Müşteri |
| `PetId` | `Guid?` | İsteğe bağlı hayvan |
| `AppointmentId` | `Guid?` | İlişkili randevu |
| `ExaminationId` | `Guid?` | İlişkili muayene |
| `Amount` | `decimal` | Tahsilat tutarı (18,2) |
| `Currency` | `string` | ISO 4217 alpha-3 |
| `Method` | `int` | `PaymentMethod` enum (JSON uyumu) |
| `PaidAtUtc` | `DateTime` | Canonical ödeme zamanı (UTC); local day bucket 13C'de türetilir |
| `SchemaVersion` | `int` | `1` — `payment.*.v1` ile hizalı |

**Event type sabitleri:** `PaymentIntegrationEventTypes.Created`, `.Updated`, `SchemaVersion = 1`.

### Query DB read-model: `ClinicDailyPaymentStatsReadModel`

| Alan | Tip | Açıklama |
|---|---|---|
| `TenantId`, `ClinicId`, `LocalDate`, `Currency` | PK bileşenleri | Günlük klinik + para birimi bucket |
| `PaidTotalAmount` | `decimal(18,2)` | Günlük ödenen toplam |
| `PaidCount` | `int` | Günlük ödeme adedi |
| `LastEventId` | `Guid` | Son uygulanan projection event |
| `LastEventOccurredAtUtc` | `DateTime` | Stale guard için (13C) |
| `LastProjectedAtUtc` | `DateTime` | Projection wall-clock |

**Index:** `IX_ClinicDailyPaymentStatsReadModels_TenantId_LocalDate` (tenant-wide dashboard aggregation).

**Migration:** `20260621035135_AddClinicDailyPaymentStatsReadModel`

Refunded/Cancelled alanları eklenmedi — domain void/refund/cancel desteklemiyor.

---

## 2. Neden projection processor bu fazda yok?

13B yalnızca **write-side event üretimi** ve **read-side şema** kurar. Processor olmadan:

- Outbox'ta payment event'ler birikir (generic processor atlar).
- Read-model tablo migration ile oluşur ama boş kalır.
- Dashboard / list / report Command DB'den okumaya devam eder.

Bu ayrım rollout riskini düşürür: event contract + schema önce doğrulanır; 13C'de processor + backfill + health eklenir.

---

## 3. 13C'de ne yapılacak?

- `PaymentProjectionProcessor` (claim/lease opt-in pattern, Client/Pet/Appointment ile uyumlu)
- `PaidAtUtc` → `LocalDate` bucket dönüşümü (Istanbul TZ — 13C dokümanda netleşir)
- `ProcessedProjectionEvents` idempotency + stale guard
- Backfill komutu (mevcut Payments → outbox veya doğrudan read-model)
- Health / parity kontrolleri

---

## 4. Dashboard routing neden 13D'ye kaldı?

`GetDashboardFinanceSummaryQueryHandler` routing + `DashboardFinanceReadEnabled` flag:

- Read-model dolu ve parity doğrulanmadan açılmamalı.
- 13C backfill/health tamamlanmadan flag anlamsız.
- Rollback: flag false + restart → Command DB fallback (13D).

---

## 5. Rollback / not used by default

| Bileşen | Rollback |
|---|---|
| Event emission | Write path revert (13B commit geri al) — mevcut payment CRUD etkilenmez |
| Read-model tablo | Migration down veya tablo kullanılmaz (handler okumuyor) |
| Dashboard | Değişmedi — Command DB |

Outbox'ta biriken payment event'ler generic processor tarafından işlenmez; 13C processor devreye alınana kadar zararsız kalır.

---

## 6. Testler

| Test | Kapsam |
|---|---|
| `PaymentCommandHandlerOutboxEmissionTests` | Create/update emit, failure no emit |
| `PaymentIntegrationEventSerializationTests` | Round-trip, decimal precision, type length |
| `QueryDbMigrationIntegrationTests` | Tablo + index + decimal/date persist |
| Mevcut `CreatePaymentCommandHandlerTests` / `UpdatePaymentCommandHandlerTests` | Handler ctor mock outbox — davranış regression |

Manuel:

```powershell
dotnet test --no-restore --filter "Payment"
dotnet build --no-restore
dotnet test --no-restore
```

---

## 7. İlgili dokümanlar

- [`cqrs-13-next-read-model-target-audit.md`](cqrs-13-next-read-model-target-audit.md)
- [`cqrs-13a-projection-hardening-dashboard-finance-design.md`](cqrs-13a-projection-hardening-dashboard-finance-design.md)
