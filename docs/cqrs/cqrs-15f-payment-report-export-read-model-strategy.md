# CQRS-15F — Payment report/export read-model strategy

**Tür:** İnceleme + dokümantasyon. **Production kod değişmedi.**

**Ön durum:**

- CQRS-14 kapandı — `PaymentReadModels`, projection, backfill, parity, health, payment list Query route (`PaymentsListReadEnabled`, default false).
- CQRS-15B — dashboard recent payments Query route (`DashboardRecentPaymentsReadEnabled`, default false).
- CQRS-15C — client payment summary route blokörü: `ClinicName` eksikti.
- CQRS-15D — `PaymentReadModels.ClinicName` eklendi; projection/backfill/parity destekliyor.
- CQRS-15E — client payment summary Query route (`ClientPaymentSummaryReadEnabled`, default false); report/export/list/GetById **dokunulmadı**.

**İlgili dokümanlar:** [`cqrs-15a-payment-read-surface-audit.md`](cqrs-15a-payment-read-surface-audit.md) · [`cqrs-15d-payment-read-model-clinic-name-enrichment.md`](cqrs-15d-payment-read-model-clinic-name-enrichment.md) · [`cqrs-15e-client-payment-summary-read-model.md`](cqrs-15e-client-payment-summary-read-model.md) · [`cqrs-14e-payment-list-routing.md`](cqrs-14e-payment-list-routing.md) · [`cqrs-12d-8-payments-report-search-lookup-routing.md`](cqrs-12d-8-payments-report-search-lookup-routing.md) · [`cqrs-12d-9-payments-export-search-lookup-routing.md`](cqrs-12d-9-payments-export-search-lookup-routing.md)

---

## 1. Özet

Payment report JSON ve payment export (CSV/XLSX) yüzeyleri **Command DB** üzerinden okunmaya devam eder. CQRS-15D sonrası **`PaymentReadModel` satır alanları tam parity** sağlar (`ClinicName` dahil). Query DB route **teknik olarak mümkün**, ancak güvenli taşıma **guard’lı, aşamalı** olmalıdır.

| Karar | Sonuç |
|---|---|
| Report JSON hemen taşınabilir mi? | **Hayır** — doğrudan değil; **guard ile** (15G): search boş + representable scope + flag |
| Export hemen taşınabilir mi? | **Hayır** — JSON’dan **sonra** (15H); bellek/performans riski yüksek |
| `PaymentReadModels` yeterli mi? | **Evet** — satır/DTO alanları için ayrı read-model gerekmez |
| Daily/finance aggregate gerekir mi? | **Hayır** — satır raporu için; `totalAmount` SQL `SUM` ile okunabilir |
| Search parity | Report/export **lookup ID stratejisi** kullanır; list Query reader’ın **direct normalized search**’ünden farklı — search dolu iken Command DB guard (14E ile uyumlu) |
| JSON + export birlikte mi? | **Hayır** — JSON önce (15G), export sonra (15H) |

**Önerilen sıra:** 15G (report JSON route) → 15H (export hardening + route) → 15I (search parity, opsiyonel) → 15J (GetById kararı).

---

## 2. İncelenen endpoint / handler / service listesi

| # | Yüzey | Endpoint | Handler / service | Kaynak dosya |
|---|---|---|---|---|
| 1 | Report JSON | `GET /api/v1/reports/payments` | `GetPaymentsReportQueryHandler` | `ReportsController`, `GetPaymentsReportQueryHandler` |
| 2 | Export CSV | `GET /api/v1/reports/payments/export` | `ExportPaymentsReportQueryHandler` | `ReportsController`, `ExportPaymentsReportQueryHandler` |
| 3 | Export XLSX | `GET /api/v1/reports/payments/export-xlsx` | `ExportPaymentsReportXlsxQueryHandler` | `ReportsController`, `ExportPaymentsReportXlsxQueryHandler` |
| 4 | Ortak export pipeline | — | `PaymentsReportExportPipeline.LoadAsync` | `PaymentsReportExportPipeline.cs` |
| 5 | Ortak validation | — | `PaymentsReportQueryValidation.ValidateAsync` | `PaymentsReportQueryValidation.cs` |
| 6 | Ortak search resolution | — | `PaymentsReportSearchResolution.ResolveSearchAsync` | `PaymentsReportSearchResolution.cs` |
| 7 | Ortak DTO mapping | — | `PaymentsReportItemMapping.MapAsync` | `PaymentsReportItemMapping.cs` |
| 8 | CSV writer | — | `PaymentsCsvWriter.WriteClinicReceiptReportUtf8Bom` | `PaymentsCsvWriter.cs` |
| 9 | XLSX writer | — | `PaymentsXlsxWriter.WriteClinicReceiptWorkbook` | `PaymentsXlsxWriter.cs` |
| 10 | Filter specs (Command DB) | — | `PaymentsFilteredCountSpec`, `PaymentsFilteredAmountsSpec`, `PaymentsFilteredPagedSpec`, `PaymentsFilteredOrderedForReportSpec` | `Application/Payments/Specs/` |
| 11 | Search lookup (paylaşımlı) | — | `PaymentsListSearchResolution.ResolveSearchIdsAsync` | `PaymentsListSearchResolution.cs` |
| 12 | Mevcut Query list reader (referans) | — | `PaymentsListReadModelReader` | `Infrastructure/Query/Payments/` |
| 13 | Read-model entity | — | `PaymentReadModel` | `Infrastructure/Persistence/Query/Models/` |

**Test envanteri (referans):**

| Alan | Test dosyaları |
|---|---|
| Report JSON | `GetPaymentsReportQueryHandlerTests`, `GetPaymentsReportQueryHandlerPaymentsSearchLookupFeatureFlagTests`, `PaymentReportSearchLookupSmokeIntegrationTests` |
| Export | `ExportPaymentsReportQueryHandlerTests`, `ExportPaymentsReportXlsxQueryHandlerTests`, `ExportPaymentsReportPaymentsSearchLookupFeatureFlagTests`, `PaymentExportSearchLookupSmokeIntegrationTests` |
| Search resolution | `PaymentsReportSearchResolutionTests` |

---

## 3. Report JSON analizi

### 3.1 Akış

```text
GET /api/v1/reports/payments
  → GetPaymentsReportQueryHandler
  → PaymentsReportQueryValidation (tenant, tarih aralığı max 93 gün, clinic scope)
  → PaymentsReportSearchResolution (opsiyonel search → pattern + client/pet ID kümeleri)
  → PaymentsFilteredCountSpec (totalCount)
  → PaymentsFilteredAmountsSpec (tüm eşleşen Amount satırları → handler'da Sum → totalAmount)
  → PaymentsFilteredPagedSpec (sayfalı satırlar, max pageSize 200)
  → PaymentsReportItemMapping (Client, Pet, Clinic Command DB lookup)
  → PaymentReportResultDto
```

### 3.2 Filtreler

| Parametre | Zorunlu | Davranış |
|---|---|---|
| `from`, `to` | Evet | UTC; `PaidAtUtc` kapalı aralık `[from, to]`; max **93 gün** |
| `clinicId` | Hayır | `effectiveClinicId = clinicId ?? IClinicContext.ClinicId`; scope resolver ile doğrulanır |
| `method` | Hayır | `PaymentMethod` eşitlik |
| `clientId` | Hayır | GUID eşitlik |
| `petId` | Hayır | GUID eşitlik |
| `search` | Hayır | Lookup + Notes/Currency LIKE (list ile aynı semantik) |
| `page`, `pageSize` | Hayır (default 1/50) | max pageSize **200** |
| `currency` | **Yok** | Ayrı filtre yok; yalnızca `search` içinde `Currency LIKE` |

**Payment list’ten fark:** Report **klinik kapsamı zorunlu değil** — tenant-wide Admin/Owner (`SingleClinicId` null, `AccessibleClinicIds` null) tüm kiracı ödemelerini görebilir. Multi-clinic ClinicAdmin `AccessibleClinicIds IN (...)` ile filtrelenir.

### 3.3 Bellek / performans

- **Sayfalı satırlar:** Yalnızca `pageSize` (≤200) satır + mapping hydration — düşük risk.
- **`totalAmount`:** `PaymentsFilteredAmountsSpec` **tüm eşleşen satırların `Amount` değerlerini** listeler; handler `Sum()` yapar. 93 günlük geniş filtrede binlerce decimal belleğe alınabilir — **orta risk** (Query DB route’ta SQL `SUM(Amount)` ile giderilebilir).
- **`totalCount`:** Ayrı count sorgusu — kabul edilebilir.

---

## 4. Export analizi

### 4.1 Akış

CSV ve XLSX **aynı pipeline**:

```text
ExportPaymentsReportQueryHandler / ExportPaymentsReportXlsxQueryHandler
  → PaymentsReportExportPipeline.LoadAsync (validation + search + count + full list)
  → PaymentsFilteredCountSpec → MaxExportRows (50_000) kontrolü
  → PaymentsFilteredOrderedForReportSpec → sayfasız TÜM satırlar (≤50k)
  → PaymentsReportItemMapping (Client, Pet, Clinic hydration)
  → PaymentsCsvWriter / PaymentsXlsxWriter
```

### 4.2 Filtreler

JSON rapor ile **birebir aynı** (`from`, `to`, `clinicId`, `method`, `clientId`, `petId`, `search`). **`page` / `pageSize` yok** — tam liste.

### 4.3 Bellek / performans

| Adım | Risk |
|---|---|
| Count (50k tavan) | Düşük |
| 50k `Payment` entity yükleme | **Yüksek** |
| Client/Pet/Clinic batch lookup | Orta–yüksek |
| `PaymentReportItemDto` listesi (50k) | **Yüksek** |
| CSV `StringBuilder` / XLSX `ClosedXML` workbook | **Yüksek** |

**Streaming/paging yok.** Query DB route hydration maliyetini azaltır (denormalize alanlar) ancak **50k satır bellek yükü devam eder**.

---

## 5. JSON report ve export aynı query/spec mi?

**Kısmen evet, tamamen hayır.**

| Bileşen | JSON | Export |
|---|---|---|
| Validation | `PaymentsReportQueryValidation` | Aynı |
| Search | `PaymentsReportSearchResolution` | Aynı |
| Count spec | `PaymentsFilteredCountSpec` | Aynı |
| Row spec | `PaymentsFilteredPagedSpec` | `PaymentsFilteredOrderedForReportSpec` (sayfasız) |
| Amount aggregate | `PaymentsFilteredAmountsSpec` | Yok (export’ta total yok) |
| Mapping | `PaymentsReportItemMapping` | Aynı |
| DTO | `PaymentReportItemDto` | Aynı → writer |

Filtre semantiği ortak; export ek olarak **50k satır tavanı** ve **tam liste** yükler.

---

## 6. Alan parity tablosu

### 6.1 `PaymentReportItemDto` ↔ `PaymentReadModel`

| DTO alanı | PaymentReadModel | 15D sonrası |
|---|---|---|
| `PaymentId` | `PaymentId` | ✓ |
| `PaidAtUtc` | `PaidAtUtc` | ✓ |
| `ClinicId` | `ClinicId` | ✓ |
| `ClinicName` | `ClinicName` | ✓ (15D) |
| `ClientId` | `ClientId` | ✓ |
| `ClientName` | `ClientName` | ✓ |
| `PetId` | `PetId` | ✓ |
| `PetName` | `PetName` | ✓ |
| `Amount` | `Amount` | ✓ |
| `Currency` | `Currency` | ✓ |
| `Method` | `Method` (int) | ✓ |
| `Notes` | `Notes` | ✓ |

**Sonuç:** Display/export kolonları için **eksik alan yok**. Command path’teki `PaymentsReportItemMapping` client/pet/clinic lookup’ları Query route’ta **kaldırılabilir** (15D denormalize alanlar).

### 6.2 Export kolonları (CSV/XLSX)

Tarih (Istanbul), Klinik, Müşteri, Hayvan, Tutar, Para Birimi, Ödeme Yöntemi, Not — hepsi `PaymentReportItemDto`’dan; teknik ID kolonları yok.

---

## 7. Search parity analizi

### 7.1 Report/export search akışı (Command DB — mevcut)

1. `ListQueryTextSearch.Normalize` + `BuildContainsLikePattern`
2. `PaymentsListSearchResolution.ResolveSearchIdsAsync` (`PaymentsSearchLookupEnabled` flag):
   - **Flag false:** `ClientsByTenantTextSearchSpec` (FullName, Email, Phone, PhoneNormalized) + `PetsByTenantTextFieldsSearchSpec` (Name, Breed, Species, BreedRef)
   - **Flag true:** `IClientReadModelLookupReader` + `IPetReadModelLookupReader` (Query DB lookup; alan kümesi Command ile hizalı)
3. Payment filtresi OR:
   - `Notes LIKE pattern`
   - `Currency LIKE pattern`
   - `ClientId IN searchClientIds` (lookup sonucu)
   - `PetId IN searchPetIds` (lookup sonucu)

### 7.2 Payment list Query reader search (14E — report **kullanmıyor**)

`PaymentsListReadModelReader.ApplyListSearchFilter` — **direct denormalize**:

- `ClientNameNormalized LIKE`
- `PetNameNormalized LIKE`
- `NotesNormalized LIKE`
- `Currency LIKE`

**Lookup ID stratejisi yok.** Email/telefon/ırk araması bu path’te **çalışmaz** — 14E bunun için search dolu iken Command DB guard koydu.

### 7.3 Parity matrisi

| Arama türü | Command report/export | List Query reader (14E) | Query report route (önerilen) |
|---|---|---|---|
| Müşteri adı | Lookup → ClientId | ClientNameNormalized | Lookup → ClientId (Command ile aynı) |
| Email / telefon | Lookup → ClientId | **Eksik** | Lookup → ClientId |
| Pet adı | Lookup → PetId | PetNameNormalized | Lookup → PetId |
| Irk / tür | Lookup → PetId | **Eksik** | Lookup → PetId |
| Notes | Payment.Notes LIKE | NotesNormalized LIKE | Notes/NotesNormalized LIKE |
| Currency | Payment.Currency LIKE | Currency LIKE | Currency LIKE |

**Karar:** Report/export için Query route **list reader’ın direct search’ünü kopyalamamalı**; mevcut **lookup ID + Notes/Currency** stratejisini korumalı. Search dolu iken ilk fazlarda **Command DB guard** (14E pattern) en güvenli seçenek. Search’ü Query’ye taşımak 15I’ye bırakılabilir (lookup + PaymentReadModel filtre implementasyonu + parity testleri).

### 7.4 `PaymentsSearchLookupEnabled` etkisi

Report/export search lookup routing **12D-8/12D-9** ile list ile paylaşımlı helper kullanır; flag yalnızca lookup kaynağını (Command spec vs Query reader) değiştirir. **Payment satırları** hâlâ Command DB’den okunur.

---

## 8. Scope parity analizi

### 8.1 Mevcut report scope (`PaymentsReportQueryValidation` + `ClinicReadScopeResolver`)

| Kullanıcı / bağlam | `SingleClinicId` | `AccessibleClinicIds` | Payment filtresi |
|---|---|---|---|
| Tek klinik (request veya JWT clinic) | Dolu | null | `ClinicId = X` |
| Tenant-wide Admin/Owner | null | null | TenantId only (tüm klinikler) |
| Multi-clinic ClinicAdmin | null | `[id,...]` | `ClinicId IN (...)` |
| ClinicAdmin, atanmış klinik yok | null | `[]` | Boş sonuç (`Where(false)`) |

**Payment list’ten fark:** List **klinik kapsamı zorunlu** (`Payments.ClinicScopeRequired`); report **tenant-wide’a izin verir**.

### 8.2 Query DB temsil edilebilirlik (15E pattern referansı)

| Scope | Query DB represent? | Öneri |
|---|---|---|
| Single clinic | Evet (`ClinicId` filtresi) | Query route aday |
| Tenant-wide | Evet (clinic filtresi yok; 15E client summary ile aynı) | Query route aday |
| Multi-clinic `IN (...)` | SQL mümkün, index zayıf | **Command DB fallback** (15E ile tutarlı) |
| Scope resolve hata | — | Command DB fallback (15E) veya hata (tercihe bağlı; report bugün hata döner) |

---

## 9. Performance / memory analizi

| Yüzey | Mevcut Command DB risk | Query DB route sonrası | İyileşme |
|---|---|---|---|
| Report JSON (sayfa) | Orta — `totalAmount` tüm Amount listesi | Düşük–orta — SQL SUM; sayfa denormalize | **Kısmen** |
| Report JSON (search) | Lookup + Command payment sorguları | Aynı lookup + Query payment (guard ile) | Minimal |
| Export CSV/XLSX | **Yüksek** — 50k entity + hydration + writer | **Yüksek** — 50k read-model + writer; hydration yok | **Kısmen** (lookup kalkar) |

**Query DB route bellek riskini export için ortadan kaldırmaz.** Streaming/paged export ayrı teknik borç olarak ele alınmalı (15H).

### 9.1 Index durumu (`PaymentReadModels`)

Mevcut (14B + 15D):

| Index | Kullanım |
|---|---|
| `PK PaymentId` | GetById |
| `(TenantId, ClinicId, PaidAtUtc DESC, PaymentId DESC)` | Tek klinik + tarih aralığı list/report |
| `(TenantId, ClientId, PaidAtUtc DESC)` | Client summary |
| `(TenantId, ClinicId, ClientNameNormalized)` | List search (direct) |
| `(TenantId, ClinicId, PetNameNormalized)` | List search (direct) |

**Eksik / tartışmalı:**

| Index | Amaç | Öncelik |
|---|---|---|
| `(TenantId, PaidAtUtc DESC, PaymentId DESC)` | Tenant-wide report (clinic filtresi yok) | **medium** (15G tenant-wide route açılırsa) |
| `(TenantId, ClinicId, Method, PaidAtUtc)` vb. | method/clientId/petId ağırlıklı filtreler | low (secondary filter) |
| Multi-clinic `IN (...)` | Composite index pratik değil | Guard ile Command fallback tercih |

---

## 10. Yüzey bazlı analiz tablosu

| Surface | Endpoint | Handler/service | Current source | Required fields | PaymentReadModel sufficient? | Missing fields | Filters | Search behavior | Scope behavior | Memory/perf risk | Query DB feasibility | Recommended action | Suggested phase | Risk |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Payment report JSON** | `GET /reports/payments` | `GetPaymentsReportQueryHandler` | Command DB + mapping hydration | `PaymentReportResultDto` + `PaymentReportItemDto` | **yes** | — | from/to (93d), clinicId, method, clientId, petId, search, page | Lookup ID + Notes/Currency | Tenant-wide + single + multi-clinic | Orta (`totalAmount` full scan) | **Guard ile evet** | Search boş + representable scope + flag; SQL SUM; ortak reader | **15G** | medium |
| **Payment export CSV** | `GET /reports/payments/export` | `ExportPaymentsReportQueryHandler` + `PaymentsReportExportPipeline` | Command DB | `PaymentReportItemDto` → CSV | **yes** | — | Aynı (sayfa yok) | Aynı | Aynı | **Yüksek** (50k) | Guard ile mümkün; bellek aynı kalır | JSON’dan **sonra**; ayrı flag; streaming kararı | **15H** | **high** |
| **Payment export XLSX** | `GET /reports/payments/export-xlsx` | `ExportPaymentsReportXlsxQueryHandler` + pipeline | Command DB | Aynı → XLSX | **yes** | — | Aynı | Aynı | Aynı | **Yüksek** | CSV ile aynı | CSV ile birlikte 15H | **15H** | **high** |

---

## 11. Karar kategorileri

### 11.1 Hemen taşınabilir

**Yok.** Routing/flag/reader/guard implementasyonu gerekiyor. Schema alan parity hazır olsa da operasyonel guard’lar olmadan taşıma önerilmez.

### 11.2 Guard ile taşınabilir

| Yüzey | Guard koşulları |
|---|---|
| **Report JSON (15G)** | `PaymentsReportReadEnabled=false` default; flag true + **search boş** + scope single-clinic **veya** tenant-wide → Query DB; search dolu → Command DB; multi-clinic → Command DB fallback; Query path’te fallback yok |
| **Export (15H)** | Ayrı flag (`PaymentsReportExportReadEnabled` önerilir); aynı scope/search guard’ları; 50k tavan korunur |

### 11.3 Ek read-model / schema / index gerekir

| İhtiyaç | Gerekçe | Faz |
|---|---|---|
| `(TenantId, PaidAtUtc, PaymentId)` index | Tenant-wide report Query performansı | 15G (route açılırsa) |
| Streaming/paged export | 50k bellek tavanı mimari borç | 15H (davranış; schema değil) |
| Search Query parity testleri | Email/phone/breed lookup + PaymentReadModel filtre | 15I |

**Ayrı daily/finance read-model gerekmez** — satır raporu `PaymentReadModels` yeterli.

### 11.4 Şimdilik Command DB’de kalmalı

| Yüzey | Gerekçe |
|---|---|
| Export (15G öncesi) | Yüksek bellek riski; JSON route ve hardening önce |
| Report/export **search dolu** (15G/15H ilk rollout) | 14E ile uyumlu güvenli guard |
| Multi-clinic scope | 15E fallback pattern; index/perf belirsizliği |

---

## 12. Strateji seçenekleri değerlendirmesi

### A) Report JSON önce, export sonra

| Soru | Cevap |
|---|---|
| Daha düşük risk mi? | **Evet** — sayfalı, 50k yük yok, UI rapor sayfası yeterli |
| UI rapor sayfası için yeterli mi? | **Evet** — `GET /reports/payments` sayfalı JSON mevcut contract |

### B) Export en sona

| Soru | Cevap |
|---|---|
| Büyük veri/memory nedeniyle doğru mu? | **Evet** — 50k satır + ClosedXML/StringBuilder; Query DB hydration’ı azaltır ama çözmez |

### C) Search boşken Query DB, search doluyken Command DB

| Soru | Cevap |
|---|---|
| 14E payment list pattern ile uyumlu mu? | **Evet** — aynı bilinçli guard; report lookup stratejisi list Query reader’dan farklı olsa da guard gerekçesi aynı (email/phone/breed) |

### D) Ayrı read model gerekir mi?

| Soru | Cevap |
|---|---|
| `PaymentReadModels` yeterli mi? | **Evet** — satır + ClinicName |
| Daily aggregate / finance read model? | **Hayır** — `ClinicDailyPaymentStatsReadModel` satır raporu için uygun değil; `totalAmount` filtreli `SUM` yeterli |

### E) Export streaming/paged export gerekir mi?

| Soru | Cevap |
|---|---|
| Mevcut export tüm satırları belleğe alıyor mu? | **Evet** — `PaymentsFilteredOrderedForReportSpec` + mapping + writer |
| Query DB bunu çözer mi? | **Hayır** — teknik borç olarak 15H’de ele alınmalı |

### JSON ve export aynı fazda mı?

**Hayır.** Ortak reader abstraction (`IPaymentsReportReadModelReader`) 15G’de tanımlanabilir; export routing **15H**’ye bırakılmalı (ayrı flag + performans hardening).

---

## 13. Önerilen migration / route sırası

| Sıra | Faz | İçerik | Risk |
|---|---|---|---|
| 1 | **CQRS-15G** | Payment report JSON Query DB route | medium |
| | | `PaymentsReportReadEnabled` (default false) | |
| | | `IPaymentsReportReadModelReader`: count, sum(amount), paged rows | |
| | | Guard: search boş; scope single-clinic veya tenant-wide; multi-clinic → Command | |
| | | Mapping hydration kaldır (denormalize alanlar) | |
| | | Opsiyonel: `(TenantId, PaidAtUtc, PaymentId)` index migration | |
| 2 | **CQRS-15H** | Payment export safety/performance | high |
| | | `PaymentsReportExportReadEnabled` (ayrı flag, default false) | |
| | | Aynı reader + scope/search guard | |
| | | Streaming veya batch export kararı; 50k tavan gözden geçirme | |
| 3 | **CQRS-15I** | Payment search parity (opsiyonel) | medium |
| | | Search dolu Query route: lookup ID + PaymentReadModel filtre | |
| | | Parity integration testleri (email/phone/breed) | |
| 4 | **CQRS-15J** | Payment GetById decision/route | low–medium |
| | | Strong consistency vs projection lag; düşük trafik | |

**Not:** 15A’daki faz numaralandırması 15B–15E ile güncellendi; bu doküman 15F strateji fazıdır, uygulama fazları 15G+ olarak numaralandırılmıştır.

---

## 14. CQRS-15G reader tasarım notları (implementasyon için referans)

Ortak filtre kümesi (Command spec’lerle hizalı):

```text
TenantId
+ optional ClinicId (single clinic)
+ optional AccessibleClinicIds IN (multi-clinic → Command fallback, reader’a girmez)
+ optional ClientId, PetId, Method
+ PaidAtUtc BETWEEN from AND to (zorunlu)
+ optional search: Notes/Currency LIKE + ClientId IN + PetId IN
```

Sıralama: `PaidAtUtc DESC, PaymentId DESC` (export ve JSON ile aynı).

DTO mapping: `PaymentReadModel` → `PaymentReportItemDto` doğrudan (lookup yok).

---

## 15. Soru-cevap özeti (15F karar checklist)

| # | Soru | Cevap |
|---|---|---|
| 1 | Report JSON hangi handler? | `GetPaymentsReportQueryHandler` · `GET /api/v1/reports/payments` |
| 2 | Export hangi handler/service? | `ExportPaymentsReportQueryHandler` / `ExportPaymentsReportXlsxQueryHandler` · `PaymentsReportExportPipeline` |
| 3 | JSON ve export aynı spec? | Ortak validation/search/count; JSON paged+amounts, export ordered full list |
| 4 | Tüm alanlar PaymentReadModel’de? | **Evet** (15D sonrası) |
| 5 | ClinicName sonrası eksik alan? | **Hayır** |
| 6 | Search hangi alanlarda? | Lookup: client ad/email/phone; pet ad/ırk/tür; payment: Notes, Currency |
| 7 | PaymentReadModel search parity yeterli mi? | **Direct normalized search için hayır**; lookup stratejisi ile **evet** |
| 8 | Command path email/phone/breed arar mı? | **Evet** — lookup spec/reader üzerinden, PaymentReadModel alanında değil |
| 9 | Report/export tüm satırları belleğe çeker mi? | JSON: yalnızca sayfa (+ totalAmount tüm Amount listesi); Export: **evet** (≤50k) |
| 10 | Export streaming/paging? | **Hayır** |
| 11 | Query DB route bellek riskini azaltır mı? | JSON: kısmen (SUM SQL, hydration yok); Export: **kısmen**, 50k yük kalır |
| 12 | Index ihtiyacı? | Tenant-wide için `(TenantId, PaidAtUtc)` önerilir; multi-clinic index yerine fallback |
| 13 | Tenant/clinic scope? | Report tenant-wide + multi-clinic destekler; list’ten farklı |
| 14 | Scope Query DB ile temsil? | Single + tenant-wide evet; multi-clinic fallback |
| 15 | JSON ve export birlikte mi taşınmalı? | **Hayır** — JSON önce (15G), export sonra (15H) |

---

## 16. Riskler

| Risk | Etki | Azaltma |
|---|---|---|
| Projection lag | Query route’ta yeni ödeme gecikmeli | Parity/health; flag rollout; fallback yok bilinci |
| `totalAmount` mixed currency | Mevcut davranış — para birimi ayırmadan SUM | Dokümante; Query route aynı semantiği korur |
| Export 50k bellek | OOM / GC baskısı | 15H streaming; export en son faz |
| Search parity (email/phone/breed) | Guard dışı Query route’ta farklı sonuç | Search dolu → Command guard (15G/15H) |
| Multi-clinic report | Query index zayıf | Command fallback (15E pattern) |
| Tenant-wide report perf | ClinicId filtresi yok | Opsiyonel tenant+PaidAt index |
| ClinicName drift | Rename event yok | 15D bilinen davranış; backfill |

---

## 17. Kapsam dışı bırakılanlar (bu faz)

- Production kod / handler / reader / flag değişikliği
- Schema / migration
- Test ekleme / çalıştırma
- Export davranışı değişikliği
- Payment list / dashboard / client summary / GetById routing değişikliği
- Git commit

---

## 18. Net karar özeti

| Yüzey | Kategori | Sonraki faz |
|---|---|---|
| Payment report JSON | **Guard ile taşınabilir** | CQRS-15G |
| Payment export CSV/XLSX | **Şimdilik Command DB** → guard + hardening sonrası | CQRS-15H |
| Search (report/export) | **Guard: Command DB** (15G/15H); tam parity opsiyonel | CQRS-15I |
| Multi-clinic report scope | **Command DB fallback** | 15G guard |
| Payment GetById | Ayrı karar | CQRS-15J |

**PaymentReadModels satır read-model olarak yeterlidir; finance daily aggregate veya ayrı report read-model gerekmez.**
