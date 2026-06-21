# CQRS-13F — Payment finance rollout smoke / acceptance

Payment finance CQRS zincirinin (13C + 13D + 13E) **operasyonel kapanışı**: rollout öncesi kontrol listesi, flag açma/rollback sırası, health/parity smoke ve acceptance test matrisi.

**Production read davranışı değişmedi:** `PaymentProjection:Enabled=false`, `QueryReadModels:DashboardFinanceReadEnabled=false` (tüm ortam appsettings).

**İlgili dokümanlar:**

- [`cqrs-13c-payment-finance-projection-processor.md`](cqrs-13c-payment-finance-projection-processor.md)
- [`cqrs-13d-payment-finance-backfill-health-parity.md`](cqrs-13d-payment-finance-backfill-health-parity.md)
- [`cqrs-13e-payment-finance-dashboard-routing.md`](cqrs-13e-payment-finance-dashboard-routing.md)

---

## 1. 13C / 13D / 13E kısa özeti

| Faz | Ne eklendi | Production etkisi |
|---|---|---|
| **13C** | Payment projection processor, contribution + daily stats recompute, `ProcessedProjectionEvents` idempotency | `PaymentProjection:Enabled` default **false** — processor kapalı |
| **13D** | Backfill komutu, parity reader, `/health/ready` → `payment-projection` | Read path değişmedi; health gözlemi eklendi |
| **13E** | `DashboardFinanceReadEnabled` flag, dashboard finance Query DB routing | Default **false** — dashboard Command DB okur |

---

## 2. Rollout öncesi kontrol listesi

| # | Kontrol | Beklenen |
|---|---|---|
| 1 | Query DB migration uygulandı mı? | `PaymentDailyContributionReadModels`, `ClinicDailyPaymentStatsReadModels` mevcut |
| 2 | Backfill çalıştırıldı mı? | DbMigrator exit code 0; parity in-sync |
| 3 | Parity in-sync mi? | `CountInSync` + `DailyBucketParityInSync` |
| 4 | Health healthy mi? | `payment-projection` Healthy (projection kapalıyken pending olsa bile) |
| 5 | Startup log flag'ler | `PaymentProjectionEnabled=False`, `DashboardFinanceReadEnabled=False` |
| 6 | Acceptance testleri yeşil mi? | §8 filtreleri |

---

## 3. Rollout sırası (flag açmadan ÖNCE)

```text
migrate-query
  → backfill-payment-finance-projections
  → parity (InSync)
  → health (/health/ready payment-projection)
  → PaymentProjection:Enabled=true + restart (opsiyonel — outbox tüketimi)
  → parity tekrar (kuyruk boşaldıktan sonra)
  → DashboardFinanceReadEnabled=true + restart (13E read path)
  → dashboard finance smoke
```

| # | Adım | Komut / aksiyon | Doğrulama |
|---|---|---|---|
| 1 | Query migration | `dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query` | Bekleyen migration yok |
| 2 | Backfill | `dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-finance-projections` | Success; parity in-sync (mismatch → exit 2) |
| 3 | Parity | `IPaymentFinanceParityReader.GetGlobalParityAsync()` veya SQL count + bucket | `InSync=true` |
| 4 | Health | `GET /health/ready` | `payment-projection` Healthy |
| 5 | Projection aç | `PaymentProjection__Enabled=true` → restart | Outbox tüketimi başlar |
| 6 | Dashboard read aç | `QueryReadModels__DashboardFinanceReadEnabled=true` → restart | Startup log `DashboardFinanceReadEnabled=True` |
| 7 | Smoke | `GET /api/v1/dashboard/finance-summary` | Command vs Query parity (staging) |

> **Altın kural:** Backfill + parity **DashboardFinanceReadEnabled açmadan önce** zorunlu. Query DB boşken flag true → sıfır KPI (fallback yok).

### Backfill komutu

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-finance-projections
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-finance-projections --tenant <guid>
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-finance-projections --batch-size 500
```

---

## 4. Parity kontrolü

**Count parity:**

```sql
-- Command DB
SELECT COUNT_BIG(*) FROM Payments;
-- Query DB
SELECT COUNT_BIG(*) FROM PaymentDailyContributionReadModels;
```

**Daily bucket parity:** Command `GROUP BY (TenantId, ClinicId, LocalDate, Currency)` vs `ClinicDailyPaymentStatsReadModels`.

Programatik: `IPaymentFinanceParityReader.GetGlobalParityAsync()` / `GetTenantParityAsync(tenantId)`.

---

## 5. Health / ready kontrolü

Endpoint: `GET /health/ready` → entry `payment-projection`.

### Beklenti matrisi

| projectionEnabled | pending / dead-letter | Beklenen |
|---|---|---|
| **false** | herhangi | **Healthy** (sinyaller `data`'da) |
| true | hepsi 0 | **Healthy** |
| true | deadLetter > 0 | **Unhealthy** |
| true | oldest pending age ≥ Unhealthy eşiği | **Unhealthy** |
| true | oldest pending age ≥ Degraded eşiği | **Degraded** |
| true | retry-waiting > 0 | **Degraded** |
| * | Query DB erişilemez / pending migration | **Unhealthy** |

`data` alanları: `pendingCount`, `retryWaitingCount`, `deadLetterCount`, `oldestPendingAgeSeconds`, `nextRetryAtUtc`, `projectionEnabled`.

Startup log (PII yok): `PaymentProjectionEnabled`, `DashboardFinanceReadEnabled`.

---

## 6. Rollback sırası

### Read path geri alma (en hızlı)

```text
QueryReadModels__DashboardFinanceReadEnabled=false → restart → dashboard Command DB
```

### Projection durdurma (opsiyonel)

```text
PaymentProjection__Enabled=false → restart → processor durur
```

| # | Adım | Doğrulama |
|---|---|---|
| 1 | `DashboardFinanceReadEnabled=false` | Dashboard finance Command DB yolu |
| 2 | Restart | Startup log flag false |
| 3 | Health | `payment-projection` Healthy (projection kapalı mod) |
| 4 | *(opsiyonel)* `PaymentProjection:Enabled=false` | Processor durur |

> Read flag rollback yeterlidir. Projection açık kalabilir (read-model sıcak tutma).

---

## 7. Production default değerleri

| Ayar | Default (Production + tüm appsettings) |
|---|---|
| `PaymentProjection:Enabled` | **false** |
| `PaymentProjection:ClaimingEnabled` | **false** |
| `QueryReadModels:DashboardFinanceReadEnabled` | **false** |

Doğrulama: `PaymentFinanceRolloutOptionsTests` (appsettings JSON taraması).

---

## 8. Manuel çalıştırılması gereken testler

```powershell
dotnet build --no-restore

dotnet test --no-restore --filter "FullyQualifiedName~PaymentFinanceRolloutAcceptance"
dotnet test --no-restore --filter "FullyQualifiedName~DashboardFinanceQueryParityIntegrationTests"
dotnet test --no-restore --filter "FullyQualifiedName~DashboardFinanceQueryHandlerFeatureFlagTests"
dotnet test --no-restore --filter "FullyQualifiedName~GetDashboardFinanceSummaryQueryHandlerTests"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentProjectionHealth"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentFinanceBackfill"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentFinanceParity"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentFinanceRolloutOptionsTests"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentProjectionHealthEvaluatorTests"
```

---

## 9. Acceptance test kapsamı (13F)

| Test sınıfı | Ne doğrular |
|---|---|
| `PaymentFinanceRolloutAcceptanceIntegrationTests` | Projection disabled; backfill+processor idempotency; **both flags false Command DB**; backfill sonrası dashboard parity; parity reader in-sync; **rollback flag false**; Query DB boş + flag true |
| `DashboardFinanceQueryParityIntegrationTests` | Clinic/tenant scope parity; 7 günlük trend; empty query |
| `DashboardFinanceQueryHandlerFeatureFlagTests` | Flag routing; no silent fallback |
| `PaymentProjectionHealthIntegrationTests` | `/health/ready` data; healthy/degraded/**unhealthy** |
| `PaymentFinanceRolloutOptionsTests` | Production-safe appsettings defaults |

---

## 10. Bilinen riskler

| Risk | Azaltma |
|---|---|
| Flag true + Query DB boş | Backfill + parity önce; acceptance test `QueryDbEmptyWithReadFlagTrue` |
| Mixed-currency dashboard | Currency satırları SUM; parity + dashboard parity testleri |
| Projection açık + lag | Health Degraded/Unhealthy; rollout sırasında izle |
| Rollback unutulursa | Tek env var + restart; startup log flag kontrolü |

---

## 11. Commit

**Commit atılmadı.** Kullanıcı onayı sonrası ayrı commit.
