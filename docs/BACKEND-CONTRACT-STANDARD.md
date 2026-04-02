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

---

## 4) Modül Bazlı Mevcut Durum Özeti

| Modül | Mevcut durum | Riskli alanlar | Gerekli standardizasyon | Öncelik | Frontend etkisi |
|---|---|---|---|---|---|
| Auth | `Result` + `ToActionResult` + açık DTO (`LoginResultDto`, `AuthActionResultDto`); logout akışı da aynı hatta | Eski istemciler `ProblemDetails.title` metninde değişiklik görebilir (`ResultExtensions`) | Faz 0: auth endpoint’leri tek sözleşmeye alındı; drift için bu doküman §9 | Tamamlandı (Faz 0) | Orta (title gösterimi) |
| Clinics | Genel olarak tutarlı | Create response yalnız `Guid` | Create response DTO standardı | P2 | Düşük |
| Clients | En tutarlı modüllerden | Düşük | Mevcut standardı referans modül olarak koruma | P2 | Düşük |
| Pets | Route/body id standardı güçlü | Create response çıplak `Guid` | Create response standardizasyonu | P2 | Düşük |
| Appointments | Update/lifecycle akışları güçlü | Bazı hata dallarında envelope farklılaşma riski | Error contract tekilleştirme | P1 | Orta |
| Examinations | Genel akış stabil | Legacy alias (`complaint`) | Canonical `visitReason` + alias deprecate | P0 | Yüksek |
| Vaccinations | Clinic context entegrasyonu var | `clinicId` ownership algısı modüller arası tutarsız | Context-first kuralını açık ve tek hale getirme | P1 | Orta |
| Payments | Clinic context entegrasyonu var | Required/nullability ve clinic ownership netliği | OpenAPI doğruluğu + context-first netliği | P0 | Yüksek |
| Dashboard | Contract açık | Dokümantasyon drift riski | Contract metinleri ve OpenAPI doğruluğunu koruma | P2 | Düşük |
| Species | Update contract tutarlı | Düşük | Tutarlı dokümantasyon ve naming temizliği | P3 | Düşük |
| Breeds | Update contract tutarlı | Düşük | Tutarlı dokümantasyon ve naming temizliği | P3 | Düşük |

---

## 5) Öncelikli Refactor Backlog

### P0 (hemen)
- **Auth:** ~~Controller dönüşlerini tek `Result/ProblemDetails` hattına çekmek.~~ (Faz 0 / Adım 1 tamamlandı; ayrıntı §9.)
- **Examinations:** `complaint` alias’ı için deprecate planı ve kaldırma takvimi oluşturmak.
- **Payments:** OpenAPI required/nullability sözleşmesini gerçek validation ile birebir eşitlemek.
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
- Clients
- Pets
- Species
- Breeds
- Dashboard

### Kısmi Hazır
- Clinics
- Appointments
- Vaccinations

### Önce Contract Temizliği Gerekli
- ~~Auth~~ (Faz 0: login/refresh/select-clinic/logout hattı standardize; §9)
- Examinations
- Payments

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
- Examinations alias deprecate planı,
- Payments OpenAPI doğruluğu,
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
