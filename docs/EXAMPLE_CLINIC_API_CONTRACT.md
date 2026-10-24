# Örnek Veteriner Kliniği API sözleşmesi (TRIAL-002 / TRIAL-003)

Backend tek doğruluk kaynağıdır. Kaynak: ürün kararı ADR-002 (örnek klinik stratejisi), backlog TRIAL-002 (onboarding seçimi) ve TRIAL-003 (sentetik seed). Tüm uçlar `api/v1` altında, JSON, standart `ProblemDetails` + `extensions.code` hata zarfı.

> **Durum:** Uygulandı (`feat/trial-003-example-clinic`). Frontend onboarding ekranı bu sözleşmeye göre yapılır.

## 1) Amaç ve sınırlar

Self-service deneme (trial) kiracısı boş klinik yerine **Örnek Veteriner Kliniği** ile başlayabilir: tutarlı, gerçekçi, tamamen sentetik Türkçe veri (gerçek kişi verisi yok). Hedef, kullanıcının ilk 5–10 dakikada müşteri → hayvan → randevu → muayene → aşı/tedavi/lab → ücret/ödeme → ürün/stok zincirini görmesi.

- İki yol vardır; ikisi de aynı yükleyiciyi çalıştırır ve aynı özet gövdesini döner:
  1. **Kayıtta:** `POST /public/owner-signup` gövdesine `startWithExampleClinic: true` (önerilen onboarding seçimi).
  2. **Sonradan:** `POST /tenants/{tenantId}/example-clinic` (boş klinikle başlayan kiracı daha sonra yükler).
- Yükleme **tek veritabanı işlemidir**: ortadaki hata hiçbir kayıt bırakmaz; kayıt akışında hata olursa kayıt da geri alınır.
- Hedef klinik **kiracının ilk aktif kliniğidir** (deneme kiracısında tek klinik vardır).
- Örnek veri **yalnız boş kiracıya** yüklenir ve **yalnız deneme (Trialing) aboneliğinde** çalışır.
- Temizleme/geri alma ucu bu sözleşmede **yoktur** (bkz. §7 açık sorular).

## 2) Yetki

`POST /tenants/{tenantId}/example-clinic` için `Subscriptions.Manage` (kiracı yöneticisi `Admin` rolünde vardır). JWT `tenant_id` route `tenantId` ile aynı olmalıdır; farklıysa **403 `Tenants.AccessDenied`** (başka kiracıya yükleme yapılamaz). `owner-signup` anonimdir; alan yalnız yeni oluşturulan kiracıya uygulanır.

## 3) `POST /api/v1/public/owner-signup` (mevcut uç, yeni isteğe bağlı alan)

Gövdeye `startWithExampleClinic` (bool, varsayılan `false`) eklendi:

```json
{
  "planCode": "Basic", "tenantName": "…", "clinicName": "…", "clinicCity": "…", "email": "…", "password": "…",
  "acceptedLegalDocuments": [ { "documentType": "Terms", "version": "0.1" }, … ],
  "discountCode": null,
  "startWithExampleClinic": true
}
```

- `true` ise kullanıcı + kiracı + klinik + abonelik + kabuller yazılır, **aynı işlemde** örnek klinik yüklenir.
- Yanıta `exampleClinic` eklendi (§5 özet gövdesi); `false`/eksikse `null`. Diğer alanlar (`nextStep: "login"` vb.) değişmedi.
- Mevcut hata kodları (`Users.DuplicateEmail`, `Tenants.DuplicateName`, `LegalAcceptances.*`, `DiscountCodes.Invalid`) aynen geçerlidir; yükleme bu kontrollerden sonra çalışır.

## 4) `POST /api/v1/tenants/{tenantId}/example-clinic` — `Subscriptions.Manage`

Gövde yok. Başarı **200** + §5 özet gövdesi.

| Kod | HTTP | Durum |
|---|---|---|
| `Tenants.ContextMissing` | 400 | JWT'de `tenant_id` yok |
| `Auth.PermissionDenied` / policy | 403 | `Subscriptions.Manage` yok |
| `Tenants.AccessDenied` | 403 | Route `tenantId` ≠ JWT kiracısı (IDOR) |
| `Tenants.NotFound` | 404 | Kiracı yok |
| `Clinics.NotFound` | 404 | Kiracının aktif kliniği yok |
| `Tenants.ExampleClinicTrialRequired` | 409 | Abonelik `Trialing` değil (aktif/salt okunur/iptal) |
| `Tenants.ExampleClinicAlreadyLoaded` | 409 | Kiracıda zaten müşteri veya hasta var (ikinci çağrı dahil); hiçbir şey yazılmaz |

İdempotentlik: aynı kiracı için ikinci çağrı her zaman 409 `Tenants.ExampleClinicAlreadyLoaded` döner; veri çoğalmaz.

## 5) Özet gövdesi (`ExampleClinicLoadSummaryDto`)

```json
{
  "tenantId": "guid", "clinicId": "guid",
  "clients": 12, "pets": 20, "appointments": 38, "examinations": 26, "vaccinations": 14, "treatments": 9,
  "labResults": 5, "charges": 48, "payments": 19, "products": 10, "stockMovements": 29
}
```

`clients`, `pets`, `products`, `labResults` sabittir; diğerleri "bugün"e (hafta içi/Pazar, saat) göre küçük oynar. Sayılar veritabanındaki kayıt sayılarına eşittir (entegrasyon testi doğrular).

## 6) Veri seti (kaynak: `ExampleClinicDataGenerator`)

- **Zaman:** `TimeProvider` ile "şimdi"; klinik günü **Europe/Istanbul** (`OperationDayBounds`). Sabit tohum (`20261024`): aynı gün için aynı küme.
- **Müşteriler (12):** uydurma ad, `0532 100 1xxx` telefon, bazılarında `@example.com` e-posta, Kadıköy/Üsküdar adresleri.
- **Hayvanlar (20):** 8 kedi, 9 köpek, 3 kuş; tür/ırk **mevcut katalogdan** (`SpeciesAndBreedCatalogSeeder`: Tekir, Golden Retriever, Muhabbet Kuşu …), cinsiyet, doğum tarihi, kilo, kısırlık, çip.
- **Randevular (~35–40):** son 30 gün + gelecek 7 gün, Pazar kapalı, 30 dk slotlar 09:30–17:00. Geçmiş: çoğu `Completed`, birkaç `Cancelled` ve `NoShow`; bugün en fazla 2 geçmiş + 2 gelecek; gelecek `Scheduled`.
- **Muayeneler (~25):** tamamlanan randevular + 4 randevusuz geliş; başvuru nedeni/bulgu/değerlendirme/plan ve vitaller; 3 günden eskiler **kesinleşmiş**, son günlerdekiler **taslak**.
- **Aşılar:** uygulanmış (muayeneye bağlı, global katalog: Kuduz, Karma, Lösemi), 4 planlı (2–21 gün içinde), **1 gecikmiş** rapel.
- **Tedaviler (~8):** antibiyotik kürü, antienflamatuar, kulak temizliği, diş taşı temizliği …; kontrol tarihli.
- **Lab (5):** 4 sonuçlanmış (biri hekim yorumu bekliyor), **1 istenmiş/bekleyen** (Tiroid paneli).
- **Ücret/ödeme:** muayene başına "Muayene ücreti" + aşı/tedavi/lab satırları; çoğu tam ödenmiş (nakit/kart/havale), **kısmi ödeme** ve **açık borç** örnekleri; bugünkü taslak muayeneler ödenmemiş. Tahsisler `Payment.AllocateFully` ile.
- **Ürünler (10):** 4 aşı, 3 ilaç, 3 bakım/mama; `ORN-…` SKU; aşı/ilaçta **parti (lot) + SKT** (bir parti ~1 ay içinde SKT'ye yaklaşır), tedarikçi girişleri, aşı/tedavi çıkışları (`referenceType: Vaccination|Examination`), tezgâh satışları; `ProductStock` = giriş − çıkış; bir ürün minimum stok altında.
- **Okuma modelleri:** Client/Pet/Appointment/Payment `Created` outbox olayları üretim handler'larıyla aynı snapshot fabrikalarından aynı işlemde yazılır.
- **İşaret (temizleme için):** her kaydın not/açıklama alanı `ÖRNEK · ` ile başlar; müşteride adres, partide tedarikçi adı, üründe açıklama (+ `ORN-` SKU öneki). `ExampleClinicDataGenerator.Marker` tek kaynaktır.

## 7) Açık sorular / kapsam dışı

- **Temizleme ucu** (örnek veriyi silip kendi verisine geçiş, ADR-002 risk maddesi): ayrı iş; işaret bunun için bırakıldı. Ayrı bir `IsExample` sütunu/migration tercih edilirse ürün kararı gerekir.
- Rapor/takvim verisi mevcut tablolardan türediği için ayrı kayıt yoktur. Yatış, görüntüleme, reçete ve hasta sahibi portalı bu sette **yok**.
- Metrik (TRIAL-008: örnek klinik seçme oranı): `Tenant.ExampleClinicLoad` denetim kaydı ve kayıt yanıtındaki `exampleClinic` alanı kaynak olabilir; ayrı sayaç eklenmedi.

## 8) Frontend etkisi

- Onboarding (kayıt) formunda "Boş klinikle başla / Örnek Veteriner Kliniğini yükle (önerilen)" seçimi → `startWithExampleClinic`.
- Kayıt yanıtında `exampleClinic` özeti ile "ne yüklendi" kartı; `null` ise boş klinik.
- Boş başlayan kiracı için panelde "Örnek kliniği yükle" eylemi → `POST /tenants/{tenantId}/example-clinic`; 409 kodlarına göre buton gizlenir/uyarı verilir.
- Smoke: kayıt (iki seçenek), yükleme ucu 200/409/403, özet sayılarının liste ekranlarıyla uyumu.

## 9) Testler

- Application: `LoadExampleClinicCommandHandlerTests` (yetki, bağlam, IDOR, trial dışı, dolu kiracı, başarı), `PublicOwnerSignupCommandHandlerTests` (bayrak açık/kapalı), `TransactionalCommandMarkerTests`.
- Entegrasyon: `Tenants/ExampleClinicIntegrationTests` (kayıtta yükleme + tutarlılık, sonradan yükleme, ikinci yükleme 409, trial dışı 409, IDOR 403, 401/403).
