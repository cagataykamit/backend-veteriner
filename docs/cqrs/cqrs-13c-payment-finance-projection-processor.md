# CQRS-13C — Payment finance projection processor

**Tür:** Payment integration event'lerinden Query DB finance read-model'lerini güncelleyen projection processor + contribution state foundation.

**Production read davranışı değişmedi:** Dashboard handler hâlâ Command DB aggregate okur. `PaymentProjection:Enabled` default **`false`**. Read-model tablolar processor açılmadan boş kalır.

---

## 1. Contribution state neden gerekli?

13B'de `payment.created.v1` / `payment.updated.v1` event contract'ı yalnızca **`Current`** snapshot taşır — **`Previous` yok**. Appointment event'lerinin aksine update delta'sı event payload'ından hesaplanamaz.

`ClinicDailyPaymentStatsReadModel` tek başına increment/decrement ile güncellenirse:

- `payment.updated` amount değişiminde eski tutar düşülmez → double count
- `PaidAtUtc` / `ClinicId` / `Currency` değişiminde eski bucket düzeltilmez

**Çözüm:** `PaymentDailyContributionReadModel` — payment başına son projeksiyonlu katkı satırı. Eski bucket bu satırdan okunur; günlük aggregate contribution satırları üzerinden **recompute** edilir (Appointment `RecalculateDailyStats` deseni).

---

## 2. Event processing flow

```text
PaymentProjectionHostedService (Enabled=false → no-op)
  → PaymentProjectionProcessor.ProcessBatchAsync
      → FIFO (default) veya claim/lease (ClaimingEnabled=true)
      → Deserialize payment.created.v1 | payment.updated.v1
      → ApplyTransactionallyAsync (tek Query DB transaction):
          1. ProcessedProjectionEvents insert (duplicate → skip)
          2. PaymentDailyContributionReadModel.Find(PaymentId)
               - stale: OccurredAtUtc < LastEventOccurredAtUtc → skip contribution + recompute
               - eski bucket = mevcut satır (varsa)
          3. Contribution upsert (Current snapshot)
          4. affectedBuckets = { eski bucket, yeni bucket }
          5. Her bucket: RecalculateDailyStats → SUM(Amount), COUNT(*)
             → ClinicDailyPaymentStatsReadModel upsert (0 satır → Remove)
      → Outbox mesajını processed işaretle (FIFO) veya claim ack (claim path)
```

---

## 3. Recompute stratejisi

- **Increment/decrement yok.** Bucket başına tüm contribution satırları okunur, SUM/COUNT yeniden hesaplanır.
- **Create:** Eski bucket yok; yalnız yeni bucket recompute.
- **Update amount (aynı gün/klinik/currency):** Tek bucket recompute → total doğru kalır.
- **Update date:** Eski gün bucket'ı contribution'dan okunur → eski gün azalır/silinir; yeni gün artar.
- **Update clinic:** Eski klinik bucket + yeni klinik bucket ikisi recompute.
- **Update currency:** Currency PK bileşeni olduğundan bucket move olarak işlenir.

---

## 4. LocalDate bucket

`PaidAtUtc` → `OperationDayBounds.ToLocalDate(PaidAtUtc)` (İstanbul takvimi — `Europe/Istanbul` / `Turkey Standard Time` fallback). Dashboard finance handler ile aynı canonical gün sınırı.

---

## 5. Idempotency / stale davranışı

| Durum | Davranış |
|---|---|
| **Duplicate EventId** | `ProcessedProjectionEvents (EventId, ConsumerName)` → ikinci uygulama skip; aggregate değişmez |
| **Stale (out-of-order)** | `OccurredAtUtc < contribution.LastEventOccurredAtUtc` → contribution ve daily aggregate **korunur**; dedup satırı yazılır; outbox yine consumed |
| **Bad JSON** | Retry + dead-letter (Client/Pet deseni); Query DB'ye yazılmaz |
| **Unknown event type** | FIFO filtresi bilinen tipleri seçer; `payment.unknown.v1` claimlenmez |

Ordering: Payment'ta per-aggregate sequence yok; ordering `OccurredAtUtc` (emit wall-clock) üzerinden.

---

## 6. Transaction boundary

Tek Query DB transaction içinde (Appointment/Client deseni):

1. `ProcessedProjectionEvents` INSERT
2. `PaymentDailyContributionReadModel` upsert (stale değilse)
3. Etkilenen bucket'lar için `ClinicDailyPaymentStatsReadModel` recompute

Ayrı commit kısmi state riski doğurur; bu fazda birleşik transaction zorunlu.

---

## 7. Bu fazda yapılmayanlar

| Bileşen | Neden sonraki faza? |
|---|---|
| **Backfill / health / parity (13D)** | Read-model dolmadan parity anlamsız; Command DB → contribution + aggregate backfill ayrı rollout adımı |
| **Dashboard routing / `DashboardFinanceReadEnabled` (13E)** | Read-model dolu + parity doğrulanmadan handler routing açılmamalı |
| Delete/cancel/refund event | Write path yok; yalnız create + update işlenir |

---

## 8. Config / rollback

| Ayar | Default | Açıklama |
|---|---|---|
| `PaymentProjection:Enabled` | **`false`** | Hosted service poll etmez |
| `PaymentProjection:ClaimingEnabled` | **`false`** | FIFO processor (mevcut desen) |
| `PaymentProjection:ConsumerName` | `payment-finance-v1` | Idempotency consumer adı |

**Rollback:** `PaymentProjection:Enabled=false` + restart → processor durur; dashboard zaten Command DB okur (değişmedi). Otomatik fallback yok.

---

## 9. Schema özeti

### `PaymentDailyContributionReadModel` (yeni — migration `20260621050105`)

| Alan | Tip | Açıklama |
|---|---|---|
| `PaymentId` | PK | Aggregate kimliği |
| `TenantId`, `ClinicId`, `LocalDate`, `Currency`, `Amount` | | Son projeksiyonlu katkı |
| `LastEventId`, `LastEventOccurredAtUtc`, `LastProjectedAtUtc` | | Stale guard + gözlem |

**Index:** `IX_PaymentDailyContributionReadModels_Tenant_Clinic_LocalDate_Currency`

### `ClinicDailyPaymentStatsReadModel` (13B — değişmedi)

Günlük klinik + currency özeti; processor recompute ile doldurur.

---

## 10. Testler

| Test | Kapsam |
|---|---|
| `PaymentProjectionIntegrationTests` | create, update amount/date/clinic, duplicate, stale, tenant isolation, unknown type, bad JSON |
| `PaymentProjectionHostedServiceTests` | `Enabled=false` → processor çağrılmaz |
| `PaymentProjectionOptionsTests` | default `Enabled=false` |
| `QueryDbMigrationIntegrationTests` | yeni tablo + index |

Manuel (kullanıcı çalıştıracak):

```powershell
dotnet build --no-restore
dotnet test --no-restore --filter "PaymentProjection"
dotnet test --no-restore --filter "Payment"
dotnet test --no-restore
```

---

## 11. İlgili dokümanlar

- [`cqrs-13c-payment-finance-projection-design-audit.md`](cqrs-13c-payment-finance-projection-design-audit.md)
- [`cqrs-13b-payment-finance-read-model-foundation.md`](cqrs-13b-payment-finance-read-model-foundation.md)

---

## 12. Commit

**Commit atılmadı.** Kullanıcı onayı sonrası ayrı commit.
