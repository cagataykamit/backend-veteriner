# Deneme kaydı kötüye kullanım önlemleri API sözleşmesi — TRIAL-001

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TRIAL-001`, kullanıcı kararları 2026-11-15: deneme açık kalır; "önce Turnstile ve tek e-posta / tek vergi numarasıyla tek deneme kuralı"; SMS telefon doğrulaması yapılmaz ("SMS'e para vermeyi şu an doğru bulmuyorum", INT-003 ertelendi). İlgili: `LEGAL_ACCEPTANCE_API_CONTRACT.md`, `SALES_PARTNER_DISCOUNT_API_CONTRACT.md`, `EXAMPLE_CLINIC_API_CONTRACT.md` (aynı `owner-signup` ucunun diğer alanları), `PORTAL_API_CONTRACT.md`, `ONLINE_APPOINTMENT_API_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (`feature/deneme-kotuye-kullanim`). **Migration var** (`AddTrialSignupEmailAndIdentity`: `SignupEmailVerifications`, `TrialSignupIdentities`). Yeni uçlar: `GET /public/owner-signup/settings`, `POST /public/owner-signup/email-code`, `POST /public/owner-signup/email-code/verify`, `GET /api/portal/v1/human-verification`. `owner-signup` gövdesinde 2 yeni alan; portal kod istekleri ve online randevu kodu isteğinde yeni `turnstileToken` alanı. Gizlilik 1.1 ve Portal gizlilik 1.1 (yeniden kabul). Yeni izin yok.

---

## 1) Kurallar

| Kural | Değer | Yer |
|---|---|---|
| E-posta doğrulaması | Kayıt, e-postaya gelen 6 haneli kodla doğrulanmadan açılmaz. Kod **10 dk** geçerli, en çok **5 deneme**, **60 sn** yeniden gönderme; yalnız özeti saklanır. Doğrulanınca **30 dk** geçerli, tek kullanımlık kayıt jetonu verilir; yeni kod öncekini ve kullanılmamış jetonu geçersiz kılar | `SignupEmailVerification` |
| Gönderim sınırları | Adres başına kayan 24 saatte **5**, IP başına **10** (IP yalnız özet), tüm adresler için **1.000** kod; ayrıca IP hız sınırı (§2) | aynı |
| Tek e-posta | Bir e-posta adresiyle **tek deneme**. Anahtar küçük harf ve yerel kısımdaki `+etiket` atılmış biçim (`ad+2@x.com` = `ad@x.com`); noktalar korunur. Kayıtlı kullanıcının adresi de reddedilir | `TrialSignupIdentity.ToEmailKey` |
| Tek vergi numarası | Bir **VKN (10 hane) ya da TCKN (11 hane)** ile **tek deneme**; resmî sağlama algoritmaları | `TurkishTaxIdentity` (tek kural) |
| Vergi numarası zorunluluğu | `TrialSignup:RequireTaxIdentityNumber` (varsayılan **açık**). Kapalıyken boş bırakılabilir; girilirse yine doğrulanır ve tek deneme kuralına girer | `TrialSignupOptions` (tek ayar) |
| Kalıcılık | `TrialSignupIdentities` satırı kiracı silinse de kalır (yabancı anahtar yok, kiracıya ait değil); e-posta anahtarı ve vergi numarası üzerinde benzersiz indeks (yarışta ikinci kayıt düşer) | `TrialSignupIdentityConfiguration` |
| Fatura bilgisi | Girilen vergi numarası kiracının fatura profiline (`TenantBillingProfile.TaxNumber`) yazılır; diğer alanları yönetici sonra doldurur | `TenantBillingProfile.CreateWithTaxNumber` |
| Doğrulanmış e-posta | Kayıtta kullanıcı `EmailConfirmed = true` | `User.ConfirmEmail` |
| İnsan doğrulaması | E-posta kodu isteği Cloudflare Turnstile jetonu ister (§4) | `HumanVerificationGuard` |

Sınırlar:
- Davetle hesap açma (`invites/signup-and-accept`) deneme açmadığı için bu kurallara girmez.
- Mevcut kiracılar geriye dönük kayda alınmadı (kayıtlı kullanıcı adresi yine reddedilir).
- Fatura profili ayarlarındaki vergi numarası doğrulaması değişmedi (yalnız 10/11 rakam; sağlama algoritması yok). Tek kurala bağlanması ayrı iş.

## 2) Uçlar

### `GET /api/v1/public/owner-signup/settings` — anonim

```json
{ "taxIdentityNumberRequired": true, "turnstileSiteKey": "0x4AAAA..." }
```

`turnstileSiteKey` boşsa (yerel geliştirme, sahte doğrulayıcı) istemci widget çizmez ve kod isteğini jetonsuz gönderir.

### `POST /api/v1/public/owner-signup/email-code` — anonim

Hız sınırı `signup-email-code`: **IP başına 10 dakikada 5**.

```json
{ "email": "sahip@ornek.com", "turnstileToken": "<widget jetonu>" }
```

**200** `{ "expiresInSeconds": 600, "resendAfterSeconds": 60 }`. Sıra: insan doğrulaması → tek e-posta kuralı (kod gönderilmez) → bekleme → adres/IP/genel sınır → e-posta (outbox).

E-posta konusu "Vetinity doğrulama kodunuz"; gövde düz metin, kod ve geçerlilik süresi.

### `POST /api/v1/public/owner-signup/email-code/verify` — anonim

Hız sınırı `signup-email-verify`: **IP başına dakikada 10**.

```json
{ "email": "sahip@ornek.com", "oneTimeCode": "123456" }
```

**200** `{ "emailVerificationToken": "<jeton>", "expiresAtUtc": "2026-11-15T10:32:00Z" }`. Yalnız adresin son kodu doğrulanır; yanlış deneme yanıt başarısız olsa da kalıcı sayılır.

## 3) `POST /api/v1/public/owner-signup` — yeni alanlar

| Alan | Kural |
|---|---|
| `emailVerificationToken` | Zorunlu; `email` ile doğrulanmış olmalı |
| `taxIdentityNumber` | VKN/TCKN; zorunluluğu `settings`. Doluysa sağlama algoritması (doğrulayıcı) |

Kayıt; kullanıcı, kiracı, abonelik, tek deneme kaydı, fatura profili ve jetonun kullanımını **tek veritabanı işleminde** yazar. Başka bir nedenle reddedilirse jeton kullanılmamış kalır, düzeltilen formla aynı jeton 30 dk içinde kullanılır. Denetimler hukuki metin denetiminden sonra, kullanıcı oluşturulmadan önce: tek e-posta → vergi numarası → e-posta jetonu.

## 4) İnsan doğrulaması (Cloudflare Turnstile)

Kaynak: developers.cloudflare.com/turnstile (2026-11-15 okundu).

| Uç | `action` | Koruma |
|---|---|---|
| `POST /public/owner-signup/email-code` | `signup-email-code` | Her zaman |
| `POST /api/portal/v1/auth/code` | `portal-signin-code` | `Turnstile:ProtectPortalSignIn` (varsayılan açık) |
| `POST /api/portal/v1/auth/password-reset` | `portal-password-reset` | aynı ayar |
| `POST /api/portal/v1/booking/{code}/verification-code` | `booking-email-code` | Her zaman |

Parola ile giriş uçlarında yoktur (kilitleme zaten var).

- Sunucu: `POST https://challenges.cloudflare.com/turnstile/v0/siteverify` (form: `secret`, `response`, `remoteip`, `idempotency_key`); `success`, `action` (eşleşmeli) ve `hostname` (`Turnstile:ExpectedHostname` ya da alt alan adı) denetlenir. Jeton tek kullanımlık, 300 sn geçerli; istemci her istekten sonra widget'ı yeniler.
- Ayarlar: `Turnstile:SiteKey`, `Turnstile:SecretKey` (gizli; user-secrets / ortam değişkeni `Turnstile__SecretKey`), `Turnstile:ExpectedHostname` (üretim `vetinity.com`), `Turnstile:ProtectPortalSignIn`, `Turnstile:TimeoutSeconds` (5, 2–15).
- Anahtarlar girilmemişse: Development ve IntegrationTests'te **sahte doğrulayıcı** (her isteği geçirir, site anahtarı vermez); diğer ortamlarda **yapılandırılmamış** → korunan uçlar **503 `HumanVerification.Unavailable`**. Yani üretimde Cloudflare anahtarları girilmeden **yeni deneme kaydı, portal kodla giriş/sıfırlama ve online randevu talebi durur**; doğrulamasız devam edilmez.
- Yanlış gizli anahtar ya da Cloudflare iç hatası `Unavailable` (yapılandırma sorunu, günlüğe hata); zaman aşımı/bağlantı hatası `Unavailable`; jeton geçersiz/kullanılmış/başka eylem ya da alan adı `Failed`.
- `GET /api/portal/v1/human-verification` (anonim): `{ "turnstileSiteKey": "...", "portalSignInProtected": true }`.
- İstemci: betik `https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit` (paketlenmez), `appearance: interaction-only`, `language: tr`. Panel ve portal ayrı bileşen kopyası taşır (portal panel kodunu içe almaz). Mağaza uygulamasında (Capacitor WebView) çalışması **denenmedi**; çalışmazsa `ProtectPortalSignIn=false`.
- CSP: SPA sayfalarında depoda CSP yok. Eklenirse `script-src` ve `frame-src` için `https://challenges.cloudflare.com` gerekir.

## 5) Hata kodları

| Durum | Kod | Mesaj (`detail`) | Uç |
|---|---|---|---|
| **400** | `HumanVerification.Failed` | Güvenlik doğrulaması tamamlanamadı. Sayfayı yenileyip yeniden deneyin. | korunan uçlar |
| **503** | `HumanVerification.Unavailable` | Güvenlik doğrulaması şu an yapılamıyor. Lütfen biraz sonra yeniden deneyin. | korunan uçlar |
| **409** | `SignupIdentity.DuplicateEmail` | Bu e-posta adresiyle daha önce deneme hesabı açılmış. | email-code, owner-signup |
| **409** | `SignupIdentity.DuplicateTaxIdentity` | Bu vergi numarasıyla daha önce deneme hesabı açılmış. | owner-signup |
| **400** | `SignupIdentity.Validation.TaxIdentityRequired` | Geçerli bir vergi numarası (10 hane) ya da TC kimlik numarası (11 hane) girin. | owner-signup (zorunluyken boş ya da geçersiz) |
| **400** | doğrulama (`errors.taxIdentityNumber`) | aynı mesaj | owner-signup (dolu ama geçersiz) |
| **400** | `SignupEmail.CodeInvalid` | Kod hatalı ya da süresi dolmuş. Kodu kontrol edin veya yeni kod isteyin. | verify |
| **400** | `SignupEmail.VerificationRequired` | E-posta adresinizi doğrulayın. Doğrulamanın süresi dolduysa yeni kod isteyin. | owner-signup |
| **429** | `SignupEmail.ResendTooSoon` | Yeni kod için N saniye bekleyin. `extensions.retryAfterSeconds` = N | email-code |
| **429** | `SignupEmail.DailyLimitReached` | Kod gönderme sınırına ulaşıldı. Lütfen birkaç saat sonra yeniden deneyin. | email-code |
| **429** | `RateLimit.Exceeded` | IP hız sınırı | email-code, verify |

## 6) Denetim ve kişisel veri

- `IAuditableRequest`: `Public.OwnerSignup.EmailCodeRequest`, `Public.OwnerSignup.EmailVerify`, `Public.OwnerSignup` (kaydın kendisi, yeni). Yükte `turnstileToken`, `emailVerificationToken`, `oneTimeCode`, `password` `***`; `taxIdentityNumber` maskeli (`123*****90`, `TurkishTaxIdentity.Mask`).
- Saklama: `TrialSignupIdentities` (e-posta anahtarı, vergi numarası, kiracı, zaman) tek deneme kuralı için hesap kapansa da tutulur (Gizlilik 1.1 §5). Süresi ve kullanılmamış `SignupEmailVerifications` satırlarının temizliği **belirlenmedi / yapılmadı** (ayrı iş, saklama politikasıyla).

## 7) Kalıcılık

`SignupEmailVerifications`: `Id`, `Email` (200), `CodeHash`, `RequestIpHash`, `CreatedAtUtc`, `ExpiresAtUtc`, `FailedAttempts`, `VerifiedAtUtc`, `SignupTokenHash`, `SignupTokenExpiresAtUtc`, `UsedAtUtc`, `InvalidatedAtUtc`; indeksler `(Email, CreatedAtUtc)`, `(RequestIpHash, CreatedAtUtc)`, `(CreatedAtUtc)`.

`TrialSignupIdentities`: `Id`, `TrialTenantId` (bilerek `TenantId` değil: kiracı filtresine girmez, ADR-040), `EmailKey` (200, benzersiz), `TaxIdentityNumber` varchar(11, benzersiz filtreli), `CreatedAtUtc`.

## 8) Testler

- Domain `TrialSignupDomainTests`; Application `TrialSignupServiceTests`, `PublicOwnerSignupCommandHandlerTests`, `PublicOwnerSignupCommandValidatorTests`, `PortalSignInHandlerTests`.
- Entegrasyon `TrialSignupAbuseIntegrationTests` (ayarlar, uçtan uca, Turnstile 400/503, ikinci deneme 409, bekleme 429, jetonsuz/vergi nosuz 400, reddedilen kayıt jetonu tüketmez), `TurnstileHumanVerifierTests` (siteverify eşleme), `OwnerPortalIntegrationTests` (portal ve randevu kodu korunması, ayarla kapatma). Kaydı sınayan diğer testler `IntegrationTestTrialSignup` ile doğrulanmış satır ve yeni VKN alır.
