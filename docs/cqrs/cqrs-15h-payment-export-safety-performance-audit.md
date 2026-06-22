# CQRS-15H — Payment export safety/performance audit

**Tür:** İnceleme + dokümantasyon. **Production kod değişmedi.**

**Ön durum:**

- CQRS-15G tamamlandı — `GET /api/v1/reports/payments` JSON yüzeyi `PaymentsReportReadEnabled` ile Query DB `PaymentReadModels` üzerinden okunabilir (search boş + single clinic veya tenant-wide scope).
- Export CSV/XLSX **15G kapsamı dışında** bırakıldı; hâlâ Command DB path üzerinde.
- CQRS-15F stratejisi export için 50k satır bellek riskini ve Query DB route'un bunu tek başına çözmeyeceğini saptamıştı.

**İlgili dokümanlar:** [`cqrs-15f-payment-report-export-read-model-strategy.md`](cqrs-15f-payment-report-export-read-model-strategy.md) · [`cqrs-15g-payment-report-read-model.md`](cqrs-15g-payment-report-read-model.md) · [`cqrs-12d-9-payments-export-search-lookup-routing.md`](cqrs-12d-9-payments-export-search-lookup-routing.md) · [`cqrs-12d-8-payments-report-search-lookup-routing.md`](cqrs-12d-8-payments-report-search-lookup-routing.md)

---

## 1. Özet

Payment export CSV ve XLSX yüzeyleri **aynı pipeline** (`PaymentsReportExportPipeline.LoadAsync`) kullanır, **sayfalama/streaming yapmaz**, eşleşen satırları (≤50.000) **tamamen belleğe** yükler ve ardından writer ile tek seferde `byte[]` üretir. 50k tavanı **count sonrası validation** ile uygulanır; query `Take` veya writer guard değildir.

Query DB'ye taşıma **teknik olarak mümkün** (15D alan parity hazır; 15G reader referansı var), ancak:

1. **Bellek riski Query route ile devam eder** — hydration kalkar, 50k DTO + writer yükü kalır; XLSX (ClosedXML) CSV'den belirgin daha ağır.
2. **Export safety hardening önce** gelmeli (limit davranışı, bellek profili, test boşlukları).
3. **Ayrı feature flag** (`PaymentsReportExportReadEnabled`) JSON flag'inden bağımsız rollout için gerekli.
4. **Search boş → Query DB, search dolu → Command DB** pattern'i 15G JSON ile aynı şekilde export için uygundur.
5. Mevcut `IPaymentsReportReadModelReader` **sayfalı**; export için **genişletme veya ayrı export read metodu** gerekir.

**Karar özeti:**

| Yüzey | Mevcut kategori | Önerilen sonraki adım |
|---|---|---|
| Export CSV | **4 — Şimdilik Command DB'de kalmalı** | 15I hardening → 15J guard ile Query DB |
| Export XLSX | **4 — Şimdilik Command DB'de kalmalı** | CSV ile birlikte; XLSX bellek riski daha yüksek |

---

## 2. İncelenen dosyalar

| # | Bileşen | Dosya |
|---|---|---|
| 1 | Export CSV endpoint | `ReportsController.cs` → `GET /api/v1/reports/payments/export` |
| 2 | Export XLSX endpoint | `ReportsController.cs` → `GET /api/v1/reports/payments/export-xlsx` |
| 3 | CSV handler | `ExportPaymentsReportQueryHandler.cs` |
| 4 | XLSX handler | `ExportPaymentsReportXlsxQueryHandler.cs` |
| 5 | Ortak pipeline | `PaymentsReportExportPipeline.cs` |
| 6 | CSV writer | `PaymentsCsvWriter.cs` |
| 7 | XLSX writer | `PaymentsXlsxWriter.cs` |
| 8 | Validation | `PaymentsReportQueryValidation.cs` |
| 9 | Search resolution | `PaymentsReportSearchResolution.cs` |
| 10 | DTO mapping | `PaymentsReportItemMapping.cs` |
| 11 | Sabitler | `PaymentsReportConstants.cs`, `ReportsSharedLimits.cs` |
| 12 | Command DB export spec | `PaymentsFilteredOrderedForReportSpec.cs`, `PaymentsFilteredCountSpec.cs` |
| 13 | Export request/response | `ExportPaymentsReportQuery.cs`, `ExportPaymentsReportXlsxQuery.cs` |
| 14 | DTO | `PaymentReportItemDto.cs` |
| 15 | JSON Query reader (referans) | `IPaymentsReportReadModelReader.cs`, `PaymentsReportReadModelReader.cs` |
| 16 | Read-model entity/index | `PaymentReadModel.cs`, `PaymentReadModelConfiguration.cs` |
| 17 | JSON handler routing (15G) | `GetPaymentsReportQueryHandler.cs` |
| 18 | Flag options | `QueryReadModelsOptions.cs` |
| 19 | Search lookup paylaşımlı | `PaymentsListSearchResolution.cs`, `ClientsByTenantTextSearchSpec.cs`, `PetsByTenantTextFieldsSearchSpec.cs` |
| 20 | Unit testler | `ExportPaymentsReportQueryHandlerTests.cs`, `ExportPaymentsReportXlsxQueryHandlerTests.cs`, `ExportPaymentsReportPaymentsSearchLookupFeatureFlagTests.cs`, `PaymentsXlsxWriterTests.cs` |
| 21 | Integration smoke | `PaymentExportSearchLookupSmokeIntegrationTests.cs` |

---

## 3. Mevcut export akışı

```text
GET /api/v1/reports/payments/export
GET /api/v1/reports/payments/export-xlsx
  → ExportPaymentsReportQueryHandler / ExportPaymentsReportXlsxQueryHandler
  → PaymentsReportExportPipeline.LoadAsync
      → PaymentsReportQueryValidation (tenant, 93 gün max, clinic scope)
      → PaymentsReportSearchResolution (opsiyonel search → pattern + client/pet ID kümeleri)
      → PaymentsFilteredCountSpec
      → if total > MaxExportRows (50_000) → Payments.ReportExportTooManyRows
      → PaymentsFilteredOrderedForReportSpec (sayfasız, tüm eşleşen satırlar)
      → PaymentsReportItemMapping (Client, Pet, Clinic Command DB batch lookup)
  → PaymentsCsvWriter.WriteClinicReceiptReportUtf8Bom(items)
     / PaymentsXlsxWriter.WriteClinicReceiptWorkbook(items)
  → byte[] + dosya adı (tahsilat-raporu-{from}-{to}.csv|.xlsx)
```

**Kaynak:** Her zaman **Command DB** (`IReadRepository<Payment>` + hydration lookup'ları). Export handler'lar `PaymentsReportReadEnabled` okumaz; yalnızca `PaymentsSearchLookupEnabled` search lookup kaynağını etkiler.

**JSON report (15G) ile fark:** JSON sayfalı + `totalAmount`; export tam liste, aggregate yok, 50k tavan var.

---

## 4. Yüzey bazlı analiz tablosu

| Surface | Endpoint | Handler | Writer | Current source | Row limit | Memory behavior | Streaming/paging | Search behavior | Scope behavior | PaymentReadModel field parity | Query DB route feasibility | Recommended action | Risk level |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Export CSV** | `GET /reports/payments/export` | `ExportPaymentsReportQueryHandler` | `PaymentsCsvWriter` | Command DB + hydration | 50_000 (count validation) | 50k `Payment` + 50k DTO + tam `StringBuilder` + `byte[]` | **Yok** | Lookup ID + Notes/Currency LIKE | Single clinic, tenant-wide, multi-clinic `IN` | **Tam** (display kolonları) | Guard ile mümkün; bellek riski kalır | **4** → 15I hardening → **2** guard route (15J) | **high** |
| **Export XLSX** | `GET /reports/payments/export-xlsx` | `ExportPaymentsReportXlsxQueryHandler` | `PaymentsXlsxWriter` (ClosedXML) | Aynı pipeline | 50_000 (aynı) | Aynı + **tüm workbook bellekte** + `MemoryStream` | **Yok** | Aynı | Aynı | **Tam** | CSV ile aynı; writer overhead daha yüksek | **4** → CSV ile birlikte 15I→15J | **high** (XLSX > CSV) |
| **Report JSON** (referans) | `GET /reports/payments` | `GetPaymentsReportQueryHandler` | — | Flag ile Query/Command | pageSize ≤200 | Sayfa + SQL SUM (Query path) | Sayfalı JSON | Aynı lookup; search dolu → Command | Aynı guard | **Tam** | **15G uygulandı** | Rollout izle | medium |

---

## 5. Karar kategorileri

### 5.1 Şimdi Query DB'ye taşınabilir

**Yok.** Export handler'larda routing/reader/flag yok; bellek profili dokümante edilmemiş; mevcut reader sayfalı-only.

### 5.2 Guard ile Query DB'ye taşınabilir (15J hedefi)

| Koşul | Export route |
|---|---|
| `PaymentsReportExportReadEnabled = true` (yeni flag, default false) | |
| Search boş/whitespace | Query DB |
| Search dolu | Command DB (15K'ya kadar) |
| Scope single clinic veya tenant-wide | Query DB |
| Multi-clinic (`AccessibleClinicIds` dolu) | Command DB fallback |

### 5.3 Önce export safety hardening gerekir (15I)

- 50k limit **validation** olarak doğru yerde; ancak tam 50k yükleme davranışı bellek açısından test edilmemiş.
- Streaming/paged export yok; XLSX ClosedXML peak memory bilinmiyor.
- Timeout / large file integration testi yok.

### 5.4 Şimdilik Command DB'de kalmalı (mevcut durum)

Export CSV ve XLSX **bugün bu kategoride**. 15G JSON route production'da açılsa bile export etkilenmez.

---

## 6. CSV analizi

| Soru | Cevap |
|---|---|
| Pipeline paylaşımı | XLSX ile **aynı** `PaymentsReportExportPipeline.LoadAsync` |
| Bellek | Tüm satırlar önce `IReadOnlyList<PaymentReportItemDto>`, sonra tek `StringBuilder`, sonra `Encoding.UTF8.GetBytes` |
| Streaming | **Hayır** — satır satır HTTP response'a yazılmıyor; tam `byte[]` döner |
| Writer guard | **Yok** — limit pipeline'da count ile |
| Query DB erken route? | Teknik olarak 15J'de CSV ve XLSX birlikte; CSV bellek profili daha düşük ama **ayrı faz gerekmez** |
| Öneri | 15I'de CSV bellek/ boyut profili; 15J'de guard route |

---

## 7. XLSX analizi

| Soru | Cevap |
|---|---|
| Kütüphane | ClosedXML (`XLWorkbook`) |
| Bellek | Tüm satırlar + biçimli hücreler + `AdjustToContents` + `AutoFilter` → workbook bellekte; `SaveAs(MemoryStream)` → `ToArray()` |
| Streaming | **Hayır** |
| CSV'ye göre risk | **Daha yüksek** — object model overhead, formatting, column width hesabı |
| Test kapsamı | `PaymentsXlsxWriterTests` yalnızca boş workbook; handler testleri 1 satır |

**Sonuç:** XLSX, export bellek riskinin **dominant** bileşeni. Query DB route hydration'ı azaltır ama ClosedXML yükünü ortadan kaldırmaz.

---

## 8. Search parity analizi

### 8.1 Export search (Command DB — mevcut)

1. `ListQueryTextSearch.Normalize` + `BuildContainsLikePattern`
2. `PaymentsListSearchResolution.ResolveSearchIdsAsync` (`PaymentsSearchLookupEnabled`):
   - **Flag false:** `ClientsByTenantTextSearchSpec` — FullName, Email, Phone, PhoneNormalized
   - **Flag false:** `PetsByTenantTextFieldsSearchSpec` — Name, Breed, Species.Name, BreedRef.Name
   - **Flag true:** Query DB lookup reader'ları (aynı alan kümesi)
3. Payment filtresi OR:
   - `Notes LIKE pattern`
   - `Currency LIKE pattern`
   - `ClientId IN searchClientIds`
   - `PetId IN searchPetIds`

### 8.2 PaymentReadModel search parity

| Arama türü | Export (Command) | PaymentReadModel direct | Lookup + PaymentReadModel filtre |
|---|---|---|---|
| Müşteri adı | Lookup → ClientId | ClientNameNormalized | **Evet** |
| Email / telefon | Lookup → ClientId | **Alan yok** | Lookup → ClientId **Evet** |
| Pet adı | Lookup → PetId | PetNameNormalized | **Evet** |
| Irk / tür | Lookup → PetId | **Alan yok** | Lookup → PetId **Evet** |
| Notes | Payment.Notes LIKE | Notes / NotesNormalized | **Evet** |
| Currency | Payment.Currency LIKE | Currency | **Evet** |

**Karar:** Display alanları tam parity. Search parity için **lookup ID stratejisi korunmalı**; list Query reader'ın direct normalized search'ü kopyalanmamalı. Search dolu export **Command DB'de kalması kabul edilebilir** (14E/15G pattern).

---

## 9. Scope parity analizi

| Scope | Export davranışı (Command) | Query DB represent? | Öneri |
|---|---|---|---|
| Single clinic | `ClinicId = X` | Evet | 15J Query route aday |
| Tenant-wide Admin/Owner | TenantId only | Evet (clinic filtresi yok) | 15J Query route aday |
| Multi-clinic ClinicAdmin | `ClinicId IN (...)` | Pratikte temsil edilmez | **Command fallback** (15G/15E ile tutarlı) |
| Scope resolve hata | Validation failure | Query'ye gidilmez | Mevcut davranış korunur |

Multi-clinic Query DB'de temsil edilmeyecekse **Command fallback yeterli** — mevcut `PaymentsFilteredOrderedForReportSpec` zaten `accessibleClinicIds.Contains` kullanır.

---

## 10. Memory/performance analizi

| Adım | Command DB (mevcut) | Query DB route (15J) | İyileşme |
|---|---|---|---|
| Count | Düşük | Düşük (SQL COUNT) | Minimal |
| Row load | 50k `Payment` entity | 50k `PaymentReadModel` | Entity join yükü yok |
| Hydration | Client/Pet/Clinic batch lookup | **Yok** (denormalize) | **Orta** |
| DTO list | 50k `PaymentReportItemDto` | 50k DTO | Aynı |
| CSV writer | Tam string + bytes | Aynı | **Yok** |
| XLSX writer | ClosedXML workbook | Aynı | **Yok** |

| Soru | Cevap |
|---|---|
| Query DB Command yükünü azaltır mı? | **Evet** — guard'lı path'lerde Payment Command DB okuması + hydration kalkar |
| Query DB bellek riskini azaltır mı? | **Kısmen** — hydration gider; 50k + writer yükü **kalır** |
| Writer streaming olmadan route değerli mi? | **Evet** — DB/read yükü ve projection okuma yolu için; bellek borcu ayrı |
| Ayrı read-model/index gerekir mi? | **Hayır** — `PaymentReadModels` yeterli; tenant-wide perf için opsiyonel `(TenantId, PaidAtUtc, PaymentId)` index |

**Peak memory tahmini (50k satır, kabaca):**

- DTO list: onlarca MB
- CSV: ek StringBuilder (benzer büyüklük)
- XLSX: ClosedXML genelde **DTO'nun 2–5× üzeri** (biçim, internal structures)

---

## 11. 50k limit analizi

| Soru | Cevap |
|---|---|
| Sabit | `ReportsSharedLimits.MaxExportRows = 50_000` → `PaymentsReportConstants.MaxExportRows` |
| Nerede uygulanır | `PaymentsReportExportPipeline.LoadAsync` — `CountAsync` sonrası, `ListAsync` **öncesi** |
| Tür | **Validation** (iş kuralı; `Payments.ReportExportTooManyRows`) |
| Query Take mi? | **Hayır** — spec sayfasız; limit count ile |
| Writer guard mı? | **Hayır** |
| Yeterli mi? | **Güvenlik tavanı olarak evet**; bellek sorununu çözmez — tam 50k hâlâ yüklenir |
| Test | Unit test: count > 50k → hata + `ListAsync` çağrılmaz; tam 50k boundary testi **yok** |

---

## 12. Query DB route kararı

### 12.1 JSON reader yeniden kullanımı

Mevcut `IPaymentsReportReadModelReader.GetReportAsync`:

- Zorunlu `Page` / `PageSize` (sayfalı JSON)
- Search desteklemez
- Export için **yetersiz** (tam liste ≤50k)

**Seçenekler (15J implementasyon notu):**

| Seçenek | Artı | Eksi |
|---|---|---|
| A) Reader'a `GetExportItemsAsync` ekle | Ortak filtre/mapping; tek abstraction | Interface genişler |
| B) Ayrı `IPaymentsReportExportReadModelReader` | JSON/export ayrımı net | Filtre kodu tekrarı riski |
| C) Pipeline'da sayfalı batch okuma (Query) + writer'a stream | Bellek iyileşir | 15I/15J kapsamını büyütür; writer değişir |

**Öneri:** **A** — `IPaymentsReportReadModelReader` genişletmesi veya aynı reader sınıfında export metodu; filtreler `PaymentsReportReadModelReader.ApplyFilters` ile paylaşılır. Tam streaming **15I+ sonrası opsiyonel** (C).

### 12.2 Routing kuralları (15J — 15G ile hizalı)

```text
PaymentsReportExportReadEnabled = false → Command DB (mevcut)
true + search boş + representable scope → Query DB
true + search dolu → Command DB
true + multi-clinic → Command DB
```

Query path'te Command DB fallback **yapılmaz** (15G ile aynı).

---

## 13. Ayrı flag kararı

| Flag | Kapsam | Mevcut |
|---|---|---|
| `PaymentsReportReadEnabled` | JSON report | 15G — default false |
| `PaymentsSearchLookupEnabled` | Search lookup kaynağı (Command spec vs Query lookup) | Export **okur** |
| `PaymentsReportExportReadEnabled` (**önerilen**) | Export CSV/XLSX Query route | **Yok** |

**Karar:** **Evet, ayrı flag mantıklı.**

- JSON ve export bağımsız rollout (export bellek riski daha yüksek).
- JSON flag açıkken export Command DB'de kalabilir.
- Rollback granülerliği.
- Startup log / appsettings explicit false (15G pattern).

Export handler'lar bugün yalnızca `PaymentsSearchLookupEnabled` inject eder; `PaymentsReportReadEnabled` **okumaz**.

---

## 14. Özel değerlendirme (A–H)

| Kod | Soru | Cevap |
|---|---|---|
| **A** | CSV için Query DB route daha erken? | **Hayır** — aynı pipeline; CSV bellek profili daha iyi ama **birlikte taşınmalı** |
| **B** | XLSX bellek riski daha yüksek? | **Evet** — ClosedXML dominant factor |
| **C** | `PaymentsReportExportReadEnabled` mantıklı mı? | **Evet** — JSON flag'inden bağımsız |
| **D** | Search boş=Query, dolu=Command export için uygun mu? | **Evet** — 15G/14E ile tutarlı |
| **E** | 50k limit yeterli mi? | Tavan olarak **evet**; bellek için **yeterli değil** (tam 50k yüklenir) |
| **F** | Streaming olmadan Query route değerli mi? | **Evet** — DB yükü + hydration; bellek ayrı faz |
| **G** | Ayrı read-model/index? | **Hayır**; opsiyonel tenant-wide index |
| **H** | JSON reader export için kullanılmalı mı? | **Doğrudan hayır** — export metodu/batch okuma gerekir; filtre/mapping paylaşılmalı |

---

## 15. Test eksiği analizi

| Alan | Mevcut | Eksik |
|---|---|---|
| Validation hataları | Unit (tenant, date, range, too many rows) | — |
| CSV/XLSX format | Unit (küçük veri seti) | — |
| Search lookup flag | Unit + integration smoke | — |
| MaxExportRows boundary | count > 50k red | **Tam 50k** kabul testi yok |
| Bellek / 50k load | **Yok** | Peak memory, OOM senaryosu |
| Streaming | **Yok** | — |
| Timeout / large file | **Yok** | Integration/load |
| Query DB export route | **Yok** (henüz yok) | 15J routing testleri |
| XLSX writer | Boş sheet only | Çok satır, geniş Not kolonu |

---

## 16. Soru-cevap checklist (20 soru)

| # | Soru | Cevap |
|---|---|---|
| 1 | Export CSV ve XLSX aynı pipeline? | **Evet** — `PaymentsReportExportPipeline.LoadAsync` |
| 2 | Export tüm satırları belleğe alıyor mu? | **Evet** — ≤50k |
| 3 | 50k limit nerede? | Pipeline, count sonrası |
| 4 | Limit türü? | **Validation** (count > MaxExportRows) |
| 5 | Paging/streaming? | **Hayır** |
| 6 | CSV streaming mi? | **Hayır** — önce tam item listesi + StringBuilder |
| 7 | XLSX tüm workbook bellekte mi? | **Evet** — ClosedXML + MemoryStream |
| 8 | Search dolu export hangi alanlar? | Client ad/email/phone; pet ad/ırk/tür; payment Notes, Currency |
| 9 | PaymentReadModel search parity? | Lookup stratejisi ile **evet**; direct email/phone/breed **hayır** |
| 10 | Query DB Command yükünü azaltır mı? | **Evet** (guard'lı path) |
| 11 | Query DB bellek riskini azaltır mı? | **Kısmen** — hydration gider, 50k+writer kalır |
| 12 | JSON reader yeniden kullanılabilir mi? | **Doğrudan hayır** — export metodu/batch gerekir |
| 13 | Ayrı export flag gerekli mi? | **Evet** — `PaymentsReportExportReadEnabled` |
| 14 | CSV/XLSX birlikte mi taşınmalı? | **Evet** — ortak pipeline |
| 15 | Önce hardening mi route mu? | **Önce hardening (15I), sonra route (15J)** |
| 16 | Tenant-wide / multi-clinic export? | Tenant-wide: tüm tenant; multi-clinic: `IN (...)` |
| 17 | Multi-clinic Command fallback yeterli mi? | **Evet** |
| 18 | Search dolu Command DB'de kalabilir mi? | **Evet** — kabul edilebilir |
| 19 | Test eksiği? | **Evet** — memory, 50k boundary, timeout, large file |
| 20 | 15H sonrası production faz? | **15I → 15J → 15K** (aşağıda) |

---

## 17. Önerilen production fazları

15F'deki numaralandırma güncellendi: **15H = bu audit**; uygulama fazları **15I+**.

| Sıra | Faz | İçerik | Risk |
|---|---|---|---|
| **1** | **CQRS-15I** — Payment export safety hardening | Export limit davranışını netleştir/dokümante et; tam 50k boundary unit test; bellek profili notları (CSV vs XLSX); opsiyonel load/integration smoke; **production davranış değiştirmeden** guard/test iyileştirmesi; streaming/paged export **tasarım kararı** (uygulama opsiyonel) | medium |
| **2** | **CQRS-15J** — Payment export Query DB route | `PaymentsReportExportReadEnabled` (default false); search boş + representable scope → Query DB; search dolu / multi-clinic → Command DB; reader export metodu; CSV+XLSX birlikte; routing unit + integration test | **high** |
| **3** | **CQRS-15K** — Payment search parity gap | Search dolu Query export/report route; email/phone/breed/species lookup + PaymentReadModel filtre; parity integration testleri | medium |
| 4 | CQRS-15L (opsiyonel) | Paged/streaming export (CSV öncelikli); XLSX batch/chunk stratejisi | high |
| 5 | CQRS-15M (mevcut 15J planı) | Payment GetById Query route kararı | low–medium |

**15F sıra doğrulaması:** JSON önce (15G ✓) → export audit (15H ✓) → **hardening önce, route sonra** onaylandı. Search parity export route'tan **sonra** (15K) güvenli.

---

## 18. CSV / XLSX net kararları

| Yüzey | Mevcut | 15I | 15J |
|---|---|---|---|
| **CSV** | Command DB, kategori **4** | Limit/bellek test + dokümantasyon | Kategori **2** — guard ile Query DB |
| **XLSX** | Command DB, kategori **4** | XLSX bellek profili öncelikli | CSV ile birlikte guard route; bellek izleme şart |

---

## 19. Riskler

| Risk | Etki | Azaltma |
|---|---|---|
| Export 50k bellek (özellikle XLSX) | OOM, GC, timeout | 15I profil/test; 15L streaming opsiyonel |
| Query DB route bellek beklentisi | "Route = güvenli" yanlış algı | Bu doküman; 15I rollout notları |
| Search parity | Query guard dışı farklı sonuç | Search dolu → Command (15J); 15K tam parity |
| Projection lag | Export'ta gecikmeli satır | Flag rollout; parity/health; export ayrı flag |
| Multi-clinic | Query temsil edilemez | Command fallback |
| Tenant-wide Query perf | ClinicId filtresi yok | Opsiyonel index; staging load |
| Reader interface genişlemesi | JSON/export coupling | Paylaşılan ApplyFilters; ayrı metodlar |
| Test boşluğu | Prod'da sürpriz | 15I test matrisi |

---

## 20. Kapsam dışı bırakılanlar (bu faz)

- Production kod / handler / reader / flag değişikliği
- Export pipeline / writer değişikliği
- Schema / migration / index
- Test ekleme / çalıştırma
- Commit
- Frontend

---

## 21. Net karar özeti

| Konu | Karar |
|---|---|
| Mevcut export kaynağı | Command DB (değişmedi) |
| CSV/XLSX pipeline | Ortak |
| Bellek | Tüm satırlar bellekte; streaming yok |
| 50k limit | Count validation; yeterli tavan, bellek çözümü değil |
| Query DB route | 15J; guard'lı; hydration azaltır, bellek tam çözmez |
| Ayrı flag | `PaymentsReportExportReadEnabled` — **evet** |
| Search guard | Boş=Query, dolu=Command — **evet** |
| Scope guard | Single+tenant-wide=Query; multi-clinic=Command — **evet** |
| Reader | JSON reader genişlet; export unpaged/batch metodu |
| Sıra | **15I hardening → 15J route → 15K search parity** |
