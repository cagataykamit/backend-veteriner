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
| Pets | CRUD + liste (§16.5); `history-summary` (§17) | Create yanıtı çıplak `Guid` (§16.5) | Create DTO standardı P2 | P2 | Düşük |
| Appointments | CRUD + liste/lifecycle (§16.6) | Null-body ve bazı hata dallarında envelope farklılaşma riski | Appointments contract Sprint 2 ile netleştirildi | P1 | Orta |
| Examinations | Kanonik `visitReason`; yazmada opsiyonel legacy `complaint` (Faz 0 / Adım 3; §11); muayene detay `related-summary` (§18) | — | İstemciler `visitReason` kullanmalı | Tamamlandı (Faz 0) | Orta (alias kaldırma takvimi) |
| Vaccinations | Clinic context entegrasyonu var | `clinicId` ownership algısı modüller arası tutarsız | Context-first kuralını açık ve tek hale getirme | P1 | Orta |
| Payments | Create/update/list/detail DTO + `PaymentsContractSchemaFilter` ile OpenAPI hizalı (Faz 0 / Adım 4; §12) | İş kuralı: clinic/müşteri/hayvan tutarlılığı | Context-first klinik uyumu operasyonel | Tamamlandı (Faz 0) | Orta (typegen) |
| Treatments | List/detail/create/update DTO + `TreatmentsContractSchemaFilter`; muayene ile isteğe bağlı ilişki (§13) | Examination clinic/pet tutarlılığı; tarih penceresi | Examinations ile aynı liste/search örüntüsü | Tamamlandı (v1 omurga) | Orta (typegen) |
| Prescriptions | List/detail/create/update DTO + `PrescriptionsContractSchemaFilter`; isteğe bağlı examination + treatment (§14) | İkili referansta examination–treatment tutarlılığı; tarih penceresi | Treatments ile aynı liste/search örüntüsü | Tamamlandı (v1 omurga) | Orta (typegen) |
| Lab Results | List/detail/create/update DTO + `LabResultsContractSchemaFilter`; isteğe bağlı examination (§15); tek kayıt (satır analiz yok) | Examination clinic/pet tutarlılığı; `resultDateUtc` penceresi | Prescriptions/treatments ile aynı liste/search örüntüsü | Tamamlandı (v1 omurga) | Orta (typegen) |
| Hospitalizations | List/detail/create/update + discharge; `HospitalizationsContractSchemaFilter` (§16); isteğe bağlı examination; aktif yatış tekilliği | Aynı pet+klinikte çift aktif yatış; taburcu sonrası update yok; tarih/plan kuralları | LabResults ile aynı liste/search; `activeOnly` filtresi | Tamamlandı (v1 omurga) | Orta (typegen) |
| Dashboard | `summary` + `finance-summary` (§19) | Dokümantasyon drift riski | Contract metinleri ve OpenAPI doğruluğunu koruma | P2 | Düşük |
| Tenants | `subscription-summary` (§20); `POST …/invites` (§22); kiracı başına `TenantSubscriptions` + `TenantInvites` | `Tenants.InviteCreate`; plan `maxUsers` + koltuk sayımı | Davet/limit drift; token URL encoding | P1 | Orta (join ekranı + admin davet) |
| Species | CRUD + liste (§16.4) | Düşük | Dokümantasyon drift riski | P3 | Düşük |
| Breeds | CRUD + liste (§16.4.1) | Düşük | Dokümantasyon drift riski | P3 | Düşük |

**Clients (müşteri detay özeti):** `GET /api/v1/clients/{id}/recent-summary` — `Clients.Read`; tek yanıtta `ClientRecentSummaryDto` (`recentAppointments`, `recentExaminations`). Kayıtlar yalnız route’taki müşterinin **pet’lerine** aittir; sıra en yeni tarih önce; her blok en fazla **10** kayıt (`ClientRecentSummaryConstants`). Aktif klinik bağlamı (`IClinicContext`) varsa randevu ve muayene listeleri bu **kliniğe** indirgenir. Müşteri tenant dışı / yoksa `Clients.NotFound`. OpenAPI: `ClientsContractSchemaFilter`.

**Clients (müşteri ödeme özeti — Finance+ v1):** `GET /api/v1/clients/{id}/payment-summary` — `Clients.Read`; `ClientPaymentSummaryDto` (`totalPaymentsCount`, `totalPaidAmount`, `currencyTotals`, `lastPaymentAtUtc`, `recentPayments`). Yalnız route’taki **müşterinin** ödemeleri; `recentPayments` en fazla **10** (`ClientPaymentSummaryConstants`); sıra `paidAtUtc` en yeni önce. `totalPaidAmount` tek para birimi olduğunda o birimin toplamı; aksi halde **0** — çoklu birim için `currencyTotals` esas. Aktif klinik bağlamı varsa ödemeler bu kliniğe indirgenir. Müşteri yoksa `Clients.NotFound`. Ayrıntı §19.

**Pets (CRUD + liste):** `POST/PUT/GET /api/v1/pets` ve sayfalı liste — sözleşme özeti **§16.5** (DTO farkları, hata kodları, `POST` gövdesi `Guid`, abonelik yazma notu).

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
- Species (§16.4)
- Breeds (§16.4.1)
- Dashboard (`summary` + `finance-summary`: `DashboardContractSchemaFilter` — §19)
- Payments (Faz 0: §12 — şema/required/nullability)
- Treatments (§13 — şema/required/nullability)
- Prescriptions (§14 — şema/required/nullability)
- Lab Results (§15 — şema/required/nullability)
- Hospitalizations (§16 — şema/required/nullability)
- Pets (CRUD/liste §16.5 + pet detay `history-summary` §17: `PetsContractSchemaFilter`)
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

### 11.1) Examinations — CRUD / list / detail / update sözleşmesi

**Endpoint ve yetki:**

- `POST /api/v1/examinations` → `Examinations.Create`, başarı `201 Created`, gövde **çıplak `Guid`** (oluşan `id`).
- `PUT /api/v1/examinations/{id}` → `Examinations.Update`, başarı `204 NoContent`.
- `GET /api/v1/examinations/{id}` → `Examinations.Read`, başarı `200` + `ExaminationDetailDto`.
- `GET /api/v1/examinations` → `Examinations.Read`, başarı `200` + `PagedResult<ExaminationListItemDto>`.
- `GET /api/v1/examinations/{id}/related-summary` → `Examinations.Read`, başarı `200` + `ExaminationRelatedSummaryDto` (ilişkili tedavi/reçete/lab/yatış/ödeme özetleri).

**Kanonik/legacy alanlar:**

- Kanonik request/response alanı `visitReason`.
- Legacy `complaint` yalnızca request body’de kabul edilir; `visitReason` doluysa `complaint` yok sayılır.
- Response DTO’ları (`ExaminationDetailDto`, `ExaminationListItemDto`) yalnızca `visitReason` döner; `complaint` dönmez.

**Create/Update iş kuralları (davranış):**

- `tenantId` yalnızca `ITenantContext` üzerinden çözülür; context yoksa `Tenants.ContextMissing`.
- `appointmentId` varsa `clinicId` ve `petId` randevu ile tutarlı olmalıdır; aksi `Examinations.AppointmentPetClinicMismatch`.
- `appointmentId` yoksa create tarafında `clinicId` + `petId` zorunlu (`Examinations.Validation`).
- Aktif clinic context varsa ve request `clinicId` ile çakışıyorsa `Examinations.ClinicContextMismatch`.
- Create/Update yolunda `clinicId`/`petId` tenant içinde bulunamazsa sırasıyla `Clinics.NotFound` / `Pets.NotFound`.
- `appointmentId` bulunamaz veya tenant dışıysa `Appointments.NotFound`.

**UTC normalization ve examined window:**

- `ExaminedAtUtc` `DateTimeKind` ne olursa olsun önce UTC’ye normalize edilir (`ToUniversalTime` / `SpecifyKind`).
- Pencere kuralı (`ExaminationExaminedAtWindow`): en fazla **7 gün geçmiş**, en fazla **2 yıl ileri**.
- İhlal kodları: `Examinations.ExaminedTooFarInPast`, `Examinations.ExaminedTooFarInFuture`.

**List/Detail DTO farkları:**

- Liste öğesi `ExaminationListItemDto`: özet alanlar (`id`, `clinicId`, `petId`, `petName`, `clientId`, `clientName`, `appointmentId`, `examinedAtUtc`, `visitReason`).
- Detay `ExaminationDetailDto`: liste alanlarına ek olarak `tenantId`, `findings`, `assessment`, `notes`, `createdAtUtc`, `updatedAtUtc`.
- Pet/client lookup bulunamazsa isim alanları boş string fallback ile döner (davranış korunur).

**Update route/body kuralı:**

- `PUT` body içindeki `id` opsiyoneldir; doluysa route `id` ile aynı olmalıdır.
- Uyumsuzlukta `409 Conflict` + `extensions.code = Examinations.RouteIdMismatch`.

**ProblemDetails / hata kodları:**

- Validasyon (FluentValidation/model state) → `400 ValidationProblemDetails`.
- İş kuralı hataları `Result` üzerinden `ProblemDetails` + `extensions.code` ile döner.
- Sık kodlar: `Examinations.NotFound`, `Examinations.Validation`, `Examinations.ClinicContextMismatch`, `Examinations.AppointmentPetClinicMismatch`, `Appointments.NotFound`, `Clinics.NotFound`, `Pets.NotFound`, `Tenants.TenantInactive`.

### 11.2) Vaccinations — CRUD / list / detail / update sözleşmesi

**Endpoint ve yetki:**

- `POST /api/v1/vaccinations` → `Vaccinations.Create`, başarı `201 Created`, gövde **çıplak `Guid`** (oluşan `id`).
- `PUT /api/v1/vaccinations/{id}` → `Vaccinations.Update`, başarı `204 NoContent`.
- `GET /api/v1/vaccinations/{id}` → `Vaccinations.Read`, başarı `200` + `VaccinationDetailDto`.
- `GET /api/v1/vaccinations` → `Vaccinations.Read`, başarı `200` + `PagedResult<VaccinationListItemDto>`.

**Canonical alan notu:**

- Vaccinations request/response alanları canonical ve tekildir; Examinations tarafındaki `visitReason/complaint` benzeri bir legacy alias **yoktur**.
- Status enum sözleşmesi backend’de `Scheduled`, `Applied`, `Cancelled` olarak taşınır; ürün dili eşlemesi sırasıyla planlı/uygulandı/iptal edildi olarak okunur.

**Route / body id kuralı:**

- `PUT` body `id` opsiyoneldir; doluysa route `id` ile aynı olmalıdır.
- Uyumsuzlukta `400` + `extensions.code = Vaccinations.RouteIdMismatch`.

**Create/Update ilişki ve context kuralları:**

- Tenant yalnızca `ITenantContext` ile çözülür; context yoksa `Tenants.ContextMissing`.
- Aktif clinic context varsa ve request `clinicId` farklıysa `Vaccinations.ClinicContextMismatch`.
- `clinicId` tenant içinde yoksa `Clinics.NotFound`; `petId` tenant içinde yoksa `Pets.NotFound`.
- `examinationId` doluysa muayene tenant içinde bulunmalı (`Examinations.NotFound`) ve muayenenin `ClinicId` + `PetId` değerleri istekle birebir eşleşmelidir (`Vaccinations.ExaminationPetClinicMismatch`).

**Status/date rule matrisi (davranış değişmeden):**

| Status | AppliedAtUtc | DueAtUtc | Kural | Kod |
|--------|--------------|----------|-------|-----|
| `Scheduled` | **olamaz** | **zorunlu** | Planlı kayıtta uygulama tarihi girilmez, vade zorunlu | `Vaccinations.ScheduledMustNotHaveAppliedAt`, `Vaccinations.ScheduledRequiresDueAt` |
| `Applied` | **zorunlu** | opsiyonel | Uygulanan kayıtta uygulama tarihi zorunlu | `Vaccinations.AppliedRequiresAppliedAt` |
| `Cancelled` | **olamaz** | opsiyonel | İptal kayıtta uygulama tarihi girilmez | `Vaccinations.CancelledMustNotHaveAppliedAt` |

**UTC normalization ve pencere kuralları:**

- `AppliedAtUtc` ve `DueAtUtc` değerleri `DateTimeKind` ne olursa olsun UTC’ye normalize edilir (`ToUniversalTime` / `SpecifyKind`).
- `AppliedAtUtc` penceresi: en fazla **7 gün geçmiş**, en fazla **2 yıl ileri**.
  - Kodlar: `Vaccinations.AppliedTooFarInPast`, `Vaccinations.AppliedTooFarInFuture`.
- `DueAtUtc` penceresi: en fazla **10 yıl geçmiş**, en fazla **5 yıl ileri**.
  - Kodlar: `Vaccinations.DueTooFarInPast`, `Vaccinations.DueTooFarInFuture`.

**List/filter/search/pagination:**

- Query: `page`, `pageSize`, `search`/`page.search`, `clinicId`, `petId`, `status`, `dueFromUtc`, `dueToUtc`, `appliedFromUtc`, `appliedToUtc`.
- Tüm yapısal filtreler birbiriyle **AND** birleşir.
- `search` doluysa `VaccineName`, `Notes` ve müşteri+hayvan metninden türetilen pet kümesi üzerinden eşleşme yapılır.
- `sort`/`order` işlenmez; liste sırası `AppliedAtUtc ?? DueAtUtc` azalan, sonra `Id` azalan.
- Handler `page >= 1`, `pageSize 1..200` clamp uygular.

**Liste/detay DTO farkı:**

- `VaccinationListItemDto`: özet alanlar (`id`, `petId`, `petName`, `clientId`, `clientName`, `clinicId`, `examinationId`, `vaccineName`, `appliedAtUtc`, `dueAtUtc`, `status`).
- `VaccinationDetailDto`: liste alanlarına ek olarak `tenantId`, `notes`, `createdAtUtc`, `updatedAtUtc`.
- Pet/client lookup bulunamazsa isimler boş string fallback ile döner (davranış korunur).

**Hata kodları:**

- İş kuralları `Result` → `ProblemDetails` + `extensions.code` ile döner.
- Sık kodlar: `Vaccinations.NotFound`, `Vaccinations.ClinicContextMismatch`, `Vaccinations.ExaminationPetClinicMismatch`, `Vaccinations.RouteIdMismatch`, status/date kural kodları, `Tenants.TenantInactive`, `Clinics.NotFound`, `Pets.NotFound`, `Examinations.NotFound`.
- FluentValidation/model state hataları `400 ValidationProblemDetails`.

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

### 12.1) Payments — CRUD / list / detail / update operasyonel sözleşme

**Endpoint ve yetki:**

- `POST /api/v1/payments` → `Payments.Create`, başarı `201 Created`, gövde **çıplak `Guid`** (oluşan `id`).
- `PUT /api/v1/payments/{id}` → `Payments.Update`, başarı `204 NoContent`.
- `GET /api/v1/payments/{id}` → `Payments.Read`, başarı `200` + `PaymentDetailDto`.
- `GET /api/v1/payments` → `Payments.Read`, başarı `200` + `PagedResult<PaymentListItemDto>`.

**Status alanı notu:**

- Payments aggregate içinde `status` alanı yoktur; tahsilat kaydı tek olay anını (`paidAtUtc`) ve yöntemi (`method`) taşır.
- Bu nedenle backend sözleşmesinde `PaymentMethod` enum’u (`Cash`, `Card`, `Transfer`) vardır; “pending/failed/refunded” benzeri bir lifecycle status bu modülün kapsamı dışındadır.

**Route / body id kuralı:**

- `PUT` body `id` opsiyoneldir; doluysa route `id` ile aynı olmalıdır.
- Uyumsuzlukta `400` + `extensions.code = Payments.RouteIdMismatch`.

**Amount / currency / method / paidAt kuralları:**

- `amount > 0` zorunlu; ihlal `Payments.InvalidAmount`.
- `currency` ISO 4217 alpha-3 (3 harf) olmalıdır; domain tarafında `Trim().ToUpperInvariant()` normalize edilir.
- `method` enum doğrulaması zorunludur (`Cash`, `Card`, `Transfer`).
- `paidAtUtc` önce UTC’ye normalize edilir (`ToUniversalTime` / `SpecifyKind`), sonra pencere doğrulanır (`PaymentPaidAtWindow`):
  - en fazla **7 gün geçmiş** → aksi `Payments.PaidTooFarInPast`
  - en fazla **2 yıl ileri** → aksi `Payments.PaidTooFarInFuture`

**Tenant / clinic / entity ilişki kuralları:**

- `tenantId` yalnızca context’ten çözülür; context yoksa `Tenants.ContextMissing`.
- Aktif clinic context varsa ve request clinic farklıysa `Payments.ClinicContextMismatch`.
- `clinicId`, `clientId`, `petId` tenant içinde bulunamazsa sırasıyla `Clinics.NotFound`, `Clients.NotFound`, `Pets.NotFound`.
- `petId` doluysa pet’in `clientId` ile eşleşmesi zorunlu: `Payments.PetClientMismatch`.

**Relationship error code matrisi:**

| Alan | Doğrulama | Kod |
|------|-----------|-----|
| `appointmentId` | Tenant içinde bulunmalı | `Appointments.NotFound` |
| `appointmentId` | Appointment clinic = payment clinic | `Payments.AppointmentClinicMismatch` |
| `appointmentId` | Appointment pet -> client, payment client ile aynı olmalı | `Payments.AppointmentClientMismatch` |
| `appointmentId` + `petId` | Appointment pet = seçilen pet | `Payments.AppointmentPetMismatch` |
| `examinationId` | Tenant içinde bulunmalı | `Examinations.NotFound` |
| `examinationId` | Examination clinic = payment clinic | `Payments.ExaminationClinicMismatch` |
| `examinationId` | Examination pet -> client, payment client ile aynı olmalı | `Payments.ExaminationClientMismatch` |
| `examinationId` + `petId` | Examination pet = seçilen pet | `Payments.ExaminationPetMismatch` |

**Appointment + examination birlikte doluysa mevcut davranış (değiştirilmedi):**

- Her referans kendi içinde clinic/client/(varsa pet) kurallarına göre **ayrı ayrı** doğrulanır.
- `petId` gönderilmediği durumda appointment ve examination’ın aynı pet’e işaret etmesi için ek bir çapraz doğrulama yoktur; bu mevcut davranış bilinçli olarak korunmuştur.

**List/filter/search/pagination:**

- Query: `page`, `pageSize`, `search`, `clinicId`, `clientId`, `petId`, `method`, `paidFromUtc`, `paidToUtc`.
- Yapısal filtreler (`clinic/client/pet/method/date`) birbiriyle **AND** birleşir.
- `search` doluysa şu alanlar OR ile taranır: `notes`, `currency`, aramaya uyan müşteri kimlikleri, aramaya uyan pet kimlikleri.
- `search` normalize edilir (`trim`, max uzunluk), whitespace-only arama yok sayılır.
- Sıralama sabittir: `paidAtUtc desc`, ardından `id desc`; `sort/order` desteklenmez.
- Sayfalama handler içinde clamp edilir (`page >= 1`, `pageSize 1..200`).

**Liste / detay DTO farkları:**

- `PaymentListItemDto`: `id`, `clinicId`, `clientId`, `clientName`, `petId`, `petName`, `amount`, `currency`, `method`, `paidAtUtc`.
- `PaymentDetailDto`: listeye ek olarak `tenantId`, `appointmentId`, `examinationId`, `notes` içerir.
- `petId` null olabilir; bu durumda `petName` boş string dönebilir (mevcut davranış).

---

## 13) Treatments — request/response ve OpenAPI

**Liste** `GET /api/v1/treatments` query: `PageRequest` (`page`, `pageSize`, `search` / `page.search` birleşimi), isteğe bağlı `clinicId`, `petId`, `dateFromUtc`, `dateToUtc`. JWT/header clinic ile `clinicId` uyumsuzsa `Treatments.ClinicContextMismatch`. `sort`/`order` işlenmez.

**Create** `POST /api/v1/treatments` gövdesi: `CreateTreatmentCommand`.

| Alan | Zorunlu | Nullable (OpenAPI) | Not |
|------|---------|---------------------|-----|
| `clinicId` | Evet | Hayır | Context clinic ile uyumsuzsa `Treatments.ClinicContextMismatch` |
| `petId` | Evet | Hayır | Kiracı kapsamında mevcut pet (`PetByIdSpec`); opsiyonel muayene doluysa clinic/pet tutumu muayene ile doğrulanır |
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

**Operasyonel notlar (davranış değişikliği değildir; mevcut API sözleşmesinin netleştirilmesi):**

- **Detay maskeleme:** `GET /api/v1/treatments/{id}` için JWT/header ile aktif klinik bağlamı varken, kayıt başka bir kliniğe aitse yanıt **404** ve `Treatments.NotFound` ile maskelenir. Klinik bağlamı yoksa bu maskeleme uygulanmaz.
- **Liste vs detay:** `TreatmentListItemDto` özet taşır (`id`, `clinicId`, `petId`, `petName`, `clientId`, `clientName`, `treatmentDateUtc`, `title`, `examinationId`, `followUpDateUtc`). **`description`**, **`notes`**, **`tenantId`**, **`createdAtUtc`**, **`updatedAtUtc`** yalnız **`TreatmentDetailDto`** içindedir.
- **Create yanıtı:** `201 Created`, gövde yalnızca yeni **`Guid`** (id); ek sarmalayıcı yok.
- **Canonical / legacy alias:** Create gövdesi doğrudan `CreateTreatmentCommand`. Update’te route id esas; gövde `UpdateTreatmentBody` → `UpdateTreatmentCommand`. **İkinci bir legacy JSON alan adı veya eş anlamlı alias yoktur.**
- **Tip / durum alanı:** Tedavi **türü veya iş akışı durumu** için ayrı şema alanı **yoktur** (`treatmentType`, `status` vb.). İçerik **`title`**, **`description`**, isteğe bağlı **`notes`** ile metin olarak taşınır. Bu not **yeni alan ekleme talebi değildir**; mevcut sözleşme korunur.

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

**Operasyonel notlar (davranış değişikliği değildir; mevcut API sözleşmesinin netleştirilmesi):**

- **Detay maskeleme:** `GET /api/v1/prescriptions/{id}` için JWT/header ile aktif klinik bağlamı varken, kayıt başka bir kliniğe aitse yanıt **404** ve `Prescriptions.NotFound` ile maskelenir (yetki sızıntısı olmaması için gerçek varlık varlığı ifşa edilmez). Klinik bağlamı yoksa (yalnızca tenant ile sorgu) bu maskeleme uygulanmaz.
- **Canonical / legacy alias:** Reçete modülünde **ek bir legacy alan adı veya alias** (ör. eski isimle aynı anlama gelen ikinci bir JSON alanı) **yoktur**. İstemci ve entegrasyonlar **`title`**, **`content`**, isteğe bağlı **`notes`**, isteğe bağlı **`followUpDateUtc`** ve route/body’deki canonical alanları kullanmalıdır.
- **Takip tarihi / reçete tarihi:** `followUpDateUtc` doluysa, **UTC normalizasyonundan sonra** reçete tarihi (`prescribedAtUtc`) ile karşılaştırılır; takip tarihi reçete tarihinden **önce** olamaz (`Prescriptions.FollowUpBeforePrescription`). Reçete tarihi ayrıca `PrescribedAtUtcWindow` ile sınırlıdır (geçmiş/ileri pencere; treatments/examinations ile aynı mantık).
- **Metin tabanlı model:** İlaç satırları, adet, doz, frekans vb. için **ayrı şema alanları yoktur**; klinik içerik **`title`** (özet başlık), **`content`** (ana metin; ilaç ve kullanım tarifinin yazıldığı tek blok) ve isteğe bağlı **`notes`** üzerinden taşınır. Bu dokümantasyon **yeni alan talebi veya şema genişletmesi anlamına gelmez**; mevcut sözleşme korunur.

---

## 15) Lab Results — request/response ve OpenAPI

**Liste** `GET /api/v1/lab-results` query: `PageRequest` (`page`, `pageSize`, `search` / `page.search` birleşimi), isteğe bağlı `clinicId`, `petId`, `dateFromUtc`, `dateToUtc`. JWT/header clinic ile `clinicId` uyumsuzsa `LabResults.ClinicContextMismatch`. `sort`/`order` işlenmez. Boş `clinicId` / `petId` GUID filtreleri liste validator’ünde reddedilir.

**Create** `POST /api/v1/lab-results` gövdesi: `CreateLabResultCommand`.

| Alan | Zorunlu | Nullable (OpenAPI) | Not |
|------|---------|---------------------|-----|
| `clinicId` | Evet | Hayır | Context clinic ile uyumsuzsa `LabResults.ClinicContextMismatch` |
| `petId` | Evet | Hayır | Kiracı kapsamında mevcut pet (`PetByIdSpec`); opsiyonel muayene doluysa clinic/pet tutumu muayene ile doğrulanır |
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

**Operasyonel notlar (davranış değişikliği değildir; mevcut API sözleşmesinin netleştirilmesi):**

- **Detay maskeleme:** `GET /api/v1/lab-results/{id}` için JWT/header ile aktif klinik bağlamı varken, kayıt başka bir kliniğe aitse yanıt **404** ve `LabResults.NotFound` ile maskelenir. Klinik bağlamı yoksa bu maskeleme uygulanmaz.
- **Liste vs detay:** `LabResultListItemDto` özet taşır (`id`, `clinicId`, `petId`, `petName`, `clientId`, `clientName`, `resultDateUtc`, `testName`, `examinationId`). **`resultText`**, **`interpretation`**, **`notes`**, **`tenantId`**, **`createdAtUtc`**, **`updatedAtUtc`** yalnız **`LabResultDetailDto`** içindedir.
- **Create yanıtı:** `201 Created`, gövde yalnızca yeni **`Guid`** (id); ek sarmalayıcı yok.
- **Canonical / legacy alias:** Create gövdesi doğrudan `CreateLabResultCommand`. Update’te route id esas; gövde `UpdateLabResultBody` → `UpdateLabResultCommand`. **İkinci bir legacy JSON alan adı veya eş anlamlı alias yoktur.**
- **Sonuç durumu / tip alanı:** Laboratuvar kaydı için **ayrı `status` veya `resultType` enum şema alanı yoktur**; yorum ve sınıflama ihtiyacı **`interpretation`** ve serbest metin alanlarıyla taşınır. Bu not **yeni alan talebi değildir**; mevcut sözleşme korunur.
- **Tek kayıt / tek metin modeli:** API **tek bir lab sonuç kaydı** döner; çok satırlı analiz tablosu veya satır bazlı sonuç koleksiyonu **şemada yoktur**. Ölçüm özeti **`testName`** + zorunlu **`resultText`** (tek blok metin) ve isteğe bağlı **`interpretation`** / **`notes`** ile temsil edilir.

---

## 16) Hospitalizations — request/response ve OpenAPI

**Liste** `GET /api/v1/hospitalizations` query: `PageRequest` (`page`, `pageSize`, `search` / `page.search` birleşimi), isteğe bağlı `clinicId`, `petId`, **`activeOnly`** (`true` → yalnız `dischargedAtUtc == null`; `false` → yalnız taburcu edilmiş; **omit** → tümü), `dateFromUtc`, `dateToUtc` (**`admittedAtUtc`** alanına göre). JWT/header clinic ile `clinicId` uyumsuzsa `Hospitalizations.ClinicContextMismatch`. `sort`/`order` işlenmez. Boş `clinicId` / `petId` GUID filtreleri liste validator’ünde reddedilir.

**Create** `POST /api/v1/hospitalizations` gövdesi: `CreateHospitalizationCommand`.

| Alan | Zorunlu | Nullable (OpenAPI) | Not |
|------|---------|---------------------|-----|
| `clinicId` | Evet | Hayır | Context clinic ile uyumsuzsa `Hospitalizations.ClinicContextMismatch` |
| `petId` | Evet | Hayır | Kiracı kapsamında mevcut pet (`PetByIdSpec`); opsiyonel muayene doluysa clinic/pet tutumu muayene ile doğrulanır |
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

**Operasyonel notlar (davranış değişikliği değildir; mevcut API sözleşmesinin netleştirilmesi):**

- **Detay maskeleme:** `GET /api/v1/hospitalizations/{id}` için JWT/header ile aktif klinik bağlamı varken, kayıt başka bir kliniğe aitse yanıt **404** ve `Hospitalizations.NotFound` ile maskelenir.
- **Discharge maskeleme:** `POST /api/v1/hospitalizations/{id}/discharge` için aktif klinik bağlamı varken, kayıt satırının `clinicId`’si bağlamdan farklıysa yanıt **404** ve `Hospitalizations.NotFound` ile maskelenir (detay ile aynı güvenlik örüntüsü). Klinik bağlamı yoksa bu maskeleme uygulanmaz.
- **Liste vs detay:** `HospitalizationListItemDto` özet taşır; **`tenantId`**, **`notes`**, **`createdAtUtc`**, **`updatedAtUtc`** yalnız **`HospitalizationDetailDto`** içindedir. Her iki şemada da **`isActive`** = `dischargedAtUtc == null` (türetilmiş; ayrı kalıcı alan yok).
- **Create yanıtı:** `201 Created`, gövde yalnızca yeni **`Guid`** (id); ek sarmalayıcı yok.
- **Canonical / legacy alias:** Create `CreateHospitalizationCommand`; update `UpdateHospitalizationBody` → `UpdateHospitalizationCommand`; taburcu `DischargeHospitalizationBody`. **İkinci bir legacy JSON alan adı veya eş anlamlı alias yoktur.**
- **Ayrı status enum:** Şemada **`HospitalizationStatus` benzeri ayrı bir enum alanı yoktur**; “aktif / taburcu” durumu **`dischargedAtUtc`** (null = aktif yatış) ve DTO’daki **`isActive`** ile türetilir.
- **Tek aktif yatış:** Aynı `tenantId` + `clinicId` + `petId` için aynı anda yalnızca **bir** `dischargedAtUtc == null` kaydına izin verilir (`Hospitalizations.ActiveHospitalizationExists`; EF filtreli unique indeks ile uyumlu).
- **Discharge `notes` semantiği:** Komutta `notes` **`null`** (property yok veya JSON `null`) → taburcu sırasında **mevcut notlar değişmez**; **`null` olmayan** (boş string dahil) → `applyNotes` ile not alanı trim sonrası güncellenir (boş string → `null`).

---

## 16.4) Species — global katalog (CRUD)

**Veri modeli:** `Species` **global referans kataloğudur**; satırda **`TenantId` yoktur** (tüm kiracılar aynı `Species` tablosunu paylaşır). Sıralama alanı kanonik olarak **`DisplayOrder`** (`sortOrder` adı kullanılmaz).

### Endpoint ve yetki

| Method | Path | Policy | Başarı yanıtı |
|--------|------|--------|----------------|
| `POST` | `/api/v1/species` | `Species.Create` | **`201 Created`**; gövde yalnızca **`Guid`** (yeni `speciesId`); `Location` → `GET .../species/{id}`. |
| `PUT` | `/api/v1/species/{id}` | `Species.Update` | **`204 No Content`** |
| `GET` | `/api/v1/species/{id}` | `Species.Read` | `SpeciesDetailDto` |
| `GET` | `/api/v1/species` | `Species.Read` | `PagedResult<SpeciesListItemDto>` |

### Liste: sayfalama ve `isActive`

- Query: `page`, `pageSize` (handler içinde **1** ve **200** clamp), opsiyonel **`isActive`** (`bool?`).
- **`isActive` yok (`null`):** filtre uygulanmaz (aktif + pasif).
- **`isActive=true` / `false`:** yalnız ilgili `IsActive` değerine kayıtlar.
- **`PageRequest.search`**, **`sort`**, **`order` işlenmez** (`SpeciesController` XML ile uyumlu; §10 genel tablo ile çelişirse bu endpoint için bu bölüm esastır).

### Detay vs liste DTO

- **`SpeciesListItemDto`** ve **`SpeciesDetailDto`** aynı alan setini taşır: `Id`, `Code`, `Name`, `IsActive`, **`DisplayOrder`**.
- **`GET` detay** pasif türü de döndürebilir (yönetim / referans). **Pet oluşturma-güncellemede** pasif tür seçilemez → `Pets.SpeciesNotFound` (§16.5 tablo).

### Yazma kapısı: create vs update (mevcut davranış)

- **`POST` (Create):** `ITenantContext` zorunludur; ardından **`TenantSubscriptionEffectiveWriteEvaluator`** ile abonelik yazma izni değerlendirilir (kiracı bağlamı yoksa veya abonelik yazmayı engelliyorsa `Result` hatası).
- **`PUT` (Update):** Bu evaluator **çağrılmaz**; güncelleme yalnızca entity ve duplicate kuralları ile yapılır (Pets §16.5’teki create/update farkına paralel **bilinçli ürün/engine ayrımı**; tekilleştirme ayrı onay + iş kuralı değişikliği gerektirir).

### Route / body id (`Species.RouteIdMismatch`)

- `PUT` için route `id` esas kaynaktır (§3.4). Body’de `UpdateSpeciesCommand.Id` dolu ve route ile farklıysa → **`400`**, `extensions.code`: **`Species.RouteIdMismatch`**.

### İş kuralı ve doğrulama hataları (`Result` → `ProblemDetails` + `extensions.code`)

| Kod | HTTP (tipik) | Koşul |
|-----|----------------|--------|
| `Tenants.ContextMissing` | `400` | Create: kiracı bağlamı yok |
| `Tenants.NotFound` / `Tenants.TenantInactive` | `404` / `403` | Tenant yok / pasif (evaluator) |
| `Subscriptions.NotFound` | `404` | Abonelik kaydı yok |
| `Subscriptions.TenantReadOnly` | `403` | Trial bitmiş salt okunur vb. (evaluator) |
| `Subscriptions.TenantCancelled` | `403` | Abonelik iptal |
| `Subscriptions.WriteNotAllowed` | `403` | Yazma desteklenmiyor |
| `Species.DuplicateCode` | `409` | Kod çakışması |
| `Species.DuplicateName` | `409` | Ad çakışması (büyük/küçük harf duyarsız) |
| `Species.NotFound` | `404` | GetById / Update’te yok |
| `Species.RouteIdMismatch` | `400` | PUT route ≠ body id |

FluentValidation → `400` `ValidationProblemDetails` (`Validation.ModelStateInvalid`).

---

## 16.4.1) Breeds — global katalog (CRUD)

**Veri modeli:** `Breed` **global referans kataloğudur**; satırda **`TenantId` yoktur**. Her ırk **`SpeciesId`** ile bir **Species** satırına bağlıdır (FK). Irk için ayrı bir **`code`** veya **`displayOrder`** alanı yoktur; kanonik alanlar **`SpeciesId`**, **`Name`**, **`IsActive`**.

### Endpoint ve yetki

| Method | Path | Policy | Başarı yanıtı |
|--------|------|--------|----------------|
| `POST` | `/api/v1/breeds` | `Breeds.Create` | **`201 Created`**; gövde yalnızca **`Guid`** (yeni `breedId`); `Location` → `GET .../breeds/{id}`. |
| `PUT` | `/api/v1/breeds/{id}` | `Breeds.Update` | **`204 No Content`** |
| `GET` | `/api/v1/breeds/{id}` | `Breeds.Read` | `BreedDetailDto` |
| `GET` | `/api/v1/breeds` | `Breeds.Read` | `PagedResult<BreedListItemDto>` |

### Create vs update body (DTO farkları)

- **Create (`CreateBreedCommand`):** **`speciesId`** + **`name`** — yeni ırk ilgili türe bağlanır.
- **Update (`UpdateBreedCommand`):** **`id`** + **`name`** + **`isActive`** — **`speciesId` yoktur**; güncelleme **türü değiştirmez** (ırkın türü **immutable**).

### Liste: sayfalama, `isActive`, `speciesId`, `search`

- Query: `page`, `pageSize` (handler içinde **1** ve **200** clamp), opsiyonel **`isActive`** (`bool?`), opsiyonel **`speciesId`** (`Guid?`), opsiyonel **`search`** (`PageRequest.Search` / query `search`); tüm dolu filtreler **AND** ile birleşir.
- **`search`:** boş veya yalnız whitespace ise **uygulanmaz**. Aksi halde **büyük/küçük harf duyarsız** alt-dize eşlemesi: **`Breed.Name`** veya **`Species.Name`** (içerir / `Contains`).
- **`sort`** ve **`order` işlenmez**; sıralama sabittir: tür adı → ırk adı → `Id` (`BreedsPagedSpec`).

### Detay vs liste DTO

- **Liste (`BreedListItemDto`):** `Id`, `SpeciesId`, **`SpeciesName`**, `Name`, `IsActive` — **`SpeciesCode` yok**.
- **Detay (`BreedDetailDto`):** ayrıca **`SpeciesCode`** (`Species.Code`) ve **`SpeciesName`** — tür kodu **yalnız detayda** döner.
- **Pet oluşturma/güncellemede** pasif ırk seçilemez → `Pets.BreedNotFound` (§16.5 tablo).

### Yazma kapısı: create vs update (mevcut davranış)

- **`POST` (Create):** `ITenantContext` zorunludur; **`TenantSubscriptionEffectiveWriteEvaluator`** ile abonelik yazma izni; ardından `SpeciesByIdSpec` ile tür varlığı (`Breeds.SpeciesNotFound`); aynı tür altında isim tekilliği (`Breeds.DuplicateName`).
- **`PUT` (Update):** Abonelik evaluator **çağrılmaz**; yalnızca ırk bulunabilirlik + aynı tür altında isim tekilliği.

### Route / body id (`Breeds.RouteIdMismatch`)

- `PUT` için route `id` esas kaynaktır (§3.4). Body `UpdateBreedCommand.Id` dolu ve route ile farklıysa → **`400`**, `extensions.code`: **`Breeds.RouteIdMismatch`**.

### İş kuralı ve doğrulama hataları (`Result` → `ProblemDetails` + `extensions.code`)

| Kod | HTTP (tipik) | Koşul |
|-----|----------------|--------|
| `Tenants.ContextMissing` | `400` | Create: kiracı bağlamı yok |
| `Tenants.NotFound` / `Tenants.TenantInactive` | `404` / `403` | Tenant yok / pasif (evaluator) |
| `Subscriptions.NotFound` | `404` | Abonelik kaydı yok |
| `Subscriptions.TenantReadOnly` | `403` | Trial bitmiş salt okunur vb. |
| `Subscriptions.TenantCancelled` | `403` | Abonelik iptal |
| `Subscriptions.WriteNotAllowed` | `403` | Yazma desteklenmiyor |
| `Breeds.SpeciesNotFound` | `404` | Create: tür yok |
| `Breeds.DuplicateName` | `409` | Aynı `SpeciesId` altında aynı ad (büyük/küçük harf duyarsız) |
| `Breeds.NotFound` | `404` | GetById / Update’te yok |
| `Breeds.Inconsistent` | `400` | Detayda tür navigasyonu yüklenemedi (`Species` null) |
| `Breeds.RouteIdMismatch` | `400` | PUT route ≠ body id |

FluentValidation → `400` `ValidationProblemDetails` (`Validation.ModelStateInvalid`).

---

## 16.5) Pets — CRUD ve liste

**Kapsam:** Kiracıya ve müşteriye bağlı hayvan kayıtları. Tüm okuma/yazma **JWT / context `tenant_id`** (`ITenantContext`) ile sınırlıdır; müşteri `ClientByIdSpec(tenantId, clientId)` ile doğrulanır. Tür (`SpeciesId`) ve isteğe bağlı katalog **ırk** (`BreedId`) global katalog tablolarına bağlıdır (`SpeciesByIdSpec`, `BreedByIdWithSpeciesSpec`).

### Endpoint ve yetki

| Method | Path | Policy | Başarı yanıtı |
|--------|------|--------|----------------|
| `POST` | `/api/v1/pets` | `Pets.Create` | **`201 Created`**; gövde **`Guid`** (yeni `petId`); `Location` → `GET .../pets/{id}`. Anlamlı wrapper DTO **yok** (P2 backlog; typegen/istemci `Guid` beklemelidir). |
| `PUT` | `/api/v1/pets/{id}` | `Pets.Create` | Ayrı `Pets.Update` yok; oluşturma ile aynı policy. **`204 No Content`** |
| `GET` | `/api/v1/pets/{id}` | `Pets.Read` | `PetDetailDto` |
| `GET` | `/api/v1/pets` | `Pets.Read` | `PagedResult<PetListItemDto>` |

Controller `TryGetResolvedTenant` ile tenant çözülmezse işlem başlamaz (diğer modüllerle aynı).

### Route / body id (`Pets.RouteIdMismatch`)

- `PUT` için route `id` **esas kaynak** (§3.4). Body `UpdatePetCommand.Id` boş (`Guid.Empty`) ise route id ile doldurulur. Body id dolu ve route ile **farklıysa** → `400`, `extensions.code`: **`Pets.RouteIdMismatch`**.

### Liste: sayfalama, arama, filtre

- Query: `page`, `pageSize` (handler içinde **1** ve **200** clamp), `search` ve/veya `page.search` — üst düzey `search` doluysa **önceliklidir** (`PageRequestQuery.WithMergedSearch`).
- Opsiyonel **`clientId`**, **`speciesId`** (`Guid?`); doluysa `PetsByTenantCountSpec` / `PetsByTenantPagedSpec` ile **AND** filtre.
- **`sort` / `order` işlenmez** (controller XML ile uyumlu).
- **`search` dolu ve anlamlıysa:** önce müşteri metni `ClientsByTenantTextSearchSpec` → eşleşen müşterilerin pet id’leri `PetsByTenantForClientIdsSpec`; ardından hayvan alanları (`Name`, serbest `Breed`, `Species.Name`, `BreedRef.Name`) ve bu pet id kümesi **OR** ile birleştirilir (§10 arama tablosu ile uyumlu).

### Detay vs liste DTO farkları

| Alan / konu | `PetDetailDto` (GET `{id}`) | `PetListItemDto` (GET liste) |
|-------------|-----------------------------|------------------------------|
| Müşteri özeti | `ClientName`, `ClientPhone`, `ClientEmail` | Yok (yalnız `ClientId`) |
| `BirthDate`, `BreedId`, `Gender`, `Notes` | Var | Yok |
| `Weight` | `decimal?` (null = bilinmiyor) | `decimal` — kaynak `Weight` **null ise `0`** atanır (`?? 0`). **Liste ile detay arasında “ağırlık yok” vs “sıfır kg” ayrımı yoktur**; istemci tam ayrım için detay endpoint’ine bakmalıdır. |
| `SpeciesName` | Tür ilişkisinden | `Species?.Name ?? ""` |

### Abonelik / yazma kapısı (davranış değişmedi — not)

- **`POST` (Create):** `TenantSubscriptionEffectiveWriteEvaluator` ile kiracı **salt okunur** vb. durumda yazma engellenebilir (evaluator sonucu `Result` hatası).
- **`PUT` (Update):** Bu evaluator **çağrılmaz**; güncelleme yolu kiracı aktif + entity doğrulamaları ile devam eder. **Bilinçli ürün/engine farkı olabilir**; tekilleştirme ayrı onay + iş kuralı değişikliği gerektirir. Bu doküman yalnızca mevcut davranışı kaydeder.

### İş kuralı hataları (`Result` → `ProblemDetails` + `extensions.code`)

| Kod | HTTP (tipik) | Koşul |
|-----|----------------|--------|
| `Tenants.ContextMissing` | `400` | Kiracı bağlamı yok |
| `Tenants.NotFound` / `Tenants.TenantInactive` | `404` / `403` | Tenant yok / pasif |
| `Clients.NotFound` | `404` | Müşteri yok veya tenant’a ait değil |
| `Pets.SpeciesNotFound` | `400` | Tür yok veya pasif |
| `Pets.BreedNotFound` / `Pets.BreedSpeciesMismatch` | `400` | Irk yok/pasif veya tür ile uyumsuz |
| `Pets.ColorNotFound` | `400` | Renk yok veya pasif |
| `Pets.BirthDateInFuture` | `400` | Doğum tarihi gelecekte |
| **`Pets.DuplicatePet`** | **`409`** | Aynı müşteri + aynı isim (normalize) + aynı `SpeciesId` (create/update; update’te kendi `id` hariç) |
| `Pets.NotFound` | `404` | Pet yok veya tenant dışı |
| **`Pets.Inconsistent`** | **`400`** | Detayda tür navigasyonu yüklenemedi (`Species` null) |
| `Pets.RouteIdMismatch` | `400` | PUT route ≠ body id |

FluentValidation → `400` `ValidationProblemDetails`.

### Özet endpoint (değişmedi)

`GET .../history-summary` için **§17** geçerlidir.

---

## 16.6) Appointments — CRUD, liste ve lifecycle

**Kapsam:** Randevular tenant bazlıdır (`ITenantContext`). Klinik etkisi context-first çalışır (`IClinicContext` varsa istek `clinicId` ile uyumlu olmalıdır). Bu bölüm `POST/PUT/GET list/detail` ve `cancel/complete/reschedule` davranışlarını tek yerde toplar.

### Endpoint, yetki ve yanıt şekli

| Method | Path | Policy | Başarı yanıtı |
|--------|------|--------|----------------|
| `POST` | `/api/v1/appointments` | `Appointments.Create` | **`201 Created`**; gövde **çıplak `Guid`** (`appointmentId`), `Location` → `GET .../appointments/{id}` |
| `PUT` | `/api/v1/appointments/{id}` | `Appointments.Reschedule` | `204 NoContent` |
| `GET` | `/api/v1/appointments/{id}` | `Appointments.Read` | `AppointmentDetailDto` |
| `GET` | `/api/v1/appointments` | `Appointments.Read` | `PagedResult<AppointmentListItemDto>` |
| `POST` | `/api/v1/appointments/{id}/cancel` | `Appointments.Cancel` | `204 NoContent` |
| `POST` | `/api/v1/appointments/{id}/complete` | `Appointments.Complete` | `204 NoContent` |
| `POST` | `/api/v1/appointments/{id}/reschedule` | `Appointments.Reschedule` | `204 NoContent` |

### Enum contract (numeric)

- `AppointmentStatus` JSON’da **numeric int**: `Scheduled=0`, `Completed=1`, `Cancelled=2`.
- `AppointmentType` JSON’da **numeric int**: `Examination=0`, `Vaccination=1`, `Checkup=2`, `Surgery=3`, `Grooming=4`, `Consultation=5`, `Other=6`.
- Create’te `status` opsiyoneldir; verilmezse `Scheduled` kabul edilir.
- Update’te `status` zorunludur (`UpdateAppointmentCommand`).

### Route/body id, context ve ilişki doğrulaması

- `PUT /appointments/{id}` için route `id` kaynaktır; body `id` dolu ve farklıysa `Appointments.RouteIdMismatch` (`400`).
- Clinic context kuralı:
  - Liste/create/update’da istek `clinicId` + aktif context clinic uyuşmazsa `Appointments.ClinicContextMismatch`.
  - Detail/lifecycle’da clinic uyuşmazlığında kaynak gizleme için `Appointments.NotFound`.
- Create/update’da `petId` tenant içinde doğrulanır (`Pets.NotFound`).
- Create/update’da clinic tenant içinde ve aktif olmalıdır (`Clinics.NotFound`, `Clinics.Inactive`).
- Create’te `clinicId` gönderilmezse: tek aktif klinik otomatik seçilir; birden fazla aktif klinikte `Clinics.ClinicSelectionRequired`.

### UTC normalization, schedule window ve conflict semantiği

- `scheduledAtUtc` create/update/reschedule’da normalize edilir:
  - `Utc` → olduğu gibi
  - `Local` → `ToUniversalTime()`
  - `Unspecified` → `DateTimeKind.Utc` varsayımı ile işlenir
- Schedule window yalnız `Scheduled` statüsünde uygulanır:
  - en fazla 7 gün geçmiş (`Appointments.ScheduledTooFarInPast`)
  - en fazla 2 yıl gelecek (`Appointments.ScheduledTooFarInFuture`)
- Conflict kuralları yalnız `Scheduled` için uygulanır:
  - aynı clinic + aynı timestamp (`Appointments.ClinicSlotDuplicate`)
  - aynı pet + aynı timestamp (`Appointments.PetSlotDuplicate`)
- **Conflict semantiği timestamp-equality’dir**: interval/overlap hesaplaması yoktur.

### Liste davranışı (filter/search/paging/sort)

- Query: `page`, `pageSize`, `search`/`page.search`, `clinicId`, `petId`, `status`, `dateFromUtc`, `dateToUtc`.
- `search` metin kümesi:
  - randevu `notes`
  - pet metni (`name`, `species`, `breed`) + müşteri metni ile eşleşen pet id kümesi (`ListSearchPetIds`)
- Filtreler (`clinicId/petId/status/date`) birbirleri ile **AND** uygulanır.
- Sort yalnızca `scheduledAtUtc` + `asc|desc`; sort boşsa varsayılan **desc** (en yeni önce).
- `page/pageSize` handler içinde clamp edilir (`1..200`).

### DTO/response shape netliği

- Create response gövdesi `Guid` (wrapper DTO yok).
- `AppointmentListItemDto` ve `AppointmentDetailDto` alanları paraleldir:
  `clinicId/name`, `petId/name`, `clientId/name`, `speciesId/name`, `appointmentType`, `scheduledAtUtc`, `status`, `notes`.
- Detail’de ilişki kayıtları bulunamazsa bazı string alanlar boş (`""`) dönebilir (örn. `clientName`, `speciesName`).

### Lifecycle ve status geçişleri

- `cancel`, `complete`, `reschedule` yalnız `Scheduled` randevuda geçerlidir; aksi `Appointments.InvalidStatusTransition`.
- Update (`PUT`) domain `ApplyWriteUpdate` ile status geçişini uygular:
  - `Scheduled` -> detay güncelle
  - `Scheduled` -> `Completed` / `Cancelled` kabul
  - `Completed/Cancelled` -> sadece aynı statü no-op; başka statü reddedilir

### Gate / standard sapması notları (davranış değişmedi)

- **Gate farkı:** Create akışında `tenant.IsActive` kontrolü vardır; update/lifecycle handler’larında tenant aktiflik gate’i yoktur. Bu doküman mevcut davranışı kaydeder; değişiklik business onayı gerektirir.
- **Reschedule null-body sapması:** Controller’da `body is null` dalı doğrudan `Problem(...)` döner; `Result -> ToActionResult` standardından sapar. Bu sprintte davranış değişikliği yapılmamıştır; teknik borç olarak izlenir.

### Hata kodları (özet)

| Kod | HTTP (tipik) | Koşul |
|-----|--------------|-------|
| `Tenants.ContextMissing` | `400` | Tenant bağlamı yok |
| `Tenants.NotFound` / `Tenants.TenantInactive` | `404` / `403` | Create akışında tenant yok/pasif |
| `Appointments.RouteIdMismatch` | `400` | PUT route id ≠ body id |
| `Appointments.ClinicContextMismatch` | `400` | Query/body clinic aktif context ile çelişir |
| `Clinics.NotFound` / `Clinics.Inactive` | `404` / `400` | Klinik yok/pasif |
| `Clinics.ClinicSelectionRequired` | `400` | Create’te clinicId yok + birden fazla aktif klinik |
| `Pets.NotFound` | `404` | Pet yok / tenant dışı |
| `Appointments.ScheduledTooFarInPast/Future` | `400` | Schedule window ihlali |
| `Appointments.ClinicSlotDuplicate` / `Appointments.PetSlotDuplicate` | `409` | Timestamp-equality slot conflict |
| `Appointments.InvalidStatusTransition` | `400` | Geçersiz lifecycle/status geçişi |
| `Appointments.NotFound` | `404` | Kayıt yok veya clinic context nedeniyle gizlendi |
| `Appointments.Validation` | `400` | Enum/alan validasyonu |

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

**Yanıt:** `TenantSubscriptionSummaryDto` — `tenantId`, `tenantName`, `planCode` (string API kodu: `Basic` / `Pro` / `Premium`), `planName`, `status` (**effective** `TenantSubscriptionStatus`; JSON **numeric**: `Trialing=0`, `Active=1`, `ReadOnly=2`, `Cancelled=3`), `trialStartsAtUtc`, `trialEndsAtUtc` (opsiyonel), `daysRemaining` (yalnız `Trialing` ve `trialEndsAtUtc` varken; aksi halde null), `isReadOnly`, `canManageSubscription` (**`Tenants.Create`** — **checkout yetkisi değil**; ayrıntı **§20.1**), `currentPeriodStartUtc`, `currentPeriodEndUtc`, `billingCycleAnchorUtc`, `nextBillingAtUtc`, `pendingPlanChange`, `availablePlans[]` (`code`, `name`, `description`, `maxUsers`).

**Veri:** `TenantSubscriptions` tablosu kiracı ile 1:1 (`TenantId` PK). Yeni kiracı oluşturma (`POST /tenants`) sonrası varsayılan olarak Basic plan + Trialing + 14 gün trial (`SubscriptionTrialDefaults.TrialDays`) yazılır. Eski kiracılar için migration ile backfill uygulanır.

**Hatalar:** Kiracı bağlamı yok → `Tenants.ContextMissing`; abonelik satırı yok → `Subscriptions.NotFound`; tenant yok → `Tenants.NotFound`; izin yok → `Auth.PermissionDenied` veya `Tenants.AccessDenied`. FluentValidation (geçersiz route `tenantId`) → `400`.

**Swagger:** Controller `ProducesResponseType(typeof(TenantSubscriptionSummaryDto), 200)`; enum şeması OpenAPI’da `TenantSubscriptionStatus` olarak görünür.

### 20.1) Settings / Subscription — özet contract (tek kaynak)

**Kapsam:** Panel “ayarlar / abonelik” ekranı; uçlar `TenantsController` (`/api/v1/tenants/{tenantId}/…`) ve anonim billing uçları (`BillingCallbacksController`, `BillingWebhooksController`). Sağlayıcı soyutlaması: `BillingProvider`, `IBillingCheckoutProvider`, `ISubscriptionCheckoutActivationService` (ayrıntı **§24**).

#### Yetki matrisi (özet okuma vs yönetim)

| İşlem | Policy / kontrol | Not |
|--------|------------------|-----|
| `GET …/subscription-summary` | Handler: `Subscriptions.Read` **veya** `Tenants.Read`; JWT `tenant_id` = route **veya** platform `Tenants.Read` | Controller’da tek policy yok; yetki handler’da. |
| Checkout başlat / durum / finalize; plan değişikliği (downgrade schedule, pending, cancel) | `Subscriptions.Manage` + `TryGetResolvedTenant` | Abonelik **yönetimi** dar yetki. |
| `canManageSubscription` (özet DTO) | **`Tenants.Create` claim’i** ile hesaplanır | **Checkout yönetimi değildir.** Gerçek checkout/plan işlemleri için **`Subscriptions.Manage`** gerekir; UI bu iki bayrağı karıştırmamalıdır. |

#### `TenantSubscriptionSummaryDto` (özet)

- Kimlik: `tenantId`, `tenantName`.
- Plan: `planCode`, `planName` (API string kodları: `Basic` / `Pro` / `Premium` — `SubscriptionPlanCatalog.ToApiCode`).
- Durum: `status` (**effective** status: trial bitişi sonrası `Trialing` bile olsa effective `ReadOnly` olabilir), `trialStartsAtUtc`, `trialEndsAtUtc`, `daysRemaining`, `isReadOnly`.
- Dönem: `currentPeriodStartUtc`, `currentPeriodEndUtc`, `billingCycleAnchorUtc`, `nextBillingAtUtc`.
- UI bayrakları: `canManageSubscription` (**yukarıdaki tablo**).
- `pendingPlanChange` (`PendingSubscriptionPlanChangeDto` veya null), `availablePlans[]` (`SubscriptionPlanOptionDto`: `code`, `name`, `description`, `maxUsers`).

#### `SubscriptionCheckoutSessionDto` (checkout oturumu)

- `checkoutSessionId`, `tenantId`, `currentPlanCode`, `targetPlanCode`, `status` (`BillingCheckoutSessionStatus`), `provider` (`BillingProvider`), `checkoutUrl`, `canContinue`, `expiresAtUtc`.
- Opsiyonel proration: `chargeCurrencyCode`, `proratedChargeMinor`, `prorationRatio` (upgrade ve fiyat yapılandırması varsa).

#### Callback vs webhook vs finalize (kodla uyumlu akış)

- **Iyzico callback** — `POST /api/v1/billing/iyzico/callback` (`AllowAnonymous`, form POST): **üretim kodu yalnızca HTTP redirect** ile panel subscription URL’sine köprüler (`checkout=processing`, `provider=iyzico`, isteğe bağlı `checkoutSessionId`). **Ödeme/aktivasyon doğrulaması burada yapılmaz**; kaynak gerçeklik **webhook** veya panel **`…/finalize`** ile **§24**’te anlatıldığı gibi.
- **Webhook** — `POST /api/v1/webhooks/billing/stripe` | `…/iyzico`: ham gövde + imza; `ProcessBillingWebhookCommand` → idempotent aktivasyon hattı.
- **Finalize** — `POST /api/v1/tenants/{tenantId}/subscription-checkout/{checkoutSessionId}/finalize` (`Subscriptions.Manage`): **`BillingActivationSource.Manual`** ile `ISubscriptionCheckoutActivationService.TryActivateAsync`; read-only tenant için guard **muafiyeti** **§24.4**.

#### Read-only tenant

- Yazma çoğu mutation’da **§23** ile engellenir; **checkout komutları ve webhook işleme** kontrollü muafiyet ile çalışır (**§24.4**). Özet okuma (`subscription-summary`) read-only kiracıda da **açıktır** (**§23.1**).

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
