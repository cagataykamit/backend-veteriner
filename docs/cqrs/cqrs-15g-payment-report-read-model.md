# CQRS-15G — Payment report JSON Query DB route

**Tür:** Feature flag routing + Query DB reader. **Schema/migration/projection/backfill/parity/health/DTO değişmedi.**

**Ön durum:** CQRS-15F stratejisi payment report JSON yüzeyinin guard ile Query DB'ye taşınabileceğini, export CSV/XLSX'in Command DB'de kalması gerektiğini, search dolu iken Command DB guard kullanılacağını ve multi-clinic scope'un bu fazda Query DB ile temsil edilmeyeceğini saptadı. Bu faz route'u açar.

**İlgili dokümanlar:** [`cqrs-15f-payment-report-export-read-model-strategy.md`](cqrs-15f-payment-report-export-read-model-strategy.md) · [`cqrs-15e-client-payment-summary-read-model.md`](cqrs-15e-client-payment-summary-read-model.md) · [`cqrs-15d-payment-read-model-clinic-name-enrichment.md`](cqrs-15d-payment-read-model-clinic-name-enrichment.md) · [`cqrs-14e-payment-list-routing.md`](cqrs-14e-payment-list-routing.md)

---

## 1. Bu fazda ne değişti?

| Bileşen | Açıklama |
|---|---|
| `QueryReadModelsOptions.PaymentsReportReadEnabled` | Yeni feature flag, default **false** |
| `IPaymentsReportReadModelReader` | Query DB `PaymentReadModels` report okuma abstraction |
| `PaymentsReportReadRequest` / `PaymentsReportReadResult` | Reader istek/sonuç sözleşmeleri |
| `PaymentsReportReadModelReader` | SQL aggregate (`COUNT` / `SUM`) + sayfalı items `PaidAtUtc DESC, PaymentId DESC` |
| `GetPaymentsReportQueryHandler` | Flag + search guard + scope guard routing (Query DB / Command DB fallback) |
| `appsettings*.json` (6 dosya) | Explicit `PaymentsReportReadEnabled = false` |
| `CqrsStartupConfigurationLogger` | Startup log satırına flag eklendi |
| Testler | Unit routing + appsettings default + integration reader |

**Yapılmadı:** Schema/migration, `PaymentReadModel` alan/index, `PaymentProjectionProcessor`, backfill, parity, health, DTO, payment list/dashboard recent/client summary/GetById routing, **export CSV/XLSX taşıma**, search parity, frontend.

---

## 2. Flag adı ve default

```json
"QueryReadModels": {
  "PaymentsReportReadEnabled": false
}
```

Tüm ortam dosyalarında explicit **false**. Production okuma yolu değişmedi. Export için ayrı flag eklenmedi (export her zaman Command DB).

---

## 3. Routing kuralları

`GET /api/v1/reports/payments` → `GetPaymentsReportQueryHandler`

Validation (tenant guard, tarih aralığı, clinic context mismatch, `IClinicReadScopeResolver` scope çözümü) **her iki yolda da birebir korunur** ve flag'tan bağımsız ilk adımdır. Routing yalnızca aggregate + items kaynağını belirler.

| Koşul | Kaynak |
|---|---|
| `PaymentsReportReadEnabled = false` | **Command DB** (mevcut path, birebir) |
| `true` + search **boş/whitespace** + scope **tek kliniğe** (`SingleClinicId`) çözüldü | **Query DB** `PaymentReadModels` (o klinik filtreli) |
| `true` + search **boş/whitespace** + scope **tenant-wide** (Admin/Owner, aktif klinik yok: `SingleClinicId` null + `AccessibleClinicIds` null) | **Query DB** `PaymentReadModels` (**clinic filtresi yok**, `TenantId` + request filtreleri) |
| `true` + search **dolu** | **Command DB** (search parity 15I'ye bırakıldı) |
| `true` + scope **multi-clinic** (ClinicAdmin, aktif klinik yok: `AccessibleClinicIds` dolu) | **Command DB** (scope represent edilemez → fallback) |
| `true` + scope resolve **hata** (Clinics.NotFound / AccessDenied) | Validation failure (Command path da aynı scope'a bağlıdır; Query DB'ye gidilmez) |

Desteklenen filtreler Query path'te Command path ile birebirdir: **date range, clinic, client, pet, method**. (Report JSON sözleşmesinde currency filtresi yoktur; eklenmedi.) Explicit `clinicId` validation tarafından scope'a çevrilir; kullanıcı scope'u ile uyumluysa Query DB'ye tek klinik olarak uygulanır, değilse validation `Clinics.AccessDenied` döndürür.

---

## 4. Search guard

- `request.Search` null/empty/whitespace (`ListQueryTextSearch.Normalize(...) is null`) → Query path uygun.
- Search dolu → Command DB. Query path'te **search resolution çalıştırılmaz** ve **Command DB lookup yapılmaz** (search boş olduğu garanti). `PaymentsReportSearchResolution` yalnız Command path için korunur.

---

## 5. Scope guard

- **Tek klinik** (`SingleClinicId`): yalnız o klinik (`ClinicId` filtresi).
- **Tenant-wide** (Admin/Owner, aktif klinik yok; `SingleClinicId` null + `AccessibleClinicIds` null): `ClinicId` filtresi yok, `TenantId` + request filtreleri. Mevcut Command DB tenant-wide davranışı ile aynı.
- **Multi-clinic** (ClinicAdmin, aktif klinik yok; `AccessibleClinicIds` dolu): tek `ClinicId`/tenant-wide ile represent edilemez → Command DB fallback (`ClinicId IN (...)` Command path'te korunur).

Kural 15E (`GetClientPaymentSummaryQueryHandler`) ile birebir aynıdır.

---

## 6. Query DB fallback yok

Query path seçildiğinde Command DB'ye **fallback yapılmaz**:

- Query DB boş → `TotalCount = 0`, `TotalAmount = 0`, `Items = []`. `PaymentReportResultDto` semantiği korunur.
- Query DB hata verirse → exception propagate olur; Command DB tekrar denenmez. Rollback = flag kapat + restart.

---

## 7. Aggregate / read davranışı

- Kaynak: `PaymentReadModels`, filtre: `TenantId` (+ opsiyonel tek `ClinicId`) + date range + opsiyonel client/pet/method.
- `TotalCount`: SQL `COUNT(*)` (sayfalamadan bağımsız).
- `TotalAmount`: SQL `SUM(Amount)` (boş küme → `0`, nullable coalesce). **Mixed currency** mevcut davranışla aynı: tüm tutarlar currency'den bağımsız toplanır.
- `Items`: `PaidAtUtc DESC, PaymentId DESC`, `Skip((page-1)*pageSize).Take(pageSize)`. Page/pageSize clamp'i (`Math.Max(1, page)`, `Math.Clamp(pageSize, 1, MaxPageSize)`) handler'da Command path ile aynı uygulanır.
- `ClinicName` / `ClientName` / `PetName` denormalize (15D) — ek Command DB lookup yok.
- **Tüm satırlar sırf aggregate için belleğe çekilmez** (Command DB report path'inin `ListAsync(amounts)` + sum-in-memory davranışı Query path'te SQL `SUM` ile karşılanır).

---

## 8. Export kapsam dışı

Export CSV/XLSX (`ExportPaymentsReportQueryHandler`, `ExportPaymentsReportXlsxQueryHandler`) **bu fazda Command DB'de kalır**; `PaymentsReportReadEnabled` bayrağını okumaz, reader'a bağımlı değildir. Export pipeline değişmedi.

---

## 9. Rollout sırası

1. Query DB migration'ları uygulanmış olmalı (14B `PaymentReadModels` + 15D `ClinicName`).
2. `PaymentProjection:Enabled=true`.
3. `backfill-payment-read-models` çalıştır.
4. PaymentReadModel parity **InSync** doğrula (`ClinicName` dahil).
5. PaymentProjection health **Healthy/InSync** doğrula.
6. Staging'de `QueryReadModels:PaymentsReportReadEnabled=true` + restart; report JSON smoke (total/totalAmount + filtreler + ordering + ClinicName). Production'da aynı sıra.

**Rollback:** `QueryReadModels:PaymentsReportReadEnabled=false` + restart → report anında Command DB yoluna döner. Kod geri alımı gerekmez. Startup logundaki `PaymentsReportReadEnabled=False` ile doğrulanır.

---

## 10. Test matrisi (referans)

| Test | Doğrulanan |
|---|---|
| `PaymentsReportReadRoutingOptionsTests` | 6 appsettings default false |
| `PaymentsReportReadRoutingTests` | Flag false→Command DB; search boş + active clinic→Query DB; tenant-wide→Query DB (clinic filtresi yok); search dolu→Command fallback; whitespace search→Query DB; multi-clinic→Command fallback; scope fail→Query yok + error korunur; empty→zero report fallback yok; reader throws→fallback yok; filtre/paging mapping; DTO mapping + ClinicName |
| `PaymentsReportReadModelReaderIntegrationTests` | Tenant/clinic izolasyon, date range/client/pet/method filtre, mixed currency toplamı, ordering, paging + unpaged total, denormalize alan mapping, boş sonuç |
| `GetPaymentsReportQueryHandlerTests` / `...PaymentsSearchLookupFeatureFlagTests` | Mevcut Command DB davranışı (flag false) regresyonsuz |
| `PaymentListRolloutAcceptanceIntegrationTests` (defaults) | Yeni flag default false posture |

---

## 11. Riskler

| Risk | Açıklama / Azaltma |
|---|---|
| Projection lag | Report total/items gecikmeli olabilir; rollout sırası (parity/health) bunu kapatır. |
| ClinicName drift | Klinik rename ayrı payment event tetiklemez (bilinen denormalizasyon); backfill ile hizalanır (15D). |
| Search parity | Search dolu istekler bilinçli olarak Command DB'de kalır (15I'ye bırakıldı); Query path search çalıştırmaz. |
| Mixed currency total | Mevcut Command DB davranışı (currency'den bağımsız toplam) Query path'te birebir korunur; rapor tüketicisi bunu varsayar. |
| Multi-clinic over/under-scope | ClinicAdmin tenant-wide isteği Command DB'de kalır; Query DB ile temsil edilmez (yeni kısıtlama yok). |

---

## 12. Kapsam dışı

- Export CSV/XLSX route/pipeline değişikliği.
- Schema / migration / `PaymentReadModel` alan/index değişikliği.
- `PaymentProjectionProcessor` değişikliği.
- Payment list / dashboard recent / client summary routing değişikliği.
- GetById taşıma.
- Search parity genişletme.
- Frontend değişikliği.
- Test çalıştırma, commit.
