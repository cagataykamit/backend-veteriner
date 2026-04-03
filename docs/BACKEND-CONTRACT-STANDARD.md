# Backend Contract Standardı

## 1) Amaç

Bu doküman, backend-frontend contract uyumunu kalıcı bir ekip standardına dönüştürmek için hazırlanmıştır.

Hedeflenen problemler:
- Endpoint, DTO, enum, validation ve hata sözleşmelerinde zamanla oluşan drift.
- Frontend’in backend davranışını tahmin ederek geliştirme yapmak zorunda kalması.
- OpenAPI çıktısının gerçek davranıştan ayrışması nedeniyle type-generation riskleri.

Bu standardın amacı:
- Backend’i tek doğruluk kaynağı haline getirmek.
- Contract değişikliklerini kontrollü, ölçülebilir ve geriye dönük yönetmek.
- Frontend etkisini sürümleme ve deprecate süreciyle güvenli yönetmek.

---

## 2) Temel İlkeler

1. **Backend contract tek doğruluk kaynağıdır.**  
   Endpoint path, HTTP method, request/response şeması, enum, validation ve error code backend tarafından belirlenir.

2. **Request/response canonical olmalıdır.**  
   Aynı iş için tek alan adı kullanılmalıdır; birden fazla isimle aynı alan taşınmamalıdır.

3. **Generic `BadRequest` yaklaşımı kullanılmamalıdır.**  
   Hatalar mümkün olduğunca `Result/Result<T> + ToActionResult` ile standardize edilmelidir.

4. **Route ID source-of-truth kuralı zorunludur.**  
   `PUT/PATCH/DELETE /{id}` akışlarında route id esas kaynaktır.

5. **Enum contract açık ve tekil olmalıdır.**  
   Enum’ların JSON temsili (numeric/string) net olmalı, OpenAPI’da doğru görünmelidir.

6. **Context alanları context-first yönetilmelidir.**  
   `tenant_id` ve `clinic_id` gibi alanlar öncelikle request context’ten çözülmelidir.

7. **Swagger/OpenAPI gerçek davranışı yansıtmalıdır.**  
   Required/nullability, response tipleri ve hata sözleşmeleri kodla birebir olmalıdır.

8. **Her contract değişikliğinde frontend etki analizi zorunludur.**  
   Etkilenen endpoint, DTO, enum, validation ve smoke test maddeleri dokümante edilmelidir.

---

## 3) Kısa Ekip Kuralları

### 3.1 DTO İsimlendirme
- Body modelleri: `CreateXxxBody`, `UpdateXxxBody`
- Response modelleri: `XxxDetailDto`, `XxxListItemDto`, `XxxCreatedDto`
- Command/Query: API sözleşmesini yansıtan açık ve tek anlamlı alan adları

### 3.2 Request/Response Ayrımı
- API body modeli ile uygulama komutları gerektiğinde ayrıştırılmalıdır.
- Controller mapping açık olmalı; gizli alias dönüşümlerinden kaçınılmalıdır.

### 3.3 Result / ProblemDetails Standardı
- Controller dönüşleri mümkün olduğunca `Result` tabanlı olmalıdır.
- `ProblemDetails` içinde business kodu (`extensions.code`) bulunmalıdır.
- Aynı hata türü modüller arasında aynı envelope yapısıyla dönmelidir.

### 3.4 `*.RouteIdMismatch` Standardı
- Update akışlarında:
  - Route id yoksa istek geçersizdir.
  - Body id boşsa route id command’e enjekte edilir.
  - Body id dolu ve route ile farklıysa `*.RouteIdMismatch` döndürülür.

### 3.5 Enum Standardı
- Proje genelinde enum JSON temsili tek olmalıdır (şu an: numeric).
- OpenAPI enum şeması bu kararı net göstermelidir.
- Enum parse/validation davranışı list/query akışlarında da tutarlı olmalıdır.

### 3.6 Alias Alan Yönetimi
- Yeni alias eklenmez.
- Mevcut alias’lar için deprecate planı zorunludur:
  1) duyur,
  2) ölç (telemetri/log),
  3) kaldır.

### 3.7 `clinicId` / `tenantId` Ownership
- `tenantId`: context-first.
- `clinicId`: context-first.
- Body’de varsa yalnızca uyumsuzluk kontrolü için kullanılır; context ile çelişirse açık hata kodu döndürülür.

### 3.8 Yeni permission kodları ve JWT
- `PermissionCatalog`’a eklenen kodlar uygulama açılışında `PermissionSeeder` + (Admin için) `AdminClaimSeeder` ile DB ve rol bağlarına yansır.
- Access token içindeki `permission` claim’leri **login / refresh / select-clinic** sırasında DB’den okunur; deploy sonrası veya seed sonrası **eski token’da yeni kodlar yoktur** — kullanıcıların **refresh veya yeniden login** ile token alması gerekir (403 Forbidden, policy eksik).
- `IntegrationTests` ortamında `CustomWebApplicationFactory`, production ile aynı Admin permission zincirini uygular (`PermissionSeeder` + `AdminClaimSeeder`); aksi halde test admin’i yalnızca dar Outbox claim’ine sahip kalır.

---

## 4) Modül Bazlı Mevcut Durum Özeti

| Modül | Mevcut durum | Riskli alanlar | Gerekli standardizasyon | Öncelik | Frontend etkisi |
|---|---|---|---|---|---|
| Auth | `Result` + `ToActionResult` + açık DTO (`LoginResultDto`, `AuthActionResultDto`); logout akışı da aynı hatta | Eski istemciler `ProblemDetails.title` metninde değişiklik görebilir (`ResultExtensions`) | Faz 0: auth endpoint’leri tek sözleşmeye alındı; drift için bu doküman §9 | Tamamlandı (Faz 0) | Orta (title gösterimi) |
| Clinics | Genel olarak tutarlı | Create response yalnız `Guid` | Create response DTO standardı | P2 | Düşük |
| Clients | En tutarlı modüllerden | Düşük | Mevcut standardı referans modül olarak koruma | P2 | Düşük |
| Pets | Route/body id standardı güçlü | Create response çıplak `Guid` | Create response standardizasyonu | P2 | Düşük |
| Appointments | Update/lifecycle akışları güçlü | Bazı hata dallarında envelope farklılaşma riski | Error contract tekilleştirme | P1 | Orta |
| Examinations | Kanonik `visitReason`; yazmada opsiyonel legacy `complaint` (Faz 0 / Adım 3; §11) | — | İstemciler `visitReason` kullanmalı | Tamamlandı (Faz 0) | Orta (alias kaldırma takvimi) |
| Vaccinations | Clinic context entegrasyonu var | `clinicId` ownership algısı modüller arası tutarsız | Context-first kuralını açık ve tek hale getirme | P1 | Orta |
| Payments | Create/update/list/detail DTO + `PaymentsContractSchemaFilter` ile OpenAPI hizalı (Faz 0 / Adım 4; §12) | İş kuralı: clinic/müşteri/hayvan tutarlılığı | Context-first klinik uyumu operasyonel | Tamamlandı (Faz 0) | Orta (typegen) |
| Treatments | List/detail/create/update DTO + `TreatmentsContractSchemaFilter`; muayene ile isteğe bağlı ilişki (§13) | Examination clinic/pet tutarlılığı; tarih penceresi | Examinations ile aynı liste/search örüntüsü | Tamamlandı (v1 omurga) | Orta (typegen) |
| Prescriptions | List/detail/create/update DTO + `PrescriptionsContractSchemaFilter`; isteğe bağlı examination + treatment (§14) | İkili referansta examination–treatment tutarlılığı; tarih penceresi | Treatments ile aynı liste/search örüntüsü | Tamamlandı (v1 omurga) | Orta (typegen) |
| Dashboard | Contract açık | Dokümantasyon drift riski | Contract metinleri ve OpenAPI doğruluğunu koruma | P2 | Düşük |
| Species | Update contract tutarlı | Düşük | Tutarlı dokümantasyon ve naming temizliği | P3 | Düşük |
| Breeds | Update contract tutarlı | Düşük | Tutarlı dokümantasyon ve naming temizliği | P3 | Düşük |

**Clients (müşteri detay özeti):** `GET /api/v1/clients/{id}/recent-summary` — `Clients.Read`; tek yanıtta `ClientRecentSummaryDto` (`recentAppointments`, `recentExaminations`). Kayıtlar yalnız route’taki müşterinin **pet’lerine** aittir; sıra en yeni tarih önce; her blok en fazla **10** kayıt (`ClientRecentSummaryConstants`). Aktif klinik bağlamı (`IClinicContext`) varsa randevu ve muayene listeleri bu **kliniğe** indirgenir. Müşteri tenant dışı / yoksa `Clients.NotFound`. OpenAPI: `ClientsContractSchemaFilter`.

---

## 5) Öncelikli Refactor Backlog

### P0 (hemen)
- **Auth:** ~~Controller dönüşlerini tek `Result/ProblemDetails` hattına çekmek.~~ (Faz 0 / Adım 1 tamamlandı; ayrıntı §9.)
- **Examinations:** ~~`complaint` alias’ı için deprecate planı ve kaldırma takvimi.~~ (Faz 0 / Adım 3: `visitReason` canonical + istekte `complaint` kabul; §11.)
- **Payments:** ~~OpenAPI required/nullability sözleşmesini gerçek validation ile birebir eşitlemek.~~ (Faz 0 / Adım 4 — §12.)
- **Cross-module:** `clinicId` ownership standardını tek kurala bağlamak (context-first).

### P1
- Appointments / Vaccinations / Payments / Examinations hata envelope’unu modüller arası tekilleştirmek.
- List/query validator’larında enum ve boş GUID edge-case’lerini netleştirmek.
- Context-body mismatch hata kodlarını modüller arasında isimlendirme standardına bağlamak.

### P2
- Create endpoint dönüşlerinde `Guid` yerine anlamlı response DTO standardı.
- Swagger response örnekleri ve hata kodu dokümantasyonunu güçlendirmek.

### P3
- Düşük riskli naming ve dokümantasyon tutarlılığı iyileştirmeleri (Species/Breeds vb.).

---

## 6) OpenAPI / Type-Generation Readiness

### Hazır (yakın)
- Clients (liste + `recent-summary` şeması: `ClientsContractSchemaFilter`)
- Pets
- Species
- Breeds
- Dashboard
- Payments (Faz 0: §12 — şema/required/nullability)
- Treatments (§13 — şema/required/nullability)
- Prescriptions (§14 — şema/required/nullability)

### Kısmi Hazır
- Clinics
- Appointments
- Vaccinations

### Önce Contract Temizliği Gerekli
- ~~Auth~~ (Faz 0: login/refresh/select-clinic/logout hattı standardize; §9)
- ~~Examinations~~ (Faz 0: `visitReason` / `complaint` §11)
- ~~Payments~~ (Faz 0: §12)

### Neden modül modül geçiş?
- Her modülün sözleşme olgunluğu farklıdır.
- Tek seferde type-generation, düşük olgun modüllerde kırılma riskini büyütür.
- Modül bazlı geçişte her adımda contract doğrulama + smoke test yapılabilir.

---

## 7) Önerilen Çalışma Süreci

### 7.1 Contract değişikliğinde zorunlu yazılacaklar
- Değişen endpoint(ler)
- Request/response alan farkları
- Error code farkları
- Enum etkisi
- Geriye dönük uyumluluk notu (var/yok)

### 7.2 Frontend impact dokümantasyonu
- Etkilenen ekran/modül listesi
- Değişen type alanları
- Mapping veya validasyon değişiklikleri
- Rollout/deprecate tarihi

### 7.3 Smoke test maddeleri
- Başarılı senaryo (happy path)
- Route/body mismatch senaryosu
- Validation hata senaryosu
- ProblemDetails alanları (`status/title/detail/code`) doğrulaması
- Enum parse/doğrulama senaryosu

### 7.4 Release / Deprecate / Remove sırası
1. **Release:** Yeni canonical contract eklenir, eski kullanım izlenir.
2. **Deprecate:** Eski alan/akış için resmi deprecate ilanı.
3. **Remove:** Süre dolunca alias/eski alan kaldırılır.

---

## 8) Kısa Sonuç

Bu doküman, backend contract kararlarının ekip genelinde tek referans metin üzerinden yönetilmesini sağlar.  
Öncelikli uygulanması gereken kararlar:
- Auth hata/response standardizasyonu,
- ~~Examinations alias deprecate planı~~ (Faz 0 / Adım 3 — §11),
- ~~Payments OpenAPI doğruluğu~~ (Faz 0 / Adım 4 — §12),
- Context-first `clinicId/tenantId` ownership kuralının modüller arası tekilleştirilmesi.

Doküman, yeni endpoint geliştirmelerinde ve mevcut endpoint refactorlarında zorunlu checklist olarak kullanılmalıdır.

---

## 9) Auth modülü — kanonik sözleşme (API v1)

**Başarı (200):** Tüm token üreten uçlar aynı gövde şemasını kullanır: `LoginResultDto` → JSON alanları `accessToken`, `refreshToken`, `expiresAt`, `resolvedTenantId`, `tenantMembershipCount` (login’de sayım; refresh/select-clinic’te çoğunlukla `null`).

| Endpoint | Başarı gövdesi | Not |
|----------|----------------|-----|
| `POST .../auth/login` | `LoginResultDto` | FluentValidation / boş JSON → `ValidationProblemDetails` veya controller `Auth.Validation.*` + `Result` |
| `POST .../auth/refresh` | `LoginResultDto` | Eksik `refreshToken` → `Auth.Validation.RefreshTokenRequired` |
| `POST .../auth/select-clinic` | `LoginResultDto` | Geçersiz gövde → `Auth.Validation.SelectClinicRequestInvalid` |
| `POST .../auth/logout` | `AuthActionResultDto` (`success`, isteğe bağlı `message`) | Boş/eksik refresh → idempotent başarı + açıklayıcı `message` |
| `POST .../auth/logout-all` | `AuthActionResultDto` | |

**İş kuralı / yetki hataları (`Result` → `ToActionResult`):** `application/problem+json`; `extensions.code` (ör. `Auth.Unauthorized.*`, `Tenants.NotFound`), `traceId`, `correlationId`, `timestampUtc`, `instance`. `title` HTTP durumuna ve koda göre ayarlanır (doğrulama kodları: «Doğrulama hatası»; 401: «Yetkisiz erişim»; vb.) — ayrıntı: `ResultExtensions.MapFailure`.

**Validation (FluentValidation / model state):** `ValidationProblemDetails`; `extensions.code` = `Validation.FluentValidation` veya `Validation.ModelStateInvalid`; alan hataları `errors` sözlüğünde.

**Throttle (429):** Rate limiter `ProblemDetails`; `extensions.code` = `RateLimit.Exceeded`; `retryAfterSeconds` (varsa).

**Önemli iş kodları (örnek):** `Auth.Unauthorized.InvalidCredentials`, `Auth.Unauthorized.RefreshTokenNotFound`, `Auth.Unauthorized.RefreshTokenExpired`, `Auth.Unauthorized.RefreshTokenReused`, `Auth.ClinicNotFound`, `Auth.UserClinicNotAssigned`, `Tenants.TenantInactive`, `Auth.UserMultipleTenantsForbidden`.

Ayrıntılı alan ve iş kuralları için bkz. `docs/AUTH_TENANT_CONTRACT.md`.

---

## 10) Liste endpoint’leri — `search` (Faz 0 / Adım 2)

**Ortak kurallar**

- Kanonik query parametre adı: **`search`**. `PageRequest` ile bağlanan listelerde ayrıca **`page.search`** kullanılabilir; **üst düzey `search` doluysa** (trim sonrası) o değer kullanılır (`PageRequestQuery.WithMergedSearch`).
- Normalizasyon: `ListQueryTextSearch.Normalize` (trim, max 200 karakter); SQL `LIKE` için `%term%` ve joker kaçışı (`ListQueryTextSearch.BuildContainsLikePattern`).
- **Sort/order:** Bu listelerde `PageRequest.Sort` / `Order` **işlenmez** (controller özetlerinde belirtilir).
- **Kiracı:** `ITenantContext.TenantId` zorunlu; klinik bağlamı olan listelerde `clinicId` istek parametresi ile JWT/header clinic uyumsuzluğunda iş kuralı hatası.

| Modül | GET endpoint | `search` | Metin hangi alanlarda |
|--------|----------------|----------|------------------------|
| Clients | `GET /api/v1/clients` | Evet | `FullName`, `Email`, `Phone`, `PhoneNormalized` |
| Pets | `GET /api/v1/pets` | Evet | Hayvan: `Name`, `Breed`, `Species.Name`, `BreedRef.Name`; müşteri metni: `ClientsByTenantTextSearchSpec` ile eşleşen sahiplerin petleri. **AND** `clientId`, `speciesId` filtreleri. |
| Appointments | `GET /api/v1/appointments` | Evet | Randevu `Notes`; pet id’ler: müşteri metni + hayvan metin alanları (`PetsByTenantTextFieldsSearchSpec` ile hayvan listesi ile aynı küme). **AND** `clinicId`, `petId`, `status`, tarih aralığı. |
| Examinations | `GET /api/v1/examinations` | Evet | `VisitReason`, `Findings`, `Assessment`, `Notes`; pet id’ler: müşteri + hayvan metin (yukarıdaki gibi). **AND** `clinicId`, `petId`, **`appointmentId`** (randevuya bağlı muayene), `dateFromUtc`, `dateToUtc` (hepsi AND). |
| Vaccinations | `GET /api/v1/vaccinations` | Evet | `VaccineName`, `Notes`; pet id’ler: müşteri + hayvan metin. **AND** klinik/pet/durum/tarih filtreleri. |
| Payments | `GET /api/v1/payments` | Evet | `Notes`, `Currency`; eşleşen `ClientId` / `PetId` ön kümesi (müşteri metni + `PetsByTenantTextFieldsSearchSpec`). **AND** klinik, müşteri, hayvan, yöntem, ödeme tarihi. |
| Treatments | `GET /api/v1/treatments` | Evet | `Title`, `Description`, `Notes`; pet id’ler: müşteri + hayvan metin (examinations ile aynı `ListSearchPetIds` örüntüsü). **AND** `clinicId`, `petId`, `dateFromUtc`, `dateToUtc` (liste query). |
| Prescriptions | `GET /api/v1/prescriptions` | Evet | `Title`, `Content`, `Notes`; pet id’ler: müşteri + hayvan metin (`ListSearchPetIds`). **AND** `clinicId`, `petId`, `dateFromUtc`, `dateToUtc`. |

**Performans notu:** `LIKE '%...%'` ve çok kiracılı indeks kullanımı; arama terimi uzunluğu üst sınırlı; pet tarafında ön liste id’leri ile `Contains` birleşimi kullanılır.

---

## 11) Examinations — `visitReason` / `complaint` (Faz 0 / Adım 3)

**Kanonik alan:** `visitReason` (domain, komutlar, yanıt DTO’ları, liste/detay). Türkçe ürün adı: başvuru nedeni.

**Legacy (yalnızca istek gövdesi):** JSON property `complaint` — eski istemciler için kabul edilir. Çözümleme: `ExaminationVisitReasonResolver.Resolve(visitReason, complaint)`:

- `visitReason` doluysa (trim sonrası) **yalnızca bu** kullanılır; `complaint` yok sayılır.
- `visitReason` boş/whitespace ve `complaint` doluysa `complaint` (trim) kullanılır.
- İkisi de boşsa validasyon (`VisitReason` zorunlu, max 2000) devreye girer.

**Yanıt:** `ExaminationDetailDto` / `ExaminationListItemDto` yalnızca **`visitReason`** döner; `complaint` **yok**.

**OpenAPI:** `CreateExaminationBody` / `UpdateExaminationBody` şemasında her iki property görünür; `complaint` açıklamasında DEPRECATED. Kaldırma **hedefi:** en az 12 ay uyarı sonrası veya iki major API sürümü (ör. 2027-Q1 — ekip sprint planına göre netleştirilir); kaldırılmadan önce istemci telemetri/usage kontrolü önerilir.

**Frontend:** Form ve state tek alan: `visitReason`. Eski `complaint` gönderen kodlar kademeli kaldırılabilir; yeni kod yalnızca `visitReason` kullanmalı.

**Liste `GET /api/v1/examinations`:** `PageRequest` (`page`, `pageSize`, `search` / `page.search`), opsiyonel `clinicId`, `petId`, **`appointmentId`** (`Examination.AppointmentId` eşitliği; randevu detayından doğrudan muayene listesi için), `dateFromUtc`, `dateToUtc`. Tüm yapılandırılmış filtreler birbiri ile **AND**; `search` doluysa mevcut metin + pet kümesi kuralları bu filtrelerle **AND** birleşir. `sort`/`order` işlenmez. Boş GUID filtre değerleri 400 (validasyon).

---

## 12) Payments — request/response ve OpenAPI (Faz 0 / Adım 4)

**Create** `POST /api/v1/payments` gövdesi: `CreatePaymentCommand`.

| Alan | Zorunlu | Nullable (OpenAPI) | Not |
|------|---------|---------------------|-----|
| `clinicId` | Evet | Hayır | JWT clinic ile uyumsuzsa `Payments.ClinicContextMismatch` |
| `clientId` | Evet | Hayır | |
| `petId` | Hayır | Evet | Doluysa müşterinin hayvanı olmalı |
| `appointmentId` | Hayır | Evet | İş kuralı tutarlılığı handler’da |
| `examinationId` | Hayır | Evet | İş kuralı tutarlılığı handler’da |
| `amount` | Evet | Hayır | &gt; 0 |
| `currency` | Evet | Hayır | ISO 4217 alpha-3 |
| `method` | Evet | Hayır | Enum: Cash, Card, Transfer |
| `paidAtUtc` | Evet | Hayır | `default` kabul edilmez; pencere `PaymentPaidAtWindow` |
| `notes` | Hayır | Evet | Max 4000 |

**Update** `PUT /api/v1/payments/{id}` gövdesi: `UpdatePaymentBody` (route id esas).

| Alan | Zorunlu | Nullable |
|------|---------|----------|
| `id` (body) | Hayır | Evet | Doluysa route ile aynı olmalı |
| `clinicId`, `clientId`, `amount`, `currency`, `method`, `paidAtUtc` | Evet | Hayır (value types / currency string) |
| `petId`, `appointmentId`, `examinationId`, `notes` | Hayır | Evet |

**Yanıtlar:** `PaymentDetailDto` (detayda `tenantId`, `appointmentId`, `examinationId`, `notes` null olabilir; `petName` string, hayvan yoksa `""`). `PaymentListItemDto` (`petId` null olabilir; `petName` boş string olabilir).

**Hatalar:** FluentValidation → 400 `ValidationProblemDetails`; iş kuralları → `Result` → `ProblemDetails` + `extensions.code` (ör. `Payments.NotFound`, `Clients.NotFound`, `Tenants.TenantInactive`).

**Swagger:** `PaymentsContractSchemaFilter` — `required` dizisi runtime zorunlularla uyumlu; opsiyonel referans alanlarda `nullable: true`; alan açıklamaları ISO/enum/tutarlılık için doldurulur.

---

## 13) Treatments — request/response ve OpenAPI

**Liste** `GET /api/v1/treatments` query: `PageRequest` (`page`, `pageSize`, `search` / `page.search` birleşimi), isteğe bağlı `clinicId`, `petId`, `dateFromUtc`, `dateToUtc`. JWT/header clinic ile `clinicId` uyumsuzsa `Treatments.ClinicContextMismatch`. `sort`/`order` işlenmez.

**Create** `POST /api/v1/treatments` gövdesi: `CreateTreatmentCommand`.

| Alan | Zorunlu | Nullable (OpenAPI) | Not |
|------|---------|---------------------|-----|
| `clinicId` | Evet | Hayır | Context clinic ile uyumsuzsa `Treatments.ClinicContextMismatch` |
| `petId` | Evet | Hayır | Tenant’ta aktif pet; klinik/pet uyumu handler’da |
| `examinationId` | Hayır | Evet | Doluysa tenant’ta muayene; clinic ve pet tedavi ile eşleşmeli (`Treatments.ExaminationClinicMismatch`, `Treatments.ExaminationPetMismatch`) |
| `treatmentDateUtc` | Evet | Hayır | `TreatmentDateUtcWindow` — en fazla 7 gün geçmiş, en fazla 2 yıl ileri (examinations `ExaminedAtUtc` ile aynı) |
| `title` | Evet | Hayır | Max 500 |
| `description` | Evet | Hayır | Max 8000 |
| `notes` | Hayır | Evet | Max 4000 |
| `followUpDateUtc` | Hayır | Evet | Tedavi tarihinden önce olamaz (`Treatments.FollowUpBeforeTreatment`) |

**Update** `PUT /api/v1/treatments/{id}` gövdesi: `UpdateTreatmentBody` (route id esas; body `id` dolu ve farklıysa `Treatments.RouteIdMismatch`). Alanlar create ile aynı zorunluluk/validasyon seti.

**Detay** `GET /api/v1/treatments/{id}` yanıtı: `TreatmentDetailDto` (`tenantId`, `examinationId`, `notes`, `followUpDateUtc`, `updatedAtUtc` null olabilir).

**Liste öğesi:** `TreatmentListItemDto` — `examinationId`, `followUpDateUtc` null olabilir; `petName` / `clientName` boş string olabilir.

**Başarı kodları:** Create → `201 Created` gövde `Guid` (yeni id); Update → `204 NoContent`.

**Hatalar:** FluentValidation → 400 `ValidationProblemDetails`; iş kuralları → `Result` → `ProblemDetails` + `extensions.code` (ör. `Treatments.NotFound`, `Pets.NotFound`, `Examinations.NotFound`, `Tenants.TenantInactive`).

**Swagger:** `TreatmentsContractSchemaFilter` — create command, update body, detail/list DTO required/nullability ile hizalı.

---

## 14) Prescriptions — request/response ve OpenAPI

**Liste** `GET /api/v1/prescriptions` query: `PageRequest` (`page`, `pageSize`, `search` / `page.search` birleşimi), isteğe bağlı `clinicId`, `petId`, `dateFromUtc`, `dateToUtc`. JWT/header clinic ile `clinicId` uyumsuzsa `Prescriptions.ClinicContextMismatch`. `sort`/`order` işlenmez.

**Create** `POST /api/v1/prescriptions` gövdesi: `CreatePrescriptionCommand`.

| Alan | Zorunlu | Nullable (OpenAPI) | Not |
|------|---------|---------------------|-----|
| `clinicId` | Evet | Hayır | Context clinic ile uyumsuzsa `Prescriptions.ClinicContextMismatch` |
| `petId` | Evet | Hayır | Tenant’ta pet; clinic/pet uyumu handler’da |
| `examinationId` | Hayır | Evet | Doluysa tenant’ta muayene; clinic ve pet reçete ile eşleşmeli (`Prescriptions.ExaminationClinicMismatch`, `Prescriptions.ExaminationPetMismatch`) |
| `treatmentId` | Hayır | Evet | Doluysa tenant’ta tedavi; clinic ve pet eşleşmeli (`Prescriptions.TreatmentClinicMismatch`, `Prescriptions.TreatmentPetMismatch`); yoksa `Treatments.NotFound` |
| `prescribedAtUtc` | Evet | Hayır | `PrescribedAtUtcWindow` — en fazla 7 gün geçmiş, en fazla 2 yıl ileri (treatment/examination ile aynı pencere) |
| `title` | Evet | Hayır | Max 500 |
| `content` | Evet | Hayır | Max 8000 (tek metin alanı; ilaç satırları yok) |
| `notes` | Hayır | Evet | Max 4000 |
| `followUpDateUtc` | Hayır | Evet | Reçete tarihinden önce olamaz (`Prescriptions.FollowUpBeforePrescription`) |

**Çift referans:** `examinationId` ve `treatmentId` birlikte doluysa tedavinin `ExaminationId` değeri, reçetenin `examinationId` ile aynı olmalı; aksi halde `Prescriptions.ExaminationTreatmentMismatch`.

**Update** `PUT /api/v1/prescriptions/{id}` gövdesi: `UpdatePrescriptionBody` (route id esas; body `id` dolu ve farklıysa `Prescriptions.RouteIdMismatch`). Alanlar create ile aynı zorunluluk/validasyon seti.

**Detay** `GET /api/v1/prescriptions/{id}` yanıtı: `PrescriptionDetailDto` (`tenantId`, `examinationId`, `treatmentId`, `notes`, `followUpDateUtc`, `updatedAtUtc` null olabilir).

**Liste öğesi:** `PrescriptionListItemDto` — `examinationId`, `treatmentId`, `followUpDateUtc` null olabilir; `petName` / `clientName` boş string olabilir.

**Başarı kodları:** Create → `201 Created` gövde `Guid` (yeni id); Update → `204 NoContent`.

**Hatalar:** FluentValidation → 400 `ValidationProblemDetails`; iş kuralları → `Result` → `ProblemDetails` + `extensions.code` (ör. `Prescriptions.NotFound`, `Pets.NotFound`, `Examinations.NotFound`, `Treatments.NotFound`, `Tenants.TenantInactive`).

**Swagger:** `PrescriptionsContractSchemaFilter` — create command, update body, detail/list DTO required/nullability ile hizalı.
