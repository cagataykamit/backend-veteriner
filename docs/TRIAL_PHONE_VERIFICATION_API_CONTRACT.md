# Deneme kaydı telefon doğrulaması API sözleşmesi — TRIAL-001 / INT-003

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TRIAL-001` (self-service deneme), `INT-003` (SMS entegrasyonu), kullanıcı kararı 2026-11-15 ("telefon doğrulamasını yapalım, toplu hesap oluşturma riskini düşürelim"). İlgili: `LEGAL_ACCEPTANCE_API_CONTRACT.md`, `SALES_PARTNER_DISCOUNT_API_CONTRACT.md`, `EXAMPLE_CLINIC_API_CONTRACT.md` (aynı `owner-signup` ucunun diğer alanları). Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (`feature/deneme-telefon-dogrulama`). **Migration var** (`AddSignupPhoneVerification`: `SignupPhoneVerifications` tablosu, `Users.PhoneNumber` ve `Users.PhoneVerifiedAtUtc`). **2 yeni anonim uç**, `owner-signup` gövdesinde **2 yeni zorunlu alan**. Yeni izin yok.

---

## 1) Amaç ve kurallar

Ücretsiz deneme açık kalır; toplu ve sahte hesap açmayı zorlaştırmak için deneme hesabı, cep telefonuna gelen tek kullanımlık SMS koduyla doğrulanmadan açılmaz.

| Kural | Değer | Yer |
|---|---|---|
| Telefon | Zorunlu, yalnız Türkiye cep (`5XX XXX XX XX`; `0`, `90`, `+90` önekli, boşluklu ya da tireli yazım kabul) | `TurkishMobilePhone` (tek normalleştirme kuralı, saklama `905XXXXXXXXX`) |
| Kod | 6 hane, **5 dakika** geçerli, en çok **5 deneme**; düz kod saklanmaz (özet satır kimliğiyle tuzlanır) | `SignupPhoneVerification` |
| Yeniden gönderme | **60 saniye** bekleme; yeni kod öncekini (ve doğrulanmış ama kullanılmamış jetonu) geçersiz kılar | aynı |
| Numara sınırı | Numara başına kayan 24 saatte en çok **5** kod | aynı |
| IP sınırı | IP başına kayan 24 saatte en çok **10** kod (IP yalnız özet olarak saklanır); ayrıca hız sınırı politikası (§2) | aynı |
| Genel sınır | Tüm numaralar için kayan 24 saatte en çok **1.000** kod (SMS bütçesi koruması) | aynı |
| Kayıt jetonu | Kod doğrulanınca verilir, **30 dakika** geçerli, tek kullanımlık; düz jeton saklanmaz | aynı |
| Tek deneme | **Bir numarayla yalnız bir deneme hesabı.** Kullanılmış doğrulama satırı numaranın hakkını kalıcı kapatır; veritabanında numara başına tek kullanılmış satır (benzersiz filtreli indeks) | aynı + `UX_SignupPhoneVerifications_Phone_Used` |
| Hesapta saklama | Kayıtta numara kullanıcıda `PhoneNumber` + `PhoneVerifiedAtUtc` olarak yazılır | `User.SetVerifiedPhone` |

Sınırlar:
- Davetle hesap açma (`invites/signup-and-accept`) deneme açmadığı için telefon istemez.
- Mevcut deneme ve ücretli kiracılar etkilenmez (geriye dönük telefon istenmez).
- Kod SMS'i İYS'ye tabi ticari ileti değildir (doğrulama iletisi); yalnız kod ve süre içerir.

## 2) `POST /api/v1/public/owner-signup/phone-code` — anonim

Kod gönderir. Hız sınırı `signup-phone-code`: **IP başına 10 dakikada 5** istek; aşımda **429** `RateLimit.Exceeded`.

```json
{ "mobilePhone": "0532 123 45 67" }
```

**200**:

```json
{ "expiresInSeconds": 300, "resendAfterSeconds": 60 }
```

Hata tablosu §4. Sıra: sağlayıcı yapılandırması → numarayla daha önce deneme açılmış mı → bekleme → numara/IP/genel günlük sınır → gönderim. Satır yalnız gönderim başarılıysa yazılır (başarısız gönderim sınırlardan düşmez).

SMS metni (Türkçe karaktersiz, tek SMS boyu; Netgsm OTP kuralı):

```
Vetinity dogrulama kodunuz: 123456. Kod 5 dakika gecerlidir. Kimseyle paylasmayin.
```

## 3) `POST /api/v1/public/owner-signup/phone-code/verify` — anonim

Hız sınırı `signup-phone-verify`: **IP başına dakikada 10** istek.

```json
{ "mobilePhone": "0532 123 45 67", "oneTimeCode": "123456" }
```

**200**:

```json
{ "phoneVerificationToken": "<tek kullanımlık jeton>", "expiresAtUtc": "2026-11-15T10:32:00Z" }
```

Yalnız numaranın **son** kodu doğrulanır. Yanlış kod denemesi, yanıt başarısız olsa da kalıcı sayılır; 5. yanlışta kod geçersiz olur. Hatalı, süresi dolmuş, hakkı bitmiş ya da hiç gönderilmemiş kodda tek tip **400** `SignupPhone.CodeInvalid`.

## 4) `POST /api/v1/public/owner-signup` — yeni zorunlu alanlar

Gövdeye eklendi:

| Alan | Kural |
|---|---|
| `mobilePhone` | Zorunlu Türkiye cep; doğrulanan numarayla aynı olmalı (yazım biçimi farklı olabilir, normalleştirilir) |
| `phoneVerificationToken` | Zorunlu; §3 yanıtındaki jeton |

Kayıt, doğrulamanın kullanımını kullanıcı, kiracı ve abonelikle **aynı veritabanı işleminde** yazar. Kayıt başka bir nedenle başarısızsa (ör. `Users.DuplicateEmail`) jeton kullanılmamış kalır, kullanıcı formu düzeltip aynı jetonla tekrar dener (30 dakika içinde). Telefon denetimi hukuki metin denetiminden sonra, kullanıcı oluşturulmadan önce yapılır.

### Hata kodları (üç uç)

| Durum | Kod | Mesaj (`detail`) | Uç |
|---|---|---|---|
| **400** | doğrulama (`errors.mobilePhone`) | Geçerli bir cep telefonu numarası girin (5XX XXX XX XX). | üçü |
| **400** | doğrulama (`errors.oneTimeCode`) | 6 haneli kodu girin. | verify |
| **400** | `SignupPhone.CodeInvalid` | Kod hatalı ya da süresi dolmuş. Kodu kontrol edin veya yeni kod isteyin. | verify |
| **400** | `SignupPhone.VerificationRequired` | Telefon numaranızı doğrulayın. Doğrulamanın süresi dolduysa yeni kod isteyin. | owner-signup (jeton yok, eşleşmiyor, başka numaranın, süresi dolmuş ya da yeni kodla geçersizleşmiş) |
| **409** | `SignupPhone.DuplicateTrial` | Bu telefon numarasıyla daha önce deneme hesabı açılmış. | phone-code, owner-signup |
| **429** | `SignupPhone.ResendTooSoon` | Yeni kod için N saniye bekleyin. `extensions.retryAfterSeconds` = N | phone-code |
| **429** | `SignupPhone.DailyLimitReached` | Kod gönderme sınırına ulaşıldı. Lütfen birkaç saat sonra yeniden deneyin. | phone-code |
| **429** | `RateLimit.Exceeded` | IP hız sınırı (`retryAfterSeconds`) | phone-code, verify |
| **503** | `Sms.NotConfigured` | Telefon doğrulaması şu an kullanılamıyor. Lütfen daha sonra yeniden deneyin. | phone-code |
| **503** | `Sms.SendFailed` | Kod gönderilemedi. Lütfen biraz sonra yeniden deneyin. | phone-code |

## 5) SMS sağlayıcı katmanı (INT-003 temeli)

- `ISmsSender` (Application): sağlayıcıdan bağımsız; tek kullanımlık kod **outbox'a girmez**, doğrudan ve zaman aşımlı gönderilir (`Sms:TimeoutSeconds`, varsayılan 10, 3–30).
- Seçim `Sms:Provider`:
  - `Netgsm`: Netgsm SMS API v3 bağdaştırıcısı (§6).
  - `Log`: sahte sağlayıcı; SMS gönderilmez, günlüğe maskeli numara yazılır. Metin (kod dahil) **yalnız Development** ortamında günlüğe yazılır. **Production'da kabul edilmez** (yapılandırılmamış sayılır).
  - Boş: Development ve IntegrationTests'te `Log`; diğer ortamlarda **yapılandırılmamış**.
- **Yapılandırılmamışsa** kod gönderilmez, `phone-code` **503 `Sms.NotConfigured`** döner ve hata günlüğe yazılır. Kayıt jetonsuz yapılamadığı için deneme **sessizce doğrulamasız açılmaz**; sağlayıcı ayarlanana kadar üretimde yeni deneme kaydı durur.
- Gizli değerler depoya yazılmaz: geliştirmede user-secrets, sunucuda ortam değişkeni.

```
Sms__Provider=Netgsm
Sms__Netgsm__Username=<API kullanıcısı>
Sms__Netgsm__Password=<API kullanıcısı parolası>
Sms__Netgsm__Sender=<onaylı SMS başlığı>
# isteğe bağlı: Sms__Netgsm__ApiBaseUrl (varsayılan https://ext-api.netgsm.com.tr; deneme ortamı https://ext-sandbox.netgsm.com.tr),
#              Sms__Netgsm__AppName (varsayılan Vetinity), Sms__Netgsm__Enterprise=true (kurumsal OTP hesabı), Sms__TimeoutSeconds
```

## 6) Netgsm bağdaştırıcısı (kaynak: api-docs.netgsm.com.tr ve netgsm.com.tr/dokuman, 2026-11-15 okundu)

| Konu | Uygulama | Doğrulama |
|---|---|---|
| Kimlik | `POST {ApiBaseUrl}/auth/v3/token` gövde `{ "username", "password", "scope": "sms" }` → `access_token` (24 saat). Jeton süreç belleğinde, bitişinden 5 dk önce yenilenir; 401'de bir kez yenilenip yeniden denenir | Belgeden doğrulandı. Yanıtta `expires_in` alanı **doğrulanmalı** (yoksa 24 saat varsayılır) |
| Gönderim | `POST {ApiBaseUrl}/sms/v3/send/otp`, `Authorization: Bearer`, gövde `{ "sender", "text", "recipient", "appName" }`; numara `905XXXXXXXXX`; başarı **202** + `jobId` | Belgeden doğrulandı; gerçek hesapla **denenmedi** |
| Kurumsal hesap | `x-client: enterprise` başlığı (`Enterprise=true`) | Belgeden doğrulandı |
| Hatalar | 400 `INVALID_REQUEST`, 401 `UNAUTHENTICATED`, 403 `FORBIDDEN` (IP izni dahil), 422 `REJECTED_HEADER` / `INSUFFICIENT_CREDIT`, 503 `SERVICE_UNAVAILABLE`, 500 `INTERNAL_ERROR` → hepsi `Sms.SendFailed`; günlükte yalnız durum ve Netgsm kodu. **500'de yeniden denenmez** (Netgsm: kod gitmiş olabilir) | Belgeden doğrulandı; hata gövdesinde `code` alan adı gerçek yanıtla **doğrulanmalı** |
| Kurallar | Tek alıcı, tek SMS (alfanümerik başlıkta 155 karakter), yalnız onaylı başlık, yurt dışı/sabit hat yok, API alt kullanıcısı zorunlu, panelde API IP izni | Netgsm belge ve SSS |

Eski API'ler (`/sms/send/otp` XML, `/sms/rest/v2/otp`) kullanılmadı; Netgsm resmi SDK'sı bunları "deprecated" işaretliyor.

## 7) Denetim ve kişisel veri

- `IAuditableRequest`: `Public.OwnerSignup.PhoneCodeRequest`, `Public.OwnerSignup.PhoneVerify`, `Public.OwnerSignup` (kaydın kendisi, yeni). `targetId` maskeli numara (`90532*****67`); yükte `mobilePhone` maskeli, `oneTimeCode`, `password`, `phoneVerificationToken` `***`.
- Günlükte numara her zaman maskeli; kod ve jeton yazılmaz (sahte sağlayıcıda metin yalnız Development).
- Gizlilik Politikası 1.1 (2026-11-15): telefon doğrulaması amacı, işlenen veri ve saklama eklendi; `LegalDocumentCatalog` Privacy sürümü 1.1, mevcut kullanıcılardan girişte yeniden kabul istenir (ADR-029).
- Saklama: doğrulama satırları (numara, özetler, zamanlar) numara başına tek deneme kuralı için tutulur. Saklama süresi ve kullanılmamış satırların temizliği **belirlenmedi / yapılmadı** (ayrı iş; saklama politikasıyla birlikte).

## 8) Kalıcılık

`SignupPhoneVerifications` (kiracıya ait değil): `Id`, `Phone` varchar(12), `CodeHash`, `RequestIpHash`, `CreatedAtUtc`, `ExpiresAtUtc`, `FailedAttempts`, `VerifiedAtUtc`, `SignupTokenHash`, `SignupTokenExpiresAtUtc`, `UsedAtUtc`, `UsedByTenantId`, `InvalidatedAtUtc`. İndeksler: `(Phone)` benzersiz filtreli `UsedAtUtc IS NOT NULL`; `(Phone, CreatedAtUtc)`; `(RequestIpHash, CreatedAtUtc)`; `(CreatedAtUtc)`. `Users`: `PhoneNumber` varchar(12) null, `PhoneVerifiedAtUtc` null.

## 9) Testler

- Domain `SignupPhoneVerificationTests` (süre, bekleme, deneme hakkı, jeton, kullanım, maske).
- Application `SignupPhoneVerificationServiceTests` (gönderim, bekleme, numara/IP/genel sınır, kullanılmış numara, sağlayıcı yok, gönderim hatası, doğrulama), `PublicOwnerSignupCommandHandlerTests` (telefon saklama, jeton yok/eşleşmez, tek deneme), doğrulayıcı testleri.
- Entegrasyon `SignupPhoneVerificationIntegrationTests` (HTTP + LocalDB: uçtan uca akış, maskeli denetim, ikinci deneme 409, bekleme 429, jetonsuz/sahte/başka numaranın jetonu 400, geçersiz numara, sağlayıcı yokken 503). Kaydı sınayan diğer entegrasyon testleri doğrulanmış satırı `IntegrationTestSignupPhone` ile hazırlar.
