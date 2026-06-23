# IDOR / Clinic Isolation Regression

Kalıcı regression komut seti — IDOR-9 Final Acceptance Audit (2026-06) sonrası. Read-side ve write-side clinic isolation roadmap kapatıldı; bu belge release öncesi ve PR sonrası çalıştırılacak test filtrelerini tanımlar.

Merkezi erişim bileşenleri: `ClinicReadScopeResolver`, `ClinicAssignmentAccessGuard`, `TenantWideClaimNames` whitelist.

---

## Current status

| Alan | Durum |
|------|--------|
| Read-side | **closed** |
| Write-side | **closed** |
| Production P0/P1 | **none** |
| Remaining | P2/P3 test-hardening backlog |

**Tenant-wide by design** (klinik izolasyonundan muaf): Products, ProductCategories, Clients, Pets. Tenant-wide roller: Admin, Owner, PlatformAdmin (`TenantWideClaimNames`).

---

## Quick IDOR Smoke

En hızlı set — PR feedback ve erken regresyon (~1 dk). Unit resolver/guard + üç integration sınıfı.

```bash
dotnet test tests/Backend.Veteriner.Application.Tests --no-restore --filter "FullyQualifiedName~ClinicReadScopeResolverTests|FullyQualifiedName~ClinicAssignmentAccessGuardTests"

dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~AppointmentDetailIdorIntegrationTests|FullyQualifiedName~AppointmentWriteClinicAssignmentIdorIntegrationTests|FullyQualifiedName~DashboardScopeIdorIntegrationTests"
```

Audit doğrulaması: integration smoke **21 passed / 0 failed** (~33 sn).

---

## Full IDOR Regression

Tüm detail + write + list/scope yüzeyleri. Nightly veya release öncesi integration güvencesi.

```bash
dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~AppointmentDetailIdorIntegrationTests|FullyQualifiedName~ExaminationDetailIdorIntegrationTests|FullyQualifiedName~TreatmentDetailIdorIntegrationTests|FullyQualifiedName~PrescriptionDetailIdorIntegrationTests|FullyQualifiedName~VaccinationDetailIdorIntegrationTests|FullyQualifiedName~HospitalizationDetailIdorIntegrationTests|FullyQualifiedName~LabResultDetailIdorIntegrationTests|FullyQualifiedName~PaymentDetailIdorIntegrationTests"

dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~AppointmentWriteClinicAssignmentIdorIntegrationTests|FullyQualifiedName~ExaminationWriteClinicAssignmentIdorIntegrationTests|FullyQualifiedName~TreatmentWriteClinicAssignmentIdorIntegrationTests|FullyQualifiedName~PrescriptionWriteClinicAssignmentIdorIntegrationTests|FullyQualifiedName~VaccinationWriteClinicAssignmentIdorIntegrationTests|FullyQualifiedName~HospitalizationWriteClinicAssignmentIdorIntegrationTests|FullyQualifiedName~LabResultWriteClinicAssignmentIdorIntegrationTests|FullyQualifiedName~PaymentWriteClinicAssignmentIdorIntegrationTests"

dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~AppointmentListCalendarIdorIntegrationTests|FullyQualifiedName~DashboardScopeIdorIntegrationTests|FullyQualifiedName~ClientPetSummaryScopeIdorIntegrationTests|FullyQualifiedName~ReportsEndpointTests|FullyQualifiedName~RemindersEndpointTests|FullyQualifiedName~ProductStocksEndpointTests|FullyQualifiedName~StockMovementsEndpointTests"
```

**Not:** `ClinicsGetByIdIdorIntegrationTests` read-detail kapsamını tamamlar; Full Regression koşumlarına eklenmesi önerilir:

```bash
dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~ClinicsGetByIdIdorIntegrationTests"
```

---

## Release Safety Set

Unit seviye güvenlik filtreleri + payment report/export routing + build. Integration güvencesi içermez — **Full IDOR Regression ile birlikte** koşulmalı.

```bash
dotnet test tests/Backend.Veteriner.Application.Tests --no-restore --filter "FullyQualifiedName~ClinicReadScopeResolverTests|FullyQualifiedName~ClinicAssignmentAccessGuardTests|FullyQualifiedName~WriteClinicAssignmentCommandHandlerTests"

dotnet test tests/Backend.Veteriner.Application.Tests --no-restore --filter "FullyQualifiedName~GetPaymentsReportQueryHandlerTests|FullyQualifiedName~PaymentsReportExportScopeGuardTests|FullyQualifiedName~PaymentsReportReadRoutingTests|FullyQualifiedName~PaymentsReportExportReadRoutingTests"

dotnet build --no-restore
```

Audit doğrulaması: birinci filtre **148 passed / 0 failed**; ikinci filtre **67 passed / 0 failed**. `WriteClinicAssignmentCommandHandlerTests` substring'i 8 handler test sınıfını (Appointment, Examination, Treatment, Prescription, Vaccination, Hospitalization, LabResult, Payment) yakalar.

---

## CI Recommendation

Repoda henüz `.github/workflows` tanımı yok. CI eklendiğinde aşağıdaki ayrım önerilir.

### PR gate (her push)

- **Quick IDOR Smoke** (yukarıdaki Seviye 1 komutları)
- Mevcut standart `Backend.Veteriner.Application.Tests` tam suite

### Nightly / release gate

- **Full IDOR Regression** (yukarıdaki integration filtreleri)
- **Release Safety Set** (unit filtreleri + `dotnet build --no-restore`)
- Tam test suite (`Application.Tests` + `IntegrationTests`)

### Paralel integration test / race riski

IDOR integration testleri `[Collection("pilot-smoke-api")]` altında serialize edilir — paylaşılan DB/fixture üzerinde paralel koşum race'ini engeller. Yeni IDOR integration testlerinde bu deseni koruyun.

CI'da integration projesi için örnek:

```bash
dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore -- xUnit.parallelizeAssembly=false
```

Seed helper'ları tenant/klinik üretirken global state paylaşıyorsa cross-test sızıntısını önlemek için tek worker veya `parallelizeAssembly=false` kullanın. Yeni integration test sınıfı eklerken farklı collection'a geçmeden önce `CustomWebApplicationFactory`/DB çakışmasını değerlendirin; şüphede `pilot-smoke-api` collection'ında kalın.

---

## New endpoint checklist

### Read endpoint checklist

- [ ] Tenant-scoped veri mi döndürüyor? Evetse `ClinicReadScopeResolver.ResolveAsync` ile scope çöz.
- [ ] `requestClinicId` parametresi resolver'a iletiliyor mu (atanmamış klinik → `Clinics.AccessDenied`)?
- [ ] `clinicId` verilmediğinde tenant-wide olmayan kullanıcı için sonuç **erişilebilir klinik kümesine** daraltılıyor mu?
- [ ] Foreign-tenant kaydı için `NotFound` dönüyor mu (varlık sızıntısı yok)?
- [ ] Tenant-wide catalog/registry (Products/ProductCategories/Clients/Pets) ise bilinçli muafiyet, dokümante edilmiş mi?

### Write endpoint checklist

- [ ] İlgili `*ClinicWriteScope.EnsureWriteAccessAsync` çağrılıyor mu?
- [ ] Klinik **değiştiren** (move/transfer) bir komutsa `EnsureEntityAndTargetWriteAccessAsync` ile hem kaynak hem hedef klinik doğrulanıyor mu?
- [ ] Erişim reddinde mutasyon **gerçekleşmiyor** mu (önce kontrol, sonra mutate)?
- [ ] Tenant-wide kullanıcı (Admin/Owner/PlatformAdmin) için doğru şekilde geçiş sağlanıyor mu?
- [ ] Permission (`PermissionCatalog`) kontrolü ayrıca var mı (403 izin senaryosu)?

### Test checklist

- [ ] Atanmamış klinikte yazma → `403 Clinics.AccessDenied` + **mutasyon yok** (DB snapshot assert).
- [ ] Atanmış klinikte → `201/204`.
- [ ] Tenant admin başka klinikte → başarılı (`LoginAsync` ile gerçek akış).
- [ ] Foreign tenant kaydı → `404 *.NotFound`.
- [ ] İzin yok → `403`.
- [ ] Read için: atanmamış klinik detay → erişim reddi; list/calendar → yalnız atanmış klinikler.
- [ ] `IssueUserAccessTokenAsync` (synthetic JWT) + scoped seed helper deseni kullanılıyor mu?
- [ ] Sınıf `[Collection("pilot-smoke-api")]` altında mı?

### CQRS / read-model fallback checklist

- [ ] Read-model (projection) yolunda da clinic scope uygulanıyor mu, yoksa sadece write-model'de mi?
- [ ] Read routing flag kapalıyken (fallback to write-model) ve açıkken (read-model) **aynı** scope sonucu üretiliyor mu? (`PaymentsReportReadRoutingTests` / `PaymentsReportExportReadRoutingTests` deseni)
- [ ] Export yolunda scope guard var mı? (`PaymentsReportExportScopeGuardTests` deseni)
- [ ] Yeni read-model reader eklerken scope filtresi reader seviyesinde de zorlanıyor mu (sadece query handler'da değil)?

---

## P2/P3 backlog (non-blocking)

- **(P2)** Bazı pull/list senaryolarında dedicated integration test eksikliği.
- **(P3)** Gelecek payment lifecycle / delete command'ları için scope enforcement + IDOR test checklist.
- **(P3)** Stock write için dedicated `*WriteClinicAssignmentIdorIntegrationTests` yok (güvenli pattern kabul edildi).
- **(P3)** `WriteClinicAssignmentCommandHandlerTests` substring filtresi yeni handler sınıflarıyla genişleyebilir — bilinçli olun.
