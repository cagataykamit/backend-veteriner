# Size özel geliştirme talepleri API sözleşmesi — TIER-010

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-010` ("Paket: Kurumsal. Vetinity içinde kalan ekran, rapor ve çıktı düzenlemeleri; uç istekler ve ürün dışı işler değil"), ADR-035 (2026-11-06 notu), ADR-040 (kiracı izolasyonu, platform yönetimi). İlgili: `PLAN_FEATURES_API_CONTRACT.md` (paket kuralı ve 403), `CLINIC_SITE_API_CONTRACT.md` §4 (platform yöneticisi ucu deseni), `PORTAL_API_CONTRACT.md` (KVKK veri talebi, açık talep sınırı ve maskeleme deseni).

> **Durum:** Uygulandı (kod + test), dal `feat/tier-010-ozel-gelistirme`. Migration `AddCustomDevelopmentRequests` (yalnız yerel DB). **Yeni izin** `CustomDevelopmentRequests.Manage`. Yeni paket özelliği `CustomDevelopment` (yalnız Kurumsal). §7'deki maddeler **çalışma varsayımıdır, kullanıcı onayı bekliyor.** E-posta bildirimi yok.

---

## 1) Kavram

Kurumsal kiracı panelden "özel geliştirme talebi" açar; talebi yalnız Vetinity'nin platform yöneticisi inceler ve durumunu değiştirir. Kapsam: Vetinity içinde kalan ekran, rapor ve çıktı (yazdırma, PDF, dışa aktarma) düzenlemeleri. Uç istekler ve ürün dışı işler "Kapsam dışı" kararıyla, gerekçesi kliniğe görünerek kapanır.

| Alan | Kural |
|---|---|
| `requestTitle` | Başlık, kırpılmış 1–120 karakter |
| `requestText` | Ne istendiği, kırpılmış 1–2000 karakter |
| `targetKind` | `Screen` \| `Report` \| `Output` (zorunlu) |
| `requestTarget` | Hangi ekran, rapor ya da çıktı (ör. "Muayene çıktısı"), kırpılmış 1–120 karakter |
| `priority` | `Low` \| `Normal` \| `High` (zorunlu) |

Metinler kişisel ya da hasta verisi için değildir; panel bunu formda uyarır, denetim kaydında maskelenir (§5).

## 2) Durum akışı

```
Received (Alındı) → InReview (İnceleniyor) → Planned (Planlandı) → Completed (Tamamlandı)
                                           ↘ OutOfScope (Kapsam dışı)
```

| Geçiş | Kural |
|---|---|
| `Received → InReview` | |
| `InReview → Planned` | |
| `InReview → OutOfScope` | `decisionReason` zorunlu (kırpılmış 1–1000), kliniğe görünür. Başka geçişte gerekçe gönderilirse `CustomDevelopment.Validation` |
| `Planned → Completed` | |
| Diğer her şey | 409 `CustomDevelopment.InvalidStatusTransition`; kapanmış talep (`OutOfScope`, `Completed`) değişmez |

`responseNote` her geçişte isteğe bağlıdır (en çok 1000), kliniğe görünür; boş gelirse önceki not kalır. Kural tek yerde: `CustomDevelopmentRequest.NextStatuses` ve `ChangeStatus`.

**Açık talep** = `Received`, `InReview`, `Planned` (`CustomDevelopmentRequest.OpenStatuses`).

## 3) Kiracı uçları — `api/v1/custom-development-requests`

İzin `CustomDevelopmentRequests.Manage`; kiracı bağlamı zorunlu. Talepler kiracı düzeyindedir (klinik bağlamı yok).

| Uç | Paket | Yanıt |
|---|---|---|
| `GET /` | Her paket (paket düşünce talepler görünür kalır) | 200 `CustomDevelopmentRequestListDto` |
| `POST /` | Kurumsal | 200 `CustomDevelopmentRequestDto` |

### 3.1 `GET /` → `CustomDevelopmentRequestListDto`

```json
{ "items": [ { "id": "…", "requestTitle": "Gün sonu raporuna sütun", "requestText": "…", "targetKind": "Report",
    "requestTarget": "Gün sonu raporu", "priority": "High", "status": "OutOfScope", "isOpen": false,
    "createdByUserId": "…", "createdByName": "Selin", "createdAtUtc": "…", "statusChangedAtUtc": "…",
    "decisionReason": "Muhasebe programına ait iş", "responseNote": "İnceledik" } ],
  "openCount": 2, "maxOpen": 3 }
```

Sıra: açılış zamanı azalan. Yalnız isteği yapan kiracının talepleri (ADR-040 filtresi + kiracı koşulu). `createdByName` `DisplayName`, yoksa e-postadan türetilir.

### 3.2 `POST /` → 200 `CustomDevelopmentRequestDto`

```json
{ "requestTitle": "Gün sonu raporuna sütun", "requestText": "Hekim adı sütunu eklensin.", "targetKind": "Report",
  "requestTarget": "Gün sonu raporu", "priority": "High" }
```

| Hata | Statü | Koşul |
|---|---|---|
| `Subscriptions.FeatureNotInPlan` | 403 | Pakette `CustomDevelopment` yok; `requiredPlanCode: "Premium"` (`PLAN_FEATURES_API_CONTRACT.md` §2). Denemede seçilen pakete göre |
| `CustomDevelopment.TooManyOpen` | 409 | Kiracının açık talep sayısı `MaxOpenPerTenant` (3). Mesajda sınır yazar; talep tamamlanınca ya da kapsam dışı kapanınca yer açılır |
| `CustomDevelopment.Validation` | 400 | Metin kuralları (§1) |
| FluentValidation | 400 | `targetKind` ya da `priority` yok / geçersiz |
| `Subscriptions.TenantReadOnly` | 403 | Salt okunur abonelik (genel yazma kapısı) |

Sıra: `targetKind`/`priority` doğrulaması → paket → açık talep sayısı → metin kuralları.

## 4) Platform yöneticisi uçları — `api/v1/admin/custom-development-requests`

Politika `PlatformAdmin` (JWT'de `platform_admin = true`), `[PlatformTenantScope]` ile kiracılar arası; izin tabanlı değildir. Kiracı rolleri (Admin/Owner dahil) 403, kimliksiz 401. Denetleyici mimari testte izinli sistem kapsamı listesindedir.

| Uç | Yanıt |
|---|---|
| `GET /?status=&openOnly=false&page=1&pageSize=50` | 200 `PagedResult<AdminCustomDevelopmentRequestDto>`; `status` tek durum, `openOnly=true` yalnız açıklar; `pageSize` en çok 200; sıra açılış zamanı azalan |
| `POST /{id}/status` | 200 `AdminCustomDevelopmentRequestDto` |

`AdminCustomDevelopmentRequestDto`: `{ "tenantId": "…", "tenantName": "…", "request": CustomDevelopmentRequestDto, "nextStatuses": ["Planned", "OutOfScope"] }`. `nextStatuses` geçerli sonraki durumlardır (`CustomDevelopmentRequest.NextStatuses`, §2 tablosunun tek yeri); kapanmış talepte boş. Panel akışı kopyalamaz, düğmeleri buradan çizer.

`POST /{id}/status` gövdesi: `{ "status": "OutOfScope", "decisionReason": "…", "responseNote": "…" }`. Hatalar: `CustomDevelopment.NotFound` (404), `CustomDevelopment.InvalidStatusTransition` (409), `CustomDevelopment.Validation` (400), `CustomDevelopment.ConcurrencyConflict` (409; `MutationSequence` belirteci). Destek işlemidir: kiracının abonelik yazma kapısına takılmaz (`IIgnoreTenantWriteSubscriptionGuard`).

## 5) Denetim ve KVKK

Yazma komutları `IAuditableRequest`; başarılı ve başarısız her çağrı `AuditLogs`'a yazılır.

| `Action` | `TargetId` |
|---|---|
| `CustomDevelopmentRequest.Create` | `TargetKind=…;Priority=…` |
| `CustomDevelopmentRequest.StatusChange` | `CustomDevelopmentRequestId=…;Status=…` (hedef durum) |

İstek gövdesinde `requestTitle`, `requestText`, `requestTarget`, `decisionReason`, `responseNote` `***` olarak saklanır (`AuditBehavior.SensitivePayloadKeys`); denetimde yalnız tür, öncelik ve durum kalır. Kaydın kendisi son durum değiştireni ve zamanı taşır (`statusChangedByUserId` yanıtta yok).

## 6) Veri modeli (migration `AddCustomDevelopmentRequests`)

Tablo `CustomDevelopmentRequests`: `Id`, `TenantId`, `RequestTitle nvarchar(120)`, `RequestText nvarchar(2000)`, `TargetKind int`, `RequestTarget nvarchar(120)`, `Priority int`, `Status int`, `CreatedByUserId`, `CreatedAtUtc`, `DecisionReason nvarchar(1000)`, `ResponseNote nvarchar(1000)`, `StatusChangedAtUtc`, `StatusChangedByUserId`, `MutationSequence bigint` (concurrency). `CHECK`: `Status = OutOfScope` ise `DecisionReason` dolu. İndeksler: `(TenantId, Status)`, `(Status, CreatedAtUtc)`. Kiracı filtresi `ITenantOwned` ile. Mevcut tablolara dokunulmaz; paylaşılan/üretim DB'ye uygulanması kullanıcı onayı ister.

## 7) Yetkiler ve çalışma varsayımları (kullanıcı onayı bekliyor)

| İzin | Varsayılan roller |
|---|---|
| `CustomDevelopmentRequests.Manage` (liste ve yeni talep) | Admin, Owner |

İzin `RolePermissionBindings` ile seed'de idempotent eklenir; **mevcut özel roller elle almalı**. DbMigrator varsayılan yönetici oluşturmaz, `PlatformAdmin` rolünü kimseye vermez (değişmedi).

1. Paket yalnız Kurumsal; liste her pakette açık, yeni talep kapalı (TIER-001 B ile aynı).
2. Sınır: aynı anda en çok **3 açık talep** (dönem/ay sayacı değil). Tek yer `CustomDevelopmentRequest.MaxOpenPerTenant`.
3. Yalnız Admin ve Owner talep açar ve görür (ticari ilişki; ClinicAdmin, Sekreter, Veteriner almaz).
4. Kiracı talebini geri çekemez, düzenleyemez; durum yalnız platform yöneticisinde. Yanlış talep "Kapsam dışı" ile kapanır.
5. Gerekçe ve not kliniğe görünür; platforma özel iç not yok.
6. Durum akışı katıdır (İnceleniyor atlanamaz, Planlandı'dan Kapsam dışı'na dönülmez).

## 8) Bilinen sınırlar

- Sınır kontrolü kilitsizdir: aynı anda gelen iki istek dördüncü açık talebi açabilir (kullanıcı sınırı ve API anahtarı sınırıyla aynı).
- Yeni talep ve durum değişikliği için e-posta bildirimi yok; platform yöneticisi listeden izler.
- Talebe ek dosya, yorum zinciri ve tahmini tarih alanı yok.

## 9) Testler

- Domain: `CustomDevelopmentRequestTests` (kırpma, zorunlu alanlar, uzunluk ve enum, durum akışı tablosu, kapsam dışında gerekçe, gerekçenin yalnız kapsam dışında yazılması, notun korunması).
- Application: `CustomDevelopmentRequestHandlerTests` (Temel ve Profesyonel'de `FeatureNotInPlan`, sınırda `TooManyOpen`, kiracı ve kullanıcıya bağlı kayıt), `CustomDevelopmentRequestsPermissionBindingsTests`, `TenantPlanFeatureEvaluatorTests`, `AuditBehaviorTests` (maskeleme), `TenantIsolationArchitectureTests`.
- Entegrasyon: `CustomDevelopmentRequestIntegrationTests` (kiracılar arası görünmezlik; sınır aşımı ve kapanışla yer açılması; Temel/Profesyonel 403 ve açık liste; paket düşünce görünen talep; izin ve doğrulama; PlatformAdmin dışı yönetim ucu 401/403; tüm kiracılar listesi, durum ve `openOnly` filtresi; akış dışı geçiş 409, gerekçesiz kapsam dışı 400; kliniğe görünen gerekçe ve not; maskeli denetim kaydı).

## 10) Frontend için

1. Kiracı: Ayarlar altında "Size özel geliştirmeler" sayfası; `GET /`. `access-state.planFeatures[CustomDevelopment].isEnabled = false` ise form yerine "üst pakete geç" kutusu, mevcut talepler listelenir.
2. Form: başlık, konu (ekran/rapor/çıktı), hangi ekran/rapor/çıktı, ne istendiği, öncelik; kişisel ya da hasta verisi yazılmaması uyarısı. `openCount >= maxOpen` ise form kapalı ve sınır açıklaması; 409 `CustomDevelopment.TooManyOpen` gelirse liste yenilenir.
3. Liste: durum etiketi, kapsam dışında gerekçe, varsa Vetinity notu.
4. Platform yöneticisi: menüde "Klinik adresleri" ile aynı yetkide (`platform_admin`) ayrı sayfa; durum ve "yalnız açıklar" filtresi, satırda kiracı adı; geçerli sonraki durumlar, kapsam dışında gerekçe zorunlu, not alanı.
