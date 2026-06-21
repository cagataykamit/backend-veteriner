# CQRS-13D — Payment finance backfill + health + parity + rollout acceptance

**Tür:** Mevcut Command DB payment verisinin Query DB finance read-model'lerine güvenli backfill'i, operasyonel health/parity gözlemi ve rollout/rollback runbook'u.

**Production read davranışı değişmedi:** Dashboard finance hâlâ Command DB aggregate okur. `PaymentProjection:Enabled` default **`false`**. Health entry eklenir ama projection kapalıyken kuyruk lag'i production'ı gereksiz unhealthy yapmaz.

---

## 1. Bu fazda ne eklendi?

| Bileşen | Açıklama |
|---|---|
| `PaymentFinanceBackfillService` | Command DB `Payments` → `PaymentDailyContributionReadModel` + `ClinicDailyPaymentStatsReadModel` (recompute) |
| `backfill-payment-finance-projections` | DbMigrator komutu (`--batch-size`, `--tenant`) |
| `PaymentProjectionStatusReader` + health evaluator/check | `/health/ready` → `payment-projection` |
| `PaymentFinanceParityReader` | Count parity + daily bucket SUM/COUNT parity |
| Testler | Backfill idempotency, tenant/clinic izolasyonu, health, parity, rollout acceptance |
| Bu doküman | Rollout/rollback sırası + manuel test listesi |

**Yapılmadı (13E):** Dashboard finance handler routing, `DashboardFinanceReadEnabled` flag.

---

## 2. Backfill / rebuild nasıl çalışır?

```text
DbMigrator backfill-payment-finance-projections [--tenant <guid>] [--batch-size 500]
  → PaymentFinanceBackfillService.BackfillAsync
      → Command DB Payments batch okuma (TenantId, Id sıralı)
      → Her payment için PaymentProjectionSnapshotFactory (processor ile aynı snapshot)
      → Query DB transaction (batch):
          1. PaymentDailyContributionReadModel upsert (stale guard)
          2. Etkilenen bucket'lar recompute → ClinicDailyPaymentStatsReadModel
      → Parity okuma (count + daily bucket)
```

- **Increment yok.** Processor ile aynı `RecalculateDailyStats` deseni (SUM/COUNT).
- **Non-destructive:** Query tabloları silinmez; canlı projection ile paralel çalışabilir.
- **ProcessedProjectionEvents'e dokunmaz.** Bekleyen gerçek event'ler dedup + stale guard ile güvenli uygulanır.

---

## 3. Idempotency garantisi

| Durum | Davranış |
|---|---|
| Backfill tekrar çalıştırma | PK `PaymentId` upsert; aynı snapshot → güvenli update, duplicate satır yok |
| Backfill + bekleyen event | Event `OccurredAtUtc` > backfill sentinel → event uygulanır; double-count yok (recompute) |
| Backfill + daha yeni projection | Stale guard → backfill satırı ezilmez |
| Duplicate event replay | `ProcessedProjectionEvents` → ikinci uygulama skip |
| Daily stats | Bucket başına contribution SUM; increment drift biriktirmez |

Backfill ordering anahtarı: `DateTime.MinValue` (UTC sentinel). Payment domain'de mutasyon timestamp yok; gerçek event'ler her zaman daha yenidir.

---

## 4. Parity nasıl okunur?

### Count parity

```sql
-- Command DB
SELECT COUNT_BIG(*) FROM Payments;              -- (opsiyonel) WHERE TenantId = @tenant
-- Query DB
SELECT COUNT_BIG(*) FROM PaymentDailyContributionReadModels;
```

`CommandCount == QueryContributionCount` → contribution parity sağlanmış.

### Daily bucket parity

Command tarafı: `Payments` üzerinde `(TenantId, ClinicId, ToLocalDate(PaidAtUtc), Currency)` GROUP BY → SUM(Amount), COUNT(*).

Query tarafı: `ClinicDailyPaymentStatsReadModel` aynı anahtarlar.

`IPaymentFinanceParityReader.GetGlobalParityAsync()` / `GetTenantParityAsync(tenantId)` → `PaymentFinanceParityResult`:

- `CountInSync`, `DailyBucketParityInSync`, `InSync`
- `DailyBucketMismatchCount`, `DailyBucketMismatches[]` (sapma detayı, PII yok)

---

## 5. Health sinyalleri

`/health/ready` → `payment-projection` entry.

### Kurallar (öncelik)

1. Query DB erişilemiyor / bekleyen migration → **Unhealthy**
2. **`PaymentProjection:Enabled=false`** → **Healthy** (pending/dead-letter/lag production'ı etkilemez; sinyaller `data`'da okunur)
3. Projection **açıkken** dead-letter > 0 → **Unhealthy**
4. Projection açıkken oldest pending age ≥ eşik → **Degraded/Unhealthy**
5. Projection açıkken retry-waiting > 0 → **Degraded**

### `data` alanları

`pendingCount`, `retryWaitingCount`, `deadLetterCount`, `oldestPendingAgeSeconds`, `nextRetryAtUtc`, `projectionEnabled`, `claimingEnabled`

---

## 6. Flag açma sırası (rollout)

1. Query DB migration uygula (`migrate-query`)
2. **`PaymentProjection:Enabled=false`** iken backfill çalıştır:
   `dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-finance-projections`
3. Parity doğrula (`IPaymentFinanceParityReader` veya SQL count + bucket kontrolü)
4. **`PaymentProjection:Enabled=true`** + API restart → outbox tüketimi başlar
5. `/health/ready` → `payment-projection` healthy/degraded izle
6. Parity tekrar doğrula (kuyruk boşaldıktan sonra)
7. **(13E)** Dashboard finance read flag — bu fazda açılmaz

---

## 7. Rollback sırası

1. **`PaymentProjection:Enabled=false`** + restart → processor durur; dashboard zaten Command DB okur (değişmedi)
2. Health kontrol: `payment-projection` healthy (projection kapalı mod)
3. Read-model sıcak tutmak istenirse projection açık bırakılabilir (dashboard routing kapalıyken zararsız)
4. Sorunlu projection durumunda yalnızca processor flag'i kapatmak yeterli; otomatik Command fallback yok

---

## 8. Manuel çalıştırılması gereken testler (Visual Studio)

Filtreli test çalıştırma — full suite gerekmez:

| Filtre | Kapsam |
|---|---|
| `PaymentFinanceBackfill` | Backfill idempotency, tenant/clinic, stale, projection sonrası double-count |
| `PaymentFinanceParity` | Count + daily bucket parity, tenant izolasyonu |
| `PaymentProjectionHealth` | Health endpoint data, projection disabled = healthy |
| `PaymentFinanceRolloutAcceptance` | Flag davranışı, replay/idempotency |
| `PaymentProjection` | 13C regression (processor, hosted service, options) |
| `QueryDbMigration` | PaymentDailyContribution tablo/index (gerekirse) |

---

## 9. Bilinen riskler

| Risk | Azaltma |
|---|---|
| Backfill sırasında yeni payment eklenmesi | Upsert idempotent; sonraki batch'te veya projection event'inde yakalanır |
| Payment'ta per-aggregate sequence yok | Stale guard `OccurredAtUtc` (wall-clock emit) |
| Backfill + aynı anda projection | Stale guard + recompute; transaction batch sınırı |
| Parity fail iken projection açma | DbMigrator exit code 2 + operatör parity kontrolü |
| Mixed-currency dashboard (13E) | Daily stats currency PK'de ayrı; dashboard routing ayrı faz |

---

## 10. İlgili dokümanlar

- [`cqrs-13c-payment-finance-projection-processor.md`](cqrs-13c-payment-finance-projection-processor.md)
- [`cqrs-13c-payment-finance-projection-design-audit.md`](cqrs-13c-payment-finance-projection-design-audit.md)

---

## 11. Commit

**Commit atılmadı.** Kullanıcı onayı sonrası ayrı commit.
