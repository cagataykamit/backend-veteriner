# Backend Contract Audit Matrix

## 1) Amaç

Bu doküman, backend modüllerinin contract olgunluğunu sistematik olarak görünür kılmak için hazırlanmıştır.

Görünür kılınan temel problem:
- Backend davranışı ile frontend beklentisi arasında oluşabilecek contract drift.

Neden önemlidir:
- Endpoint, DTO, enum, validation ve hata sözleşmelerindeki tutarsızlıklar frontend tarafında kırılma, yanlış parse ve tahmine dayalı geliştirme maliyeti üretir.
- Refactor önceliğini veri temelli belirlemek için modül bazlı risk matrisi gerekir.

---

## 2) Kapsam

İncelenen modüller:
- Auth
- Clinics
- Clients
- Pets
- Appointments
- Examinations
- Vaccinations
- Payments
- Dashboard
- Species
- Breeds

Denetim başlıkları:
- create / list / detail / update kapsaması
- route/body id standardı
- validation sözleşmesi
- ProblemDetails / error code sözleşmesi
- enum sözleşmesi
- tenant/clinic context ownership
- Swagger/OpenAPI doğruluğu

---

## 3) Modül Bazlı Denetim

### Auth
- **Mevcut durum:** Login/refresh/select-clinic/logout/logout-all uçları mevcut. CRUD kalıbı yerine akış bazlı tasarım var.
- **Sorun/risk:** `Result` hattı + doğrudan `Problem(...)` + ad-hoc `Ok(new { ok = true })` birlikte kullanılıyor; error/success envelope tutarsız.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Api/Controllers/AuthController.cs`, `src/Backend.Veteriner.Api/Common/Extensions/ResultExtensions.cs`
- **Öncelik:** P0
- **Frontend etkisi:** Yüksek (hata ve başarı parse mantığı dallanıyor).

### Clinics
- **Mevcut durum:** Create/list/detail mevcut, update endpoint yok.
- **Sorun/risk:** Create response yalnız `Guid`; modüller arası response standardı farklı.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Api/Controllers/ClinicsController.cs`
- **Öncelik:** P2
- **Frontend etkisi:** Düşük-Orta.

### Clients
- **Mevcut durum:** Create/list/detail mevcut, update endpoint yok.
- **Sorun/risk:** Kritik risk düşük; create response DTO bazlı ve temiz.
- **Değişmesi gereken yerler:** Kritik zorunlu değişiklik yok; referans modül olarak korunmalı.
- **Öncelik:** P3
- **Frontend etkisi:** Düşük.

### Pets
- **Mevcut durum:** Create/list/detail/update mevcut; route id source-of-truth uygulanmış.
- **Sorun/risk:** Update body `id` zorunluluk algısı ile route-id standardı arasında OpenAPI/codegen gerilimi oluşabiliyor.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Api/Controllers/PetsController.cs`, `src/Backend.Veteriner.Application/Pets/Commands/Update/UpdatePetCommand.cs`
- **Öncelik:** P1
- **Frontend etkisi:** Orta.

### Appointments
- **Mevcut durum:** Create/list/detail/update + cancel/complete/reschedule mevcut.
- **Sorun/risk:** Bazı akışlarda doğrudan `Problem(...)` dalları nedeniyle hata sözleşmesi tek tip değil.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Api/Controllers/AppointmentsController.cs`, `src/Backend.Veteriner.Application/Appointments/*`
- **Öncelik:** P1
- **Frontend etkisi:** Orta.

### Examinations
- **Mevcut durum:** Create/list/detail/update mevcut; route/body mismatch standardı uygulanıyor.
- **Sorun/risk:** Legacy alias (`complaint`) hala aktif; canonical alan standardı zayıflıyor.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Api/Controllers/ExaminationsBodies.cs`, `src/Backend.Veteriner.Api/Controllers/ExaminationsController.cs`, ilgili validator dosyaları
- **Öncelik:** P0
- **Frontend etkisi:** Yüksek.

### Vaccinations
- **Mevcut durum:** Create/list/detail mevcut; update endpoint yok.
- **Sorun/risk:** Ürün update beklentisi varsa kapsama açığı; required/nullability yansıması sınırlı.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Api/Controllers/VaccinationsController.cs`, `src/Backend.Veteriner.Application/Vaccinations/Commands/Create/*`
- **Öncelik:** P1
- **Frontend etkisi:** Orta.

### Payments
- **Mevcut durum:** Create/list/detail mevcut; update endpoint yok.
- **Sorun/risk:** OpenAPI required/nullability drift (özellikle kritik alanlarda) type-generation ve form validasyonunu riske atıyor.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Application/Payments/Commands/Create/CreatePaymentCommand.cs`, `.../CreatePaymentCommandValidator.cs`, `src/Backend.Veteriner.Api/Controllers/PaymentsController.cs`
- **Öncelik:** P0
- **Frontend etkisi:** Yüksek.

### Dashboard
- **Mevcut durum:** Read-only summary endpoint mevcut.
- **Sorun/risk:** Aynı response içinde clinic-scoped operasyon metrikleri ile tenant-wide toplamların birlikte sunulması yanlış yorumlanabilir.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Application/Dashboard/Queries/GetSummary/GetDashboardSummaryQueryHandler.cs`, `.../Contracts/Dtos/DashboardSummaryDto.cs`
- **Öncelik:** P2
- **Frontend etkisi:** Düşük-Orta.

### Species
- **Mevcut durum:** Create/list/detail/update mevcut; route/body mismatch standardı var.
- **Sorun/risk:** Query doğrulama tutarlılığı modüller arası farklı.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Application/Species/Queries/*`
- **Öncelik:** P2
- **Frontend etkisi:** Düşük-Orta.

### Breeds
- **Mevcut durum:** Create/list/detail/update mevcut; route/body mismatch standardı var.
- **Sorun/risk:** Query doğrulama tutarlılığı modüller arası farklı.
- **Değişmesi gereken yerler:** `src/Backend.Veteriner.Application/Breeds/Queries/*`
- **Öncelik:** P2
- **Frontend etkisi:** Düşük-Orta.

---

## 4) Modül Matrisi

| Modül | Create | List | Detail | Update | Contract riski | Context riski | Enum riski | Swagger durumu | Öncelik | Frontend etkisi |
|---|---|---|---|---|---|---|---|---|---|---|
| Auth | Var | N.A. | N.A. | N.A. | Yüksek | Orta | Düşük | Orta-Zayıf | P0 | Yüksek |
| Clinics | Var | Var | Var | Yok | Düşük-Orta | Düşük | Düşük | Orta | P2 | Düşük |
| Clients | Var | Var | Var | Yok | Düşük | Düşük | Düşük | Orta | P3 | Düşük |
| Pets | Var | Var | Var | Var | Orta | Düşük | Düşük | Orta | P1 | Orta |
| Appointments | Var | Var | Var | Var | Orta | Düşük | Düşük-Orta | Orta | P1 | Orta |
| Examinations | Var | Var | Var | Var | Yüksek | Düşük | Düşük | Orta-Zayıf | P0 | Yüksek |
| Vaccinations | Var | Var | Var | Yok | Orta | Düşük | Orta | Orta-Zayıf | P1 | Orta |
| Payments | Var | Var | Var | Yok | Yüksek | Düşük | Orta | Zayıf | P0 | Yüksek |
| Dashboard | N.A. | Summary | N.A. | N.A. | Orta | Orta | Düşük | Orta | P2 | Düşük-Orta |
| Species | Var | Var | Var | Var | Düşük-Orta | Düşük | Düşük | Orta | P2 | Düşük-Orta |
| Breeds | Var | Var | Var | Var | Düşük-Orta | Düşük | Düşük | Orta | P2 | Düşük-Orta |

---

## 5) Kritik Bulgular

1. **Eksik update endpointler**
- Clinics, Clients, Vaccinations, Payments (ürün kuralı ile netleştirilmeli).

2. **Alias/legacy alanlar**
- Examinations modülünde `complaint` alias’ı aktif.

3. **Generic/ad-hoc response’lar**
- Özellikle Auth ve bazı yardımcı controller akışlarında typed/standard response dışına çıkılıyor.

4. **Enum binding riski**
- Enum sözleşmesi numeric; tüketici string beklerse kırılma riski oluşuyor.

5. **Context ownership karışıklığı**
- Context-first hedefi mevcut; ancak modüller arası body zorunluluğu algısı tam tekilleşmiş değil.

6. **Frontend’in yanlış tahminine açık belirsizlikler**
- OpenAPI required/nullability drift.
- ProblemDetails alanlarının kaynak/path’e göre değişmesi.

---

## 6) Refactor Önceliği

### P0
- Auth error/success contract tekilleştirme.
- Examinations alias (`complaint`) deprecate ve kaldırma planı.
- Payments OpenAPI required/nullability doğruluğu.
- Cross-cutting ProblemDetails envelope standardizasyonu.

### P1
- Appointments/Vaccinations/Payments/Examinations hata ve validation sözleşme tutarlılığı.
- Query validator standardizasyonu (enum/Guid edge-case predictability).
- Ürün beklentisine göre eksik update endpoint kararları.

### P2
- Swagger/OpenAPI cleanup ve dokümantasyon güçlendirme.
- Create response DTO standardının modüller arası hizalanması.
- Dashboard scope semantiğinin daha açık kontrata bağlanması.

### P3
- Düşük riskli naming/dokümantasyon/estetik standardizasyonu.

---

## 7) En Kritik 10 Problem

1. Auth’ta tek tip olmayan error ve success response sözleşmesi.
2. Examinations’ta `complaint` alias’ının devam etmesi.
3. Payments’ta required/nullability OpenAPI drift’i.
4. Cross-cutting ProblemDetails alanlarının tutarsızlığı (`code`, `traceId`, `correlationId`).
5. Rate-limit hata payload’ının diğer hata sözleşmelerinden farklı olması.
6. Bazı modüllerde update endpoint eksikliği (ürün kararına bağlı risk).
7. Route/body id standardının OpenAPI’da tam net yansımaması.
8. Query validator davranışının modüller arası tam uniform olmaması.
9. Enum sözleşmesinin tüketici ekipte yanlış varsayılma riski (string vs numeric).
10. Create response formatının modüller arası farklı olması.

---

## 8) En Güvenli Refactor Sırası

1. Cross-cutting error envelope standardizasyonu.
2. Auth response/error contract cleanup.
3. Examinations alias deprecate -> remove planı.
4. Payments required/nullability + OpenAPI doğruluk düzeltmeleri.
5. Eksik update endpointlerin ürün kararı ile netleştirilmesi.
6. Swagger/OpenAPI hardening ve modül modül type-generation readiness geçişi.

---

## 9) Build/Test Notu

Bu doküman audit-only teknik rapordur; kod değişikliği içermez.

Refactor aşamasında her değişiklik paketi için zorunlu doğrulama:
- `dotnet build`
- ilgili modül testleri
- sözleşme etkisi olan endpointler için smoke test doğrulaması.

