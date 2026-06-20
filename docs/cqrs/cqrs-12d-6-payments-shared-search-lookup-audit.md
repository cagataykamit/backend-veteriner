# CQRS-12D-6 — Payments / reports / export shared search lookup audit ve geçiş tasarımı

**Tür:** Yalnızca inceleme + tasarım. **Production C# davranışı değişmedi.** Yeni reader/flag/migration/
routing yok. Bu doküman, ödeme liste + ödeme raporu + ödeme export yüzeylerindeki Client/Pet ön-arama
adımını Query DB read-model'e taşımadan önce risk ve geçiş tasarımını netleştirir.

## Ön durum

- `QueryReadModels:SharedSearchLookupEnabled` flag mevcut (default false).
- Examinations, treatments, vaccinations, hospitalizations, lab-results, prescriptions ve appointments
  (Command DB yolu) için shared search lookup uygulandı (12D-3/4/5).
- Payments / report / export **henüz dokunulmadı**.
- 12D-2 altyapısı `IClientReadModelLookupReader` ve `IPetReadModelLookupReader` ile birlikte hazır.

---

## 1. İncelenen dosyalar

### Production (üretim) kodu

| Dosya | Rol |
|---|---|
| `Payments/Queries/GetList/GetPaymentsListQueryHandler.cs` | Liste handler — search resolution **inline** |
| `Reports/Payments/Queries/GetPaymentReport/GetPaymentsReportQueryHandler.cs` | Rapor handler |
| `Reports/Payments/Queries/ExportPaymentReport/ExportPaymentsReportQueryHandler.cs` | CSV export handler |
| `Reports/Payments/Queries/ExportPaymentReport/ExportPaymentsReportXlsxQueryHandler.cs` | XLSX export handler |
| `Reports/Payments/PaymentsReportSearchResolution.cs` | Rapor + export ortak search resolution (Strateji B) |
| `Reports/Payments/PaymentsReportExportPipeline.cs` | CSV/XLSX ortak doğrulama + satır yükleme |
| `Reports/Payments/PaymentsReportItemMapping.cs` | id-hydration (client/pet/clinic isim) |
| `Reports/Payments/PaymentsReportConstants.cs` | `MaxExportRows`, `MaxPageSize` tavanları |
| `Payments/Specs/PaymentsFilteredCountSpec.cs` | Count — dual-id `cids`/`pids` filtresi |
| `Payments/Specs/PaymentsListFilteredPagedSpec.cs` | Liste sayfa (PaymentListRow projeksiyonu) |
| `Payments/Specs/PaymentsFilteredPagedSpec.cs` | Rapor sayfa (Payment entity) |
| `Payments/Specs/PaymentsFilteredAmountsSpec.cs` | Rapor toplam tutar |
| `Payments/Specs/PaymentsFilteredOrderedForReportSpec.cs` | Export (sayfasız, sıralı) |
| `Common/ListSearchPetIds.cs` | Strateji A (kıyas için — payments kullanmaz) |
| `Common/SharedSearchPetIdsLookup.cs` | 12D-3/4/5 helper (Strateji A — payments için **yeniden kullanılamaz**) |

### Lookup altyapısı (12D-2 — hazır)

| Dosya | İlgili metot |
|---|---|
| `Clients/ReadModels/IClientReadModelLookupReader.cs` | `ResolveClientIdsByTextSearchAsync` |
| `Pets/ReadModels/IPetReadModelLookupReader.cs` | `ResolvePetIdsByPetTextFieldsAsync` (Strateji B) |
| `Infrastructure/Query/Clients/ClientReadModelReader.cs` | parity doğrulandı |
| `Infrastructure/Query/Pets/PetReadModelReader.cs` | parity doğrulandı |

### Doküman / mevcut handler referansları

`docs/cqrs/cqrs-12d-1-client-pet-shared-lookup-audit.md`, `cqrs-12d-3/4/5-*.md`,
`GetExaminationsListQueryHandler` (Strateji A referans uygulaması).

---

## 2. Payments search flow özeti

Ödeme tarafında metin araması **Strateji B (dual-id)** kullanır: pet id ve client id kümeleri **ayrı**
çözülür, çünkü bir ödeme **her zaman bir client'a** bağlıdır ama **pet opsiyoneldir** (`Payment.PetId`
nullable). Bu yüzden client adı eşleşmesi pet id'ye çökertilemez — ayrı `searchClientIds` gerekir.

### Search resolution tam olarak nerede yapılıyor

| Yüzey | Resolution noktası | Client lookup | Pet lookup | Aggregate filtre |
|---|---|---|---|---|
| Liste (`GetPaymentsListQueryHandler`) | Handler içinde **inline** (satır 110-123) | `ClientsByTenantTextSearchSpec` → `searchClientIds` | `PetsByTenantTextFieldsSearchSpec` → `searchPetIds` | `PaymentsFilteredCountSpec` + `PaymentsListFilteredPagedSpec` |
| Rapor (`GetPaymentsReportQueryHandler`) | `PaymentsReportSearchResolution.ResolveSearchAsync` | aynı | aynı | `PaymentsFilteredCountSpec` + `PaymentsFilteredAmountsSpec` + `PaymentsFilteredPagedSpec` |
| Export CSV (`ExportPaymentsReportQueryHandler`) | `PaymentsReportExportPipeline.LoadAsync` → `PaymentsReportSearchResolution` | aynı | aynı | `PaymentsFilteredCountSpec` (tavan) + `PaymentsFilteredOrderedForReportSpec` (sayfasız) |
| Export XLSX (`ExportPaymentsReportXlsxQueryHandler`) | aynı pipeline | aynı | aynı | aynı |

### Aggregate filtre deseni (tüm yüzeyler)

```text
searchPattern != null iken Payment.Where:
    (Notes LIKE pattern) OR (Currency LIKE pattern)
    OR (searchClientIds contains ClientId)
    OR (PetId != null AND searchPetIds contains PetId)
```

### id-hydration (aramadan ayrı adım)

- Liste: `ClientsByTenantIdsNameSpec` + `PetsByTenantIdsNameClientSpec` (sayfa satırları için isim).
- Rapor/export: `PaymentsReportItemMapping` → `ClientsByTenantIdsSpec` + `PetsByTenantIdsSpec` +
  `ClinicsByTenantIdsSpec`.
- **Not:** Clinic isim hydration'ın read-model lookup karşılığı **yok**; bu yüzden id-hydration tüm
  fazlarda Command DB'de kalmalı (12D-3/4/5 deseniyle aynı — yalnız *search resolution* taşınır).

### Command DB ↔ Read-model parity (doğrulandı)

| Command DB spec | Read-model lookup metodu | Alan kümesi |
|---|---|---|
| `ClientsByTenantTextSearchSpec` | `IClientReadModelLookupReader.ResolveClientIdsByTextSearchAsync` | FullName / Email / Phone / PhoneNormalized — **birebir** |
| `PetsByTenantTextFieldsSearchSpec` | `IPetReadModelLookupReader.ResolvePetIdsByPetTextFieldsAsync` | Name / Breed / SpeciesName / BreedRefName — **birebir** |

> Kritik: Payments **Strateji A `ResolvePetIdsByTextSearchAsync`'i KULLANAMAZ**. O metot pet aramasına
> denormalize `ClientFullName` ekler (client+pet birleşik pet id). Payments ise client eşleşmesini ayrı
> `searchClientIds` ile yapar; Strateji A kullanılırsa client adı **hem** pet id'ye **hem** client id'ye
> yansır → çift sayım/yanlış genişleme. Doğru karşılık `ResolvePetIdsByPetTextFieldsAsync`'tir.

---

## 3. Risk tablosu

| Risk | Liste | Rapor | Export (CSV/XLSX) |
|---|---|---|---|
| **Veri eksiltme** (read-model boş/stale → arama satır düşer) | Orta — sayfalı, kullanıcı yeniden arar | Yüksek — `total` + `totalAmount` finansal toplam yanlış olur | **Kritik** — sayfasız tek-atış; eksik satır sessizce dışa aktarılır |
| **Export bütünlüğü** | Yok | Dolaylı (rapor toplamı) | **Kritik** — CSV/XLSX uyumluluk/muhasebe artefaktı; eksik satır tespit edilmesi zor |
| **Tenant/clinic scope** | Düşük — lookup tenant-only; clinic scope aggregate spec + `IClinicReadScopeResolver`'da kalır (değişmez) | Düşük — aynı; `PaymentsReportQueryValidation` scope'u korur | Düşük — aynı pipeline validation |
| **Pagination / count** | Orta — `count` ve sayfa **aynı** id kümesini kullanmalı; tutarsızlık sayfa/toplam uyumsuzluğu yaratır | Yüksek — `count`, `amounts` ve `paged` üç ayrı sorgu **aynı** id kümesiyle çalışmalı; toplam tutar etkilenir | Orta — `MaxExportRows` tavanı `count` üzerinden; eksik id → tavan kontrolü yanıltıcı |

### Ortak gözlem

- Lookup adımı **tenant-only**; clinic izolasyonu aggregate tarafında korunur → **scope riski düşük**
  (12D-1 ile aynı sonuç).
- Asıl risk **sessiz veri eksiltme**; finansal yüzeylerde (rapor toplamı, sayfasız export) etkisi
  klinik listelere göre **daha ağır**.
- **Tek search resolution, çok sorgu:** Bir yüzeyde count/amounts/paged sorguları aynı `searchClientIds`
  + `searchPetIds` ile beslenmeli; resolution bir kez yapılıp paylaşıldığı için bu zaten korunur, ancak
  routing eklenirken bu değişmez korunmalı.

---

## 4. Önerilen flag kararı

**Ayrı `QueryReadModels:PaymentsSearchLookupEnabled` flag önerilir (default false).**
`SharedSearchLookupEnabled` **yeniden kullanılmamalı**.

Gerekçe:

1. **Bağımsız blast radius / rollback:** `SharedSearchLookupEnabled` 7 klinik/randevu yüzeyini etkiler.
   Finansal yüzeyler (rapor toplamı + sayfasız export) farklı bütünlük profiline sahip; bağımsız bir
   "kapat" düğmesi gerekir. Tek bayrağa bağlamak, klinik liste geçişini finansal export davranışıyla
   istemeden birleştirir.
2. **Daha sıkı ön-koşul kapısı:** Finansal lookup açılmadan önce hem Client hem Pet read-model parity'si
   + export bütünlüğü doğrulaması gerekir. Ayrı bayrak, operatörün klinik yüzeyleri açtıktan **sonra**
   finansal yüzeyleri ayrı doğrulayıp açmasını sağlar.
3. **12D-1 audit yönü:** 12D-1 zaten finansal yüzeyler için ayrı `PaymentsSearchLookupEnabled`
   değerlendirilmesini önermişti; bu audit onu doğruluyor.

> Alternatif (reddedildi): Tek `SharedSearchLookupEnabled`. Daha az config yüzeyi sağlar ama finansal
> rollback'i klinik yüzeylere bağlar; export bütünlüğü riski nedeniyle kabul edilmez.

---

## 5. Önerilen alt fazlar

| Faz | Kapsam | Davranış değişikliği | Bayrak |
|---|---|---|---|
| **12D-6** (bu doküman) | Audit + tasarım | Yok | — |
| **12D-7** | **Payment LİSTE routing.** `GetPaymentsListQueryHandler` inline resolution'ı flag ile Query DB'ye (`ResolveClientIdsByTextSearchAsync` + `ResolvePetIdsByPetTextFieldsAsync`) yönlendir. En düşük finansal risk: sayfalı, otoriter export değil. id-hydration Command DB'de kalır. | Bayrak açıkken | `PaymentsSearchLookupEnabled` (default false) |
| **12D-8** | **Payment RAPOR routing.** `PaymentsReportSearchResolution` (rapor handler yolu) aynı flag + reader'lara bağlanır. `total` / `totalAmount` parity testi zorunlu. | Bayrak açıkken | aynı |
| **12D-9** | **Payment EXPORT routing (son).** CSV + XLSX (`PaymentsReportExportPipeline`). Sayfasız bütünlük testi + `MaxExportRows` tavan etkileşimi doğrulaması zorunlu. | Bayrak açıkken | aynı |
| **Kapsam dışı** | Clinic isim hydration (read-model lookup yok), `GetPaymentByIdQueryHandler`, `GetClientPaymentSummaryQueryHandler`, dashboard finance | — | — |

### Ortak helper notu (rapor + export aynı resolution'ı paylaşır)

`PaymentsReportSearchResolution.ResolveSearchAsync` hem rapor hem export tarafından çağrılır. 12D-8 ve
12D-9'u **ayrı** sahnelemek için iki yaklaşım:

- **(a) Tek bayrak, tek helper:** Helper'a reader'lar + `enabled` parametresi eklenince rapor ve export
  **aynı anda** flip olur (bayrak açıkken ikisi de Query DB). Daha basit; ama export bağımsız sahneleme
  yapılamaz.
- **(b) Sahnelenmiş:** Resolution kararı çağrı tarafından parametre olarak geçilir (liste/rapor/export
  başına). Bayrak tek kalır ama prod'da açmadan önce **önce rapor parity, sonra export bütünlüğü**
  doğrulanır.

Öneri: helper'ı reader + flag alacak şekilde imzala (tek `PaymentsSearchLookupEnabled`), ancak prod'da
açmadan önce doğrulama sırasını **liste → rapor → export** olarak zorunlu kıl (kod tek PR'da birleşebilir
ama prod enable export bütünlüğü testi geçmeden yapılmaz).

---

## 6. İlk kod fazı önerisi (12D-7)

**`GetPaymentsListQueryHandler` (payment liste) ilk routing fazı olmalı.**

Gerekçe:
- Finansal yüzeyler içinde **en düşük risk**: sonuç sayfalı, otoriter muhasebe artefaktı değil; eksik
  arama sonucu kullanıcı tarafından yeniden aranabilir.
- Search resolution handler içinde **inline** → ortak `PaymentsReportSearchResolution` helper'ına
  dokunmadan izole routing yapılabilir (rapor/export'u etkilemez).
- Clinic scope `IClinicReadScopeResolver` ile net; korunum doğrulaması burada en temiz.
- Mevcut `GetPaymentsListQueryHandlerTests` regresyon ağı hazır.

Önerilen ilk kod adımı (12D-7'de yapılacak — bu fazda **yapılmadı**):
- `GetPaymentsListQueryHandler`'a `IClientReadModelLookupReader` + `IPetReadModelLookupReader` +
  `IOptions<QueryReadModelsOptions>` enjekte et.
- Inline resolution'ı bir `PaymentsSearchResolution.ResolveAsync(tenantId, pattern, flag, clientReader,
  petReader, clients, pets, ct) -> (clientIds, petIds)` helper'ına çıkar (Strateji B dual-id).
- Flag false → mevcut Command DB davranışı birebir; flag true → iki reader.

---

## 7. Query DB boş/stale davranışı (açık)

- `PaymentsSearchLookupEnabled=true` + Client/Pet read-model boş veya stale ise:
  - Lookup eksik `searchClientIds` / `searchPetIds` döndürür.
  - Arama sonucundan **satır düşer** (sessiz veri eksiltme).
  - Rapor `total` / `totalAmount` **eksik** hesaplanır.
  - Export **eksik satır** üretir (sayfasız; tespiti zor).
- **Otomatik fallback YOK.** Command DB'ye düşülmez.
- **Rollback:** `PaymentsSearchLookupEnabled=false` + restart.
- **Ön-koşul:** Açmadan önce hem Client hem Pet için `migrate-query` → backfill → parity in-sync →
  health Healthy (12D-3/4/5 ile aynı kapı), **ayrıca** export bütünlüğü parity testi.

---

## 8. Gerekli test kapsamı (öneri — bu fazda yazılmadı)

### Liste (12D-7)
- `PaymentsSearchLookupEnabled=false` → Command DB (`ClientsByTenantTextSearchSpec` +
  `PetsByTenantTextFieldsSearchSpec`) çağrılır, reader çağrılmaz.
- `=true` → iki reader çağrılır, Command DB search spec'leri çağrılmaz.
- Search boş/null → ne reader ne Command DB search; lookup atlanır.
- Reader throw → Command DB fallback **yok** (exception yükselir).
- Tenant + escaped pattern reader'a iletilir.
- Clinic scope: aggregate spec'te korunur; lookup tenant-only.
- DTO shape + sıralama (`PaidAtUtc DESC, Id DESC`) değişmez.
- Smoke (integration): flag off + boş Query DB → bulur; flag on + boş → fallback yok; flag on + dolu →
  bulur; tenant isolation.

### Rapor (12D-8)
- Yukarıdakiler + **`total` ve `totalAmount` parity:** Command DB vs read-model aynı sonucu vermeli
  (parity dolu read-model ile).
- count / amounts / paged üç sorgu **aynı** id kümesini kullanır.

### Export (12D-9)
- Yukarıdakiler + **sayfasız bütünlük:** `PaymentsFilteredOrderedForReportSpec` satır kümesi parity.
- `MaxExportRows` tavanı: count eksik id ile yanıltıcı tavan vermez (dolu parity senaryosu).
- CSV ve XLSX byte/row sayısı parity.

---

## 9. Garanti

- Bu faz **yalnızca doküman** ekledi; production C# / routing / flag / migration / health / backfill /
  event contract / route-auth-permission-tenant-clinic scope **değişmedi**.
- Yeni kod yazılmadı; test çalıştırılmadı (audit fazı).
- `SharedSearchLookupEnabled` ve diğer bayraklar default **false** kaldı; production default davranış
  değişmedi.
- Payments / report / export production davranışı değişmedi.
- Commit atılmadı.
