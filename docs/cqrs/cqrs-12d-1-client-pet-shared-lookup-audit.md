# CQRS-12D-1 — Client/Pet shared search lookup audit ve geçiş tasarımı

**Tür:** Yalnızca inceleme + tasarım. **Production C# davranışı değişmedi.** Yeni reader/flag/migration
yok. Bu doküman, Client/Pet read-model'lerinin (CQRS-12B / 12C) yalnız kendi liste endpoint'lerinde
değil, **diğer liste/search handler'larındaki ön arama (lookup) adımlarında** da kullanılabilmesi için
mevcut durumu, scope/risk değerlendirmesini ve önerilen 12D alt faz planını çıkarır.

## Özet bulgu

Diğer aggregate listelerindeki Client/Pet aramaları **tek bir paylaşılan ön-arama desenine** dayanır:
Command DB'de önce client/pet metin eşleşmesi yapılır, eşleşen **pet id kümesi** çözülür, sonra asıl
aggregate (randevu, muayene, ödeme, …) bu id kümesine göre filtrelenir. İki varyant var:

- **Strateji A — `ListSearchPetIds.ResolveForAggregateListAsync`** (pet id birleşimi döner)
- **Strateji B — Payments dual-id** (`searchClientIds[]` + `searchPetIds[]` ayrı ayrı)

Kritik gözlem: **lookup adımı her zaman tenant-only'dır.** Klinik scope (`IClinicReadScopeResolver`)
yalnızca asıl aggregate'e uygulanır, client/pet aramasına değil. Bu, lookup'ı Query DB read-model'e
taşımayı **scope açısından düşük riskli** kılar; asıl risk **read-model parity eksikse arama
sonucundan satır düşmesidir** (sessiz veri eksiltme).

---

## 1. İncelenen handler / spec listesi

### İncelenen query handler'lar (18)

| Handler | Endpoint | Client/Pet lookup? |
|---|---|---|
| `GetAppointmentsListQueryHandler` | `GET /appointments` | Evet (A + id-hydration) |
| `GetExaminationsListQueryHandler` | `GET /examinations` | Evet (A + id-hydration) |
| `GetTreatmentsListQueryHandler` | `GET /treatments` | Evet (A + id-hydration) |
| `GetPrescriptionsListQueryHandler` | `GET /prescriptions` | Evet (A + id-hydration) |
| `GetVaccinationsListQueryHandler` | `GET /vaccinations` | Evet (A + id-hydration) |
| `GetHospitalizationsListQueryHandler` | `GET /hospitalizations` | Evet (A + id-hydration) |
| `GetLabResultsListQueryHandler` | `GET /lab-results` | Evet (A + id-hydration) |
| `GetPaymentsListQueryHandler` | `GET /payments` | Evet (B + id-hydration) |
| `GetPaymentsReportQueryHandler` | `GET /reports/payments` | Evet (B + mapping) |
| `ExportPaymentsReportQueryHandler` | `GET /reports/payments/export` | Evet (B + mapping) |
| `ExportPaymentsReportXlsxQueryHandler` | `GET /reports/payments/export-xlsx` | Evet (B + mapping) |
| `GetDashboardSummaryQueryHandler` | `GET /dashboard/summary` | Evet (count + recent, **arama yok**) |
| `GetDashboardFinanceSummaryQueryHandler` | `GET /dashboard/finance-summary` | Evet (yalnız id-hydration) |
| `GetDashboardOperationalAlertsQueryHandler` | `GET /dashboard/operational-alerts` | Hayır |
| `GetDashboardCapabilitiesQueryHandler` | `GET /dashboard/capabilities` | Hayır |
| `GetPaymentByIdQueryHandler` | `GET /payments/{id}` | Evet (by-id) |
| `GetClientPaymentSummaryQueryHandler` | `GET /clients/{id}/payment-summary` | Evet (by-id + id-hydration) |
| `GetClientsList/ GetPetsListQueryHandler` | `GET /clients`, `GET /pets` | **Zaten read-model (12B/12C)** |

### İncelenen paylaşılan yardımcılar

- `Common/ListSearchPetIds.cs` — Strateji A (7 klinik aggregate listesi kullanır)
- `Reports/Payments/PaymentsReportSearchResolution.cs` — Strateji B (rapor + export)
- `Reports/Payments/PaymentsReportItemMapping.cs` — id-hydration (rapor)

### İncelenen Client/Pet spec'leri (15 read-side + 4 command-only)

Client: `ClientsByTenantTextSearchSpec`, `ClientsByTenantPagedSpec`, `ClientsByTenantCountSpec`,
`ClientByIdSpec`, `ClientsByTenantIdsSpec`, `ClientsByTenantIdsNameSpec`.
Pet: `PetsByTenantPagedSpec`, `PetsByTenantCountSpec`, `PetsByTenantForClientIdsSpec`,
`PetsByTenantTextFieldsSearchSpec`, `PetByIdSpec`, `PetsByTenantIdsSpec`,
`PetsByTenantIdsNameClientSpec`, `PetsByTenantIdsNameClientSpeciesSpec`, `PetsByTenantClientIdSpec`.
Command-only (duplicate detection, read-side değil): `ClientByTenantNormalizedFullNameAndEmailSpec`,
`ClientByTenantNormalizedFullNameAndPhoneSpec`, `PetByClientNameAndSpeciesIdSpec`,
`PetByClientNameAndSpeciesIdExcludingIdSpec`.

### Read-model okuma yüzeyi (bugün)

| Reader | Metot | Filtreler | Sıralama |
|---|---|---|---|
| `ClientReadModelReader` | `GetListAsync` (yalnız) | TenantId + arama (FullName/Email/Phone/PhoneNormalized) | FullNameNormalized, ClientId |
| `PetReadModelReader` | `GetListAsync` (yalnız) | TenantId + ClientId? + SpeciesId? + arama (Name/Breed/SpeciesName/BreedRefName/**ClientFullName**) | Name, SpeciesName, PetId |

**Boşluk:** Her iki reader da yalnızca **paged liste** döner. "Aramaya uyan pet id kümesini döndür"
(`Guid[]`), "id listesiyle toplu isim getir", "tek client'ın tüm petleri", "GetById" gibi
**lookup metotları yoktur.**

---

## 2. Client/Pet lookup kullanım tablosu

| Handler | Lookup ne yapıyor (Command DB) | Client lookup | Pet lookup | Scope | Read-model'e taşımak güvenli mi? | Risk | Önerilen faz |
|---|---|---|---|---|---|---|---|
| `GetExaminationsListQueryHandler` | Strateji A: arama → petId kümesi; + sayfa sonrası isim hydration | Var (`ClientsByTenantTextSearchSpec`, `ClientsByTenantIdsNameSpec`) | Var (`PetsByTenantTextFieldsSearchSpec`, `PetsByTenantForClientIdsSpec`, `PetsByTenantIdsNameClientSpec`) | Lookup tenant-only; aggregate clinic-scoped | Evet, lookup tenant-only; sonuç shape değişmez | **medium** | **12D-3 (pilot)** |
| `GetTreatmentsListQueryHandler` | Strateji A | Var | Var | aynı | Evet | medium | 12D-3 |
| `GetVaccinationsListQueryHandler` | Strateji A | Var | Var | aynı | Evet | medium | 12D-3 |
| `GetHospitalizationsListQueryHandler` | Strateji A | Var | Var | aynı | Evet | medium | 12D-3 |
| `GetLabResultsListQueryHandler` | Strateji A | Var | Var | aynı | Evet | medium | 12D-3 |
| `GetPrescriptionsListQueryHandler` | Strateji A | Var | Var | aynı | Evet | medium | 12D-3 |
| `GetAppointmentsListQueryHandler` | Strateji A (yalnız `AppointmentsEnabled=false` yolunda) | Var | Var | aynı | Kısmen — randevu zaten kendi read-model bayrağına sahip; çift bayrak etkileşimi | medium | 12D-3 (dikkatli) |
| `GetPaymentsListQueryHandler` | Strateji B: ayrı clientIds + petIds | Var | Var | clinic-scoped + resolver | Evet ama finansal | **high** | 12D-4 |
| `GetPaymentsReportQueryHandler` | Strateji B + mapping | Var | Var | clinic-scoped | Finansal; rapor bütünlüğü | high | 12D-4 |
| `ExportPaymentsReportQueryHandler` | Strateji B + mapping (sayfasız) | Var | Var | clinic-scoped | Finansal; export bütünlüğü (eksik satır = uyumluluk riski) | high | 12D-4 |
| `ExportPaymentsReportXlsxQueryHandler` | Strateji B + mapping (sayfasız) | Var | Var | clinic-scoped | aynı | high | 12D-4 |
| `GetDashboardFinanceSummaryQueryHandler` | Yalnız id-hydration (arama yok) | Var (`ClientsByTenantIdsSpec`) | Var (`PetsByTenantIdsSpec`) | tenant + opsiyonel clinic | Sadece isim zenginleştirme; toplu by-ids gerekir | medium | 12D-4 (veya 12D-2 batch reader) |
| `GetDashboardSummaryQueryHandler` | count + "recent" (Id-desc); arama yok | Var | Var | tenant; clinic randevu join | **Hayır (kısmen):** PetReadModel'de `CreatedAtUtc` yok → "recent pets" sıralaması read-model'de replike edilemez | medium | **Kapsam dışı / ertelendi** |
| `GetPaymentByIdQueryHandler` | by-id (`ClientByIdSpec`, `PetByIdSpec`) | Var | Var | tenant + clinic gate | Hayır — detay DTO tam entity/navigation ister | low (taşıma adayı değil) | Kapsam dışı |
| `GetClientPaymentSummaryQueryHandler` | by-id + id-hydration | Var | Var | tenant + opsiyonel clinic | Hayır — detay; batch by-ids kısmı 12D-2 ile mümkün | low | Kapsam dışı |
| `GetDashboardOperationalAlertsQueryHandler` | — | Yok | Yok | tenant + opsiyonel clinic | — | — | — |
| `GetDashboardCapabilitiesQueryHandler` | — | Yok | Yok | tenant | — | — | — |

> Not: "id-hydration" = sayfa satırlarının client/pet **isimlerini** id ile toplu getirme
> (`*IdsName*Spec`). Bu, aramadan ayrı bir adımdır ve read-model'de **toplu by-ids** metodu gerektirir
> (bugün yok).

---

## 3. Scope / risk değerlendirmesi

### Karar kriteri uygulaması

- **Tenant-only lookup → düşük scope riski.** Tüm client/pet ön-arama adımları tenant-only'dır; klinik
  filtre asıl aggregate'te kalır. Dolayısıyla lookup'ı Query DB'ye taşımak klinik izolasyonunu
  **bozmaz** (read-model zaten tenant kapsamlıdır, backfill tüm tenant'ı doldurur).
- **Sonuç shape korunur.** Lookup yalnızca `Guid[]` (pet id) veya id kümeleri döndürür; endpoint DTO'su
  ve sıralaması değişmez. Bu yüzden işlevsel risk **yalnızca arama eşleşmelerinin eksiksizliğidir.**
- **Asıl risk — sessiz veri eksiltme:** Read-model boş/stale ise (backfill yapılmadı veya projector
  kapalı), lookup eksik pet id döndürür ve **arama sonucundan satır düşer**. Bu, kendi liste
  endpoint'inden (12B/12C) **daha tehlikelidir**: kullanıcı, örn. randevu/ödeme aramasında eksik sonuç
  aldığını fark etmeyebilir. Bu nedenle **otomatik fallback yok**; parity ön-koşulu zorunlu.
- **Finansal yüzeyler (ödeme liste/rapor/export) yüksek risk:** Export sayfasızdır; eksik satır
  raporlama/uyumluluk sorunu yaratır → ayrı, daha sıkı faz.

### Bayrak kararı — ayrı Lookup flag öner

Mevcut `ClientsEnabled` / `PetsEnabled` **yeniden kullanılMAmalı**, ayrı bir bayrak önerilir
(örn. `QueryReadModels:SharedSearchLookupEnabled`). Gerekçe:

1. **Farklı blast radius:** `PetsEnabled` yalnız `/pets` listesini etkiler; paylaşılan lookup 7+
   endpoint'i aynı anda etkiler. Aynı bayrağa bağlamak, liste geçişini cross-aggregate arama
   davranışıyla istemeden birleştirir.
2. **Çift ön-koşul:** Lookup hem Client hem Pet read-model'inin **aynı anda** parity'de olmasını ister
   (tek bayraktan daha güçlü ön-koşul). Ayrı bayrak, operatörün önce liste endpoint'lerini açıp parity
   doğrulamasını, sonra lookup'ı açmasını sağlar.
3. **Daha yüksek eksiltme riski:** Cross-aggregate aramada sessiz satır düşmesi, kendi listesinden daha
   ciddi → bağımsız rollback düğmesi gerekir.

Finansal yüzeyler için (12D-4) ek olarak ayrı bir bayrak (`QueryReadModels:PaymentsSearchLookupEnabled`)
değerlendirilebilir; export bütünlüğü riski nedeniyle bağımsız rollback istenebilir.

---

## 4. Önerilen 12D alt fazları

| Faz | Kapsam | Davranış değişikliği | Bayrak |
|---|---|---|---|
| **12D-1** (bu doküman) | Audit + tasarım | Yok | — |
| **12D-2** | **Additive infra:** Query DB lookup reader metotları — `ResolvePetIdsByTextSearchAsync(tenantId, pattern) -> Guid[]`, toplu `GetNamesByIdsAsync` (client/pet), gerekiyorsa `GetPetIdsByClientIdsAsync`. Handler'lara **bağlanmaz**; tam unit/integration test. | Yok (kullanılmıyor) | — |
| **12D-3** | **Pilot routing:** Paylaşılan klinik-aggregate metin aramasını (`ListSearchPetIds`) Query DB'ye taşı. Önce **tek pilot** (`GetExaminationsListQueryHandler` — resolver + iyi test kapsamı), sonra diğer klinik listeler (treatment/vaccination/hospitalization/labresult/prescription, dikkatle appointment). Fallback yok; rollback = bayrak kapat. | Bayrak açıkken | `QueryReadModels:SharedSearchLookupEnabled` (default false) |
| **12D-4** | **Finansal:** payments liste + rapor + export (Strateji B) + finance dashboard id-hydration. Export bütünlüğü için ek doğrulama/test. | Bayrak açıkken | Ayrı `QueryReadModels:PaymentsSearchLookupEnabled` (default false) değerlendir |
| **Kapsam dışı / sonraki** | GetById detay endpoint'leri (tam entity/navigation ister), dashboard "recent" (PetReadModel'de `CreatedAtUtc` yok → engelli) | — | — |

### Parity ön-koşulu (12D-3/12D-4 için)

Bayrak açmadan önce **hem Client hem Pet** için: `migrate-query` → `backfill-client-projections` +
`backfill-pet-projections` → parity in-sync → health Healthy. Aksi halde cross-aggregate aramada
sessiz eksik sonuç döner (fallback yok).

---

## 5. İlk uygulanacak güvenli aday

**12D-2 (additive infra)** ilk yapılacak: davranış değişikliği sıfır, yeni Query DB lookup metotları +
testleri eklenir, hiçbir handler'a bağlanmaz. Tamamen geri-uyumlu ve risksiz.

**İlk routing pilotu (12D-3):** `GetExaminationsListQueryHandler`.
Gerekçe:
- Strateji A'yı (en yaygın desen) temsil eder; başarısı 6 handler'a aynen genişler.
- `IClinicReadScopeResolver` kullanır → clinic scope korunumu en net burada doğrulanır.
- Finansal değil → eksik arama sonucu uyumluluk riski taşımaz.
- Mevcut liste/search testleri mevcut (regresyon ağı hazır).
- Lookup tenant-only; sonuç DTO/sıralaması değişmez → düşük işlevsel risk.

---

## 6. Korunması gereken davranışlar

- **Sonuç shape:** Tüm endpoint DTO'ları ve sıralamaları (örn. `ExaminedAtUtc DESC, Id DESC`;
  `PaidAtUtc DESC, Id DESC`) **birebir** korunmalı.
- **Arama alanları:** Pet (Name/Breed/Species/BreedRef) + Client (FullName/Email/Phone) eşleşme kümesi
  korunmalı. Read-model pet araması client adını **denormalize `ClientFullName`** ile tek sorguda yapar
  (Command DB'nin iki-sorgu deseniyle işlevsel eşdeğer).
- **Tenant zorunluluğu** ve **clinic scope** asıl aggregate'te aynen kalmalı (lookup bunlara dokunmaz).
- **Fallback yok:** Query DB boş/eksikken otomatik Command DB'ye düşülmez; rollback = bayrak kapat.
- **Normalizasyon/escape:** `ListQueryTextSearch.Normalize` + `BuildContainsLikePattern` aynı kalmalı.

---

## 7. Bilinen boşluklar (12D-2'de kapatılacak)

| Boşluk | Bugün (Command DB) | Read-model durumu |
|---|---|---|
| Aramaya uyan pet id kümesi | `ListSearchPetIds` (3 spec) | Yok — `ResolvePetIdsByTextSearchAsync` gerekir |
| Toplu isim hydration | `*IdsName*Spec` | Yok — `GetNamesByIdsAsync` gerekir |
| Çoklu client filtresi | `PetsByTenantForClientIdsSpec` (`ClientId IN`) | Reader yalnız tek `ClientId?` |
| Tek client'ın tüm petleri (sayfasız) | `PetsByTenantClientIdSpec` | Yalnız paged liste |
| GetById detay | `ClientByIdSpec` / `PetByIdSpec` (+Include) | Yok — detay read-model gerekir (kapsam dışı) |
| Dashboard "recent" | Id-desc / appointment join | PetReadModel'de `CreatedAtUtc` yok (engelli) |

---

## 8. Garanti

- Bu faz **yalnızca doküman** ekledi; production C# / routing / flag / migration / health / backfill /
  event contract / route-auth-permission-tenant scope **değişmedi**.
- `ClientsEnabled` ve `PetsEnabled` default **false** kaldı.
- Mevcut testler kırılmadı.
- Commit atılmadı.
