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
| Clients | CRUD + liste (§18.5); `recent-summary` + `payment-summary` (§19) | Düşük | CRUD contract Sprint 2 ile netleştirildi | P2 | Düşük |
| Pets | Route/body id standardı güçlü; pet detay için `history-summary` (§17) | Create response çıplak `Guid` | Create response standardizasyonu | P2 | Düşük |
| Appointments | Update/lifecycle akışları güçlü | Bazı hata dallarında envelope farklılaşma riski | Error contract tekilleştirme | P1 | Orta |
| Examinations | Kanonik `visitReason`; yazmada opsiyonel legacy `complaint` (Faz 0 / Adım 3; §11); muayene detay `related-summary` (§18) | — | İstemciler `visitReason` kullanmalı | Tamamlandı (Faz 0) | Orta (alias kaldırma takvimi) |
| Vaccinations | Clinic context entegrasyonu var | `clinicId` ownership algısı modüller arası tutarsız | Context-first kuralını açık ve tek hale getirme | P1 | Orta |
| Payments | Create/update/list/detail DTO + `PaymentsContractSchemaFilter` ile OpenAPI hizalı (Faz 0 / Adım 4; §12) | İş kuralı: clinic/müşteri/hayvan tutarlılığı | Context-first klinik uyumu operasyonel | Tamamlandı (Faz 0) | Orta (typegen) |
| Treatments | List/detail/create/update DTO + `TreatmentsContractSchemaFilter`; muayene ile isteğe bağlı ilişki (§13) | Examination clinic/pet tutarlılığı; tarih penceresi | Examinations ile aynı liste/search örüntüsü | Tamamlandı (v1 omurga) | Orta (typegen) |
| Prescriptions | List/detail/create/update DTO + `PrescriptionsContractSchemaFilter`; isteğe bağlı examination + treatment (§14) | İkili referansta examination–treatment tutarlılığı; tarih penceresi | Treatments ile aynı liste/search örüntüsü | Tamamlandı (v1 omurga) | Orta (typegen) |
| Lab Results | List/detail/create/update DTO + `LabResultsContractSchemaFilter`; isteğe bağlı examination (§15); tek kayıt (satır analiz yok) | Examination clinic/pet tutarlılığı; `resultDateUtc` penceresi | Prescriptions/treatments ile aynı liste/search örüntüsü | Tamamlandı (v1 omurga) | Orta (typegen) |
| Hospitalizations | List/detail/create/update + discharge; `HospitalizationsContractSchemaFilter` (§16); isteğe bağlı examination; aktif yatış tekilliği | Aynı pet+klinikte çift aktif yatış; taburcu sonrası update yok; tarih/plan kuralları | LabResults ile aynı liste/search; `activeOnly` filtresi | Tamamlandı (v1 omurga) | Orta (typegen) |
| Dashboard | `summary` + `finance-summary` (§19) | Dokümantasyon drift riski | Contract metinleri ve OpenAPI doğruluğunu koruma | P2 | Düşük |
| Tenants | `subscription-summary` (§20); `POST …/invites` (§22); kiracı başına `TenantSubscriptions` + `TenantInvites` | `Tenants.InviteCreate`; plan `maxUsers` + koltuk sayımı | Davet/limit drift; token URL encoding | P1 | Orta (join ekranı + admin davet) |
| Species | Update contract tutarlı | Düşük | Tutarlı dokümantasyon ve naming temizliği | P3 | Düşük |
| Breeds | Update contract tutarlı | Düşük | Tutarlı dokümantasyon ve naming temizliği | P3 | Düşük |

**Clients (müşteri detay özeti):** `GET /api/v1/clients/{id}/recent-summary` — `Clients.Read`; tek yanıtta `ClientRecentSummaryDto` (`recentAppointments`, `recentExaminations`). Kayıtlar yalnız route’taki müşterinin **pet’lerine** aittir; sıra en yeni tarih önce; her blok en fazla **10** kayıt (`ClientRecentSummaryConstants`). Aktif klinik bağlamı (`IClinicContext`) varsa randevu ve muayene listeleri bu **kliniğe** indirgenir. Müşteri tenant dışı / yoksa `Clients.NotFound`. OpenAPI: `ClientsContractSchemaFilter`.

**Clients (müşteri ödeme özeti — Finance+ v1):** `GET /api/v1/clients/{id}/payment-summary` — `Clients.Read`; `ClientPaymentSummaryDto` (`totalPaymentsCount`, `totalPaidAmount`, `currencyTotals`, `lastPaymentAtUtc`, `recentPayments`). Yalnız route’taki **müşterinin** ödemeleri; `recentPayments` en fazla **10** (`ClientPaymentSummaryConstants`); sıra `paidAtUtc` en yeni önce. `totalPaidAmount` tek para birimi olduğunda o birimin toplamı; aksi halde **0** — çoklu birim için `currencyTotals` esas. Aktif klinik bağlamı varsa ödemeler bu kliniğe indirgenir. Müşteri yoksa `Clients.NotFound`. Ayrıntı §19.

**Pets (hayvan detay geçmiş özeti):** `GET /api/v1/pets/{id}/history-summary` — `Pets.Read`; tek yanıtta `PetHistorySummaryDto` (`recentAppointments`, `recentExaminations`, `recentTreatments`, `recentPrescriptions`, `recentLabResults`, `recentHospitalizations`, `recentPayments`) + üst düzey `petId`, `petName`, `clientId`, `clientName`. Kayıtlar yalnız route’taki **pet**’e aittir; sıra en yeni tarih önce; her blok en fazla **10** kayıt (`PetHistorySummaryConstants`). Aktif klinik bağlamı (`IClinicContext`) varsa tüm bloklar bu **kliniğe** indirgenir; yoksa tenant içindeki tüm klinikler. Pet tenant’ta yoksa `Pets.NotFound`. OpenAPI: `PetsContractSchemaFilter`. Ayrıntı §17.

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
- Clients (liste + `recent-summary` + `payment-summary`: `ClientsContractSchemaFilter` — §19)
- Species
- Breeds
- Dashboard (`summary` + `finance-summary`: `DashboardContractSchemaFilter` — §19)
- Payments (Faz 0: §12 — şema/required/nullability)
- Treatments (§13 — şema/required/nullability)
- Prescriptions (§14 — şema/required/nullability)
- Lab Results (§15 — şema/required/nullability)
- Hospitalizations (§16 — şema/required/nullability)
- Pets (liste + pet detay `history-summary`: `PetsContractSchemaFilter` — §17)
- Examinations (liste + muayene detay `related-summary`: `ExaminationsContractSchemaFilter` — §18)

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
- **Sort/order:** Çoğu listede `PageRequest.Sort` / `Order` **işlenmez** (controller özetlerinde belirtilir). **İstisna:** `GET /api/v1/appointments` — yalnızca `sort=ScheduledAtUtc` (büyük/küçük harf duyarsız) ve `order=asc|desc`; sort yoksa varsayılan **en yeni üstte** (`scheduledAtUtc` azalan).
- **Kiracı:** `ITenantContext.TenantId` zorunlu; klinik bağlamı olan listelerde `clinicId` istek parametresi ile JWT/header clinic uyumsuzluğunda iş kuralı hatası.

| Modül | GET endpoint | `search` | Metin hangi alanlarda |
|--------|----------------|----------|------------------------|
| Clients | `GET /api/v1/clients` | Evet | `FullName`, `Email`, `Phone`, `PhoneNormalized` |
| Pets | `GET /api/v1/pets` | Evet | Hayvan: `Name`, `Breed`, `Species.Name`, `BreedRef.Name`; müşteri metni: `ClientsByTenantTextSearchSpec` ile eşleşen sahiplerin petleri. **AND** `clientId`, `speciesId` filtreleri. |
| Appointments | `GET /api/v1/appointments` | Evet | Randevu `Notes`; pet id’ler: müşteri metni + hayvan metin alanları (`PetsByTenantTextFieldsSearchSpec` ile hayvan listesi ile aynı küme). **AND** `clinicId`, `petId`, `status`, tarih aralığı. **Sıralama:** `sort`/`order` yalnızca `ScheduledAtUtc` + `asc`/`desc`; sort yoksa en yeni üstte. |
| Examinations | `GET /api/v1/examinations` | Evet | `VisitReason`, `Findings`, `Assessment`, `Notes`; pet id’ler: müşteri + hayvan metin (yukarıdaki gibi). **AND** `clinicId`, `petId`, **`appointmentId`** (randevuya bağlı muayene), `dateFromUtc`, `dateToUtc` (hepsi AND). |
| Vaccinations | `GET /api/v1/vaccinations` | Evet | `VaccineName`, `Notes`; pet id’ler: müşteri + hayvan metin. **AND** klinik/pet/durum/tarih filtreleri. |
| Payments | `GET /api/v1/payments` | Evet | `Notes`, `Currency`; eşleşen `ClientId` / `PetId` ön kümesi (müşteri metni + `PetsByTenantTextFieldsSearchSpec`). **AND** klinik, müşteri, hayvan, yöntem, ödeme tarihi. |
| Treatments | `GET /api/v1/treatments` | Evet | `Title`, `Description`, `Notes`; pet id’ler: müşteri + hayvan metin (examinations ile aynı `ListSearchPetIds` örüntüsü). **AND** `clinicId`, `petId`, `dateFromUtc`, `dateToUtc` (liste query). |
| Prescriptions | `GET /api/v1/prescriptions` | Evet | `Title`, `Content`, `Notes`; pet id’ler: müşteri + hayvan metin (`ListSearchPetIds`). **AND** `clinicId`, `petId`, `dateFromUtc`, `dateToUtc`. |
| Lab Results | `GET /api/v1/lab-results` | Evet | `TestName`, `ResultText`, `Interpretation`, `Notes`; pet id’ler: müşteri + hayvan metin (`ListSearchPetIds`). **AND** `clinicId`, `petId`, `dateFromUtc`, `dateToUtc`. |
| Hospitalizations | `GET /api/v1/hospitalizations` | Evet | `Reason`, `Notes`; pet id’ler: müşteri + hayvan metin (`ListSearchPetIds`). **AND** `clinicId`, `petId`, **`activeOnly`** (`true` = yalnız açık yatış, `false` = yalnız taburcu), `dateFromUtc`, `dateToUtc` (`admittedAtUtc` üzerinden). |

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

---

## 15) Lab Results — request/response ve OpenAPI

**Liste** `GET /api/v1/lab-results` query: `PageRequest` (`page`, `pageSize`, `search` / `page.search` birleşimi), isteğe bağlı `clinicId`, `petId`, `dateFromUtc`, `dateToUtc`. JWT/header clinic ile `clinicId` uyumsuzsa `LabResults.ClinicContextMismatch`. `sort`/`order` işlenmez. Boş `clinicId` / `petId` GUID filtreleri liste validator’ünde reddedilir.

**Create** `POST /api/v1/lab-results` gövdesi: `CreateLabResultCommand`.

| Alan | Zorunlu | Nullable (OpenAPI) | Not |
|------|---------|---------------------|-----|
| `clinicId` | Evet | Hayır | Context clinic ile uyumsuzsa `LabResults.ClinicContextMismatch` |
| `petId` | Evet | Hayır | Tenant’ta pet |
| `examinationId` | Hayır | Evet | Doluysa tenant’ta muayene; clinic ve pet lab sonucu ile eşleşmeli (`LabResults.ExaminationClinicMismatch`, `LabResults.ExaminationPetMismatch`) |
| `resultDateUtc` | Evet | Hayır | `ResultDateUtcWindow` — en fazla 7 gün geçmiş, en fazla 2 yıl ileri (prescriptions/treatments ile aynı) |
| `testName` | Evet | Hayır | Max 500 |
| `resultText` | Evet | Hayır | Max 8000 (tek metin; analiz satırları yok) |
| `interpretation` | Hayır | Evet | Max 4000 |
| `notes` | Hayır | Evet | Max 4000 |

**Update** `PUT /api/v1/lab-results/{id}` gövdesi: `UpdateLabResultBody` (route id esas; body `id` dolu ve farklıysa `LabResults.RouteIdMismatch`). Alanlar create ile aynı zorunluluk/validasyon seti.

**Detay** `GET /api/v1/lab-results/{id}` yanıtı: `LabResultDetailDto` (`tenantId`, `examinationId`, `interpretation`, `notes`, `updatedAtUtc` null olabilir). Aktif klinik bağlamı varsa ve kayıt farklı klinikteyse `LabResults.NotFound` (Prescriptions ile aynı gizleme örüntüsü).

**Liste öğesi:** `LabResultListItemDto` — `examinationId` null olabilir; `petName` / `clientName` boş string olabilir.

**Başarı kodları:** Create → `201 Created` gövde `Guid` (yeni id); Update → `204 NoContent`.

**Hatalar:** FluentValidation → 400 `ValidationProblemDetails`; iş kuralları → `Result` → `ProblemDetails` + `extensions.code` (ör. `LabResults.NotFound`, `LabResults.DateTooFarInPast`, `Pets.NotFound`, `Examinations.NotFound`, `Tenants.TenantInactive`).

**Swagger:** `LabResultsContractSchemaFilter` — create command, update body, detail/list DTO required/nullability ile hizalı.

---

## 16) Hospitalizations — request/response ve OpenAPI

**Liste** `GET /api/v1/hospitalizations` query: `PageRequest` (`page`, `pageSize`, `search` / `page.search` birleşimi), isteğe bağlı `clinicId`, `petId`, **`activeOnly`** (`true` → yalnız `dischargedAtUtc == null`; `false` → yalnız taburcu edilmiş; **omit** → tümü), `dateFromUtc`, `dateToUtc` (**`admittedAtUtc`** alanına göre). JWT/header clinic ile `clinicId` uyumsuzsa `Hospitalizations.ClinicContextMismatch`. `sort`/`order` işlenmez. Boş `clinicId` / `petId` GUID filtreleri liste validator’ünde reddedilir.

**Create** `POST /api/v1/hospitalizations` gövdesi: `CreateHospitalizationCommand`.

| Alan | Zorunlu | Nullable (OpenAPI) | Not |
|------|---------|---------------------|-----|
| `clinicId` | Evet | Hayır | Context clinic ile uyumsuzsa `Hospitalizations.ClinicContextMismatch` |
| `petId` | Evet | Hayır | Aynı tenant’ta pet |
| `examinationId` | Hayır | Evet | Doluysa muayene clinic/pet ile eşleşmeli (`Hospitalizations.ExaminationClinicMismatch`, `Hospitalizations.ExaminationPetMismatch`) |
| `admittedAtUtc` | Evet | Hayır | `AdmittedAtUtcWindow` — en fazla 7 gün geçmiş, en fazla 2 yıl ileri |
| `plannedDischargeAtUtc` | Hayır | Evet | Varsa `admittedAtUtc`’den önce olamaz (`Hospitalizations.PlannedDischargeBeforeAdmission`) |
| `reason` | Evet | Hayır | Max 2000 |
| `notes` | Hayır | Evet | Max 4000 |

**Tek aktif yatış:** Aynı `tenantId` + `clinicId` + `petId` için `dischargedAtUtc` null ikinci kayıt oluşturulamaz (`Hospitalizations.ActiveHospitalizationExists`); DB’de filtreli unique indeks ile de korunur.

**Update** `PUT /api/v1/hospitalizations/{id}` gövdesi: `UpdateHospitalizationBody` (route id esas; body `id` dolu ve farklıysa `Hospitalizations.RouteIdMismatch`). Taburcu edilmiş kayıt güncellenemez (`Hospitalizations.AlreadyDischarged`). Pet/klinik değişiminde başka aktif yatış çakışması yine `Hospitalizations.ActiveHospitalizationExists`.

**Discharge** `POST /api/v1/hospitalizations/{id}/discharge` gövdesi: `DischargeHospitalizationBody`. Yetki: `Hospitalizations.Discharge`. `dischargedAtUtc` zorunlu; `admittedAtUtc`’den önce olamaz (`Hospitalizations.DischargedBeforeAdmission`). Zaten taburcu ise `Hospitalizations.AlreadyDischarged`. **`notes`:** JSON’da property **yok** veya `null` → mevcut notlar değişmez; **dolu** (boş string dahil) → not alanı trim sonrası güncellenir (boş string → `null`).

**Detay** `GET /api/v1/hospitalizations/{id}` yanıtı: `HospitalizationDetailDto` (`examinationId`, `plannedDischargeAtUtc`, `dischargedAtUtc`, `notes`, `updatedAtUtc` null olabilir; `isActive` = `dischargedAtUtc == null`). Aktif klinik bağlamı varsa ve kayıt farklı klinikteyse `Hospitalizations.NotFound`.

**Liste öğesi:** `HospitalizationListItemDto` — `isActive` dahil; opsiyonel tarih alanları null olabilir.

**Başarı kodları:** Create → `201 Created` gövde `Guid`; Update / Discharge → `204 NoContent`.

**Hatalar:** FluentValidation → 400 `ValidationProblemDetails`; iş kuralları → `Result` → `ProblemDetails` + `extensions.code` (ör. `Hospitalizations.NotFound`, `Tenants.TenantInactive`, `Examinations.NotFound`).

**Swagger:** `HospitalizationsContractSchemaFilter` — create, update body, discharge body, detail/list DTO required/nullability ile hizalı.

---

## 17) Pets — `history-summary` (pet detay timeline)

**Endpoint:** `GET /api/v1/pets/{id}/history-summary` — yetki: `Pets.Read` (pet detay ile aynı).

**Amaç:** Pet detay ekranında birden fazla modül listesini ayrı ayrı çağırmak yerine, ilgili pet’in son klinik kayıtlarını tek yanıtta sunmak.

**Yanıt:** `PetHistorySummaryDto` — üst düzey `petId`, `petName`, `clientId`, `clientName` ve şu bloklar: `recentAppointments`, `recentExaminations`, `recentTreatments`, `recentPrescriptions`, `recentLabResults`, `recentHospitalizations`, `recentPayments`. Her blok öğesi ilgili modülün özet alanlarını taşır (klinik adı `clinicName` ile birlikte `clinicId`).

**Sıralama ve limit:** Her blok kendi tarih alanına göre **en yeni önce** (`scheduledAtUtc`, `examinedAtUtc`, `treatmentDateUtc`, `prescribedAtUtc`, `resultDateUtc`, `admittedAtUtc`, `paidAtUtc`); blok başına en fazla **10** kayıt (`PetHistorySummaryConstants.RecentItemsTake`).

**Klinik bağlamı:** `IClinicContext.ClinicId` doluysa tüm bloklar yalnız bu klinikteki kayıtlarla sınırlıdır; boşsa tenant içindeki tüm kliniklerdeki ilgili pet kayıtları dahil edilir (client `recent-summary` ile aynı yaklaşım).

**Hatalar:** Tenant bağlamı yoksa `Tenants.ContextMissing`; pet yok / tenant dışıysa `Pets.NotFound`. FluentValidation (boş route id) → 400.

**Swagger:** `PetsContractSchemaFilter` — `PetHistorySummaryDto` ve alt öğe DTO’ları required/nullability ile hizalı.

---

## 18) Examinations — `related-summary` (muayene detay ilişkili kayıtlar)

**Endpoint:** `GET /api/v1/examinations/{id}/related-summary` — yetki: `Examinations.Read` (muayene detay ile aynı).

**Amaç:** Muayene detay ekranında bu muayeneye bağlı tedavi, reçete, lab, yatış ve ödemeleri ayrı liste endpoint’leri yerine tek yanıtta sunmak.

**Yanıt:** `ExaminationRelatedSummaryDto` — üst düzey `examinationId`, `petId`, `petName`, `clientId`, `clientName` ve şu bloklar: `treatments`, `prescriptions`, `labResults`, `hospitalizations`, `payments`. Her blok öğesi ilgili modülün özet alanlarını taşır (`clinicName` ile birlikte `clinicId`). Tedavi öğelerinde `examinationId` tekrarlanmaz (route’taki muayene zaten bağlamdır).

**Sıralama ve limit:** Her blok kendi tarih alanına göre **en yeni önce** (`treatmentDateUtc`, `prescribedAtUtc`, `resultDateUtc`, `admittedAtUtc`, `paidAtUtc`); blok başına en fazla **10** kayıt (`ExaminationRelatedSummaryConstants.RelatedItemsTake`).

**Klinik bağlamı:** `IClinicContext.ClinicId` doluysa önce muayenenin kendisi bu klinikte olmalıdır (aksi halde `Examinations.NotFound`, `GET /api/v1/examinations/{id}` ile aynı gizleme); ardından her blok yalnız bu klinikteki ve `ExaminationId` bu muayeneye eşit kayıtları içerir. Bağlam yoksa tenant içindeki tüm kliniklerde bu muayeneye bağlı kayıtlar dahildir (pet `history-summary` ile uyumlu yaklaşım).

**Hatalar:** Tenant bağlamı yoksa `Tenants.ContextMissing`; muayene yok / tenant dışı / aktif klinik ile uyumsuzsa `Examinations.NotFound`. FluentValidation (boş route id) → 400.

**Swagger:** `ExaminationsContractSchemaFilter` — `ExaminationRelatedSummaryDto` ve alt öğe DTO’ları required/nullability ile hizalı.

---

## 18.5) Clients — CRUD ve liste

**Kapsam:** Müşteri (hayvan sahibi) oluşturma, güncelleme, detay ve sayfalı liste. Tüm kayıtlar **kiracı (`tenant_id`)** kapsamındadır; müşteri **klinik bazlı değildir** (`ClientsController` + handler’lar `ITenantContext`).

### Endpoint ve yetki

| Method | Path | Policy | Not |
|--------|------|--------|-----|
| `POST` | `/api/v1/clients` | `Clients.Create` | `201 Created` + `ClientCreatedDto`; `Location` → `GET .../clients/{id}` |
| `PUT` | `/api/v1/clients/{id}` | `Clients.Create` | **Ayrı `Clients.Update` permission yok**; oluşturma ile aynı policy (ürün kararı). `204 No Content` |
| `GET` | `/api/v1/clients` | `Clients.Read` | Sayfalı liste `PagedResult<ClientListItemDto>` |
| `GET` | `/api/v1/clients/{id}` | `Clients.Read` | `ClientDetailDto` |

Controller, tenant çözülemezse `TryGetResolvedTenant` ile **işleme girmeden** hata döner (diğer modüllerle aynı).

### Route / body id (`Clients.RouteIdMismatch`)

- `PUT` isteğinde **route `id` esas kaynaktır** (§3.4).
- Body’deki `UpdateClientCommand.Id` **boş (`Guid.Empty`)** ise route id ile doldurulur.
- Body id **dolu** ve route id ile **farklıysa** → `400`, `extensions.code`: **`Clients.RouteIdMismatch`**.

### DTO farkları (yanıt şekli değiştirilmez)

| DTO | Amaç | Alanlar (özet) |
|-----|------|----------------|
| `ClientCreatedDto` | `POST` başarı gövdesi | `Id`, `TenantId`, `FullName`, `Email`, `Phone` — **Address, CreatedAtUtc yok** (tam alan için `GET` detay) |
| `ClientListItemDto` | Liste satırı | `Id`, `TenantId`, `CreatedAtUtc`, `FullName`, `Email`, `Phone` — **Address, UpdatedAtUtc yok** |
| `ClientDetailDto` | `GET` detay | `Id`, `TenantId`, `CreatedAtUtc`, `UpdatedAtUtc`, `FullName`, `Email`, `Phone`, `Address` |

### Liste: sayfalama ve arama

- Query: `page`, `pageSize` (1–200, handler içinde **clamp**), `search` ve/veya `page.search` — üst düzey `search` doluysa **önceliklidir** (`PageRequestQuery.WithMergedSearch`, Payments ile aynı).
- **Sıralama:** `PageRequest.Sort` / `Order` alanları **işlenmez**; liste `FullName` sonra `Id` ile sabit sıralıdır (`ClientsByTenantPagedSpec`).
- **Arama:** Normalize edilmiş terim `LIKE %...%` ile `FullName`, `Email`, `Phone`, `PhoneNormalized` üzerinde (§10 arama tablosu ile uyumlu).

### İş kuralı hataları (`Result` → `ProblemDetails` + `extensions.code`)

| Kod | HTTP | Koşul |
|-----|------|--------|
| `Tenants.ContextMissing` | `400` | JWT / context’te kiracı yok |
| `Tenants.NotFound` | `404` | Kiracı satırı yok |
| `Tenants.TenantInactive` | `403` | Pasif kiracıda oluşturma/güncelleme |
| `Clients.NotFound` | `404` | Bu kiracıda müşteri yok / yanlış id |
| **`Clients.DuplicateClient`** | **`409`** | Aynı kiracıda **(1)** normalize **ad + e-posta** (e-posta dolu) veya **(2)** normalize **ad + telefon** (telefon dolu); ad `Trim` + küçük harf ile karşılaştırılır. **Yalnızca** aynı e-posta, **yalnızca** aynı telefon veya **e-posta+telefon ama farklı ad** tek başına engel değildir (create/update; güncellemede kendi `id` hariç) |
| `Clients.RouteIdMismatch` | `400` | `PUT` route id ≠ body id |

FluentValidation (validator’lar) → `400` `ValidationProblemDetails` (MediatR pipeline).

### Özet endpoint’ler (değişmedi)

`GET .../recent-summary` ve `GET .../payment-summary` için §19 ve mevcut §4/§19 maddeleri geçerlidir.

---

## 19) Finance+ v1 — `payment-summary` & `finance-summary`

### 19.1 Clients — `GET /api/v1/clients/{id}/payment-summary`

**Yetki:** `Clients.Read` (müşteri detay ile aynı).

**Yanıt:** `ClientPaymentSummaryDto` — `clientId`, `clientName`, `totalPaymentsCount`, `totalPaidAmount`, `currencyTotals[]` (`currency`, `totalAmount`), `lastPaymentAtUtc` (yoksa null), `recentPayments[]` (`id`, `paidAtUtc`, `clinicId`, `clinicName`, `petId`, `petName`, `amount`, `currency`, `method`, `notes`).

**Klinik bağlamı:** `IClinicContext.ClinicId` doluysa yalnız bu klinikteki ödemeler; boşsa tenant içindeki tüm kliniklerdeki bu müşterinin ödemeleri.

**Hatalar:** Tenant yok → `Tenants.ContextMissing`; müşteri yok → `Clients.NotFound`. FluentValidation (boş route id) → 400.

**Swagger:** `ClientsContractSchemaFilter` — `ClientPaymentSummaryDto` ve alt DTO’lar.

### 19.2 Dashboard — `GET /api/v1/dashboard/finance-summary`

**Yetki:** `Dashboard.Read` (`GET /dashboard/summary` ile aynı).

**Yanıt:** `DashboardFinanceSummaryDto` — `todayTotalPaid`, `weekTotalPaid`, `monthTotalPaid`, `todayPaymentsCount`, `weekPaymentsCount`, `monthPaymentsCount`, `recentPayments[]` (`id`, `paidAtUtc`, `clientId`, `clientName`, `petId`, `petName`, `amount`, `currency`, `method`).

**Tarih pencereleri:** İstanbul takvimine göre **bugün** (`OperationDayBounds` ile aynı gün kutusu); **hafta** Pazartesi 00:00–Pazartesi 00:00 (bitiş hariç); **ay** ayın 1’i 00:00–sonraki ayın 1’i 00:00 (bitiş hariç). `PaidAtUtc` bu aralıklarda `[start, end)` kuralıyla filtrelenir.

**Toplamlar:** Aynı penceredeki ödemelerin `amount` değerleri toplanır; **kur dönüşümü yok** (farklı para birimleri aynı toplama eklenir — KPI yorumu için `currencyTotals` yok; ileri sürümde ayrılabilir).

**Klinik bağlamı:** Aktif klinik varsa tüm metrikler ve `recentPayments` yalnız bu klinik için.

**Hatalar:** `Tenants.ContextMissing`.

**Swagger:** `DashboardContractSchemaFilter` — `DashboardFinanceSummaryDto`, `DashboardFinanceRecentPaymentDto`.

---

## 20) Tenants — `subscription-summary` (kiracı abonelik özeti, Faz 1)

**Endpoint:** `GET /api/v1/tenants/{tenantId}/subscription-summary`

**Yetki:** `Subscriptions.Read` **veya** `Tenants.Read`. JWT’deki `tenant_id` ile route `tenantId` aynı olmalıdır; **başka kiracının** özetini yalnızca `Tenants.Read` (platform) ile görebilirsiniz. Aksi halde `Tenants.AccessDenied` (`403`).

**Amaç:** Kiracıya bağlı plan kataloğu, abonelik durumu, trial tarihleri ve kalan gün bilgisini tek yanıtta sunmak (ödeme entegrasyonu yok; Faz 1 salt okunur özet).

**Yanıt:** `TenantSubscriptionSummaryDto` — `tenantId`, `tenantName`, `planCode` (string API kodu: `basic` / `pro` / `premium`), `planName`, `status` (`TenantSubscriptionStatus` enum, JSON **numeric**: `Trialing=0`, `Active=1`, `ReadOnly=2`, `Cancelled=3`), `trialStartsAtUtc`, `trialEndsAtUtc` (opsiyonel), `daysRemaining` (yalnız `Trialing` ve `trialEndsAtUtc` varken; aksi halde null), `isReadOnly` (`status == ReadOnly`), `canManageSubscription` (`Tenants.Create` yetkisi varsa true — ileride paket yönetimi için kanca), `availablePlans[]` (`code`, `name`, `description`).

**Veri:** `TenantSubscriptions` tablosu kiracı ile 1:1 (`TenantId` PK). Yeni kiracı oluşturma (`POST /tenants`) sonrası varsayılan olarak Basic plan + Trialing + 14 gün trial (`SubscriptionTrialDefaults.TrialDays`) yazılır. Eski kiracılar için migration ile backfill uygulanır.

**Hatalar:** Kiracı bağlamı yok → `Tenants.ContextMissing`; abonelik satırı yok → `Subscriptions.NotFound`; tenant yok → `Tenants.NotFound`; izin yok → `Auth.PermissionDenied` veya `Tenants.AccessDenied`. FluentValidation (geçersiz route `tenantId`) → `400`.

**Swagger:** Controller `ProducesResponseType(typeof(TenantSubscriptionSummaryDto), 200)`; enum şeması OpenAPI’da `TenantSubscriptionStatus` olarak görünür.

---

## 21) Public — `owner-signup` (Faz 2 public onboarding)

**Endpoint:** `POST /api/v1/public/owner-signup`

**Yetki:** `AllowAnonymous` (public endpoint). Mevcut panel içi onboarding/tenant setup akışının yerine geçmez; ayrı use-case.

**Amaç:** İlk müşterinin tek çağrıda seçtiği paket ile owner hesabı, tenant, ilk klinik ve üyelik bağlarını kurmak.

**Request body:** `planCode`, `tenantName`, `clinicName`, `clinicCity`, `email`, `password`.

**İş akışı (tek command transaction sınırı):**
- `planCode` katalog doğrulaması (`Basic`, `Pro`, `Premium`).
- `email` duplicate kontrolü (`Users.DuplicateEmail`).
- `tenantName` duplicate kontrolü (`Tenants.DuplicateName`).
- owner user oluşturma + `Admin` role.
- tenant + seçilen plan ile `TenantSubscription.StartTrial(...)`.
- ilk clinic oluşturma + `UserTenant` / `UserClinic` üyelikleri.
- `Admin` operation-claim bağlantısı (login/refresh permission claim zinciri ile uyumlu).

**Response:** `PublicOwnerSignupResultDto` — `tenantId`, `clinicId`, `userId`, `planCode`, `trialStartsAtUtc`, `trialEndsAtUtc`, `canLogin`, `nextStep` (`login`).

**Hatalar:** FluentValidation → `400 ValidationProblemDetails`; iş kuralı hataları `Result` + `ProblemDetails` (`extensions.code`) döner. Örnek kodlar: `Subscriptions.PlanCodeInvalid`, `Users.DuplicateEmail`, `Tenants.DuplicateName`, `Clinics.DuplicateName`, `Auth.AdminClaimMissing`.

**Out-of-scope (Faz 2):** ödeme entegrasyonu, invite/join, trial sonrası read-only enforcement.

---

## 22) Tenants — davet / join (Faz 3) + kullanıcı kotası

### 22.1 Kullanıcı limiti (ürün kararı)

- **Kotanın kaynağı:** `SubscriptionPlanCatalog` içindeki `MaxUsers` (Basic **3**, Pro **10**, Premium **50**).
- **Sayım tanımı:** Belirli bir `tenantId` için **`UserTenants` satır sayısı** = o kiracıdaki aktif üyelik sayısı (slot).
- **Bekleyen davetler:** Süresi dolmamış ve `Pending` durumundaki `TenantInvites` kayıtları da slot rezervasyonu sayılır (davet oluştururken `üye + bekleyen davet + 1 ≤ maxUsers` kuralı).
- **İki noktada kontrol:** (1) `POST …/invites` oluşturma, (2) kabul (`accept` / `signup-and-accept`) — araya giren yeni üyeler için kabul anında tekrar `üye sayısı < maxUsers` doğrulanır.
- **Kota aşımı kodu:** `Subscriptions.UserLimitExceeded` → HTTP **403** (`ResultExtensions`).

**Tek-kiracı kullanıcı modeli:** `UserTenants` üzerinde kullanıcı başına tek satır (mevcut indeks). Davet kabulü, kullanıcının zaten **başka bir kiracıda** üyeliği varsa reddedilir (`Invites.UserBelongsToAnotherTenant`).

### 22.2 Abonelik / tenant durumu

Davet **oluşturma ve kabul** için: kiracı **aktif** olmalı; abonelik **Trialing** veya **Active** olmalı. Ancak **trial tarihi geçmişse** (status hâlâ `Trialing` olsa bile) abonelik **effective ReadOnly** sayılır ve davet akışları engellenir.

- **Engelleme kodları (Faz 4):**
  - `Subscriptions.TenantReadOnly` → HTTP **403**
  - `Subscriptions.TenantCancelled` → HTTP **403**

### 22.3 Endpoint’ler

**A) Davet oluştur** — `POST /api/v1/tenants/{tenantId}/invites`

- **Yetki:** `Tenants.InviteCreate`; JWT `tenant_id` route `tenantId` ile aynı olmalı (`TryGetResolvedTenant`).
- **Body:** `email`, `clinicId`, `operationClaimId`, isteğe bağlı `expiresAtUtc` (yoksa **7 gün**).
- **Response:** `CreateTenantInviteResultDto` — `inviteId`, `token` (ham, yalnızca bu yanıtta), `email`, `tenantId`, `clinicId`, `expiresAtUtc`.

**B) Davet doğrula (public)** — `GET /api/v1/public/invites/{token}`

- **Yetki:** `AllowAnonymous`.
- **Response:** `PublicTenantInviteDetailDto` — `inviteToken`, `tenantId`, `tenantName`, `clinicId`, `clinicName`, `email`, `expiresAtUtc`, `isExpired`, `isPending`, `canJoin`, `requiresLogin`, `requiresSignup`.

**C) Mevcut kullanıcı kabul** — `POST /api/v1/public/invites/{token}/accept`

- **Yetki:** `[Authorize]` (access token).
- Oturumdaki kullanıcının e-postası davetteki `email` ile eşleşmeli (`Invites.EmailMismatch`).

**D) Kayıt + kabul** — `POST /api/v1/public/invites/{token}/signup-and-accept`

- **Yetki:** `AllowAnonymous`; **body:** `password`.
- Davet e-postası ile hesap zaten varsa `Invites.RequiresLogin` (login + `accept` kullanılmalı).

### 22.4 `subscription-summary` plan seçenekleri

`SubscriptionPlanOptionDto` alanları: `code`, `name`, `description`, **`maxUsers`** (panelde kota gösterimi için).

### 22.5 Davet — atanabilir operation claim (rol) listesi

**Endpoint:** `GET /api/v1/tenants/{tenantId}/assignable-operation-claims`

**Yetki:** `Tenants.InviteCreate`; JWT `tenant_id` route `tenantId` ile aynı (`TryGetResolvedTenant`).

**Amaç:** Davet oluşturma ekranındaki rol dropdown’unu beslemek. Liste **kullanıcının sahip olduğu claim’lerden değil**, **`OperationClaims` tablosundaki whitelist’li kiracı-üyelik rollerinden** üretilir; dönen `operationClaimId` değerleri doğrudan `POST /api/v1/tenants/{tenantId}/invites` gövdesindeki `operationClaimId` ile aynı olmalıdır.

**Ürün kararı:** Tüm `OperationClaim` kayıtları expose edilmez; uygulama tarafında `InviteAssignableOperationClaimsCatalog.NamesInDisplayOrder` **whitelist** (ör. `Admin`, `ClinicAdmin`, `Veteriner`, `Sekreter`). Teknik/internal roller bu listede yoksa API’de görünmez. Startup’ta `InviteAssignableOperationClaimsSeeder` eksik claim satırlarını idempotent oluşturur (permission matrisi bu seeder’da bağlanmaz; yalnız satır vardır).

**Yanıt:** `AssignableOperationClaimForInviteDto[]` — `operationClaimId`, `operationClaimName` (sıra whitelist görüntü sırasıyla uyumlu).

**Invite create ile ilişki:** `POST …/invites` hem kaydın `OperationClaims` içinde varlığını hem de adının whitelist’te olduğunu doğrular; whitelist dışı id için `Invites.OperationClaimNotAssignable` (`403`).

---

## 23) Subscription / Trial sonrası read-only enforcement (Faz 4)

### 23.1 Ürün kararı (write kapatma)

- **Write açık statüler:** `Trialing`, `Active`
- **Write kapalı statüler:** `ReadOnly`, `Cancelled`
- **Trial bitişi:** `TrialEndsAtUtc` geçmişse, saklanan status `Trialing` olsa bile **effective status = `ReadOnly`** kabul edilir.
- **Read açık kalır:** List/detail/summary/dashboard ve `subscription-summary`; ayrıca auth akışları: **login**, **refresh**, **select-clinic**.

### 23.2 Merkezi enforcement noktası

- **Uygulama katmanı MediatR pipeline** içinde, tenant context çözümlenen tüm `*Command` (mutation) istekleri için merkezi write-guard çalışır.
- **Kapsam:** create/update/delete/discharge gibi tüm mutation command’lar; davet akışları (`POST …/invites`, `accept`, `signup-and-accept`) dahil.
- **İstisnalar:** `Backend.Veteriner.Application.Auth.*` altındaki command’lar (login/refresh/select-clinic vb.) enforcement dışıdır.

### 23.3 Subscription yoksa davranış

- Tenant için subscription kaydı yoksa write işlemi **engellenir** ve `Subscriptions.NotFound` döner (HTTP **404**).
- Public owner signup akışı bu guard’dan etkilenmez (tenant context henüz yoktur; tenant oluşturma sırasında subscription zaten yaratılır).

### 23.4 Hata sözleşmesi

- **Hata kodu:** `Subscriptions.TenantReadOnly` (trial bitmiş / read-only) → HTTP **403**
- **Hata kodu:** `Subscriptions.TenantCancelled` (iptal) → HTTP **403**
- Envelope: `Result` → `ProblemDetails` (`extensions.code` ile).

---

## 24) Subscription checkout / aktivasyon omurgası (Faz 5)

### 24.1 Amaç ve tasarım

- Trial/read-only omurgası üzerine, owner/admin'in bilinçli plan aktivasyonu için provider-agnostic checkout session modeli eklendi.
- **Manual** provider’da otomatik tahsilat yok; ödeme sinyali **panel finalize** ile tamamlanır. **Iyzico** (production default) ve **Stripe** gerçek checkout hazırlığı sağlar; aktivasyon webhook-first modelle tamamlanır.
- Sağlayıcı bağımlılığı tek noktada tutuldu: `BillingProvider` enum (`None`, `Manual`, `Stripe`, `Iyzico`).
- Uygulama / altyapı sınırı: `IBillingCheckoutProvider` (checkout hazırlığı), `IBillingWebhookSignatureVerifier` + `IBillingWebhookPayloadParser` (webhook), `ISubscriptionCheckoutActivationService` (ortak aktivasyon; idempotent).
- Varsayılan checkout sağlayıcısı: `Billing:DefaultCheckoutProvider` — `Manual`, `Stripe`, `Iyzico`, `Auto` veya boş string.
  - **`Manual`:** Her zaman manuel/test akışı; provider önkoşulları aranmaz.
  - **`Iyzico`:** Production akış; ApiKey / SecretKey / BaseUrl / CallbackUrl / tüm katalog planları için `PlanPricesTry` eksikse checkout **başlamaz** (`Billing.IyzicoConfigurationIncomplete`, **503**).
  - **`Stripe`:** Stripe zorunlu; SecretKey / SuccessUrl / CancelUrl / tüm katalog planları için `SubscriptionPriceIds` eksikse checkout **başlamaz** (`Billing.StripeConfigurationIncomplete`, **503**).
  - **`Auto` veya boş:** öncelik **Iyzico → Stripe**; hiçbiri hazır değilse **503** (`Billing.ProviderConfigurationIncomplete`) — sessizce `Manual`e düşülmez.
  - Geçersiz değer: `Billing.InvalidCheckoutProvider` (**400**).
  - `appsettings.Development.json` şablonu: `Manual`; `appsettings.Production.json` şablonu: `Iyzico`.

### 24.2 Checkout session modeli

- Yeni entity: `BillingCheckoutSession`
  - Alanlar: `id`, `tenantId`, `currentPlanCode`, `targetPlanCode`, `status`, `provider`, `externalReference`, `checkoutUrl`, `expiresAtUtc`, `completedAtUtc`, `failedAtUtc`, `createdAtUtc`, `updatedAtUtc`
- Status enum: `Pending`, `RedirectReady`, `Completed`, `Failed`, `Expired`, `Cancelled`
- Açık session tanımı: `Pending`/`RedirectReady` ve `expiresAtUtc` geçmiş değil.
- Açık session politikası:
  - Aynı tenant + aynı hedef plan için açık session varsa **mevcut session döner**.
  - Farklı hedef plan için açık session varsa önce **Cancelled** yapılır, sonra yeni session açılır.

### 24.3 Endpoint’ler

**A) Checkout başlat** — `POST /api/v1/tenants/{tenantId}/subscription-checkout`

- **Yetki:** `Subscriptions.Manage`
- **Body:** `targetPlanCode`
- **Yanıt:** `SubscriptionCheckoutSessionDto`
  - `checkoutSessionId`, `tenantId`, `currentPlanCode`, `targetPlanCode`, `status`, `provider`, `checkoutUrl`, `canContinue`, `expiresAtUtc`
- **Kurallar:**
  - `targetPlanCode` katalogda olmalı (`Subscriptions.PlanCodeInvalid`)
  - Aynı plan zaten aktifse reddedilir (`Subscriptions.SamePlanAlreadyActive`, `409`)
  - ReadOnly tenant checkout başlatabilir
  - Cancelled tenant checkout başlatamaz (`Subscriptions.TenantCancelled`, `403`)
- **Stripe provider (`DefaultCheckoutProvider` = `Stripe` veya `Auto` ve Stripe önkoşulları tam):**
  - `PrepareCheckout` Stripe’da **subscription** Checkout Session açar; dönen `checkoutUrl` DTO’da döner, `externalReference` = Stripe `cs_...` id’si `BillingCheckoutSession` üzerine yazılır.
  - Stripe oturum metadata (ve `SubscriptionData.Metadata`): `billing_checkout_session_id`, `tenant_id`, `target_plan_code`, `current_plan_code` — webhook çözümlemesi ile uyumlu.
  - Yapılandırma: `Billing:Stripe:SecretKey`, `SuccessUrl` (isteğe bağlı `{CHECKOUT_SESSION_ID}`), `CancelUrl`, `SubscriptionPriceIds` (plan API kodu → recurring `price_...`). Para birimi/tutar **Stripe Price** kaydındadır.
  - Eksik secret/URL/price veya Stripe API hatası: handler iç session silinir; kodlar `Billing.StripeSecretMissing` / `Billing.StripeCheckoutUrlsMissing` / `Billing.StripePriceNotConfigured` (**503**), `Billing.StripeApiError` (**502**).
- **Iyzico provider (`DefaultCheckoutProvider` = `Iyzico` veya `Auto` ve Iyzico önkoşulları tam):**
  - `PrepareCheckout` Iyzico Checkout Form initialize çağrısı yapar; `paymentPageUrl` DTO `checkoutUrl` alanına döner.
  - `CallbackUrl` bir **backend POST endpoint** olmalıdır (SPA route değil): `POST /api/v1/billing/iyzico/callback`.
  - `conversationId` ve `basketId` olarak dahili `billing_checkout_session_id` (GUID) kullanılır; webhook çözümlemesi `paymentConversationId` üzerinden yapılır.
  - `externalReference` olarak Iyzico `token` (fallback `conversationId`) yazılır.
  - `buyerEmail` kaynağı: checkout başlatan authenticated kullanıcı (`IClientContext.UserId` -> `User.Email`). Boş/format dışı email Iyzico'ya gitmeden fail-fast (`Billing.IyzicoBuyerEmailMissing` / `Billing.IyzicoBuyerEmailInvalid`, **400**).
  - Yapılandırma: `Billing:Iyzico:ApiKey`, `SecretKey`, `MerchantId`, `BaseUrl`, `CallbackUrl`, `ReturnSuccessUrl`, `ReturnFailureUrl`, `PlanPricesTry`, `WebhookSecret`.
  - Eksik yapılandırma/fiyat veya provider API hatası: handler iç session silinir; kodlar `Billing.IyzicoConfigurationIncomplete` / `Billing.IyzicoPlanPriceNotConfigured` (**503**), `Billing.IyzicoApiError` (**502**).

### 24.3.1 Iyzico callback bridge

- Endpoint: `POST /api/v1/billing/iyzico/callback` (`application/x-www-form-urlencoded`, anonim).
- Amaç: Iyzico hosted checkout sonrası gelen POST'u backend'de karşılamak, `token` ile `CheckoutForm.Retrieve` yapmak ve kullanıcıyı frontend subscription sayfasına yönlendirmek.
- Redirect:
  - success: `Billing:Iyzico:ReturnSuccessUrl?checkout=success&provider=iyzico&checkoutSessionId=...`
  - fail/cancel: `Billing:Iyzico:ReturnFailureUrl?checkout=cancel&provider=iyzico&reason=...`
- Güvenlik:
  - callback payload'daki token frontend'e ham haliyle taşınmaz.
  - retrieve sonucu başarılı ve `PaymentStatus=SUCCESS` ise ortak aktivasyon servisi çağrılır (idempotent); webhook ile yarış durumunda yan etki üretmez.

**B) Checkout durumu** — `GET /api/v1/tenants/{tenantId}/subscription-checkout/{checkoutSessionId}`

- **Yetki:** `Subscriptions.Manage`
- Session bulunduğunda `SubscriptionCheckoutSessionDto` döner.
- Açık ama süresi geçmiş session okunurken `Expired` işaretlenir.

**C) Checkout finalize / activate** — `POST /api/v1/tenants/{tenantId}/subscription-checkout/{checkoutSessionId}/finalize`

- **Yetki:** `Subscriptions.Manage`
- **Body (opsiyonel):** `externalReference`
- **Etki:**
  - Checkout session `Completed`
  - `TenantSubscription.ActivatePaidPlan(targetPlanCode, utcNow)` çağrılır:
    - `planCode = targetPlanCode`
    - `status = Active`
    - `activatedAtUtc` set edilir
- Kapanmış/expired/cancelled session finalize edilemez (`Subscriptions.CheckoutSessionNotOpen`, `409`).

### 24.4 Read-only guard ilişkisi

- Faz 4 merkezi write guard korunur.
- Billing checkout komutları (`StartSubscriptionCheckout`, `FinalizeSubscriptionCheckout`) ve **`ProcessBillingWebhook`** kontrollü şekilde guard muafiyet marker'ı ile çalışır (`IIgnoreTenantWriteSubscriptionGuard`).
- Bu sayede read-only tenant panel finalize ile aktivasyon akışına girebilir; webhook ise JWT’siz çalışır ancak imza doğrulaması zorunludur.

### 24.5 Provider webhook’ları ve idempotency

- **Stripe** — `POST /api/v1/webhooks/billing/stripe`
  - **Auth:** Yok (`AllowAnonymous`); güvenlik `Stripe-Signature` + `Billing:Stripe:WebhookSecret` ile `Stripe.EventUtility.ConstructEvent` doğrulamasından gelir.
  - **Gövde:** Ham JSON (imza için değiştirilmemiş).
  - **Normalize olaylar:** `checkout.session.completed` → ödeme başarılı; `checkout.session.async_payment_failed` → session açıksa `Failed`; diğer tipler işlenmez (`Ignored`).
  - **Korelasyon:** `StripeBillingCheckoutProvider` Checkout Session + `SubscriptionData` üzerinde `billing_checkout_session_id`, `tenant_id`, `target_plan_code`, `current_plan_code` metadata’sını yazar; webhook bu alanlardan `billing_checkout_session_id` ile iç session’ı çözer.
- **İyzico** — `POST /api/v1/webhooks/billing/iyzico`
  - **Auth:** Yok (`AllowAnonymous`); güvenlik `X-IYZ-SIGNATURE-V3` doğrulamasından gelir.
  - **İmza doğrulama:** HPP / Direct / Subscription formatları için docs.iyzico.com V3 HMAC-SHA256 kuralıyla (`WebhookSecret` fallback `SecretKey`).
  - **Normalize olaylar:** `status=SUCCESS` veya `subscription.order.success` → `PaymentSucceeded`; `status=FAILURE` veya `subscription.order.failure` → `PaymentFailed`; diğerleri `Ignored`.
  - **Korelasyon:** `paymentConversationId` alanı GUID ise doğrudan `BillingCheckoutSessionId` olarak çözülür.
- **Yanıt (başarı):** `BillingWebhookAckDto` — `duplicate`, `processed`, `providerEventId`.
  - Aynı `(Provider, ProviderEventId)` ikinci kez gelirse `duplicate: true`, `processed: false`, HTTP **200** (sağlayıcı yeniden denemeleri için güvenli).
- **Idempotency tablosu:** `BillingWebhookReceipt` — benzersiz indeks `(Provider, ProviderEventId)`.
- **Çakışma kuralları:**
  - Webhook aktivasyonu yalnızca session `Provider` değeri webhook sağlayıcısı ile eşleşiyorsa çalışır (`Billing.ProviderMismatch`, **403**).
  - Panel finalize `provider` eşlemesi zorunlu tutulmaz (`providerMustMatch: null`).
  - Aynı session için finalize tekrarı: session zaten `Completed` ve abonelik hedef planda **Active** ise başarılı yanıt (yan etki yok).
- **Hata eşlemesi (özet):** `Billing.WebhookSignatureInvalid` / `Billing.WebhookSignatureMissing` → **401**; `Billing.ProviderMismatch` → **403**; `Billing.StripeWebhookNotConfigured` / `Billing.IyzicoWebhookNotConfigured` / `Billing.StripeSecretMissing` / `Billing.StripeCheckoutUrlsMissing` / `Billing.StripePriceNotConfigured` / `Billing.IyzicoConfigurationIncomplete` / `Billing.ProviderConfigurationIncomplete` / `Billing.IyzicoPlanPriceNotConfigured` → **503**; `Billing.StripeApiError` / `Billing.IyzicoApiError` → **502**; payload hataları → **400**; iş kuralı (session kapalı vb.) → mevcut `Subscriptions.*` kodları (çoğu **409**).

### 24.6 State geçişleri (özet)

- **Completed:** `ActivatePaidPlan` + `MarkCompleted` (manuel veya webhook `PaymentSucceeded`).
- **Failed:** `TryMarkFailedIfOpen` (webhook `PaymentFailed`; terminal session’da no-op).
- **Expired:** Okuma veya finalize denemesinde açık session için süre dolmuşsa `MarkExpired`.
- **Cancelled:** Yeni checkout’ta farklı hedef plan için önceki açık session iptal.

### 24.7 Scheduled downgrade lifecycle (stabilizasyon notu)

- Aynı plan seçimi her durumda reddedilir (active/trialing fark etmeksizin checkout veya downgrade schedule açılamaz).
- Downgrade hiçbir koşulda checkout akışına girmez; yalnız schedule edilir.
- Pending downgrade varken yeni downgrade gelirse mevcut pending kayıt iptal edilip yenisi açılır (replace).
- Pending downgrade varken upgrade checkout başlatılırsa pending downgrade otomatik iptal edilir.
- `GET /api/v1/tenants/{tenantId}/subscription-plan-change/pending` endpoint’i pending yoksa **200 OK + `null`** döner.
- Scheduled apply idempotent çalışır: abonelik zaten hedef planda aktifse plan tekrar yazımı no-op kalır, change kaydı `Applied` durumuna alınır.
- `EffectiveAt` önceliği: modelde mevcut gerçek dönem sonu verisi (`trialEndsAtUtc`) -> aktivasyon bazlı dönem sonu yaklaşımı -> kontrollü fallback (`utcNow + 1 gün`).

### 24.8 Model A (membership transition)

- **Aynı plan** her durumda reddedilir (active/trial/read-only fark etmez).
- **Upgrade** immediate uygulanır; billing cycle anchor korunur, yeni dönem başlatılmaz.
- **Downgrade** checkout'a gitmez; bir sonraki dönem başlangıcına schedule edilir.
- **Pending downgrade** görüntülenebilir / iptal edilebilir; yeni downgrade geldiğinde replace edilir.
- Pending downgrade varken upgrade checkout başlatılırsa pending kayıt otomatik iptal edilir.
- Pending endpoint davranışı: kayıt yoksa `200 OK + null`.

Proration hesabı dönem oranı ile yapılır:

- `remaining = CurrentPeriodEndUtc - now`
- `totalPeriod = CurrentPeriodEndUtc - CurrentPeriodStartUtc`
- `prorationRatio = remaining / totalPeriod`
- `priceDiff = newPlanPriceMinor - currentPlanPriceMinor`
- `proratedChargeMinor = round(priceDiff * prorationRatio)`

Notlar:

- Tüm tarih/zaman alanları UTC'dir.
- Parasal hesaplar minor unit (TRY için kurus) ile yapılır.
- Plan fiyatları `Billing:PlanPricesMinor` üzerinden okunur; yoksa Iyzico `PlanPricesTry` değerlerinden türetilir.
