# Veteriner bul — API sözleşmesi

Kaynak karar: ürün deposu `decisions/ADR-047-veteriner-bul.md` (K1–K9), backlog TIER-015. Hayvan sahibi il, ilçe ve hizmete göre
listelenen klinikleri bulur; sayfalar `vetinity.com/veteriner/...` altında site uygulamasında sunucuda çizilir (frontend
`docs/SITE_APP.md`). Tüm saatler UTC ISO-8601 taşınır. Hata gövdesi mevcut ProblemDetails (`code` alanı). Paket kilidi yoktur, tüm
paketlerde çalışır (K1); listelenme ücretsizdir (K2).

## 1. Model ve kurallar

| Konu | Kural | Tek yer |
|---|---|---|
| Kayıt | `ClinicVetFinderListing`: kiracı + klinik başına bir satır; listelenme, hizmetler, isteğe bağlı konum, herkese açık anahtar. | Domain `ClinicVetFinderListing` |
| Katılım (K4) | Kliniğin seçimi, varsayılan **kapalı**. Kapatınca hemen çıkar: liste önbelleklenmez. | Domain |
| Listelenme koşulu | Klinik listelenmeyi açmış **ve** klinik aktif **ve** kiracı aktif **ve** abonelik yazmaya izin veriyor (deneme ya da ödenmiş dönem). Salt okunur (deneme/dönem bitti) ve iptal kiracı listelenmez; ödeme yapılınca kendiliğinden döner (klinik sitesi K13 ile aynı ilke). | `VetFinderDirectory` |
| Klinik sitesi | Şart değil. Kart klinik kaydıyla çalışır; yayında ve açık site varsa `sitePath` dolar. | `VetFinderDirectory` |
| Hizmetler | Sabit liste, klinik seçer (§3 `services`). Sitedeki serbest hizmet metni filtrelenebilir olmadığı için kullanılmaz. Anahtar adres parçasıdır, değişmez. | Domain `VetFinderServices` |
| İl / ilçe | Klinik kaydındaki serbest metin, adres biçimine çevrilir: `İstanbul` → `istanbul`, `Kadıköy` → `kadikoy`; I/ı/İ/i hepsi `i`, ç ğ ö ş ü dönüşür, harf/rakam dışı tek tire. Aynı biçime düşen yazımlar tek yer sayılır, görünen ad en çok kullanılan yazımdır. İlçesi olmayan klinik yalnız il sayfasında görünür. | Domain `TurkishSlug`, `VetFinderRegions` |
| Şu an açık | İstanbul saatiyle; o günün satırı açık, an `[açılış, kapanış)` içinde ve molada değil. Satırı olmayan gün kapalıdır; varsayılan program kullanılmaz (kliniğin kendi bilgisi). | Domain `ClinicWorkingHour.IsOpenAt` |
| Sıralama (K3) | Önce şu an açık; sonra ziyaretçi konum verdiyse konumu olan klinikler mesafeye göre; sonra aynı ilçe, aynı il; eşitlikte ad (Türkçe karşılaştırma anahtarı), sonra anahtar. Ödeme ya da paket sıraya girmez. | Domain `VetFinderRanking` |
| Dizin (K8) | Eşleşen klinik sayısı (şu an açık filtresi ve konum hariç) `minClinicsToIndex`'ten (3) azsa `indexable=false`. | Domain `VetFinderPages.MinClinicsToIndex` |
| Konum | Kliniğin enlem/boylamı isteğe bağlıdır, Türkiye sınırları içinde (enlem 35.5–42.5, boylam 25.5–45.0). Herkese açık yanıtta konum dönmez, yalnız mesafe. | Domain |

## 2. Panel — `/api/v1/clinics/{clinicId}/vet-finder`

Yetki online randevu ayarlarıyla aynıdır: okuma `Clinics.Read`, yazma `Clinics.Update`; kiracı filtresi ve klinik ataması uygulanır
(başka kiracının kliniği 404 `Clinics.NotFound`). Abonelik yazma kapısı `PUT`'a uygulanır (salt okunur ya da iptal kiracıda 403
`Subscriptions.TenantReadOnly` / `Subscriptions.TenantCancelled`).

| Uç | Yetki | Açıklama |
|---|---|---|
| `GET` | `Clinics.Read` | `ClinicVetFinderListingDto`. Kayıt yoksa `isListed=false`, hizmet ve konum boş. |
| `PUT` | `Clinics.Update` | `{ isListed, services: ["asi"], latitude, longitude }`. İlk kayıtta herkese açık anahtar üretilir. Denetim: `VetFinder.Listing.Update`. |

```json
// ClinicVetFinderListingDto
{ "clinicId": "…", "isListed": true, "services": ["asi", "cerrahi"], "latitude": 40.990123, "longitude": 29.028765,
  "publicPath": "/veteriner/istanbul/kadikoy", "missing": ["phone"],
  "availableServices": [{ "key": "genel-muayene", "name": "Genel muayene" }, { "key": "asi", "name": "Aşı" }] }
```

- `services` liste sırasıyla döner, tekrar düşer. Bilinmeyen anahtar 400 `VetFinder.Validation`.
- Konum ikisi birlikte ya da ikisi boş; yarım ya da Türkiye dışı 400 `VetFinder.Validation`. Altı ondalığa yuvarlanır.
- `publicPath` kliniğin görüneceği ilçe sayfasıdır (ilçe yoksa il sayfası); alan adı yayın işidir.
- `missing`: kartı zayıflatan eksik klinik bilgisi, listelenmeyi **engellemez**. Değerler `district`, `address`, `phone`,
  `workingHours` (açık işaretli hiç satır yok). Klinik kaydı mevcut `/api/v1/clinics/{id}` ve `/working-hours` uçlarıyla değişir.

## 3. Herkese açık — `GET /api/v1/public/vet-finder/regions`

Kimliksiz. Hız sınırı klinik sitesinin `public-site` politikası (IP başına dakikada 120); aşımda 429 `RateLimit.Exceeded`. Önbellek
başlığı yoktur. Yalnız listelenen kliniği olan il ve ilçeler döner.

```json
{ "totalClinics": 4, "minClinicsToIndex": 3,
  "cities": [ { "slug": "istanbul", "name": "İstanbul", "clinicCount": 3,
      "districts": [ { "slug": "kadikoy", "name": "Kadıköy", "clinicCount": 2, "services": [ { "key": "asi", "clinicCount": 2 } ] } ] } ],
  "services": [ { "key": "genel-muayene", "name": "Genel muayene" }, { "key": "asi", "name": "Aşı" } ] }
```

İl ve ilçeler ada göre (Türkçe karşılaştırma anahtarı) sıralı. İlçenin `services` listesinde yalnız en az bir kliniği olan hizmet
bulunur; site haritası buradan üretilir. `services` (kök) bütün hizmet listesidir, liste sırasıyla.

Hizmet listesi (anahtar → ad): `genel-muayene` Genel muayene, `asi` Aşı, `kisirlastirma` Kısırlaştırma, `cerrahi` Cerrahi,
`dis-tedavisi` Diş tedavisi, `laboratuvar` Laboratuvar, `rontgen` Röntgen, `ultrason` Ultrason, `mikrocip` Mikroçip, `acil` Acil,
`evde-muayene` Evde muayene, `egzotik-hayvan` Egzotik hayvan.

## 4. Herkese açık — `GET /api/v1/public/vet-finder/clinics`

Sorgu: `city`, `district`, `service` (adres biçiminde; büyük/küçük harf ve baş/son boşluk önemsiz), `openNow` (`true` ise yalnız şu
an açık), `page` (1'den, en çok 1000; sayfa `pageSize` = 24), `lat` + `lng` (ziyaretçi konumu, birlikte). `city` yoksa bütün
Türkiye; `district` için `city` zorunlu. Hız sınırı ve önbellek §3 ile aynı.

**Konum:** sunucu iki ondalığa (yaklaşık 1 km) yuvarlayıp yalnız sıralama için kullanır; saklamaz, yanıtta döndürmez, günlüğe yolun
sorgu kısmı yazılmaz. Site uygulaması konumu ziyaretçinin tarayıcısından kendi sunucusuna gövdeyle alır ve bu uca sunucudan iletir
(frontend `docs/SITE_APP.md`).

```json
{ "city": { "slug": "istanbul", "name": "İstanbul" }, "district": { "slug": "kadikoy", "name": "Kadıköy" },
  "service": { "key": "asi", "name": "Aşı" }, "openNow": false, "usesDistance": false, "anyClinicHasLocation": true,
  "indexable": false, "total": 2, "page": 1, "pageSize": 24,
  "clinics": [ { "key": "k7m2p9xq4r", "name": "Patiler Veteriner", "city": "İstanbul", "citySlug": "istanbul",
      "district": "Kadıköy", "districtSlug": "kadikoy", "address": "Moda Cad. 1", "phone": "05550000000",
      "services": [ { "key": "asi", "name": "Aşı" } ],
      "workingHours": [ { "dayOfWeek": 1, "isClosed": false, "opensAt": "09:00:00", "closesAt": "18:00:00", "breakStartsAt": null, "breakEndsAt": null } ],
      "isOpenNow": true, "distanceKm": null, "sitePath": "/k/patiler-veteriner",
      "bookingUrl": "https://portal…/randevu/k7m2p9xq4r8t", "showsNextSlot": true } ],
  "suggestedDistricts": [] }
```

| Alan | Anlam |
|---|---|
| `key` | Kartın herkese açık anahtarı (10 karakter). Kiracı, klinik ve site kimliği dönmez. |
| `workingHours` | Klinik kaydındaki satırlar; `dayOfWeek` mevcut klinik saat uçlarıyla aynı biçim. Satır yoksa boş. |
| `distanceKm` | Yalnız konum verildiyse ve kliniğin konumu varsa; bir ondalık. |
| `sitePath` | Yayında ve geçici kapalı olmayan klinik sitesi (`/k/{adres}`); yoksa `null`. |
| `bookingUrl` | Online randevu açıksa ADR-042 talep bağlantısı. Site bu bağlantıya `?kaynak=veteriner-bul` ekler (§6). Kapalıysa `null`, düğme telefona döner. |
| `showsNextSlot` | Online randevu açık ve klinik en yakın saat gösterimini kapatmamış (TIER-017). `false` ise §5 sorulmaz. |
| `usesDistance` | Konum sıralamaya girdi. `anyClinicHasLocation`: sonuçlarda konumu olan klinik var (konum düğmesi bunun için). |
| `indexable` | K8 (§1). Site sorgu filtreli sayfaları (`openNow`, konum, `page>1`) ayrıca dizine almaz. |
| `suggestedDistricts` | Yalnız ilçe verilmiş ve sonuç yokken: aynı ildeki diğer ilçeler, istenen hizmeti veren klinik sayısıyla (çok olan önce, en çok 6). Konum verisi olmadığı için "yakın" aynı il demektir. |

E-posta, açıklama, hasta, kullanıcı, abonelik ve paket bilgisi dönmez. Yorum ve puan yoktur (K5).

| Durum | Yanıt |
|---|---|
| Bilinmeyen il, ilçe ya da hizmet; ilde listelenen klinik yok | 404 `VetFinder.NotFound` (ayrım yapılmaz) |
| `district` var, `city` yok; `page`, `lat`/`lng` aralık dışı ya da biri eksik | 400 doğrulama hatası |
| Filtreyle sonuç yok | 200, `total=0`, ilçe sayfasında `suggestedDistricts` |

## 5. Herkese açık — `GET /api/v1/public/vet-finder/next-slots?keys=a,b,c`

Kartın en yakın müsait saati (K9, TIER-017 yolu): anahtar → listelenen klinik → online randevu kapısı
(`OnlineBookingClinicResolver.ResolveByClinicAsync`) → `AppointmentRequestIntake.GetNextSlotsAsync` ilk saati. İkinci bir müsaitlik
hesabı yoktur (ONLINE_APPOINTMENT_API_CONTRACT.md §12). Bir istekte bir sayfa kart (en çok 24 anahtar); fazlası ya da 10 karakterden
uzun anahtar 400. Hız sınırı §3 ile aynı.

```json
{ "items": [ { "key": "k7m2p9xq4r", "slot": { "date": "2026-11-09", "startUtc": "2026-11-09T06:00:00Z" } },
             { "key": "bilinmeyen", "slot": null } ] }
```

- İstek sırası korunur, tekrar düşer. Bilinmeyen, listelenmeyen anahtar; kapalı online randevu, kapatılmış gösterim, boş takvim:
  `slot: null`, neden ayırt edilmez.
- **Maliyet ve önbellek:** liste ucu saat hesaplamaz; kartlar bu ucu sayfa başına bir kez ister. Hesap klinik başına **60 saniye**
  sunucu belleğinde tutulur; listelenme her istekte yeniden denetlenir. Dolan saat kartta en çok bu süre görünebilir; talep formu
  müsaitliği yine denetler. Yanıt önbellek başlığı taşımaz.
- Kartta saat seçilince bağlantıya `?saat={startUtc}` eklenir (§12 akışı).

## 6. Talep kaynağı (TIER-016)

Kart randevu bağlantısına `?kaynak=veteriner-bul` ekler; portal formu değeri `channel` olarak taşır, sunucu
`AppointmentRequestChannels.ForLink` ile `4 VetFinder` sayar (ONLINE_APPOINTMENT_API_CONTRACT.md §14). Değer kimliksiz istemciden
gelir; yalnız istatistiği etkiler.

## 7. Hata kodları

| HTTP | Kod | Durum |
|---|---|---|
| 400 | `VetFinder.Validation` | Bilinmeyen hizmet, yarım ya da Türkiye dışı konum (panel) |
| 400 | `Validation.FluentValidation` | Sorgu ya da gövde biçimi |
| 403 | `Auth.PermissionDenied`, `Clinics.AccessDenied`, `Subscriptions.TenantReadOnly`, `Subscriptions.TenantCancelled` | Panel |
| 404 | `Clinics.NotFound` | Panel: başka kiracının ya da olmayan klinik |
| 404 | `VetFinder.NotFound` | Herkese açık liste: bilinmeyen il, ilçe ya da hizmet |
| 429 | `RateLimit.Exceeded` | `public-site` |

## 8. Migration

`AddClinicVetFinderListings` — `ClinicVetFinderListings` tablosu (benzersiz `TenantId+ClinicId`, benzersiz `PublicKey`, `IsListed`
indeksi). Yalnız yerel geliştirme veritabanına uygulandı; paylaşılan ya da üretim veritabanına uygulanmadı. Geri almak için tablo
düşer, başka tabloya dokunmaz.

## 9. Bu sürümde olmayanlar

- Yorum, puan, öne çıkarma ya da ücretli sıra (K2, K3, K5).
- Konumdan il/ilçe bulma (ters coğrafi kodlama): üçüncü taraf servis gerekir; konum yalnız mesafe için.
- İlçe komşuluğu: "yakın ilçe" önerisi aynı ildeki ilçelerdir.
- Tam il/ilçe kataloğu: yalnız listelenen kliniği olan yerler bilinir; boş il/ilçe sayfası 404'tür.
- CORS: tarayıcı bu uçlara gitmez, site sunucusu gider (ADR-045 K12 ile aynı).
