# Görüntüleme kaydı ve dosya katmanı API sözleşmesi — v1

Backend tek doğruluk kaynağıdır. Kaynak: ADR-007 (Görüntüleme modülü, kabul), `docs/ASAMA6_DISCOVERY.md` (§1.5, R2/R3/R4, I1/I2, L3/L6/L7; D4/D6/D8/D9/D10), kullanıcı kararı 2026-10-22 (dosya katmanı yapılacak; altyapı/VPS/yedek işi yapılmaz, depolama yerel diskte soyutlama arkasında). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme v1. **Kapsam dışı:** DICOM, video, büyük dosya, küçük resim (thumbnail) üretimi, virüs taraması uygulaması (yalnız arayüz yeri), bulut depolama, yedekleme, fiziksel silme/saklama süresi, tek dosya kaldırma, ücret kaynağı bağı, cihaz entegrasyonu.

---

## 1) Kavramlar

- **Dosya katmanı (genel).** Her dosya bir meta satırıdır (`StoredFile`) ve tek bir sahip kayda bağlıdır: `ImagingRecord` veya `LabResult` (`ownerType` + `ownerId`). Sahipsiz dosya yoktur; dosyaya erişim **her zaman sahip kaydın izni ve klinik kapsamı** üzerinden geçer.
- **Dosya durumu:** `Uploading` (meta yazıldı, içerik yazılıyor) → `Ready` (içerik yazıldı, boyut ve SHA-256 kayıtlı) → `Deleted` (erişim kapalı). Yalnız `Ready` dosyalar listelenir ve indirilir.
- **Yükleme sırası (R4):** meta `Uploading` olarak kaydedilir → içerik depolamaya yazılır (hash ve boyut akış sırasında hesaplanır) → `Ready`. Yazma hatasında içerik silinir ve meta `Deleted` olur. Süreç ortasında çöken yüklemeyi bakım işi temizler (§6). Veritabanı işlemi dosya sistemini kapsamaz; tutarlılık bu sıra ve temizlik işiyle sağlanır.
- **Metin ve dosya ayrı çağrıdır (L6).** Görüntüleme kaydı önce JSON ile oluşturulur, dosyalar sonra tek tek yüklenir. Dosya yüklemesi başarısız olursa kayıt ve girilen metin korunur; istemci yalnız başarısız dosyayı yeniden dener. Bu nedenle kayıt geçici olarak dosyasız olabilir; "en az bir dosya" kuralı arayüzdedir.
- **Görüntüleme kaydı (`ImagingRecord`, I1, ADR-007):** hasta, isteğe bağlı muayene, tarih (`performedAtUtc`), tür (`modality`), açıklama, klinik not, dosyalar (0..N, arayüzde 1..N), durum `Active` | `Cancelled`. **Silme yoktur;** yanlış kayıt gerekçeyle iptal edilir (D10).
- **Görüntüleme türleri (`modality`):** `XRay` (Röntgen), `Ultrasound` (Ultrason), `CT` (BT), `MRI` (MR), `Endoscopy` (Endoskopi), `ClinicalPhoto` (Klinik fotoğraf), `Other` (Diğer). JSON'da metin.
- **Kesinleşmiş muayene (ADR-013 uyumu):** kesinleşmiş muayenenin içeriği değişmez; ona görüntüleme **bağlamak yeni kayıt sayılır** ve serbesttir (lab kaydı ile aynı desen). Yeni kaydın `addedAfterExaminationFinalization = true` olur (timeline ve detayda "kesinleştirmeden sonra eklendi" rozeti). **Muayene kesinleştiğinde zaten var olan** görüntüleme kaydı o muayenenin parçasıdır: düzenlenemez ve ona dosya eklenemez (`Examinations.Finalized`, 409); ek görüntü yeni görüntüleme kaydı olarak girilir. Kesinleşmeden sonra oluşturulmuş kayıt (ek kayıt) kendi içinde düzenlenebilir ve dosya alabilir. Muayene bağını değiştirmek için eski ve yeni muayene açık olmalıdır. Gerekçeli iptal her zaman serbesttir.
- **Lab sonucuna dosya (L3):** mevcut `LabResult` kaydına dosya (PDF/fotoğraf) eklenir; lab istem/sonuç akışı ve gövdeleri değişmez. Lab kesinleşmiş muayeneye bağlı olsa da dosya eklenebilir (geç gelen sonuç kuralı, Aşama 6 D2). İptal edilmiş lab kaydına dosya eklenemez; lab iptal edilince dosyaları `Deleted` olur.

## 2) Dosya güvenliği (R2)

| Kural | Uygulama |
|---|---|
| İzinli türler | Yalnız **JPEG, PNG, WebP, PDF**. Tür **içerikten** (ilk baytlar, magic bytes) belirlenir; uzantı ve istemcinin bildirdiği `Content-Type` yok sayılır. JPEG `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`, WebP `RIFF????WEBP`, PDF `%PDF-` (0. bayttan). SVG, HTML, GIF, TIFF, DICOM, video vb. reddedilir: 415 `Files.UnsupportedType`. |
| Saklanan tür | Yanıtta `Content-Type` tespit edilen türün sabit değeridir (`image/jpeg`, `image/png`, `image/webp`, `application/pdf`). |
| Boyut | Varsayılan **15 MB** (`FileStorage:MaxFileBytes`), en çok 50 MB'a kadar yapılandırılabilir. Gövde okunmadan önce ve akış sırasında denetlenir: 413 `Files.TooLarge`. Boş dosya 400 `Files.Validation`. Uç düzeyinde istek gövdesi tavanı 51 MB (`RequestSizeLimit` + `MultipartBodyLengthLimit`). **Ters vekil notu:** önde nginx/IIS varsa gövde sınırı (`client_max_body_size` / `maxAllowedContentLength`) yapılandırılan dosya sınırının en az 1 MB üstüne ayarlanmalıdır; aksi hâlde vekil 413'ü API'den önce döner. |
| Kayıt başına sayı | Sahip kayıt başına en çok **20** hazır/yüklenen dosya (`FileStorage:MaxFilesPerOwner`): 409 `Files.LimitReached`. |
| Dosya adı | Orijinal ad yalnız meta tabloda tutulur, temizlenir: dizin kısmı atılır, denetim karakterleri ve `<>:"/\|?*` çıkarılır, baştaki/sondaki nokta-boşluk kırpılır, en çok 120 karakter; boş kalırsa `dosya`. İndirmede ad, temizlenmiş ad + tespit edilen türün uzantısıdır (ör. `röntgen.exe` adlı JPEG `röntgen.jpg` iner). |
| Depolama anahtarı | Diskte ad **tahmin edilemez Guid** (dosya kimliği), uzantısız: `{tenantId}/{clinicId}/{yyyy}/{MM}/{fileId}` (kiracı/klinik/yıl-ay alt dizinleri). Anahtar yalnız sunucuda üretilir; istemci yol vermez. |
| Kök dışı yazma, ezme | Depolama, anahtarı kök altında çözer ve kök dışına çıkan yolu reddeder; yazma yalnız yeni dosya oluşturur (var olan ad ezilmez). |
| Statik sunum yok | Dosyalar hiçbir zaman statik URL ile sunulmaz; yalnız aşağıdaki yetkili akış ucu veya kısa ömürlü imzalı URL. |
| Yanıt başlıkları | `Content-Type` sabit (yukarıda), `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`, `X-Frame-Options: DENY` (genel API başlıkları), `Content-Disposition: inline` (gösterim) veya `attachment` (`?download=true`), ad RFC 6266 `filename*` ile kodlanır. Önbellek: `Cache-Control: private, no-store`. |
| PDF | Gösterim için istemci PDF'i Bearer ile akış ucundan blob olarak alır ve tarayıcının PDF görüntüleyicisinde açar; API sayfası çerçeveye gömülemez (`frame-ancestors 'none'`). |
| Virüs taraması | **v1'de uygulama yok.** `IFileMalwareScanner` arayüzü yazma sonrası, `Ready` öncesi çağrılır; v1 kaydı tarama yapmayan uygulamadır (her dosyayı geçirir). Gerçek tarayıcı (ör. ClamAV) ürün/maliyet kararıdır (D8: yükleyen yalnız klinik personeli). Tarayıcı reddederse içerik silinir, meta `Deleted`, yanıt 422 `Files.MalwareDetected`. |
| Depolama yeri | `FileStorage:RootPath` (yapılandırma). Boşsa yükleme/indirme 503 `Files.StorageUnavailable` döner (uygulamanın diğer bölümleri çalışır). Göreli yol uygulama dizinine göre çözülür; geliştirmede repo dışı mutlak bir klasör verilir (ör. `C:\VetinityData\files`). Yedekleme bu sözleşmenin dışındadır (kullanıcı işi). |

## 3) Yetkiler

| İzin | Admin | Owner | ClinicAdmin | Veteriner | Sekreter |
|---|---|---|---|---|---|
| `Imaging.Read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `Imaging.Create` | ✓ | ✓ | ✓ | ✓ | — |
| `Imaging.Update` | ✓ | ✓ | ✓ | ✓ | — |
| `Imaging.Cancel` | ✓ | ✓ | ✓ | ✓ | — |

Lab dosyaları yeni izin almaz: okuma `LabResults.Read`, yükleme `LabResults.Update`. Yeni `Imaging.*` izinleri `PermissionCatalog` + `RolePermissionBindings` ile seeder'a girer; **mevcut özel roller elle almalıdır** (deploy notu). Seed yalnız birleşik `main`'den çalıştırılır.

Klinik kapsamı: her uç kiracı + klinik kapsamını (`IClinicReadScopeResolver`, aktif klinik bağlamı) denetler. Başka kiracının veya atanmamış kliniğin kaydı/dosyası **404** döner (varlık sızdırılmaz). Dosya, URL'deki sahip kayda ait değilse 404 `Files.NotFound`.

## 4) Görüntüleme kaydı uçları

| Uç | Policy | Not |
|---|---|---|
| `POST /imaging-records` | `Imaging.Create` | 201 + `id`. Denetim kaydı. |
| `PUT /imaging-records/{id}` | `Imaging.Update` | Tam gövde + `rowVersion` zorunlu. 200 + `{ id, rowVersion }`. Denetim kaydı. |
| `POST /imaging-records/{id}/cancel` | `Imaging.Cancel` | Gerekçe (5–500) + `rowVersion`. Dosyalar `Deleted`, erişim kapanır. 200 + `{ id, rowVersion }`. Denetim kaydı. |
| `GET /imaging-records/{id}` | `Imaging.Read` | Detay + `Ready` dosyalar. |
| `GET /imaging-records` | `Imaging.Read` | Sayfalı liste. |
| `POST /imaging-records/{id}/files` | `Imaging.Create` | `multipart/form-data`, alan `file`. 201 + dosya. Denetim kaydı. |
| `GET /imaging-records/{id}/files/{fileId}/content` | `Imaging.Read` | Akış (Bearer). `?download=true` → `attachment`. |
| `POST /imaging-records/{id}/files/{fileId}/access-url` | `Imaging.Read` | Kısa ömürlü imzalı URL (§5). |

Dosya yükleme `Imaging.Create` ister (yeni kaydın dosyaları oluşturma akışının parçasıdır); varsayılan rollerde `Create` sahibi `Update`'e de sahiptir.

### `POST /imaging-records`

```json
{
  "clinicId": "guid", "petId": "guid", "examinationId": "guid?",
  "performedAtUtc": "datetime", "modality": "XRay",
  "description": "string?", "clinicalNote": "string?"
}
```

- `description` ≤ 1000, `clinicalNote` ≤ 4000 (boş metin `null` sayılır).
- `performedAtUtc` zorunlu; gelecekte olamaz (saat farkı için 1 gün tolerans): `ImagingRecords.Validation`. Geçmiş sınırı yoktur (eski görüntülerin arşive girmesi).
- Klinik aktif klinik bağlamıyla uyuşmalı (`ImagingRecords.ClinicContextMismatch`), kullanıcı kliniğe atanmış olmalı (`Clinics.AccessDenied` 403). Hasta kiracıya ait olmalı (`Pets.NotFound`). Muayene verilirse aynı klinik ve hasta (`ImagingRecords.ExaminationClinicMismatch` / `ImagingRecords.ExaminationPetMismatch`, 400; bulunamazsa `Examinations.NotFound`).
- Kesinleşmiş muayeneye bağlamak serbesttir; `addedAfterExaminationFinalization = true`.

### `PUT /imaging-records/{id}`

Gövde POST alanları + `rowVersion` (zorunlu) + `id` (opsiyonel, rotayla uyuşmalı: `ImagingRecords.RouteIdMismatch`). Klinik ve hasta değiştirilemez (gövdede yoktur; yanlış hasta → iptal + yeni kayıt).

| Durum | Sonuç |
|---|---|
| İptal edilmiş | 409 `ImagingRecords.AlreadyCancelled` |
| Bağlı muayene kesinleşmiş ve kayıt kesinleşmeden önce oluşturulmuş | 409 `Examinations.Finalized` |
| Muayene bağı değişiyor ve eski/yeni muayeneden biri kesinleşmiş | 409 `Examinations.Finalized` |
| Eski `rowVersion` | 409 `ImagingRecords.ConcurrencyConflict` |
| `rowVersion` yok/bozuk | 400 `ImagingRecords.Validation` |

### `POST /imaging-records/{id}/cancel`

`{ "reason": "string", "rowVersion": "base64" }`. Kayıt `Cancelled`, `cancelledAtUtc/cancelledByUserId/cancelReason` dolar; bütün `Uploading`/`Ready` dosyaları **aynı işlemde** `Deleted` olur ve akış/imzalı URL 404 döner. Fiziksel dosya v1'de silinmez (saklama/imha politikası ayrı karar). İkinci iptal 409 `ImagingRecords.AlreadyCancelled`.

### `GET /imaging-records/{id}`

```json
{
  "id": "guid", "clinicId": "guid", "petId": "guid", "petName": "string",
  "clientId": "guid", "clientName": "string", "examinationId": "guid?",
  "performedAtUtc": "datetime", "modality": "XRay",
  "description": "string?", "clinicalNote": "string?",
  "status": "Active", "addedAfterExaminationFinalization": false,
  "createdAtUtc": "datetime", "createdByUserId": "guid", "updatedAtUtc": "datetime?",
  "cancelledAtUtc": "datetime?", "cancelledByUserId": "guid?", "cancelReason": "string?",
  "rowVersion": "base64",
  "files": [ /* StoredFileDto, yalnız Ready, yükleme sırasına göre */ ]
}
```

İptal edilmiş kayıtta `files` boştur.

### `GET /imaging-records`

Sorgu: `clinicId` (aktif klinik bağlamı yoksa **zorunlu**: `ImagingRecords.ClinicScopeRequired`), `petId`, `examinationId`, `modality`, `dateFromUtc`, `dateToUtc` (`performedAtUtc` üzerinde, kapsayıcı), `status` (`Active` | `Cancelled`; verilmezse yalnız `Active`), `search` (açıklama, klinik not, hasta/müşteri adı), `page`, `pageSize` (≤200), `sort=performedAtUtc`, `order=asc|desc` (varsayılan yeniden eskiye).

Öğe:

```json
{
  "id": "guid", "clinicId": "guid", "petId": "guid", "petName": "string",
  "clientId": "guid", "clientName": "string", "examinationId": "guid?",
  "performedAtUtc": "datetime", "modality": "Ultrasound", "description": "string?",
  "status": "Active", "fileCount": 2, "addedAfterExaminationFinalization": false
}
```

## 5) Dosya uçları (ortak biçim)

### Yükleme — `POST /{owner}/{id}/files`

`multipart/form-data`, tek alan `file`. Sırayla: sahip kayıt kapsamda ve dosya kabul ediyor mu (iptal → 409 `ImagingRecords.AlreadyCancelled` / `LabResults.AlreadyCancelled`; kesinleşme kuralı → 409 `Examinations.Finalized`) → boyut → kayıt başına sayı → içerik türü → yazma → (tarama arayüzü) → `Ready`.

Yanıt 201, `StoredFileDto`:

```json
{
  "id": "guid", "fileName": "röntgen-sol-arka.jpg", "contentType": "image/jpeg",
  "kind": "Image", "sizeBytes": 1832211, "sha256": "hex(64)",
  "uploadedAtUtc": "datetime", "uploadedByUserId": "guid"
}
```

`kind`: `Image` | `Pdf`. Hatalar: 400 `Files.Validation` (dosya yok/boş), 413 `Files.TooLarge`, 415 `Files.UnsupportedType`, 409 `Files.LimitReached`, 422 `Files.MalwareDetected`, 503 `Files.StorageUnavailable`.

### Akış — `GET /{owner}/{id}/files/{fileId}/content[?download=true]`

Bearer ile; sahip kaydın okuma izni + klinik kapsamı. 200 gövde dosyanın kendisi, başlıklar §2. `Deleted`/`Uploading` dosya, başka kayda ait dosya, kapsam dışı kayıt: 404 `Files.NotFound` (kayıt bulunamazsa sahip kaydın `NotFound` kodu). Erişim **denetim kaydına yazılmaz** (yalnız yükleme ve iptal yazılır).

### İmzalı URL — `POST /{owner}/{id}/files/{fileId}/access-url`

`<img src>` Bearer başlığı gönderemediği için. Akış ucu ile aynı yetki ve kapsam denetimi; yanıt:

```json
{ "url": "/api/v1/files/signed/{fileId}?expires=1761130000&signature=base64url", "expiresAtUtc": "datetime" }
```

`url` API köküne görelidir (istemci API taban adresini önüne ekler). Ömür varsayılan **5 dakika** (`FileStorage:AccessUrlLifetimeSeconds`, 30–3600). İmza HMAC-SHA256 (`fileId` + `expires`); anahtar `FileStorage:AccessUrlSigningKey` (Base64, ≥32 bayt; gizli bilgi, user-secrets/ortam değişkeni) verilmezse süreç başına rastgele üretilir (yeniden başlatmada açık URL'ler geçersizleşir; çok örnekli kurulumda anahtar verilmelidir).

### İmzalı içerik — `GET /files/signed/{fileId}?expires=&signature=`

Anonim (Bearer gerekmez). İmza bozuk, eksik veya süresi geçmiş: **403 `Files.AccessUrlInvalid`**. Dosya `Ready` değilse (ör. kayıt iptal edildi) 404 `Files.NotFound`. Her zaman `inline`; başlıklar §2.

### Uç eşlemesi

| Sahip | Liste | Yükleme | Akış / imzalı URL |
|---|---|---|---|
| Görüntüleme | detayda `files` | `POST /imaging-records/{id}/files` (`Imaging.Create`) | `Imaging.Read` |
| Lab sonucu | `GET /lab-results/{id}/files` (`LabResults.Read`) → `StoredFileDto[]` | `POST /lab-results/{id}/files` (`LabResults.Update`) | `LabResults.Read` |

Lab detay/list gövdeleri değişmez.

## 6) Yarım yükleme temizliği (R4)

Bakım işi (`FileStorage:Maintenance`: `Enabled` varsayılan `true`, `IntervalMinutes` 30, `BatchSize` 100, `StaleUploadMinutes` 60): `Uploading` durumunda `StaleUploadMinutes`'ten eski meta satırlarının içeriğini (varsa) depolamadan siler ve satırı `Deleted` yapar (silen kullanıcı yok = sistem). Hata işi durdurmaz; günlüğe yalnız dosya kimliği ve sayılar yazılır.

## 7) Timeline (ADR-006, IMG-008)

Hasta timeline'ına yeni tür `Imaging`: olay zamanı `performedAtUtc`, başlık türün Türkçe adı (ör. "Röntgen"), özet açıklama, `status` `Active` | `Cancelled`, iptal edilen varsayılan gizli (`includeVoided` ile görünür), `relatedExaminationId`, `addedAfterFinalization` = kesinleştirmeden sonra eklendi. Küçük resim/dosya bilgisi yoktur. İzin `Imaging.Read`; izni olmayan kullanıcıda tür `omittedTypes`'ta döner. Hedef rota: `/panel/imaging/{id}`.

Muayene `related-summary` yanıtı `imaging` bloğunu taşır (muayeneye bağlı kayıtlar, iptaller `status` ile, `Imaging.Read` yoksa `[]`): `BACKEND-CONTRACT-STANDARD.md` §18.

## 8) Denetim kaydı

`ImagingRecord.Create`, `ImagingRecord.Update`, `ImagingRecord.Cancel`, `ImagingRecord.FileUpload`, `LabResult.FileUpload`. Yükleme kaydında dosya adı ve boyut vardır, **içerik yazılmaz**. İndirme/gösterim ve imzalı URL üretimi denetime yazılmaz.

## 9) Veri modeli (migration)

- `StoredFiles`: `Id`, `TenantId`, `ClinicId`, `OwnerType` (1 Imaging, 2 LabResult), `OwnerId`, `StorageKey` (≤200, benzersiz), `OriginalFileName` (≤120), `ContentType` (≤50), `SizeBytes`, `Sha256` (char 64, `Ready` ile dolar), `Status` (0 Uploading, 1 Ready, 2 Deleted), `UploadedByUserId`, `CreatedAtUtc`, `ReadyAtUtc`, `DeletedAtUtc`, `DeletedByUserId`, `RowVersion`. İndeksler: `(TenantId, OwnerType, OwnerId)`, `(Status, CreatedAtUtc)`.
- `ImagingRecords`: `Id`, `TenantId`, `ClinicId`, `PetId`, `ExaminationId?`, `PerformedAtUtc`, `Modality`, `Description` (≤1000), `ClinicalNote` (≤4000), `Status`, `AddedAfterExaminationFinalization`, `CreatedByUserId`, `CreatedAtUtc`, `UpdatedAtUtc`, iptal alanları, `RowVersion`. İndeksler: `(TenantId, ClinicId, PerformedAtUtc)`, `(TenantId, PetId)`, `(TenantId, ExaminationId)`.
- Yalnız yeni tablolar; mevcut tablolar değişmez. Migration yerel/paylaşılan veritabanına **uygulanmadı**.

## 10) Doğrulanmayan / açık

- Gerçek ters vekil (nginx/IIS) gövde sınırı ve üretim disk konumu/yedek: dağıtım işi, kullanıcı söyleyene kadar yapılmaz.
- Fotoğraflardaki EXIF (konum vb.) meta verisi temizlenmez; içerik olduğu gibi saklanır.
- Fiziksel silme ve saklama/imha süresi (KVKK imha politikası ile) ayrı karar.
