# CQRS-13E — Payment finance dashboard routing

**Tür:** Dashboard finance read path feature flag routing + Query DB daily stats reader.

**Production read davranışı değişmedi:** `QueryReadModels:DashboardFinanceReadEnabled` default **`false`**. Flag kapalıyken handler Command DB aggregate yolunu birebir korur.

---

## 1. Bu fazda ne eklendi?

| Bileşen | Açıklama |
|---|---|
| `QueryReadModels:DashboardFinanceReadEnabled` | Dashboard finance routing flag (default `false`) |
| `IDashboardFinanceReadModelReader` | Query DB `ClinicDailyPaymentStatsReadModel` okuma abstraction |
| `DashboardFinanceReadModelReader` | Today/week/month toplamları + 7 günlük trend (daily stats) |
| `GetDashboardFinanceSummaryQueryHandler` | Flag routing: false → Command DB, true → Query DB (totals + trend) |
| `DashboardFinanceLocalDateRanges` | İstanbul takvim günü aralıkları (13C/13D bucket kurallarıyla uyumlu) |
| Startup log | `DashboardFinanceReadEnabled` CQRS startup log satırına eklendi |
| Testler | Unit feature-flag + integration parity (backfill sonrası command vs query) |

**Yapılmadı:** Payment write/projection/backfill/health değişikliği; recent payments per-payment read-model; otomatik Command DB fallback.

---

## 2. Flag false / true davranışı

| `DashboardFinanceReadEnabled` | Totals (today/week/month) | 7 günlük trend (`last7DaysPaid`) | Recent payments + isim hydration |
|---|---|---|---|
| **`false` (default)** | Command DB `IDashboardFinancePaymentAggregatesReader` + `PaymentsPaidAtAmountInWindowSpec` | Command DB payment satırları → in-memory bucket | Command DB |
| **`true`** | Query DB `ClinicDailyPaymentStatsReadModel` (currency satırları toplanır) | Query DB daily stats `LocalDate` bucket | Command DB (değişmedi) |

Otomatik fallback **yoktur**: flag açıkken Query DB hata verirse exception propagate olur; rollback = flag kapat + restart.

---

## 3. Veri kaynağı özeti

```text
GET /api/v1/dashboard/finance-summary

DashboardFinanceReadEnabled=false
  → IDashboardFinancePaymentAggregatesReader (Command DB Payments)
  → PaymentsPaidAtAmountInWindowSpec (trend)
  → PaymentsForDashboardRecentSpec + client/pet lookup

DashboardFinanceReadEnabled=true
  → IDashboardFinanceReadModelReader (Query DB ClinicDailyPaymentStatsReadModel)
  → PaymentsForDashboardRecentSpec + client/pet lookup (Command DB)
```

Mixed-currency: read-model PK'de `Currency` ayrı satır; window toplamları tüm currency satırları **SUM** ile birleştirilir — mevcut Command DB davranışıyla uyumlu.

---

## 4. Tenant / clinic scope

- Her okuma `TenantId` ile filtrelenir.
- `IClinicContext.ClinicId` doluysa yalnız ilgili klinik `ClinicId` satırları dahil edilir.
- Tenant-wide (clinic seçilmemiş) kullanıcı: tenant içindeki tüm kliniklerin daily stats toplamı — mevcut dashboard kurallarıyla uyumlu.
- Auth/permission akışına dokunulmadı (`Dashboard.Read` aynı).

---

## 5. Date bucket / timezone

- Query yolu `OperationDayBounds` / `OperationPeriodBounds` ile aynı İstanbul takvim günlerini kullanır.
- Daily stats `LocalDate` = `PaidAtUtc` → İstanbul günü (13C/13D projection ile aynı).
- Yeni bucket hesaplama logic eklenmedi; hazır daily stats okunur.

---

## 6. Rollout

1. 13D backfill + parity doğrula
2. `PaymentProjection:Enabled=true` (opsiyonel, read-model sıcak tutma)
3. Staging'de `QueryReadModels:DashboardFinanceReadEnabled=true` + restart
4. Parity + dashboard finance smoke
5. Production'da aynı sıra

**Rollback:** `QueryReadModels:DashboardFinanceReadEnabled=false` + restart → Command DB yolu anında geri döner.

`PaymentProjection:Enabled` ile **bağımsızdır** — projection açık/kapalı kalabilir; read flag kapalıyken dashboard Command DB okur.

---

## 7. Manuel çalıştırılması gereken testler

```powershell
dotnet build --no-restore

dotnet test --no-restore --filter "FullyQualifiedName~DashboardFinanceQueryHandlerFeatureFlagTests"
dotnet test --no-restore --filter "FullyQualifiedName~GetDashboardFinanceSummaryQueryHandlerTests"
dotnet test --no-restore --filter "FullyQualifiedName~DashboardFinanceQueryParityIntegrationTests"
dotnet test --no-restore --filter "PaymentFinanceBackfill|PaymentFinanceParity|PaymentProjectionHealth"
```

---

## 8. Bilinen riskler

| Risk | Azaltma |
|---|---|
| Query DB boş/eksik + flag true | Sıfır/eksik KPI; otomatik fallback yok — parity + backfill önce |
| Mixed-currency toplamlar | Currency satırları SUM; Command path ile parity testi |
| Recent payments Command DB'de kalır | Flag true iken totals query, recent command — bilinçli 13E kapsamı |
| Query DB outage + flag true | 5xx; health/parity ile görünür; rollback flag false |

---

## 9. İlgili dokümanlar

- [`cqrs-13d-payment-finance-backfill-health-parity.md`](cqrs-13d-payment-finance-backfill-health-parity.md)
- [`cqrs-13c-payment-finance-projection-design-audit.md`](cqrs-13c-payment-finance-projection-design-audit.md) §9

---

## 10. Commit

**Commit atılmadı.** Kullanıcı onayı sonrası ayrı commit.
