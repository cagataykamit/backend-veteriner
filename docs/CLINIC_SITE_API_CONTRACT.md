# Klinik web sitesi — API sözleşmesi

Kaynak karar: ürün deposu `decisions/ADR-045-klinik-web-sitesi-olusturucu.md` (K1–K14). Bu belge ADR-045'in 1. aşamasının
(backend) sözleşmesidir; panel sihirbazı ve site uygulaması bunu izler. Tüm saatler UTC ISO-8601 taşınır. Hata gövdesi mevcut
ProblemDetails (`code` alanı). Herkese açık yüzeyde `status` metindir; `workingHours[].dayOfWeek` mevcut klinik saat uçlarıyla
aynı biçimdedir.

## 1. Model özeti

| Varlık | Not |
|---|---|
| `ClinicSite` | Klinik başına bir site (K2): güncel adres, taslak yapılandırma, yayındaki sürüm numarası, geçici kapalı bayrağı, `rowVersion`. |
| `ClinicSiteVersion` | Değişmez yayın sürümü: sıra numarası, yapılandırma, yayınlayan, zaman. Alan değişikliği EF'te hata verir. Site başına en çok **30** sürüm saklanır; yayında en eskisi düşer, yayındaki sürüm her zaman en yenisidir. |
| `ClinicSiteAddress` | Güncel ve eski adresler. `slug` **tüm kiracılarda** benzersizdir (K10). Site başına en çok **5** eski adres. |
| `ClinicSiteImage` | Herkese açık site görseli (K12): kiracı ve siteye bağlı; JPEG, PNG, WebP; en çok **5 MB**; site başına en çok **100**. `StoredFile`'dan ayrıdır, hasta dosyaları bu yoldan açılmaz. |

Paket kilidi yoktur, tüm paketlerde çalışır (K13). Boyutlandırma bu aşamada yoktur (§9).

## 2. Yönetim — `/api/v1/clinics/{clinicId}/site`

Yetki online randevu ayarlarıyla aynıdır: okuma `Clinics.Read`, yazma `Clinics.Update`; kiracı filtresi ve klinik ataması
uygulanır (başka kiracının kliniği 404 `Clinics.NotFound`). Abonelik yazma kapısı bütün komutlara uygulanır: salt okunur ya da
iptal kiracıda 403 `Subscriptions.TenantReadOnly` / `Subscriptions.TenantCancelled`.

| Uç | Yetki | Açıklama |
|---|---|---|
| `GET` | `Clinics.Read` | `ClinicSiteDetailDto`. Site yoksa `exists=false`, `suggestedSlug` dolu; diğerleri boş. |
| `PUT /draft` | `Clinics.Update` | Taslağı kaydeder; `{ config, rowVersion }`. İlk kayıt siteyi önerilen adresle oluşturur. Site varsa `rowVersion` zorunlu. Yanıt `ClinicSiteStateDto` (yapılandırma geri dönmez). Otomatik kayıt olduğundan denetim kaydı yazmaz. |
| `GET /address-availability?slug=` | `Clinics.Update` | §4. |
| `PUT /address` | `Clinics.Update` | `{ slug }`; site yoksa 404 `ClinicSites.NotFound`. Yanıt `ClinicSiteStateDto`. Denetim: `ClinicSite.Address.Change`. |
| `POST /publish` | `Clinics.Update` | `{ rowVersion }` zorunlu: klinik gördüğünü yayınlar. §3. Denetim: `ClinicSite.Publish`. |
| `GET /versions` | `Clinics.Read` | `ClinicSiteVersionDto[]`, en yeni önce; yapılandırma dönmez. |
| `POST /versions/{versionNumber}/restore` | `Clinics.Update` | `{ rowVersion }` zorunlu. Sürümü **taslağa** kopyalar, yayın değişmez (K2). Denetim: `ClinicSite.Version.Restore`. |
| `POST /close` \| `POST /reopen` | `Clinics.Update` | Geçici kapatma (K9); idempotent, gövde yok. Denetim: `ClinicSite.Close` / `ClinicSite.Reopen`. |
| `POST /images` | `Clinics.Update` | §5. Denetim: `ClinicSite.Image.Upload`. |
| `GET /images/{imageId}/content` | `Clinics.Read` | §5. Panel önizlemesi. |
| `DELETE /images/{imageId}` | `Clinics.Update` | §5. 200 `ClinicSiteStateDto`: `rowVersion` değişir. Denetim: `ClinicSite.Image.Delete`. |

```json
// ClinicSiteStateDto
{ "siteId": "…", "clinicId": "…", "slug": "cagri-veteriner", "publicPath": "/k/cagri-veteriner",
  "isTemporarilyClosed": false, "publishedVersionNumber": 2, "publishedAtUtc": "…",
  "hasUnpublishedChanges": true, "draftUpdatedAtUtc": "…", "rowVersion": "AAAAAAAAB9E=" }

// ClinicSiteDetailDto — site varken
{ "exists": true, "suggestedSlug": null, "site": { …ClinicSiteStateDto… }, "draft": { "schemaVersion": 1, "…": "…" },
  "isClosedBySubscription": false, "missingForPublish": ["phone"], "missingImageIds": [],
  "images": [{ "id": "…", "reference": "site-image:…", "contentType": "image/jpeg", "sizeBytes": 1234, "createdAtUtc": "…" }] }

// ClinicSiteDetailDto — site yokken
{ "exists": false, "suggestedSlug": "cagri-veteriner", "site": null, "draft": null,
  "isClosedBySubscription": false, "missingForPublish": ["phone", "workingHours"], "missingImageIds": [], "images": [] }

// ClinicSiteVersionDto
{ "versionNumber": 2, "isPublished": true, "publishedAtUtc": "…", "publishedByUserId": "…", "publishedByName": "Ayşe" }
```

`rowVersion` her yazmada değişir; yanıttaki değer bir sonraki yazmada kullanılır. Eski değer 409
`ClinicSites.ConcurrencyConflict`, eksik ya da bozuk değer 400 `ClinicSites.Validation`. `hasUnpublishedChanges` "değişiklik var,
yayında değil" şeridi içindir (K9). `isClosedBySubscription` yalnız paneldedir; herkese açık uçta bu bilgi yoktur (§6).

## 3. Yapılandırma (`config`) ve yayın

Yapılandırma sihirbazın ürettiği, motorun çizdiği JSON nesnesidir; backend tasarım alanlarını **yorumlamaz**, olduğu gibi saklar
ve döner. Yeni tasarım paketi backend değişikliği istemez (K3). Taslak modeli: `content`, `design`, `structure`, `seo`, `publish`.

Backend'in zorladığı sınırlar (ihlal 400 `ClinicSites.Validation`):

| Kural | Ayrıntı |
|---|---|
| Sürüm anahtarı | Kök nesnede `schemaVersion`: 1 ya da üstü tam sayı. Değeri motor yorumlar. |
| Boyut ve karmaşıklık | En çok 1.000.000 bayt (UTF-8), 32 düzey, 100.000 düğüm; alan adı en çok 200 karakter. Metne çözülemeyen kaçış (eşsiz vekil, ör. `\uD800`) geçersiz JSON sayılır. |
| Her metin değeri | `javascript:`, `vbscript:`, `data:` ile başlayamaz; boşluk, denetim ve biçim karakterleri (Unicode Cf, ör. U+FEFF, U+200B) ile büyük/küçük harf bu denetimi atlatmaz. `şema://` biçiminde yalnız `http`, `https`. Cümle içinde geçmesi engellenmez. |
| Bağlantı alanı | Ad `href`, `url`, `link`, `website`, `instagram`, `facebook`, `twitter`, `youtube`, `linkedin`, `tiktok` ya da `Url`, `Href`, `Link` ile biten: yalnız `http`, `https`, `tel`, `mailto` ya da şemasız değer (`ornek.example.com`, `kullaniciadi`, `/hizmetler`). Biçim karakteri içeremez. |
| Görsel alanı | Ad `src`, `logo`, `favicon`, `image` ya da `Image` ile biten: boş, `site-image:{id}` ya da `stock:{anahtar}` (`a-z 0-9 - _ / .`, `..` yok). Üçüncü taraf adresi kabul edilmez (K7); taslaktaki Unsplash adresleri `stock:` anahtarına çevrilmelidir. Biçim karakteri içeremez. |
| Görsel başvurusu | Hangi alanda olursa olsun değeri `site-image:` ile **başlayan** metin başvurudur: kimlik geçerli ve görsel bu sitenin olmalı. Metin içinde geçen `site-image:` başvuru sayılmaz; motor da yalnız değerin tamamı başvuruysa çözer. |
| Klinik kaydı alanları (K8) | `content.clinic` içinde `name`, `phone`, `email`, `address`, `district`, `city`, `workingDays`, `workingHours` ve `seo.slug` bulunamaz. Klinik kaydı mevcut `/api/v1/clinics/{id}` ve `/working-hours` uçlarıyla değişir; yeni yazma yolu yoktur. Taslaktaki `SiteClinic`'in bu alanları yapılandırmadan çıkarılmalıdır. |

```json
// 400 ClinicSites.Validation
{ "code": "ClinicSites.Validation", "detail": "Site yapılandırması geçersiz: bağlantı yalnız http, https, tel ya da mailto olabilir ($.content.clinic.mapUrl).",
  "issues": [{ "path": "$.content.clinic.mapUrl", "reason": "linkSchemeNotAllowed" }] }
```

`reason` değerleri: `invalidJson`, `tooLarge`, `tooComplex`, `schemaVersionRequired`, `linkSchemeNotAllowed`,
`imageReferenceNotAllowed`, `clinicRecordField`, `imageNotInSite`. En çok 20 sorun raporlanır, sonra tarama durur. Yeni alan adı kurala uymazsa (ör. `cta` metin alanına bağlantı)
yalnız "her metin değeri" denetiminden geçer; bağlantı ve görsel alanlarını bu ad kurallarıyla adlandırın.

**Yayın zorunlu listesi (K9, tek yer `ClinicSitePublishRequirements`):** klinik adı, telefon, adres ve en az bir açık çalışma
saati satırı (kapalı işaretli gün sayılmaz). Eksikse 409:

```json
{ "code": "ClinicSites.PublishRequirementsNotMet", "detail": "Yayınlamadan önce tamamlayın: telefon, en az bir çalışma saati.",
  "missing": ["phone", "workingHours"], "missingImageIds": [] }
```

`missing` değerleri: `name`, `phone`, `address`, `workingHours`. `GET` yanıtındaki `missingForPublish` aynı listedir; son kontrol
adımı kuralı kopyalamadan bunu gösterir. Silinmiş görsele başvuran taslak `missingImageIds` ile aynı hatayı alır. Diğer eksikler
(K9'daki uyarılar) panel kuralıdır. Pasif klinik 409 `ClinicSites.ClinicInactive`.

## 4. Adres (K10)

Biçim: 3–60 karakter, yalnız `a-z`, `0-9` ve tek tire; başta ve sonda tire yok. Ayrılmış sözcükler (`api`, `panel`, `portal`,
`admin`, `vetinity`, `randevu`, …) alınamaz; liste `ClinicSiteSlug`'dadır. Öneri klinik adından üretilir (`Çağrı Veteriner` →
`cagri-veteriner`); alınmışsa `-2`, `-3` eklenir.

```json
// GET /address-availability?slug=...; girdi küçük harfe indirilip döner
{ "slug": "cagri-vet", "isAvailable": false, "reason": "Taken" }   // reason: null | Invalid | Reserved | Taken
```

`PUT /address`: geçersiz ya da ayrılmış adres 400 `ClinicSites.Validation`; başkasının güncel ya da eski adresi 409
`ClinicSites.SlugTaken` (ayrım yapılmaz). Yayınlanmış sitenin önceki adresi **eski adres** olur: yeni adrese yönlenir ve başkasına
verilmez; site kendi eski adresine dönebilir. Yönlendirme her zaman güncel adrese gider, zincir oluşmaz. Hiç yayınlanmamış sitenin
adresi paylaşılmadığı için değişince serbest kalır.

Adres kapmaya karşı iki sınır, yalnız yayınlanmış sitede:

| Sınır | Aşılınca |
|---|---|
| Site başına en çok 5 eski adres. En eskisi serbest bırakılmaz; kendi eski adresine dönmek sayıyı artırmaz, serbesttir. | 409 `ClinicSites.AddressLimitReached` |
| 24 saatte en çok 3 adres değişikliği (kendi eski adresine dönüş dahil; pencere ilk değişiklikle başlar). | 409 `ClinicSites.AddressChangeLimitReached` |

## 5. Görseller

`POST /images`: `multipart/form-data`, alan `file`. Site önce `PUT /draft` ile oluşmuş olmalı (yoksa 404 `ClinicSites.NotFound`).
Tür dosya adından ya da bildirilen içerik türünden değil **içeriğin imzasından** belirlenir. Yanıt 201 `ClinicSiteImageDto`;
`reference` yapılandırmaya yazılan değerdir.

| Durum | Kod |
|---|---|
| Boş dosya | 400 `Files.Validation` |
| 5 MB üstü | 413 `Files.TooLarge` |
| JPEG, PNG, WebP dışı (PDF dahil) | 415 `Files.UnsupportedType` |
| Site başına 100 görsel | 409 `Files.LimitReached` |
| Deneme kotası (kiracının toplam depolaması; site görselleri de sayılır) | 422 `Trial.*` (TRIAL_LIMITS_API_CONTRACT.md) |
| Depo erişilemiyor | 503 `Files.StorageUnavailable` |

`GET /images/{imageId}/content`: Bearer ile içerik; yayınlanmamış görseli de açar, `Cache-Control: private, no-store`. Önizlemede
motor `site-image:` başvurusunu bu uçtan okur.

`DELETE /images/{imageId}`: taslakta ya da yayındaki sürümde kullanılıyorsa 409 `ClinicSites.ImageInUse`
(`usedIn: ["draft", "published"]`). Yalnız eski sürümdeki başvuru silmeyi engellemez; o sürüm taslağa kopyalanırsa taslak
kaydedilebilir ama yayın `missingImageIds` ile durur. Yanıt 200 `ClinicSiteStateDto`'dur: silme site satırını da yazar, böylece
arada kaydedilen taslak ya da yayın canlı görseli kıramaz (eşzamanlı olan 409 `ClinicSites.ConcurrencyConflict` alır). **Panel
yanıttaki `rowVersion`'ı sonraki yazmada kullanmalıdır.** Yükleme `rowVersion`'ı değiştirmez.

**Küçültme ve EXIF (K12):** görsel backend'de yeniden kodlanmaz, olduğu gibi saklanır ve sunulur. Küçültme ve EXIF temizliği
tarayıcıda, panelde yapılır. Doğrudan API yüklemesinde bunun garantisi yoktur: konum gibi EXIF verisi ve büyük boyut (5 MB'a kadar)
korunur.

## 6. Herkese açık uçlar — `/api/v1/public/sites/{slug}`

Kimliksizdir; IP başına hız sınırı: site `public-site` (dakikada 120), görsel `public-site-image` (dakikada 600, genel 200/dk
tavanının dışında); aşımda 429 `RateLimit.Exceeded`. Yanıt yalnız yayınlanmış sürümü ve K8'deki klinik alanlarını taşır; taslak,
kiracı, klinik, site ve kullanıcı kimliği ile hasta verisi dönmez. Bu uçlar taslak sütununu hiç okumaz; görsel ucu sürüm
yapılandırmasını da yüklemez.

### `GET /api/v1/public/sites/{slug}`

Yanıtta her alan bulunur, duruma uymayanlar `null` olur. `slug` istekteki adrestir.

```json
// status: Published
{ "status": "Published", "slug": "cagri-veteriner", "redirectToSlug": null, "message": null,
  "versionNumber": 2, "publishedAtUtc": "…", "config": { "schemaVersion": 1, "…": "…" },
  "clinic": { "name": "…", "city": "İstanbul", "district": "Kadıköy", "address": "…", "phone": "…", "email": "…",
    "workingHours": [{ "dayOfWeek": 1, "isClosed": false, "opensAt": "09:00:00", "closesAt": "18:00:00", "breakStartsAt": null, "breakEndsAt": null }] },
  "onlineBooking": { "isEnabled": true, "bookingCode": "k7m2p9xq4r8t", "bookingUrl": "https://portal…/randevu/k7m2p9xq4r8t" } }

// status: Redirect — eski adres; site uygulaması kalıcı yönlendirme verir
{ "status": "Redirect", "slug": "eski-adres", "redirectToSlug": "yeni-adres", "message": null, "versionNumber": null,
  "publishedAtUtc": null, "config": null, "clinic": null, "onlineBooking": null }

// status: Closed — geçici kapatma ya da abonelik; ikisi birebir aynıdır
{ "status": "Closed", "slug": "cagri-veteriner", "redirectToSlug": null, "message": "Bu site şu anda yayında değil.",
  "versionNumber": null, "publishedAtUtc": null, "config": null, "clinic": null, "onlineBooking": null }
```

| Durum | Yanıt |
|---|---|
| Adres yok, biçimi geçersiz; kiracı ya da klinik pasif; site hiç yayınlanmamış | 404 `PublicSites.NotFound` (ayrım yapılmaz) |
| Klinik "geçici kapat" dedi (güncel ya da eski adresten) | 200 `Closed`; yeni adres açıklanmaz |
| Abonelik salt okunur (deneme ya da dönem bitti) veya iptal (güncel ya da eski adresten) | 200 `Closed`; yeni adres açıklanmaz |
| Açık sitenin eski adresi | 200 `Redirect` |
| Diğer | 200 `Published` |

**Randevu (K11):** kapalıysa `isEnabled=false` ve kod boş; düğme `clinic.phone`'a döner. Açıksa düğme ADR-042 talep akışına gider
(`bookingUrl`, ONLINE_APPOINTMENT_API_CONTRACT.md §5). Kod site üzerinden görünür; koruma kod doğrulama ve hız sınırına dayanır.

**Klinik alanları (K8)** okuma anında klinik kaydından gelir: klinik kaydı değişince yeniden yayın gerekmez.

**Abonelik kapanması (K13, kullanıcı kararı "Hemen kapansın"):** ek süre yoktur. Arka plan işi yoktur, durum her istekte
abonelikten türer. Veri silinmez: taslak, sürümler, adres, eski adresler ve görseller durur. Ödeme yapılınca son yayınlanan sürüm
kendiliğinden döner; klinik elle kapattıysa kapalı kalır. Kapanma nedeni, abonelik ve kiracı bilgisi dönmez.

### `GET /api/v1/public/sites/{slug}/images/{imageId}`

Yalnız **yayındaki sürümün başvurduğu** görseli sunar. Taslak görseli, başvurulmayan görsel, başka sitenin görseli, kapalı site,
hasta dosyası kimliği: 404 `PublicSites.NotFound`. Kapalı sitede eski adres de 404'tür; açık sitede eski adresten gelen istek yeni
adresteki gibi yanıtlanır.

Yanıt: görselin türü, `Content-Disposition: inline`, `ETag` (içerik özeti, `If-None-Match` ile 304), `Cache-Control: public,
max-age=300`, `X-Content-Type-Options: nosniff`. Önbellek süresi yüzünden kapanan sitenin görseli istemci önbelleğinde en çok
5 dakika görünebilir; sunucu kapanma anından itibaren sunmaz. Site JSON'u önbellek başlığı taşımaz.

Motor başvuruyu çözer: `site-image:{id}` → `/api/v1/public/sites/{slug}/images/{id}`.

## 7. Hata kodları

| HTTP | Kod | Durum |
|---|---|---|
| 400 | `ClinicSites.Validation` | Yapılandırma (§3), adres biçimi (§4), `rowVersion` eksik ya da bozuk |
| 403 | `Auth.PermissionDenied`, `Subscriptions.TenantReadOnly`, `Subscriptions.TenantCancelled`, `Clinics.AccessDenied` | Yetki, abonelik yazma kapısı, atanmamış klinik |
| 404 | `Clinics.NotFound`, `ClinicSites.NotFound`, `ClinicSites.VersionNotFound`, `ClinicSites.ImageNotFound` | Yönetim uçları |
| 404 | `PublicSites.NotFound` | Herkese açık uçlar |
| 409 | `ClinicSites.ConcurrencyConflict` | Eski `rowVersion` ya da eşzamanlı ilk kayıt |
| 409 | `ClinicSites.SlugTaken` | Adres başkasında |
| 409 | `ClinicSites.AddressLimitReached`, `ClinicSites.AddressChangeLimitReached` | §4 |
| 409 | `ClinicSites.PublishRequirementsNotMet` | `missing`, `missingImageIds` |
| 409 | `ClinicSites.ImageInUse` | `usedIn` |
| 409 | `ClinicSites.ClinicInactive` | Pasif klinikte yayın |
| 413, 415, 409, 503 | `Files.*` | §5 |
| 429 | `RateLimit.Exceeded` | `public-site`, `public-site-image` |

## 8. Migration

`AddClinicSites` — `ClinicSites`, `ClinicSiteVersions`, `ClinicSiteAddresses`, `ClinicSiteImages`.

`AddClinicSiteAddressChangeWindow` — `ClinicSites.AddressChangeWindowStartedAtUtc`, `ClinicSites.AddressChangesInWindow` (§4).

Paylaşılan ya da üretim veritabanına uygulanmadı.

## 9. Bu aşamada olmayanlar

- **Sunucuda görsel işleme (K12):** bilinçli olarak yok, küçültme ve EXIF temizliği tarayıcıdadır (§5). Backend'de kütüphane yoktur.
- **Hazır fotoğraf seti (K7):** backend yalnız `stock:{anahtar}` biçimini kabul eder, dosyaları sunmaz.
- **SSR ve hız sınırı:** sınır istemci IP'sine bağlıdır. Site uygulaması sunucuda çalışınca istekler tek IP'den gelir; 3. aşamada
  SSR gerçek istemci IP'sini `X-Forwarded-For` ile iletmelidir. Backend bu başlığa yalnız güvendiği vekilden inanır: varsayılan
  loopback'tir, davranış değişmedi. SSR ya da ters vekil başka makinedeyse adresi `ForwardedHeaders:KnownProxies` listesine yazılır
  (IP dizisi; geçersiz değer açılışta hata verir). Hiçbir adrese kendiliğinden güvenilmez.
- **CORS:** herkese açık JSON tarayıcıdan okunacaksa site adresi `AllowedOrigins`'e eklenmelidir (ADR-033 geçişi).
- **Tam adres:** `publicPath` yol verir; alan adı yayın işidir (K12).
