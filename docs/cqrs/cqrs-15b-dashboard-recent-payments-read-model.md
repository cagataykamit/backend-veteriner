# CQRS-15B — Dashboard recent payments Query DB route

**Tür:** Feature flag routing + Query DB reader. **Schema/migration/backfill değişmedi.**

**Ön durum:** CQRS-15A audit — dashboard recent payments Command DB'den okunuyordu; `PaymentReadModels` alan/index yeterli.

**İlgili dokümanlar:** [`cqrs-15a-payment-read-surface-audit.md`](cqrs-15a-payment-read-surface-audit.md) · [`cqrs-13e-payment-finance-dashboard-routing.md`](cqrs-13e-payment-finance-dashboard-routing.md) · [`cqrs-14e-payment-list-routing.md`](cqrs-14e-payment-list-routing.md)

---

## 1. Bu fazda ne değişti?

| Bileşen | Açıklama |
|---|---|
| `QueryReadModelsOptions.DashboardRecentPaymentsReadEnabled` | Yeni feature flag, default **false** |
| `IDashboardRecentPaymentsReadModelReader` | Query DB `PaymentReadModels` recent okuma abstraction |
| `DashboardRecentPaymentsReadModelReader` | `PaidAtUtc DESC, PaymentId DESC`, Take(10) |
| `GetDashboardFinanceSummaryQueryHandler` | Recent payments bölümü flag + scope routing |
| `appsettings*.json` (6 dosya) | Explicit `DashboardRecentPaymentsReadEnabled = false` |
| `CqrsStartupConfigurationLogger` | Startup log satırına flag eklendi |
| Testler | Unit feature-flag + integration reader + appsettings default test |

**Yapılmadı:** Schema/migration, projection, backfill, parity, payment list/report/export, GetById, client summary.

---

## 2. Flag adı ve default

```json
"QueryReadModels": {
  "DashboardRecentPaymentsReadEnabled": false
}
```

Tüm ortam dosyalarında explicit **false**. Production default değişmedi.

---

## 3. Routing kuralları

`GET /api/v1/dashboard/finance-summary` → `GetDashboardFinanceSummaryQueryHandler`

| Koşul | Recent payments kaynağı |
|---|---|
| `DashboardRecentPaymentsReadEnabled = false` | **Command DB** (mevcut path, birebir) |
| `true` + klinik kapsamı `SingleClinicId` ile tek kliniğe çözüldü | **Query DB** `PaymentReadModels` |
| `true` + tenant-wide scope (`SingleClinicId` null, `AccessibleClinicIds` null) | **Command DB** (scope guard fallback) |
| `true` + multi-clinic scope (`AccessibleClinicIds` dolu) | **Command DB** (scope guard fallback) |

**Dashboard finance totals/trend** (`DashboardFinanceReadEnabled`) bu flag'den **bağımsızdır** — 13E davranışı korunur.

---

## 4. Query DB fallback yok

Query path seçildiğinde Command DB'ye **fallback yapılmaz**:

- Query DB boş → `recentPayments` boş dizi döner
- Query DB hata verirse → exception propagate olur; rollback = flag kapat + restart

---

## 5. Scope guard sebebi

`PaymentReadModels` reader tenant + **tek** `ClinicId` ile filtrelenir. Tenant-wide veya multi-clinic kullanıcılar için mevcut Command DB davranışı (`PaymentsForDashboardRecentSpec` + opsiyonel clinic filtresi veya tenant-wide) korunur.

Scope çözümü yalnızca `DashboardRecentPaymentsReadEnabled = true` iken `IClinicReadScopeResolver.ResolveAsync(tenantId, clinicContext.ClinicId)` ile yapılır.

---

## 6. Reader davranışı

- Kaynak: `PaymentReadModels`
- Sıralama: `PaidAtUtc DESC`, `PaymentId DESC`
- Limit: `DashboardFinanceSummaryConstants.RecentPaymentsTake` (**10**)
- Mapping: denormalize `ClientName` / `PetName` — ek Command DB lookup yok
- Index: `IX_PaymentReadModels_TenantId_ClinicId_PaidAtUtc_PaymentId` (14B)

---

## 7. Rollout sırası

1. `PaymentProjection:Enabled=true`
2. `backfill-payment-read-models` çalıştır
3. PaymentReadModel parity **InSync** doğrula
4. PaymentProjection health **Healthy/InSync** doğrula
5. Staging'de `QueryReadModels:DashboardRecentPaymentsReadEnabled=true` + restart
6. Dashboard finance smoke (recent payments alanları + ordering)
7. Production'da aynı sıra

**Rollback:** `QueryReadModels:DashboardRecentPaymentsReadEnabled=false` + restart → recent payments anında Command DB yoluna döner.

---

## 8. Kapsam dışı

- Payment list routing (`PaymentsListReadEnabled`)
- Report/export
- GetById
- Client payment summary
- Search parity
- Schema/migration/projection/backfill değişikliği

---

## 9. Test matrisi (referans)

| Test | Doğrulanan |
|---|---|
| `DashboardRecentPaymentsReadRoutingOptionsTests` | 6 appsettings default false |
| `DashboardRecentPaymentsQueryHandlerFeatureFlagTests` | Flag routing, scope fallback, no Command fallback on Query path |
| `DashboardRecentPaymentsReadModelReaderIntegrationTests` | Tenant/clinic izolasyon, ordering, mapping |
| `DashboardFinanceQueryHandlerFeatureFlagTests` | Totals/trend flag bağımsızlığı (mevcut) |
| `PaymentListRolloutAcceptanceIntegrationTests` | Yeni flag default false posture |
