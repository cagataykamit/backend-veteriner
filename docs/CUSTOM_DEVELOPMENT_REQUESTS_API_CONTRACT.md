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

## 11) Geçiş desteği ve kurulum görüşmesi — TIER-021

Kaynak: ürün deposu backlog `TIER-021`, ADR-035 (2026-11-09 notu: geçiş desteği Kurumsal'da ücretsiz, diğer paketlerde ücretli; kurulum görüşmesi 1 saat), ADR-014 K8 ve ADR-019 (içe aktarma kapsamı ve 30 gün saklama). İçe aktarmadaki hazır eşleme profilleri: `IMPORT_API_CONTRACT.md` §4.5.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-021-veri-aktarimi`. Migration `AddDataMigrationSupport` (yalnız yerel DB). Yeni izin yok (`CustomDevelopmentRequests.Manage`). Yeni paket özelliği `FreeMigrationSupport` (yalnız Kurumsal; yalnız ücreti belirler). §11.6 maddeleri **çalışma varsayımıdır**. E-posta bildirimi yok.

### 11.1 Neden aynı kayıt

Geçiş desteği talebi ayrı tablo değil, bu sözleşmedeki talebin yeni konusudur: `targetKind = "Migration"`. Durum akışı (§2), platform yöneticisi listesi ve durum ucu (§4), denetim maskelemesi (§5) ve eşzamanlılık aynen kullanılır; ikinci bir yönetim ekranı ve ikinci bir durum akışı yoktur. Farklar: her pakette açılır, özel geliştirmenin 3'lük sınırına ve listesine (`GET /`) karışmaz, kendi uçları ve sınırı vardır. `POST /` ile `targetKind = "Migration"` gönderilemez (400 `Validation.FluentValidation`).

Durumların anlamı: Alındı → İnceleniyor → **Planlandı** (görüşme günü ve saati `responseNote` ile kliniğe yazılır) → Tamamlandı; uygun değilse Kapsam dışı (gerekçe zorunlu).

### 11.2 Alanlar

| Alan | Kural |
|---|---|
| `sourceProgram` | `Kolayvet` \| `EVet` \| `Generic` (Genel Excel / CSV ya da listede olmayan program; zorunlu). Liste `IMPORT_API_CONTRACT.md` §4.5 ile aynı enum |
| `otherProgramName` | İsteğe bağlı, yalnız `Generic` iken kullanılır; kırpılmış en çok 120 karakter. Boşsa "Genel Excel / CSV" yazılır |
| `recordVolume` | Yaklaşık kayıt (müşteri + hasta) sayısı: `Unknown` \| `UpTo1000` \| `UpTo5000` \| `UpTo10000` \| `Over10000` (zorunlu) |
| `meetingDays` | Kurulum görüşmesi için uygun günler, bayrak listesi `"Monday, Thursday"` (`Monday` … `Sunday`); en az biri |
| `meetingTimes` | Uygun saat aralıkları (İstanbul saati), bayrak listesi: `Morning` (09.00–12.00), `Midday` (12.00–15.00), `Afternoon` (15.00–18.00); en az biri |
| `note` | İsteğe bağlı not, kırpılmış en çok 2000 karakter. Kişisel ya da hasta verisi için değildir; denetimde maskelenir |

Kayıtta: `requestTitle` sabit "Geçiş desteği ve kurulum görüşmesi", `requestTarget` programın adı ("Kolayvet", "E-vet" ya da kliniğin yazdığı ad), `requestText` not (boş olabilir), `priority = Normal`. Ayrıntılar `migration` nesnesinde döner.

### 11.3 Ücret kuralı (tek yer)

| Paket | `fee` | Ekranda |
|---|---|---|
| Kurumsal (`Premium`) | `Free` | Ücretsiz |
| Temel, Profesyonel | `Quoted` | Ücretli; fiyat size bildirilecek |

Kural `PlanFeature.FreeMigrationSupport` ve `SubscriptionPlanCatalog` tablosundadır; uygulamada tek okuyan `MigrationSupportRules.FeeForAsync`. Denemede seçilen pakete göre. **Tutar tutulmaz ve kodda yazılmaz** (fiyat ürün kararı değil). Ücret bilgisi talep anında kayda yazılır (`migration.fee`); sonradan paket değişse de değişmez.

### 11.4 Uçlar — `api/v1/custom-development-requests/migration-support`

İzin `CustomDevelopmentRequests.Manage`; kiracı bağlamı zorunlu; her pakette.

| Uç | Yanıt |
|---|---|
| `GET` | 200 `{ "items": CustomDevelopmentRequestDto[], "openCount": 0, "maxOpen": 1, "fee": "Free\|Quoted", "meetingDurationMinutes": 60 }`; yalnız kiracının geçiş talepleri, yeniden eskiye |
| `POST` | 200 `CustomDevelopmentRequestDto` (`targetKind = "Migration"`, `migration` dolu) |

```json
{ "sourceProgram": "Kolayvet", "otherProgramName": null, "recordVolume": "UpTo5000",
  "meetingDays": "Monday, Thursday", "meetingTimes": "Morning, Afternoon", "note": "3 yıllık kayıt var" }
```

`CustomDevelopmentRequestDto.migration` (diğer konularda `null`): `{ "sourceProgram", "recordVolume", "meetingDays", "meetingTimes", "fee", "meetingDurationMinutes": 60 }`.

| Hata | Statü | Koşul |
|---|---|---|
| `CustomDevelopment.MigrationAlreadyOpen` | 409 | Kiracıda açık (`Received`, `InReview`, `Planned`) geçiş talebi var (`MaxOpenMigrationPerTenant` = 1) |
| `CustomDevelopment.Validation` | 400 | Gün ya da saat seçilmemiş / geçersiz bayrak, program adı ya da not uzunluğu |
| FluentValidation | 400 | `sourceProgram`, `recordVolume`, `meetingDays`, `meetingTimes` yok ya da geçersiz |
| `Subscriptions.TenantReadOnly` | 403 | Salt okunur abonelik (genel yazma kapısı) |

Platform yöneticisi: §4 listesi geçiş taleplerini de gösterir; yeni sorgu parametresi `targetKind` (ör. `?targetKind=Migration`) yalnız o konuyu listeler. Satırdaki `request.migration` ayrıntıları ve kiracı adı panel ekranında görünür.

Denetim: `CustomDevelopmentRequest.CreateMigrationSupport`, `TargetId = SourceProgram=…;RecordVolume=…`; not (`requestText`) maskelenir.

### 11.5 Kapsam ve KVKK (ekranda açıkça yazılır)

- Kurulum görüşmesi **1 saattir** (`meetingDurationMinutes`; `CustomDevelopmentRequest.MeetingDurationMinutes`).
- Geçiş desteğinin kapsamı **müşteri, hasta ve aşı geçmişidir** (ADR-014 K8). **Muayene ve ödeme geçmişi bu kapsamda yoktur.**
- Klinik dosyayı Vetinity personeline **e-postayla göndermez**. Dosyayı kendisi panele (içe aktarma sihirbazı) yükler. Destek ekibi gerekirse kliniğin izniyle **ekran paylaşımında** yardım eder; dosyayı devralmaz, kopyalamaz.
- Yüklenen satır verisi içe aktarmanın saklama kuralına tabidir: işlem bittikten 30 gün sonra silinir (ADR-019 K9; `IMPORT_API_CONTRACT.md` §10).
- Talep dosya eki ve kişisel veri almaz; not alanı denetimde maskelenir.

### 11.6 Çalışma varsayımları

1. Aynı anda en çok **1 açık** geçiş talebi.
2. Talep ve görüş yalnız Admin ve Owner'da (`CustomDevelopmentRequests.Manage`); içe aktarma izni olan ClinicAdmin talep açamaz.
3. Uygunluk gün + saat aralığı tercihi olarak alınır (kesin tarih değil); kesin görüşme saatini platform yöneticisi "Planlandı" notunda yazar. Saat aralıkları 09.00–18.00 arası üç parça.
4. Kayıt sayısı aralıklarla alınır (tam sayı değil).
5. Talep geri çekilemez, düzenlenemez (§7 madde 4 ile aynı).

### 11.7 Testler

- Domain: `MigrationSupportRequestTests` (alanlar, gün ve saat zorunluluğu, not isteğe bağlı, `Create` ile Migration konusu açılamaz, aynı durum akışı).
- Application: `MigrationSupportHandlerTests` (her pakette açılır ve pakete göre ücret, denemede seçilen paket, açık talep sınırı, program adı, özel geliştirme listesi ve sınırına karışmama, kiracı filtresi), `TenantPlanFeatureEvaluatorTests` (`FreeMigrationSupport` satırı).
- Entegrasyon: `MigrationSupportIntegrationTests` (Temel/Profesyonel `Quoted`, Kurumsal `Free`; kiracılar arası görünmezlik; özel geliştirme listesinde yok; ikinci açık talep 409, tamamlanınca yer açılır; platform listesinde `targetKind` filtresiyle görünür; maskeli denetim; izin ve doğrulama).
