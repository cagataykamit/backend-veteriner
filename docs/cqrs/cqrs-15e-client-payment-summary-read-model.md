# CQRS-15E — Client payment summary Query DB route

**Tür:** Feature flag routing + Query DB reader. **Schema/migration/projection/backfill/parity/health değişmedi.**

**Ön durum:** CQRS-15C audit client payment summary route'unu blokladı (tek eksik alan `PaymentReadModel.ClinicName`). CQRS-15D `ClinicName`'i ekledi (projection/backfill/parity dahil). Bu faz route'u açar.

**İlgili dokümanlar:** [`cqrs-15c-client-payment-summary-read-model-decision.md`](cqrs-15c-client-payment-summary-read-model-decision.md) · [`cqrs-15d-payment-read-model-clinic-name-enrichment.md`](cqrs-15d-payment-read-model-clinic-name-enrichment.md) · [`cqrs-15b-dashboard-recent-payments-read-model.md`](cqrs-15b-dashboard-recent-payments-read-model.md) · [`cqrs-14e-payment-list-routing.md`](cqrs-14e-payment-list-routing.md)

---

## 1. Bu fazda ne değişti?

| Bileşen | Açıklama |
|---|---|
| `QueryReadModelsOptions.ClientPaymentSummaryReadEnabled` | Yeni feature flag, default **false** |
| `IClientPaymentSummaryReadModelReader` | Query DB `PaymentReadModels` aggregate + recent okuma abstraction |
| `ClientPaymentSummaryReadRequest` / `ClientPaymentSummaryReadResult` | Reader istek/sonuç sözleşmeleri |
| `ClientPaymentSummaryReadModelReader` | SQL aggregate (count / currency totals / last) + recent `PaidAtUtc DESC, PaymentId DESC` Take(10) |
| `GetClientPaymentSummaryQueryHandler` | Flag + scope routing (Query DB / Command DB fallback) |
| `appsettings*.json` (6 dosya) | Explicit `ClientPaymentSummaryReadEnabled = false` |
| `CqrsStartupConfigurationLogger` | Startup log satırına flag eklendi |
| Testler | Unit feature-flag + integration reader + appsettings default test |

**Yapılmadı:** Schema/migration, `PaymentReadModel` alan/index, `PaymentProjectionProcessor`, backfill, parity, health, DTO, payment list/dashboard recent/report/export/GetById routing, search parity, frontend.

---

## 2. Flag adı ve default

```json
"QueryReadModels": {
  "ClientPaymentSummaryReadEnabled": false
}
```

Tüm ortam dosyalarında explicit **false**. Production okuma yolu değişmedi.

---

## 3. Routing kuralları

`GET /api/v1/clients/{id}/payment-summary` → `GetClientPaymentSummaryQueryHandler`

Tenant guard ve client existence/access validation (`ClientByIdSpec` → `Clients.NotFound`) **her iki yolda da birebir korunur**; header `ClientName` her zaman client entity'den okunur. Routing yalnızca payment aggregate + recent kaynağını belirler.

| Koşul | Kaynak |
|---|---|
| `ClientPaymentSummaryReadEnabled = false` | **Command DB** (mevcut path, birebir) |
| `true` + scope **tek kliniğe** (`SingleClinicId`) çözüldü | **Query DB** `PaymentReadModels` (o klinik filtreli) |
| `true` + scope **tenant-wide** (Admin/Owner, aktif klinik yok: `SingleClinicId` null + `AccessibleClinicIds` null) | **Query DB** `PaymentReadModels` (**clinic filtresi yok**, `TenantId + ClientId`) |
| `true` + scope **multi-clinic** (ClinicAdmin, aktif klinik yok: `AccessibleClinicIds` dolu) | **Command DB** (scope represent edilemez → fallback) |
| `true` + scope resolve **hata** (Clinics.NotFound / AccessDenied) | **Command DB** (fallback) |

Scope çözümü yalnızca `ClientPaymentSummaryReadEnabled = true` iken `IClinicReadScopeResolver.ResolveAsync(tenantId, clinicContext.ClinicId)` ile yapılır.

### 15B'den farkı

15B (dashboard recent) tenant-wide scope'ta Command DB'ye düşer. **15E tenant-wide'ı destekler:** mevcut Command DB davranışı (aktif klinik yoksa client'ın tüm klinik ödemeleri) Query path'te `ClinicId` filtresi olmadan `TenantId + ClientId` ile birebir karşılanır (15C §126 kararı). Yalnız multi-clinic ClinicAdmin scope'u tek `ClinicId`/tenant-wide ile represent edilemediği için Command DB'de kalır.

---

## 4. Query DB fallback yok

Query path seçildiğinde Command DB'ye **fallback yapılmaz**:

- Query DB boş → `TotalPaymentsCount = 0`, `CurrencyTotals = []`, `LastPaymentAtUtc = null`, `RecentPayments = []` (`TotalPaidAmount = 0`). DTO semantiği korunur.
- Query DB hata verirse → exception propagate olur; Command DB tekrar denenmez. Rollback = flag kapat + restart.

---

## 5. Clinic / tenant-wide scope davranışı

- **Aktif klinik var:** yalnız o klinik (`ClinicId` filtresi).
- **Tenant-wide (Admin/Owner, aktif klinik yok):** client'ın tüm klinik ödemeleri (`ClinicId` filtresi yok). Mevcut Command DB tenant-wide davranışı ile aynı.
- **Multi-clinic (ClinicAdmin, aktif klinik yok):** Query reader tek `ClinicId` veya tenant-wide ile represent edemez → Command DB fallback (mevcut davranış birebir korunur, yeni kısıtlama eklenmez).

---

## 6. Query DB reader aggregate davranışı

- Kaynak: `PaymentReadModels`, filtre: `TenantId + ClientId` (+ opsiyonel tek `ClinicId`).
- `TotalPaymentsCount`: SQL `COUNT(*)`.
- `LastPaymentAtUtc`: SQL `MAX(PaidAtUtc)` (boşsa null).
- `CurrencyTotals`: SQL `GROUP BY Currency` + `SUM(Amount)`; grup listesi (küçük) bellekte `OrdinalIgnoreCase` ile sıralanır (Command DB semantiği ile birebir).
- `TotalPaidAmount`: handler'da türetilir — tek currency ise o currency toplamı, aksi halde `0` (mevcut DTO semantiği).
- `RecentPayments`: `PaidAtUtc DESC, PaymentId DESC`, en fazla `ClientPaymentSummaryConstants.RecentPaymentsTake` (**10**). `ClinicName` / `PetName` denormalize (15D) — ek Command DB lookup yok.
- **Tüm ödeme satırları belleğe çekilmez** (Command DB path'in yüksek hacimli müşteri riski Query path'te yok).
- Boş Query DB → count 0 / totals boş / last null / recent boş.

---

## 7. Rollout sırası

1. Query DB migration'ları uygulanmış olmalı (14B `PaymentReadModels` + 15D `ClinicName`).
2. `PaymentProjection:Enabled=true`.
3. `backfill-payment-read-models` çalıştır.
4. PaymentReadModel parity **InSync** doğrula (`ClinicName` dahil).
5. PaymentProjection health **Healthy/InSync** doğrula.
6. Staging'de `QueryReadModels:ClientPaymentSummaryReadEnabled=true` + restart; client payment summary smoke (count/currency totals/last payment + recent ordering + ClinicName).
7. Production'da aynı sıra.

**Rollback:** `QueryReadModels:ClientPaymentSummaryReadEnabled=false` + restart → summary anında Command DB yoluna döner. Kod geri alımı gerekmez. Startup logundaki `ClientPaymentSummaryReadEnabled=False` ile doğrulanır.

---

## 8. Test matrisi (referans)

| Test | Doğrulanan |
|---|---|
| `ClientPaymentSummaryReadRoutingOptionsTests` | 6 appsettings default false |
| `ClientPaymentSummaryQueryHandlerFeatureFlagTests` | Flag false→Command DB; active clinic→Query DB; tenant-wide→Query DB (clinic filtresi yok); multi-clinic→Command fallback; scope fail→Command fallback; empty→zero summary fallback yok; reader throws→fallback yok; request mapping; DTO mapping + tek/çoklu currency `TotalPaidAmount`; client not found / tenant missing guard |
| `ClientPaymentSummaryReadModelReaderIntegrationTests` | Tenant/client/clinic izolasyon, currency totals + last payment, recent ordering + Take, ClinicName/PetName mapping, boş sonuç, nullable pet |
| `PaymentListRolloutAcceptanceIntegrationTests` (defaults) | Yeni flag default false posture |

---

## 9. Riskler

| Risk | Açıklama / Azaltma |
|---|---|
| ClinicName drift | Klinik rename ayrı payment event tetiklemez (bilinen denormalizasyon); backfill ile hizalanır (15D). |
| Projection lag | Summary count/lastAt/recent gecikmeli olabilir; rollout sırası (parity/health) bunu kapatır. |
| Tenant-wide over-exposure | Mevcut Command DB davranışıyla aynıdır (yeni risk eklenmedi); multi-clinic ClinicAdmin Command DB'de kalır. |
| Clinic-scoped index | Yoğun müşteride `(TenantId, ClinicId, ClientId, PaidAtUtc)` tercih edilir; route blokörü değil (14B index yeterli). |

---

## 10. Kapsam dışı

- Schema / migration / `PaymentReadModel` alan/index değişikliği.
- `PaymentProjectionProcessor` değişikliği.
- Payment list routing (`PaymentsListReadEnabled`).
- Dashboard recent routing (`DashboardRecentPaymentsReadEnabled`).
- Report/export taşıma, GetById taşıma.
- Search parity genişletme.
- Frontend değişikliği.
- Test çalıştırma, commit.
