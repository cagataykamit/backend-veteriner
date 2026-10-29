# Rapor tercihleri — API sözleşmesi (REPORT-003 favoriler, REPORT-004 kayıtlı filtreler)

Backend tek doğruluk kaynağıdır. Kaynak: backlog REPORT-003 ve REPORT-004 (ürün deposu), ADR-003 (Rapor Merkezi). Hata zarfı standart `ProblemDetails` + `extensions.code` (`BACKEND-CONTRACT-STANDARD.md` §9).

> **Durum:** uygulandı, dal `feat/rapor-favori-filtre`. Migration `AddReportFavorites` (tablo `ReportFavorites`) ve `AddReportSavedFilters` (tablo `ReportSavedFilters`). Yeni izin yok.

## 1) Kapsam ve kurallar

- Kayıtlar **kiracı + kullanıcı** kapsamındadır; ikisi de JWT'den (`tenant_id`, kullanıcı kimliği) gelir, istekten alınmaz. Aynı kullanıcının başka kiracıdaki favorileri görünmez; aynı kiracıdaki diğer kullanıcılar birbirinin kaydını görmez.
- Kimlik: personel JWT, `[Authorize]`. Ayrı izin yoktur: kayıt rapor verisi açmaz. Raporun Rapor Merkezi'nde görünüp açılması paneldeki izin kuralına (REPORT-006) bağlıdır; panel izni olmayan raporun favorisini göstermez.
- Rapor kataloğu panelde tutulur; backend yalnız **rapor anahtarının biçimini** doğrular (`Domain/Reports/ReportKey`): 1–100 karakter, küçük harf, rakam, `-` ve `.`; ayraçla başlayıp bitemez. Panel anahtarı rapor adresinden türetir: `/panel/reports/` sonrası, `/` → `.` (ör. `payments`, `end-of-day`, `registers.examinations`).
- Deneme sona erip kiracı salt okunur olduğunda yazma uçları abonelik yazma korumasına takılır (diğer yazma uçlarıyla aynı); okuma çalışır.

## 2) Favoriler

| Uç | Yanıt | Not |
|---|---|---|
| `GET /api/v1/reports/favorites` | 200 `[{ "reportKey": "payments", "createdAtUtc": "2026-10-30T08:00:00Z" }]` | Eklenme sırasıyla (eşitse anahtar) |
| `PUT /api/v1/reports/favorites/{reportKey}` | 204 | Ekler; zaten favorideyse yine 204 (idempotent; eşzamanlı ekleme de 204) |
| `DELETE /api/v1/reports/favorites/{reportKey}` | 204 | Çıkarır; favoride değilse yine 204 |

Hatalar:

| Durum | Kod |
|---|---|
| 400 | `Validation` (anahtar biçimi) |
| 400 | `ReportFavorites.LimitReached` — kullanıcı başına en çok 50 favori |
| 401 | Token yok |

Benzersizlik: `(TenantId, UserId, ReportKey)` benzersiz indeks.

## 3) Kayıtlı filtreler

Bir rapordaki filtre setini adla kaydetme, listeleme, uygulama (panel) ve silme. Kiracıda paylaşım yoktur; güncelleme yoktur (sil + yeniden kaydet).

| Uç | Yanıt | Not |
|---|---|---|
| `GET /api/v1/reports/saved-filters?reportKey=payments` | 200 `[ReportSavedFilterDto]` | Oturumdaki kullanıcının o rapordaki kayıtları, ada göre (Türkçe karşılaştırma anahtarı) |
| `POST /api/v1/reports/saved-filters` | 201 `ReportSavedFilterDto` | Gövde: `{ "reportKey": "payments", "name": "Nakit tahsilatlar", "filters": { "method": "Cash", "clinicId": "…" } }` |
| `DELETE /api/v1/reports/saved-filters/{id}` | 204 | Başka kullanıcının kaydı 404 |

`ReportSavedFilterDto`: `{ "id": "…", "reportKey": "payments", "name": "Nakit tahsilatlar", "filters": { "method": "Cash" }, "createdAtUtc": "…" }`

Kurallar (`Domain/Reports/ReportSavedFilter`, tek yer):

- `name`: kırpılır, art arda boşluk teke iner; 1–100 karakter. Aynı kullanıcının aynı raporunda Türkçe harf ve büyük-küçük farkı yok sayılarak benzersiz (`İptaller` = `iptaller`).
- `filters`: JSON nesnesi, metin → metin. En çok 20 alan; alan adı harfle başlar, yalnız harf/rakam (en çok 50); değer boş olamaz, en çok 200 karakter. Boş nesne geçerli. Backend alanları **yorumlamaz**: panelin rapor sorgu parametreleridir (`clinicId`, `search`, `method`, `status`, `minAmount`, `includeVoided`, `productId`, `veterinarianId`, `hbsStatus`, `kind`, `paymentMethod`, `performedByUserId`). Uygulanırken rapor ucu her değeri yine kendi kuralıyla doğrular (kayıtlı `clinicId` erişimi kalkmışsa rapor ucu 403 döner, panel filtreyi temizler).
- Panel tarih aralığını kaydetmez: uygulandığında ekrandaki dönem korunur (kaydedilen filtre her dönemde yeniden kullanılır).
- Kullanıcı başına, rapor başına en çok 20 kayıt.

Hatalar:

| Durum | Kod |
|---|---|
| 400 | `Validation` (anahtar, ad, filtre biçimi) |
| 400 | `ReportSavedFilters.LimitReached` |
| 404 | `ReportSavedFilters.NotFound` |
| 409 | `ReportSavedFilters.DuplicateName` |

Benzersizlik: `(TenantId, UserId, ReportKey, NormalizedName)` benzersiz indeks; filtreler `FiltersJson` kolonunda (nvarchar(max)).

## 4) Testler

- Domain: `ReportFavoriteTests` (anahtar biçimi, oluşturma), `ReportSavedFilterTests` (ad, normalleştirme, filtre sınırları).
- Application: `ReportFavoriteHandlersTests` (ekleme, idempotent ekleme, tavan, kullanıcı bağlamı yok, silme, listeleme, doğrulayıcı), `ReportSavedFilterHandlersTests` (kayıt, aynı ad, tavan, başkasının kaydı 404, silme, listeleme, doğrulayıcı).
- Entegrasyon: `ReportPreferencesIntegrationTests` (401, kullanıcı ayrımı, kiracı ayrımı, idempotent ekle/çıkar, geçersiz anahtar 400; kayıtlı filtre oluştur/listele/sil, kullanıcı ve rapor ayrımı, Türkçe büyük-küçük harf duyarsız 409, geçersiz alan adı 400).
