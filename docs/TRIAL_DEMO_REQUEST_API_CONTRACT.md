# Canlı demo talebi API sözleşmesi — TRIAL-005

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu ADR-001 madde 4 (self-service deneme ana kanal; isteğe bağlı canlı demo korunur), ADR-034 karar 5 ("demo talebi eklenir"), backlog `TRIAL-005`. İlgili: `SALES_PARTNER_DISCOUNT_API_CONTRACT.md` (anonim uç + hız sınırı deseni), `AUTH_TENANT_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme (uygulanıyor, `feat/trial-005-demo-request`). **Migration var** (`DemoRequests` tablosu), yeni izin yok, **1 yeni anonim uç**. Frontend tanıtım sitesi İletişim sayfasındaki demo formunu bu belgeye göre yapar.

---

## 1) Amaç ve sınırlar

Tanıtım sitesinde potansiyel müşteri, hesap açmadan 20 dakikalık görüntülü canlı demo ister. Talep veritabanına yazılır ve (adres yapılandırılmışsa) platform ekibine e-postayla bildirilir.

Ürün kararları (kullanıcı, 2026-10-26):
- Kapsam: anonim form + uç + e-posta bildirimi. **Panelde talep listesi yok** (ilk sürüm; §7).
- Demo herkese açık; site metni çok hekimli/çok şubeli klinikleri vurgular, ana çağrı ücretsiz deneme kalır.
- Demo biçimi "20 dakikalık görüntülü görüşme" (yalnız site metni; API'de süre/kanal alanı yok).

Sınırlar:
- Kiracı verisi değildir; kiracı/klinik bağlamı yok, abonelik yazma korumasına girmez.
- Talep sahibine **onay e-postası gönderilmez** (anonim uç rastgele adrese e-posta tetiklemesin). Site başarı mesajı gösterir.
- IP adresi ve tarayıcı bilgisi **saklanmaz** (kişisel veri asgari).

## 2) `POST /api/v1/public/demo-requests` — anonim

- Hız sınırı `demo-request`: **IP başına 10 dakikada 3** istek; aşımda **429** `RateLimit.Exceeded` (`Retry-After` başlığı).
- Gövde:

```json
{
  "contactName": "Ayşe Yılmaz",
  "clinicName": "Işık Veteriner Kliniği",
  "city": "İzmir",
  "email": "ayse@example.com",
  "phone": "0532 000 00 00",
  "clinicSize": "MultiBranch",
  "currentSoftware": "Excel ve kâğıt defter",
  "message": "Hafta içi öğleden sonra uygunuz.",
  "privacyNoticeAccepted": true,
  "website": ""
}
```

| Alan | Zorunlu | Kural |
|---|---|---|
| `contactName` | evet | Kırpılır; 2–100 karakter |
| `clinicName` | evet | Kırpılır; 2–200 karakter |
| `city` | hayır | Kırpılır; en çok 100; boşsa `null` |
| `email` | evet | Kırpılır; geçerli e-posta, en çok 200 |
| `phone` | hayır | Kırpılır; en çok 30; boşsa `null` |
| `clinicSize` | evet | `SingleVet` (tek hekim) · `SmallTeam` (2–5 hekim) · `MultiBranch` (6+ hekim veya birden fazla şube/hastane); string enum |
| `currentSoftware` | hayır | Kırpılır; en çok 200 |
| `message` | hayır | Kırpılır; en çok 2000 |
| `privacyNoticeAccepted` | evet | `true` olmalı (Gizlilik Politikası / aydınlatma metni okundu; site `/gizlilik` bağlantısı verir) |
| `website` | — | **Tuzak alan** (honeypot). Site gizli tutar, insan doldurmaz. Doluysa yanıt yine **204**, kayıt ve e-posta **yok** |

Yanıtlar:

| Durum | Kod | Açıklama |
|---|---|---|
| **204** | — | Talep alındı (ya da tuzak alan dolu; ayırt edilmez) |
| 400 | `DemoRequests.Validation.InvalidRequestBody` | Gövde boş veya JSON hatalı |
| 400 | `Validation.FluentValidation` | Alan kuralı ihlali; `errors` sözlüğü camelCase alan adlarıyla (`contactName`, `email`, `clinicSize`, `privacyNoticeAccepted` …) |
| 429 | `RateLimit.Exceeded` | Hız sınırı |

Yinelenen talep (aynı e-posta tekrar gönderir) **reddedilmez**; her gönderim ayrı kayıttır (hız sınırı yeterli, ilk sürüm).

## 3) Bildirim e-postası

- Yapılandırma: `DemoRequests:NotifyEmail` (tek adres). Kullanıcı kararı (2026-10-26, AK-6): `vetinity.app@gmail.com`, `appsettings.Production.json` şablonunda. Bu dosya yayına girmez; canlıda ortam değişkeni `DemoRequests__NotifyEmail` olarak verilir. Temel `appsettings.json` ve geliştirme/test ortamında boştur. **Boşken talep yalnız veritabanına yazılır**, e-posta kuyruğa girmez, bilgi logu yazılır (adres maskeli değil çünkü yok; talep sahibinin e-postası logda maskeli).
- Doluysa e-posta mevcut outbox portuyla (`IEmailSender`) talep kaydıyla **aynı `SaveChanges`** içinde kuyruğa girer: ya ikisi birden yazılır ya hiçbiri.
- Konu: `Vetinity demo talebi — {clinicName}`. Gövde düz metin: tüm alanlar (Türkçe etiketlerle), talep kimliği ve UTC zamanı. Yanıt için talep sahibinin e-postası gövdededir (port `Reply-To` desteklemez).
- Kayıtta `NotificationQueuedAtUtc` tutulur: adres sonradan girildiğinde bildirilmemiş talepler veritabanından ayırt edilir (geri dönük gönderim ilk sürümde yok).

## 4) Veri modeli

Tablo `DemoRequests` (kiracısız):

| Kolon | Tip | Not |
|---|---|---|
| `Id` | uniqueidentifier PK | |
| `ContactName` | nvarchar(100) | |
| `ClinicName` | nvarchar(200) | |
| `City` | nvarchar(100) null | |
| `Email` | nvarchar(200) | |
| `Phone` | nvarchar(30) null | |
| `ClinicSize` | int | enum |
| `CurrentSoftware` | nvarchar(200) null | |
| `Message` | nvarchar(2000) null | |
| `CreatedAtUtc` | datetime2 | indeksli |
| `NotificationQueuedAtUtc` | datetime2 null | §3 |

Saklama süresi: ilk sürümde otomatik silme yok; KVKK saklama/imha politikasına (ürün deposu hukuki taslaklar) bağlanması açık iş (§7).

## 5) Kod yerleri

- Domain: `DemoRequests/DemoRequest.cs` (`Create(...)`, `MarkNotificationQueued(utcNow)`; uzunluk sabitleri burada), `DemoRequests/DemoClinicSize.cs`.
- Application: `DemoRequests/Commands/Create/CreateDemoRequestCommand(.Handler, .Validator)`, `Common/Options/DemoRequestOptions.cs` (`DemoRequests` bölümü).
- Infrastructure: `Persistence/Configurations/DemoRequestConfiguration.cs`, `AppDbContext.DemoRequests`, migration `AddDemoRequests`, options kaydı.
- Api: `PublicController.CreateDemoRequest` (`public/demo-requests`), `RateLimitingExtensions.DemoRequestPolicy`.

## 6) Testler

- Domain: kırpma/boş→`null`, uzunluk ihlali, bildirim işareti.
- Application: validator kuralları; handler — tuzak alan doluyken kayıt/e-posta yok, adres boşken e-posta yok ve işaret boş, adres doluyken e-posta konusu/gövdesi ve işaret.
- Entegrasyon (HTTP + LocalDB): 204 ve kayıt; adres yapılandırılmışken outbox mesajı; doğrulama 400 alan adları; tuzak alan 204 ve kayıt yok; hız sınırı (4. istek 429).

## 7) Kapsam dışı / açık

- Panelde platform yöneticisi talep listesi ve durum (arandı/demo yapıldı) — talep birikirse sonraki iş.
- Takvimden saat seçtirme (Calendly vb.) — harici entegrasyon, ürün kararı.
- Talep sahibine onay e-postası, bildirilmemiş taleplerin geri dönük gönderimi.
- Saklama süresi ve imha (KVKK).
