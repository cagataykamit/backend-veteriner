# CQRS-15K — Payment search parity gap audit

**Tür:** İnceleme + karar dokümanı. **Production kod, schema/migration, projection/backfill, test ve commit değişmedi.**

**Ön durum:**

- **CQRS-14E** — Payment list Query DB route (`PaymentsListReadEnabled`); search boş + tek klinik → Query DB; search dolu → Command DB guard.
- **CQRS-15G** — Payment report JSON Query DB route (`PaymentsReportReadEnabled`); search boş + representable scope → Query DB; search dolu → Command DB guard.
- **CQRS-15J** — Payment export CSV/XLSX Query DB route (`PaymentsReportExportReadEnabled`); aynı guard kuralları.
- **CQRS-12D-7/8/9** — Command path search resolution; `PaymentsSearchLookupEnabled` ile lookup kaynağı Command spec ↔ Query DB Client/Pet lookup reader arasında seçilir (payment satırları hâlâ Command DB'den okunur).

**İlgili dokümanlar:** [`cqrs-14e-payment-list-routing.md`](cqrs-14e-payment-list-routing.md) · [`cqrs-15g-payment-report-read-model.md`](cqrs-15g-payment-report-read-model.md) · [`cqrs-15j-payment-export-read-model-route.md`](cqrs-15j-payment-export-read-model-route.md) · [`cqrs-15f-payment-report-export-read-model-strategy.md`](cqrs-15f-payment-report-export-read-model-strategy.md) · [`cqrs-12d-6-payments-shared-search-lookup-audit.md`](cqrs-12d-6-payments-shared-search-lookup-audit.md)

---

## 1. Özet

Payment list, report JSON ve export CSV/XLSX yüzeylerinde **search dolu istekler bilinçli olarak Command DB'de kalır**. Sebep: Command path **lookup ID stratejisi** (client email/telefon, pet tür/ırk) kullanır; Query DB `PaymentReadModel` yalnızca **denormalize ad/not/currency** alanlarını taşır ve mevcut Query reader'lar search desteklemez (list reader'da hazır ama kullanılmayan direct LIKE kodu parity dışıdır).

**Ana bulgu:** Search parity için `PaymentReadModel`'e email/telefon/tür/ırk kolonları **eklemek zorunlu değildir**. CQRS-15F ile uyumlu en doğru strateji: mevcut **`IClientReadModelLookupReader` + `IPetReadModelLookupReader`** (Query DB) ile client/pet ID çözümü + **`PaymentReadModels` üzerinde ID + Notes/Currency filtre** — Command spec ile birebir semantik.

**Önerilen karar:** Seçenek **B (Query DB lookup + PaymentReadModel filtre)**; denormalize alan zenginleştirme (A) ve ayrı search model (C) reddedilir; kalıcı Command DB (D) geçici kabul; limited search (E) kullanıcı kırılması nedeniyle reddedilir.

**Önerilen faz sırası (audit sonucu — kullanıcı tahmininden farklı):**

1. **CQRS-15L** — Query DB search implementation (reader search + handler guard kaldırma; **schema/migration yok**)
2. **CQRS-15M** — Multi-clinic Query search + search parity testleri + rollout

Schema enrichment fazı (**15L-field** gibi) yalnızca performans ölçümü sonrası opsiyonel kalır.

---

## 2. Current behavior

### 2.1 Ortak search akışı (Command path — tüm yüzeyler)

```text
request.Search
  → ListQueryTextSearch.Normalize (trim, max 200, boş → null)
  → ListQueryTextSearch.BuildContainsLikePattern (LIKE escape + %...%)
  → PaymentsListSearchResolution.ResolveSearchIdsAsync (Strategy B)
       ├─ PaymentsSearchLookupEnabled=false → Command DB specs
       │     ClientsByTenantTextSearchSpec → searchClientIds
       │     PetsByTenantTextFieldsSearchSpec → searchPetIds
       └─ PaymentsSearchLookupEnabled=true → Query DB lookup readers
             IClientReadModelLookupReader.ResolveClientIdsByTextSearchAsync
             IPetReadModelLookupReader.ResolvePetIdsByPetTextFieldsAsync
  → Payment aggregate filtre (Command DB):
       (Notes LIKE pattern) OR (Currency LIKE pattern)
       OR (searchClientIds contains ClientId)
       OR (PetId != null AND searchPetIds contains PetId)
```

Report/export: `PaymentsReportSearchResolution.ResolveSearchAsync` aynı resolution'ı paylaşır; ardından `PaymentsFilteredCountSpec` / `PaymentsFilteredAmountsSpec` / `PaymentsFilteredPagedSpec` / `PaymentsFilteredOrderedForReportSpec`.

### 2.2 Query DB route guard (14E / 15G / 15J)

| Yüzey | Flag | Query path koşulları | Search dolu |
|---|---|---|---|
| Payment list | `PaymentsListReadEnabled` | search boş + **tek klinik** (`SingleClinicId`) | Command DB |
| Report JSON | `PaymentsReportReadEnabled` | search boş + representable scope (tek klinik **veya** tenant-wide) | Command DB |
| Export CSV/XLSX | `PaymentsReportExportReadEnabled` | search boş + representable scope | Command DB |

Query path seçildiğinde search resolution **çalıştırılmaz**; Command DB fallback **yok**.

### 2.3 İki farklı Query search tasarımı (kritik)

| Tasarım | Nerede | Davranış |
|---|---|---|
| **Lookup ID + aggregate filtre** | Command path (mevcut); 15F önerisi | Client/Pet lookup → Payment filtre by ID + Notes/Currency |
| **Direct normalized LIKE** | `PaymentsListReadModelReader.ApplyListSearchFilter` (hazır, handler'dan **çağrılmıyor**) | ClientNameNormalized / PetNameNormalized / NotesNormalized / Currency |

Report/export Query reader'larında search kodu **yok**. List reader'daki direct search, email/telefon/tür/ırk aramasını **desteklemez** ve Command semantiği ile **birebir değildir** (client adında FullName vs normalized farkı riski). **Query route search bu direct path'i kopyalamamalı** (15F §7).

---

## 3. Payment list search parity

### 3.1 Command DB search alanları

**Doğrudan Payment entity:**

| Alan | Spec |
|---|---|
| `Notes` | `PaymentsListFilteredPagedSpec` — `EF.Functions.Like` |
| `Currency` | aynı |

**Lookup ile dolaylı (client):**

| Alan | Kaynak |
|---|---|
| `Client.FullName` | `ClientsByTenantTextSearchSpec` |
| `Client.Email` | aynı |
| `Client.Phone` | aynı |
| `Client.PhoneNormalized` | aynı |

**Lookup ile dolaylı (pet — payment'ta pet opsiyonel):**

| Alan | Kaynak |
|---|---|
| `Pet.Name` | `PetsByTenantTextFieldsSearchSpec` |
| `Pet.Breed` (serbest metin) | aynı |
| `Pet.Species.Name` | aynı |
| `Pet.BreedRef.Name` (katalog ırk) | aynı |

**Aranmayan:** `Payment.Method` (yalnız filtre), `Clinic` adı, tutar, tarih metni.

### 3.2 Query DB list reader (mevcut)

- `PaymentsListReadModelReader`: filtre + sıralama (`PaidAtUtc DESC, PaymentId DESC`); search parametresi handler'dan **null** geçilir.
- Hazır `ApplyListSearchFilter`: yalnız `ClientNameNormalized`, `PetNameNormalized`, `NotesNormalized`, `Currency` — **lookup yok**.

### 3.3 List scope farkı

List **tenant-wide'a izin vermez** (`Payments.ClinicScopeRequired`); Query route yalnız `SingleClinicId`. Report/export tenant-wide Query route destekler.

---

## 4. Payment report search parity

Report JSON (`GetPaymentsReportQueryHandler`) Command path'te list ile **aynı search resolution + aynı aggregate filtre deseni** kullanır (`PaymentsFilteredCountSpec`, `PaymentsFilteredAmountsSpec`, `PaymentsFilteredPagedSpec`).

Query path (`PaymentsReportReadModelReader`): date range + clinic/client/pet/method; **search yok**.

**Finansal risk:** Search eksikliği `TotalCount` ve `TotalAmount`'u birlikte etkiler — export'tan sonra en yüksek doğruluk gereksinimi burada.

---

## 5. Payment export search parity

Export CSV/XLSX (`PaymentsReportExportPipeline`) report ile **aynı search resolution**; satırlar `PaymentsFilteredOrderedForReportSpec` (sayfasız, sıralı).

Query path (`PaymentsReportExportReadModelReader`): report reader ile aynı filtreler; **search yok**; 50k `MaxExportRows` guard korunur.

**Bütünlük riski:** Sayfasız export'ta eksik satır sessiz kalır — search parity en kritik yüzey.

---

## 6. Missing fields matrix

| Search kavramı | Command path | PaymentReadModel (mevcut) | Query reader search (mevcut) | Gap |
|---|---|---|---|---|
| Müşteri adı | Lookup → ClientId | `ClientName`, `ClientNameNormalized` | Direct LIKE (list only, unused) | Lookup stratejisi ile **kapalı**; direct path normalization farkı riski |
| Müşteri email | Lookup → ClientId | **Yok** | **Yok** | **Eksik** (lookup ile çözülür) |
| Müşteri telefon | Lookup → ClientId | **Yok** | **Yok** | **Eksik** (lookup ile çözülür) |
| Pet adı | Lookup → PetId | `PetName`, `PetNameNormalized` | Direct LIKE (list only) | Lookup ile **kapalı** |
| Pet tür (species) | Lookup → PetId | **Yok** | **Yok** | **Eksik** (lookup ile çözülür) |
| Pet ırk (breed/breedRef) | Lookup → PetId | **Yok** | **Yok** | **Eksik** (lookup ile çözülür) |
| Payment notes | Payment.Notes LIKE | `Notes`, `NotesNormalized` | Direct LIKE (list only) | Query route'ta **implement edilmemiş** |
| Currency | Payment.Currency LIKE | `Currency` | Direct LIKE (list only) | Query route'ta **implement edilmemiş** |
| Clinic adı | Aranmıyor | `ClinicName` (display) | Aranmıyor | Gap **yok** (parity gerekmez) |
| Payment method | Filtre only | `Method` | — | Gap **yok** |

**PaymentReadModel mevcut searchable alanlar (direct LIKE anlamında):** `ClientNameNormalized`, `PetNameNormalized`, `NotesNormalized`, `Currency`.

**Eksik searchable alanlar (direct denormalize anlamında):** `ClientEmail`, `ClientPhone`, `PetSpecies`, `PetBreed` (+ normalized karşılıkları). **ClinicNameNormalized gerekmez** — Command path clinic adı aramaz.

---

## 7. Soru-cevap (1–20)

| # | Soru | Cevap |
|---|---|---|
| 1 | Payment list search Command DB hangi alanları kapsıyor? | Notes, Currency + lookup: client FullName/Email/Phone/PhoneNormalized + pet Name/Breed/SpeciesName/BreedRefName |
| 2 | Report/export Command search alanları? | List ile **birebir aynı** resolution + aggregate filtre |
| 3 | Search resolution hangi entity'lere gidiyor? | **Client** (email/phone/name) ve **Pet** (name/breed/species/breedRef). Clinic, payment notes/currency doğrudan Payment'ta; method aranmaz |
| 4 | PaymentReadModel searchable alanları? | Direct: ClientNameNormalized, PetNameNormalized, NotesNormalized, Currency. Display-only: ClinicName |
| 5 | Eksik searchable alanlar? | Client email/phone; pet species/breed (denormalize yok). Notes/Currency var ama Query route search implementasyonu yok |
| 6 | Eksik alanlar denormalize edilmeli mi? | **Hayır (birincil yol).** Lookup ID stratejisi mevcut Client/Pet read-model altyapısını kullanır; stale email/phone/breed riskini Payment projection'a taşımaz |
| 7 | Ayrı PaymentSearchReadModel gerekir mi? | **Hayır** — 15F ile uyumlu; lookup + PaymentReadModel yeterli |
| 8 | Query DB Client/Pet read model join daha doğru mu? | **Evet (lookup aşamasında).** Payment sorgusunda join değil, **önce ID lookup sonra PaymentReadModels filtre** — Command ile aynı desen, cross-join riski düşük |
| 9 | Cross-read-model join riskli mi? | **Payment ↔ Client/Pet join (tek sorgu) riskli:** tutarsız snapshot, karmaşık SQL, tenant-wide performans. **Ayrı lookup + ID IN filtresi güvenli** (12D-6 doğrulaması) |
| 10 | Projection snapshot/backfill nasıl etkilenir? | Lookup stratejisinde **PaymentReadModel schema/snapshot değişmez**. Client/Pet projection freshness search doğruluğu için ön-koşul |
| 11 | Backfill/parity testleri nasıl etkilenir? | Mevcut `PaymentReadModelParityEvaluator` search kapsamaz; **yeni search parity testleri** gerekir (Command vs Query aynı search → aynı ID kümesi). Client/Pet backfill/parity search ön-koşulu |
| 12 | Index ihtiyacı? | Lookup stratejisinde **PaymentReadModel schema index değişikliği zorunlu değil**. Mevcut `(TenantId, ClientId, PaidAtUtc)` client-ID filtre için yeterli. Tenant-wide report için `(TenantId, PaidAtUtc DESC, PaymentId DESC)` (15F, medium). Notes/Currency LIKE index opsiyonel/düşük öncelik |
| 13 | Search dolu istekleri Query DB'ye taşımak değerli mi? | **Evet**, flag'ler açıkken CQRS coverage tamamlanır; Command DB yükü ve report/export hydration azalır. Ön-koşul: Client/Pet Query read-model sağlıklı |
| 14 | Hangi yüzey önce? | **(1) Payment list** — en düşük finansal risk, 12D-7 sırası. **(2) Report JSON**. **(3) Export** — aynı search impl paylaşılabilir; export rollout en son |
| 15 | Search birebir mi olmalı? | **Evet** — finansal yüzeyler ve mevcut API sözleşmesi; lookup ID + Notes/Currency Command semantiği korunmalı |
| 16 | Limited search kabul edilebilir mi? | **Hayır** — email/telefon/tür/ırk araması Command'de çalışıyor; Query'de kısıtlamak sessiz veri kaybı |
| 17 | Multi-clinic + search birlikte? | **Bağımsız guard'lar.** Multi-clinic: Query `ClinicId IN (...)` implementasyonu veya Command fallback (15G/15J mevcut). Search parity multi-clinic'i bloklamamalı; 15M'de birlikte ele alınmalı |
| 18 | Tenant-wide search Query DB'de? | TenantId filtresi + tenant-only lookup (mevcut). Report/export Query route zaten tenant-wide destekler; list tenant-wide **desteklemez** (scope kuralı ayrı) |
| 19 | Search text normalization uyumlu mu? | **Evet** — tüm yüzeyler `ListQueryTextSearch.Normalize` + `BuildContainsLikePattern`. Lookup reader'lara aynı pattern iletilir (12D testleri) |
| 20 | Sonraki production faz? | **15L:** Query reader'lara lookup ID search + handler guard kaldırma. **15M:** multi-clinic Query search, parity/integration testleri, rollout |

---

## 8. Yüzey bazlı çıktı tablosu

| Surface | Endpoint | Current search source | Query DB route (search empty) | Search fields (Command) | PaymentReadModel fields | Missing fields | Parity risk | Recommended approach | Priority | Suggested next phase |
|---|---|---|---|---|---|---|---|---|---|---|
| **Payment list** | `GET /api/v1/payments` | Command DB (+ optional Query lookup when `PaymentsSearchLookupEnabled`) | `PaymentsListReadEnabled` + tek klinik | Notes, Currency, client name/email/phone, pet name/breed/species | ClientName*, PetName*, Notes*, Currency (*normalized) | Email, phone, species, breed (on model); Query route search impl yok | **Yüksek** — direct LIKE vs lookup; email/phone/breed | **B:** Query lookup + PaymentReadModel ID/Notes/Currency filter | **1** | 15L (reader+handler), 15M (parity) |
| **Report JSON** | `GET /api/v1/reports/payments` | Command DB (+ Query lookup flag) | `PaymentsReportReadEnabled` + representable scope | Aynı | Aynı | Aynı + TotalAmount etkilenir | **Kritik** (finansal toplam) | **B:** aynı; SQL SUM korunur | **2** | 15L |
| **Export CSV/XLSX** | `GET /reports/payments/export` (+ xlsx) | Command DB (+ Query lookup flag) | `PaymentsReportExportReadEnabled` + representable scope | Aynı | Aynı | Aynı | **Kritik** (sayfasız bütünlük) | **B:** aynı | **3** | 15L rollout son |

---

## 9. Option analysis

### A) PaymentReadModel'e eksik search alanları ekle

| Artı | Eksi |
|---|---|
| Tek tablo LIKE; join/lookup yok | Schema + migration + projection snapshot + backfill + parity genişlemesi |
| List reader direct search ile hizalanır | Client email/phone/pet breed değişince stale payment satırları |
| | 15F stratejisi ile çelişir; duplication |

**Karar:** Reddedildi (birincil yol). Performans profili zayıfsa ileride opsiyonel değerlendirilir.

### B) Query DB Client/Pet lookup + PaymentReadModel filtre

| Artı | Eksi |
|---|---|
| Command semantiği **birebir** | Client/Pet read-model freshness zorunlu |
| PaymentReadModel sade kalır | Reader'lara lookup dependency (infra/application) |
| Mevcut `PaymentsListSearchResolution` yeniden kullanılabilir | Multi-clinic `IN` filtresi ayrıca gerekir |
| 12D-6/15F ile uyumlu | |

**Karar:** **Önerilen birincil yol.**

### C) Ayrı PaymentSearchReadModel

Ayrı projection/backfill karmaşıklığı; 15F "gerekmez" kararı geçerli.

**Karar:** Reddedildi.

### D) Search dolu istekleri kalıcı Command DB'de bırak

Güvenli; CQRS coverage ve Command DB yükü eksik kalır.

**Karar:** Geçici kabul; kalıcı hedef değil.

### E) Limited Query search (yalnız PaymentReadModel alanları)

Email/telefon/tür/ırk araması kırılır; kullanıcı fark eder.

**Karar:** Reddedildi.

---

## 10. Recommended decision

**Birincil:** Seçenek **B** — Query DB'de `PaymentsListSearchResolution` (veya eşdeğeri) ile client/pet ID lookup + `PaymentReadModels` üzerinde Command spec ile aynı OR filtresi:

```text
(NotesNormalized LIKE pattern OR Notes LIKE pattern)
OR (Currency LIKE pattern)
OR (searchClientIds.Contains(ClientId))
OR (PetId != null AND searchPetIds.Contains(PetId))
```

**Not:** Notes için Command `Payment.Notes` (raw) kullanır; Query'de parity için raw veya normalized seçimi 15L'de tekilleştirilmeli (`Notes` raw LIKE Command ile hizalı).

**List reader'daki mevcut `ApplyListSearchFilter` (direct normalized):** Query route search için **kullanılmamalı**; refactor veya lookup tabanlı metoda birleştirilmeli.

**Handler değişikliği:** Search dolu + representable scope iken Query reader çağrılmalı; search resolution Query path'te de çalışmalı (`PaymentsSearchLookupEnabled` true iken tamamen Query DB).

**Multi-clinic:** 15M'de `ClinicId IN (accessibleClinicIds)` Query reader desteği veya bilinçli Command fallback devam (mevcut 15G/15J ile tutarlı).

---

## 11. Required schema/projection/backfill impact

| Bileşen | Lookup stratejisi (önerilen B) | Denormalize zenginleştirme (A) |
|---|---|---|
| `PaymentReadModel` schema | **Değişmez** | Yeni kolonlar + index |
| `PaymentProjectionSnapshot` | **Değişmez** | Yeni alanlar |
| `PaymentProjectionProcessor` / backfill | **Değişmez** | Snapshot enrichment |
| `PaymentReadModelParityEvaluator` | **Değişmez** | Genişletme gerekir |
| Client/Pet projection | Search doğruluğu için **Healthy/InSync ön-koşul** | Aynı |

---

## 12. Required tests

| Test alanı | Kapsam |
|---|---|
| Reader unit | Search pattern + clientIds + petIds → doğru SQL filtresi; boş lookup → yalnız Notes/Currency |
| Handler routing | Search dolu + flag true → Query reader (Command payment repo **çağrılmaz**) |
| Search parity integration | Email, phone, breed, species, notes, currency — Command vs Query aynı PaymentId kümesi |
| Report parity | `TotalCount`, `TotalAmount`, sayfa items — search senaryoları |
| Export parity | Satır sayısı + sıralama — search senaryoları; 50k guard |
| Lookup freshness | Client/Pet read-model boş → fallback yok, boş sonuç (12D smoke pattern) |
| Multi-clinic | `AccessibleClinicIds` + search — scope doğruluğu |
| Tenant-wide report | Search + clinic filtresi yok — tenant izolasyonu |
| Regression | `PaymentsSearchLookupEnabled` false iken Command path bozulmaz |

Mevcut test referansları: `PaymentsListSearchResolutionTests`, `GetPaymentsListQueryHandlerPaymentsSearchLookupFeatureFlagTests`, `PaymentsReportSearchResolutionTests`, `PaymentListSearchLookupSmokeIntegrationTests`, `PaymentReportSearchLookupSmokeIntegrationTests`, `PaymentExportSearchLookupSmokeIntegrationTests`.

---

## 13. Riskler

| Risk | Etki | Azaltma |
|---|---|---|
| Client/Pet read-model stale | Search sonuçları eksik | Rollout öncesi Client/Pet parity/health; `PaymentsSearchLookupEnabled` true |
| Report TotalAmount yanlış | Finansal | Search parity integration zorunlu |
| Export eksik satır | Muhasebe/uyumluluk | Export parity en son rollout |
| Multi-clinic scope | Over/under filtering | 15M scope testleri veya Command fallback |
| List vs report scope farkı | Tenant-wide list yok | Dokümante; ayrı test |
| Direct vs lookup search karışıklığı | Yanlış implementasyon | 15L'de direct `ApplyListSearchFilter` kaldır/birleştir |
| Query path fallback yok | Boş sonuç | Operasyonel parity/backfill kapısı |

---

## 14. Kapsam dışı

- Production code, schema/migration, projection/backfill/parity implementation
- Feature flag ekleme/değiştirme
- Search behavior değişikliği (mevcut Command semantiği korunur)
- Test ekleme/çalıştırma, commit
- Streaming/paged export (ayrı faz)
- GetById, dashboard recent, client payment summary routing

---

## 15. Sonraki faz önerisi

### CQRS-15L — Query DB payment search implementation

**Kapsam (schema yok):**

1. Ortak search filtre helper (lookup ID + Notes/Currency) — Command spec ile hizalı.
2. `PaymentsListReadModelReader` — lookup tabanlı search; direct normalized path refactor.
3. `PaymentsReportReadModelReader` — search + aggregate (COUNT/SUM) search filtreli.
4. `PaymentsReportExportReadModelReader` — search + sıralı export filtreli.
5. Handler guard güncelleme: search dolu + representable scope → Query path (`GetPaymentsListQueryHandler`, `GetPaymentsReportQueryHandler`, `PaymentsReportExportPipeline`).
6. Reader'lara lookup reader enjeksiyonu (infra DI).

**Ön-koşul:** Client/Pet Query read-model backfill + parity InSync.

### CQRS-15M — Multi-clinic search + parity + rollout

1. Multi-clinic Query search (`ClinicId IN (...)`) veya bilinçli Command fallback kararı netleştirme.
2. Search parity integration test suite (email/phone/breed/species/notes/currency).
3. Staging rollout: list → report → export; flag'ler sırayla.
4. Opsiyonel: tenant-wide report index değerlendirmesi.

### Opsiyonel (performans sonrası) — CQRS-15N?

Denormalize search alan enrichment (Seçenek A) yalnızca lookup performansı yetersiz kalırsa.

---

## 16. İncelenen dosyalar

| Alan | Dosyalar |
|---|---|
| List handler | `GetPaymentsListQueryHandler.cs`, `PaymentsListSearchResolution.cs`, `PaymentsListFilteredPagedSpec.cs` |
| Report handler | `GetPaymentsReportQueryHandler.cs`, `PaymentsReportSearchResolution.cs`, `PaymentsReportQueryValidation.cs` |
| Export | `ExportPaymentsReportQueryHandler.cs`, `ExportPaymentsReportXlsxQueryHandler.cs`, `PaymentsReportExportPipeline.cs` |
| Query readers | `PaymentsListReadModelReader.cs`, `PaymentsReportReadModelReader.cs`, `PaymentsReportExportReadModelReader.cs` |
| Read model | `PaymentReadModel.cs`, `PaymentReadModelConfiguration.cs`, `PaymentProjectionSnapshot.cs`, `PaymentProjectionSnapshotFactory.cs` |
| Lookup | `ClientsByTenantTextSearchSpec.cs`, `PetsByTenantTextFieldsSearchSpec.cs`, `ClientReadModelReader.cs`, `PetReadModelReader.cs` |
| Parity | `PaymentReadModelParityEvaluator.cs` |
| Tests | `PaymentsListSearchResolutionTests`, `*PaymentsSearchLookupFeatureFlagTests`, `Payment*SearchLookupSmokeIntegrationTests` |
| Docs | `cqrs-14e`, `cqrs-15g`, `cqrs-15j`, `cqrs-15f`, `cqrs-12d-6` |

---

## 17. Garanti

Bu faz **yalnızca doküman** ekledi. Production kod, schema, migration, projection, backfill, parity, flag, test ve commit **değişmedi**. Mevcut search dolu → Command DB guard davranışı aynen devam eder.
