# CQRS-15A — Payment read surface audit

**Tür:** İnceleme + dokümantasyon. **Production kod değişmedi.**

**Ön durum:** CQRS-14 (14B–14G) kapandı — `PaymentReadModels` tablosu, projection, backfill, parity, health ve `PaymentsListReadEnabled` routing (default **false**) tamamlandı.

**İlgili dokümanlar:** [`cqrs-14b`](cqrs-14b-payment-read-model-schema.md) · [`cqrs-14c`](cqrs-14c-payment-read-model-projection.md) · [`cqrs-14d`](cqrs-14d-payment-read-model-reader.md) · [`cqrs-14e`](cqrs-14e-payment-list-routing.md) · [`cqrs-14f`](cqrs-14f-payment-list-backfill-parity-health.md) · [`cqrs-14g`](cqrs-14g-payment-list-rollout-acceptance.md) · [`cqrs-13e`](cqrs-13e-payment-finance-dashboard-routing.md)

---

## 1. Özet

CQRS-14 sonrası **yalnızca ödeme listesi** (`GET /payments`, arama boş + tek klinik + flag açık) Query DB `PaymentReadModels` üzerinden okunabilir. Diğer tüm payment read yüzeyleri **Command DB** (`IReadRepository<Payment>`) kullanmaya devam eder.

Finance tarafında dashboard **totals + 7 günlük trend** (`DashboardFinanceReadEnabled`) Query DB `ClinicDailyPaymentStatsReadModel` ile okunabilir; **recent payments** hâlâ Command DB'dedir.

| Kategori | Yüzey sayısı (Command DB) | Query DB'ye kısmen/hazır |
|---|---:|---|
| Birincil API read | 6 | 1 (list, koşullu) |
| Rapor/export | 3 (JSON + CSV + XLSX) | 0 |
| Dashboard | 1 (recent; totals ayrı flag) | 1 (totals/trend, koşullu) |
| Client/pet/examination gömülü | 3 | 0 |

**En güvenli sonraki faz:** **CQRS-15B — Dashboard recent payments Query DB route** (10 satır, mevcut index + denormalize isimler, düşük risk).

---

## 2. İncelenen yüzeyler

| # | Yüzey | Endpoint / handler | Kaynak dosya |
|---|---|---|---|
| 1 | Payment list | `GET /api/v1/payments` · `GetPaymentsListQueryHandler` | `PaymentsController`, `GetPaymentsListQueryHandler` |
| 2 | Payment GetById | `GET /api/v1/payments/{id}` · `GetPaymentByIdQueryHandler` | `PaymentsController`, `GetPaymentByIdQueryHandler` |
| 3 | Payment report JSON | `GET /api/v1/reports/payments` · `GetPaymentsReportQueryHandler` | `ReportsController`, `GetPaymentsReportQueryHandler` |
| 4 | Payment export CSV | `GET /api/v1/reports/payments/export` · `ExportPaymentsReportQueryHandler` | `ReportsController`, `ExportPaymentsReportQueryHandler` |
| 5 | Payment export XLSX | `GET /api/v1/reports/payments/export-xlsx` · `ExportPaymentsReportXlsxQueryHandler` | `ReportsController`, `ExportPaymentsReportXlsxQueryHandler` |
| 6 | Dashboard recent payments | `GET /api/v1/dashboard/finance-summary` (partial) · `GetDashboardFinanceSummaryQueryHandler` | `DashboardController`, `GetDashboardFinanceSummaryQueryHandler` |
| 7 | Client payment summary | `GET /api/v1/clients/{id}/payment-summary` · `GetClientPaymentSummaryQueryHandler` | `ClientsController`, `GetClientPaymentSummaryQueryHandler` |
| 8 | Examination related payments | `GET /api/v1/examinations/{id}/related-summary` (partial) · `GetExaminationRelatedSummaryQueryHandler` | `PaymentsForExaminationRelatedSpec` |
| 9 | Pet history recent payments | `GET /api/v1/pets/{id}/history-summary` (partial) · `GetPetHistorySummaryQueryHandler` | `PaymentsForPetRecentSpec` |
| 10 | Dashboard finance aggregates | Aynı finance-summary (totals/trend) · `IDashboardFinanceReadModelReader` / `IDashboardFinancePaymentAggregatesReader` | 13E — **zaten Query path var** |

**Okunmayan (write-only / projection):** `PaymentProjectionProcessor`, backfill, parity, health — CQRS-14 kapsamında.

---

## 3. Yüzey bazlı tablo

### 3.1 Payment list (14E/14G — mevcut durum özeti)

| Alan | Değer |
|---|---|
| **Surface name** | Payment list |
| **Current source** | **Mixed** — flag + koşullara göre Query DB veya Command DB |
| **Handler/endpoint** | `GetPaymentsListQueryHandler` · `GET /api/v1/payments` |
| **Required fields** | `PaymentListItemDto`: Id, ClinicId, ClientId, ClientName, PetId, PetName, Amount, Currency, Method, PaidAtUtc |
| **PaymentReadModel sufficient?** | **yes** (list DTO için) |
| **Indexes sufficient?** | **yes** (`TenantId+ClinicId+PaidAtUtc+PaymentId` DESC) |
| **Search/filter complexity** | clientId, petId, method, paidFrom/To UTC; search OR (client/pet/notes/currency + lookup ID'leri) |
| **Security scope risk** | **medium** — klinik kapsamı zorunlu; Query path yalnız `SingleClinicId`; multi-clinic scope Command DB'de kalır; Query path'te fallback yok |
| **Recommended action** | Rollout devam (14G); search parity ayrı faz |
| **Suggested phase** | — (tamamlandı); search → **15F** |
| **Risk level** | **low** (routing mevcut, default kapalı) |

**Routing özeti (`PaymentsListReadEnabled`):**

| Koşul | Kaynak |
|---|---|
| Flag false | Command DB |
| Flag true + search boş + single clinic scope | Query DB |
| Search dolu | Command DB (search route guard) |
| Single clinic scope yok | Command DB |
| Query path seçilirse | Fallback **yok** |

---

### 3.2 Payment GetById

| Alan | Değer |
|---|---|
| **Surface name** | Payment GetById |
| **Current source** | **Command DB** |
| **Handler/endpoint** | `GetPaymentByIdQueryHandler` · `GET /api/v1/payments/{id}` |
| **Required fields** | `PaymentDetailDto`: Id, TenantId, ClinicId, ClientId, ClientName, PetId, PetName, AppointmentId, ExaminationId, Amount, Currency, Method, PaidAtUtc, Notes |
| **PaymentReadModel sufficient?** | **yes** — tüm alanlar mevcut (`PaymentId`→Id, denormalize ClientName/PetName) |
| **Indexes sufficient?** | **yes** (PK `PaymentId`) |
| **Search/filter complexity** | Yok (tek PK lookup) |
| **Security scope risk** | **medium** — tenant filtresi var; aktif clinic context varsa `ClinicId` eşleşmesi; `ClinicReadScopeResolver` kullanılmıyor (list/report'tan farklı). Query route'ta projection lag → stale detail riski |
| **Recommended action** | Şimdilik Command DB'de kalabilir; taşınacaksa **ayrı read-model gerekmez**, `PaymentReadModel` yeterli |
| **Suggested phase** | **CQRS-15E** (düşük aciliyet) |
| **Risk level** | **low–medium** |

**Karar:** Detail ekranı için eksik alan yok. Tek satır okuma; operasyonel yük düşük. Strong consistency tercih ediliyorsa Command DB mantıklı kalır.

---

### 3.3 Payment report JSON

| Alan | Değer |
|---|---|
| **Surface name** | Payment report JSON |
| **Current source** | **Command DB** |
| **Handler/endpoint** | `GetPaymentsReportQueryHandler` · `GET /api/v1/reports/payments` |
| **Required fields** | `PaymentReportResultDto`: totalCount, totalAmount, items (`PaymentReportItemDto` + **ClinicName**) |
| **PaymentReadModel sufficient?** | **partial** — satır alanları var; **ClinicName eksik** |
| **Indexes sufficient?** | **partial** — tarih aralığı + clinic list index uygun; multi-clinic `accessibleClinicIds` için composite index yok |
| **Search/filter complexity** | **Yüksek** — zorunlu `from`/`to` (max 93 gün), clinicId, method, clientId, petId, search (list ile aynı lookup), sayfalama (max 200) |
| **Security scope risk** | **medium** — `PaymentsReportQueryValidation` + `ClinicReadScopeResolver`; multi-clinic erişim desteklenir |
| **Recommended action** | Report + export için **ortak Query reader**; ClinicName enrichment stratejisi (Clinic read-model lookup veya projection'a `ClinicName`); search dolu iken Command guard |
| **Suggested phase** | **CQRS-15D** |
| **Risk level** | **high** |

**Spec/join özeti:** `PaymentsFilteredCountSpec`, `PaymentsFilteredAmountsSpec`, `PaymentsFilteredPagedSpec` + `PaymentsReportItemMapping` (Client, Pet, **Clinic** lookup).

**Export ile paylaşım:** Evet — aynı filtre semantiği ve `PaymentReportItemDto`; export pipeline ortak olmalı.

---

### 3.4 Payment export (CSV + XLSX)

| Alan | Değer |
|---|---|
| **Surface name** | Payment export |
| **Current source** | **Command DB** (ortak `PaymentsReportExportPipeline`) |
| **Handler/endpoint** | `ExportPaymentsReportQueryHandler` (CSV) · `ExportPaymentsReportXlsxQueryHandler` (XLSX) · `GET .../payments/export` · `GET .../payments/export-xlsx` |
| **Required fields** | `PaymentReportItemDto` → CSV/XLSX (Tarih, Klinik, Müşteri, Hayvan, Tutar, Para Birimi, Yöntem, Not — ID kolonları yok) |
| **PaymentReadModel sufficient?** | **partial** (ClinicName eksik) |
| **Indexes sufficient?** | **partial** (report ile aynı) |
| **Search/filter complexity** | Report JSON ile aynı; **sayfa yok** |
| **Security scope risk** | **medium** (report ile aynı) |
| **Recommended action** | 15D ile birlikte taşınmalı; **MaxExportRows = 50_000** — tüm satırlar bellekte; Query DB'de de paging/streaming ihtiyacı **devam eder** (mevcut mimari değişmeden taşınsa bile) |
| **Suggested phase** | **CQRS-15D** (JSON ile birlikte) |
| **Risk level** | **high** |

**Formatlar:** UTF-8 BOM CSV (`;` ayırıcı, Europe/Istanbul tarih) + XLSX (ClosedXML).

**Memory/performance:** `PaymentsReportExportPipeline` önce count, sonra `PaymentsFilteredOrderedForReportSpec` ile **tüm satırları** yükler; client/pet/clinic hydration. 50k tavan bellek baskısı yapabilir.

---

### 3.5 Dashboard recent payments

| Alan | Değer |
|---|---|
| **Surface name** | Dashboard recent payments |
| **Current source** | **Command DB** (totals/trend ayrı — `DashboardFinanceReadEnabled` ile Query DB olabilir) |
| **Handler/endpoint** | `GetDashboardFinanceSummaryQueryHandler` · `PaymentsForDashboardRecentSpec` · `GET /api/v1/dashboard/finance-summary` |
| **Required fields** | `DashboardFinanceRecentPaymentDto` (10 kayıt): Id, PaidAtUtc, ClientId, ClientName, PetId, PetName, Amount, Currency, Method |
| **PaymentReadModel sufficient?** | **yes** — denormalize ClientName/PetName ile Command lookup'ları kaldırılabilir |
| **Indexes sufficient?** | **yes** (`IX_PaymentReadModels_TenantId_ClinicId_PaidAtUtc_PaymentId`) |
| **Search/filter complexity** | Düşük — Take(10), PaidAtUtc DESC; clinicId opsiyonel (tenant-wide) |
| **Security scope risk** | **low** — tenant + opsiyonel clinic filtresi; 13E ile uyumlu scope |
| **Recommended action** | **En kolay sonraki aday** — yeni reader veya list reader genişletmesi; flag (`DashboardRecentPaymentsReadEnabled` veya `PaymentsListReadEnabled` genişletmesi) |
| **Suggested phase** | **CQRS-15B** |
| **Risk level** | **low** |

**Finance read-model ilişkisi:** `ClinicDailyPaymentStatsReadModel` yalnız aggregate; recent için **PaymentReadModel** doğru kaynak. 13E dokümanı recent'in Command DB'de kaldığını açıkça belirtir.

---

### 3.6 Client payment summary

| Alan | Değer |
|---|---|
| **Surface name** | Client payment summary |
| **Current source** | **Command DB** |
| **Handler/endpoint** | `GetClientPaymentSummaryQueryHandler` · `GET /api/v1/clients/{id}/payment-summary` |
| **Required fields** | Count, currencyTotals, lastPaymentAtUtc, recentPayments (10) — **ClinicName** recent'te gerekli |
| **PaymentReadModel sufficient?** | **partial** — satır alanları var; ClinicName yok; tüm ödemeler bellekte aggregate |
| **Finance aggregate sufficient?** | **no** — `ClinicDailyPaymentStatsReadModel` client boyutu içermez |
| **Indexes sufficient?** | **partial** — `TenantId+ClientId+PaidAtUtc` var; clinic-scoped summary için `TenantId+ClinicId+ClientId+PaidAtUtc` daha iyi |
| **Search/filter complexity** | Orta — clientId sabit; opsiyonel clinic context; **tüm eşleşen ödemeler** yüklenir (recent için Take yok, in-memory aggregate) |
| **Security scope risk** | **medium** — müşteri tenant kontrolü; clinic context varsa filtre |
| **Recommended action** | Query DB route veya ileride `ClientPaymentStatsReadModel`; önce PaymentReadModel + clinic lookup ile taşınabilir |
| **Suggested phase** | **CQRS-15C** |
| **Risk level** | **medium** |

---

### 3.7 Diğer payment read yüzeyleri

| Surface name | Source | Handler/spec | PaymentReadModel | Index | Phase | Risk |
|---|---|---|---|---|---|---|
| Examination related payments | Command DB | `PaymentsForExaminationRelatedSpec` · `GetExaminationRelatedSummaryQueryHandler` | partial (ExaminationId var, ClinicName yok) | **no** (`ExaminationId` index yok) | 15G+ | low |
| Pet history recent payments | Command DB | `PaymentsForPetRecentSpec` · `GetPetHistorySummaryQueryHandler` | partial (PetId var, ClinicName yok) | **no** (`PetId` filter index yok) | 15G+ | low |
| Dashboard finance totals/trend | Mixed | `DashboardFinanceReadEnabled` → Query DB daily stats | N/A (aggregate model) | yes (finance) | 13E (done) | low |
| Payment list search (Query path) | Command DB (guard) | Reader search metodu var, handler route etmez | yes (kısıtlı parity) | partial | **15F** | medium |
| Payment list multi-clinic scope | Command DB | `accessibleClinicIds` | partial (reader tek clinic) | partial | **15H** | medium |

**Bulunmayan yüzeyler:** Payment autocomplete, unpaid/paid status lookup, ayrı stats/totals endpoint'i yok (dashboard finance + client summary karşılıyor).

---

## 4. PaymentReadModel yeterlilik analizi

### 4.1 Mevcut kolonlar (Query DB)

`PaymentId`, `TenantId`, `ClinicId`, `ClientId`, `ClientName`, `ClientNameNormalized`, `PetId`, `PetName`, `PetNameNormalized`, `Amount`, `Currency`, `Method`, `PaidAtUtc`, `Notes`, `NotesNormalized`, `AppointmentId`, `ExaminationId`, `LastEventId`, `LastEventOccurredAtUtc`, `LastProjectedAtUtc`.

### 4.2 DTO karşılaştırması

| DTO / yüzey | PaymentReadModel karşılığı | Eksik |
|---|---|---|
| `PaymentListItemDto` | Tam | — |
| `PaymentDetailDto` | Tam (+ TenantId) | — |
| `PaymentReportItemDto` | Satır alanları | **ClinicName** |
| `DashboardFinanceRecentPaymentDto` | Tam | — |
| `ClientPaymentRecentItemDto` | Satır alanları | **ClinicName** |
| `ExaminationRelatedPaymentItemDto` | Amount, currency, method, clinicId | **ClinicName** |
| `PetHistoryPaymentItemDto` | Aynı | **ClinicName** |

### 4.3 Finance read-model alanları (referans)

| Model | Amaç | Client/report için yeterli? |
|---|---|---|
| `ClinicDailyPaymentStatsReadModel` | Dashboard totals/trend (Tenant+Clinic+LocalDate+Currency) | **no** |
| `PaymentDailyContributionReadModel` | Finance projection katkı / bucket recompute | **no** |

---

## 5. Eksik alan / index listesi

### 5.1 Eksik veya tartışmalı alanlar

| Alan | Etkilenen yüzeyler | Öneri |
|---|---|---|
| `ClinicName` | Report, export, client summary recent, examination/pet gömülü | Projection enrichment **veya** runtime `Clinic` read-model lookup (list'teki client/pet pattern) |
| Client email/phone/breed (search) | List (search guard), report/export search | Search parity fazında snapshot'a taşınmaz; lookup ID stratejisi korunur veya genişletilir |

### 5.2 Eksik indexler (Query DB taşıma için)

| Index | Amaç | Öncelik |
|---|---|---|
| `(TenantId, ClinicId, ClientId, PaidAtUtc DESC)` | Client summary clinic-scoped | medium (15C) |
| `(TenantId, ClinicId, PetId, PaidAtUtc DESC)` | Pet history recent | low (15G) |
| `(TenantId, ClinicId, ExaminationId, PaidAtUtc DESC)` | Examination related | low (15G) |
| Multi-clinic `IN (accessibleClinicIds)` | List/report tenant-wide kullanıcı | medium (15H) — veya Command guard devam |

Mevcut indexler (14B): list/recent, client summary (tenant+client), search normalized name.

---

## 6. Önerilen faz sırası

Kod incelemesi sonrası önerilen sıra (kullanıcı tahmininden farklı gerekçelerle):

| Sıra | Faz | Gerekçe | Risk |
|---|---|---|---|
| 1 | **CQRS-15B** — Dashboard recent payments Query DB route | 10 satır, index hazır, denormalize isimler, 13E pattern devamı, en küçük diff | low |
| 2 | **CQRS-15C** — Client payment summary route | Orta karmaşıklık; client index var; ClinicName stratejisi netleşir | medium |
| 3 | **CQRS-15D** — Payment report/export read-model strategy | Yüksek karmaşıklık; JSON+CSV+XLSX ortak pipeline; search + multi-clinic + 50k bellek | high |
| 4 | **CQRS-15E** — Payment GetById (opsiyonel) | Schema yeterli; düşük trafik; projection lag endişesi | low–medium |
| 5 | **CQRS-15F** — List/report search Query parity + routing | Search route guard kaldırma öncesi parity testleri | medium |
| 6 | **CQRS-15G** — Examination/pet embedded payments | Düşük trafik; ek index | low |
| 7 | **CQRS-15H** — Multi-clinic scope Query list/report | `accessibleClinicIds` reader desteği | medium |

---

## 7. Riskler

| Risk | Açıklama | Etki |
|---|---|---|
| Projection lag | Query path'te yeni/ güncel ödeme gecikmeli görünür | Detail/list/recent |
| Fallback yok (list) | Query boş → boş sonuç; operasyonel backfill/health şart | list rollout |
| Search parity | Query reader yalnız normalized name/notes/currency; email/telefon/ırk yok | list, report, export |
| Export bellek | 50k satır + hydration — Query DB taşınsa da devam eder | export |
| Mixed currency totals | Report `totalAmount` ve dashboard toplamları para birimi ayırmadan SUM | mevcut davranış |
| GetById scope tutarsızlığı | List clinic zorunlu; GetById yalnız context eşleşmesi — tenant-wide ID ile okuma mümkün | güvenlik (mevcut) |
| Client summary ölçek | Yüksek hacimli müşteride tüm ödemeler bellekte | Command ve Query'de aynı |

---

## 8. Kapsam dışı bırakılanlar (bu audit)

- Production kod / handler / routing değişikliği
- Feature flag ekleme
- Schema / migration
- Test ekleme / çalıştırma
- Backfill / parity / health değişikliği
- Frontend
- Git commit

---

## 9. Net karar

### Hemen taşınabilir (schema/index hazır, düşük diff)

- **Dashboard recent payments** (15B) — `PaymentReadModel` + mevcut clinic+PaidAt index
- **Payment GetById** (15E) — teknik olarak hazır; operasyonel öncelik düşük

### Ek schema / enrichment / reader gerekir

- **Payment report + export** (15D) — ClinicName; ortak reader; search guard; multi-clinic
- **Client payment summary** (15C) — ClinicName; isteğe bağlı composite index; aggregate stratejisi
- **List search Query route** (15F) — search parity genişletmesi veya bilinçli kısıtlama
- **Examination / pet gömülü** (15G) — PetId / ExaminationId index

### Şimdilik Command DB'de kalmalı (veya son faz)

- **Report/export** — 15D tamamlanana kadar (yüksek risk, ortak pipeline)
- **Client summary** — 15C öncesi (tüm satır yükleme + ClinicName)
- **GetById** — strong consistency / düşük öncelik tercih edilirse
- **Multi-clinic list/report Query** — 15H

---

## 10. Test envanteri (referans)

| Alan | Test dosyaları |
|---|---|
| List routing / flag | `PaymentListQueryHandlerFeatureFlagTests`, `PaymentListRolloutAcceptanceIntegrationTests` |
| List reader | `PaymentReadModelReaderIntegrationTests` |
| Parity / backfill / health | `PaymentReadModelParityIntegrationTests`, `PaymentReadModelBackfillIntegrationTests`, `PaymentProjectionHealthIntegrationTests` |
| Report | `GetPaymentsReportQueryHandlerTests`, `GetPaymentsReportQueryHandlerPaymentsSearchLookupFeatureFlagTests`, `PaymentReportSearchLookupSmokeIntegrationTests` |
| Export | `ExportPaymentsReportQueryHandlerTests`, `ExportPaymentsReportXlsxQueryHandlerTests`, `ExportPaymentsReportPaymentsSearchLookupFeatureFlagTests`, `PaymentExportSearchLookupSmokeIntegrationTests` |
| GetById | `GetPaymentByIdQueryHandlerTests` |
| Dashboard finance | `DashboardFinanceQueryParityIntegrationTests`, `PaymentFinanceRolloutAcceptanceIntegrationTests` |
| Search lookup | `PaymentListSearchLookupSmokeIntegrationTests`, `GetPaymentsListQueryHandlerPaymentsSearchLookupFeatureFlagTests` |

---

## 11. Feature flag durumu (audit anı)

Tüm ortam `appsettings*.json`: `PaymentsListReadEnabled=false`, `DashboardFinanceReadEnabled=false`, `PaymentsSearchLookupEnabled=false`, `PaymentProjection:Enabled=false`.
