# Rapor tercihleri — API sözleşmesi (REPORT-003 favoriler)

Backend tek doğruluk kaynağıdır. Kaynak: backlog REPORT-003 (ürün deposu), ADR-003 (Rapor Merkezi). Hata zarfı standart `ProblemDetails` + `extensions.code` (`BACKEND-CONTRACT-STANDARD.md` §9).

> **Durum:** uygulandı, dal `feat/rapor-favori-filtre`. Migration `AddReportFavorites` (tablo `ReportFavorites`). Yeni izin yok.

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

## 3) Testler

- Domain: `ReportFavoriteTests` (anahtar biçimi, oluşturma).
- Application: `ReportFavoriteHandlersTests` (ekleme, idempotent ekleme, tavan, kullanıcı bağlamı yok, silme, listeleme, doğrulayıcı).
- Entegrasyon: `ReportPreferencesIntegrationTests` (401, kullanıcı ayrımı, kiracı ayrımı, idempotent ekle/çıkar, geçersiz anahtar 400).
