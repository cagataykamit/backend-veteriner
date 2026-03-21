# Rate Limiting ve Temel Security Hardening – Analiz ve Öneriler

> **Veteriner reposu:** **ContactController** bu depoda yoktur; aşağıdaki Contact bölümleri tarihsel öneridir. [`docs/README.md`](README.md).

**Tarih:** 2025-03-11  
**Kapsam:** Auth closure / hardening sprint – abuse’a karşı auth ve public giriş yüzeyi

---

## 1. Genel değerlendirme

- **Rate limiting altyapısı sağlam.** `RateLimitingExtensions.cs` merkezi, policy’ler net (login, refresh, password-reset-request/confirm, email-verify-request/confirm, contact). 429 yanıtı ProblemDetails-benzeri JSON, Retry-After ve correlation/traceId ile uyumlu. Global limiter (200/dk/IP) ile genel abuse sınırı var.
- **Ana eksik:** **ContactController.SendMessage** için policy tanımlı ama **endpoint’e bağlanmamış** (`[EnableRateLimiting("contact")]` yok). Spam riski açık.
- **Logout / Logout-all:** Auth zorunlu; brute force yok. İsteğe bağlı: çok yüksek sayıda logout-all için per-user limit (düşük öncelik).
- **Email verification:** Policy’ler hazır; projede bu komutları çağıran **public API controller bulunmuyor**. Endpoint eklendiğinde attribute ile bağlanmalı.
- **Me/sessions revoke:** Authorize; risk düşük. İsteğe bağlı per-user limit.
- **Refresh:** AllowAnonymous olduğu için middleware’da `User` boş, key fiilen `ip:anon:ua`. Token bucket (≈30/dk) IP+UA bazlı; makul. Refresh token reuse tespiti uygulama katmanında (handler/db) kalmalı; rate limit tek başına yeterli değil.

---

## 2. Endpoint bazlı risk tablosu

| Endpoint | Brute force | Spam/abuse | Enumeration | Mail bombardımanı | Token abuse | Not |
|----------|-------------|------------|-------------|--------------------|-------------|-----|
| **POST auth/login** | Yüksek | Orta | Orta (user exists) | - | - | Şifre denemesi; rate limit kritik. |
| **POST auth/refresh** | Düşük | Orta | - | - | Orta | Token çalınırsa sürekli refresh; IP+UA limiti var. |
| **POST auth/logout** | - | Düşük | - | - | - | Auth gerekli. |
| **POST auth/logout-all** | - | Düşük | - | - | - | Auth gerekli. |
| **POST password/request-reset** | - | Orta | Orta (email exists) | Yüksek | - | Aynı IP’den çok istek = spam + mail bombardımanı. |
| **POST password/confirm** | - | Orta | - | - | Orta | Token denemesi; IP limiti makul. |
| **Email verification request** (yok) | - | Orta | Orta | Yüksek | - | Policy var, endpoint yok. |
| **Email verification confirm** (yok) | - | Orta | - | - | Orta | Policy var, endpoint yok. |
| **POST contact/message** | - | Yüksek | - | - | - | Spam/form abuse; **rate limit uygulanmıyor**. |
| **GET/DELETE me/sessions** | - | Düşük | - | - | - | Auth gerekli. |

---

## 3. Önerilen rate limit policy tablosu

| Endpoint | Risk | Policy adı | Limit | Window | Key | Gerekçe |
|----------|------|-------------|-------|--------|-----|---------|
| POST auth/login | Brute force | login | 5 | 1 dk | IP | Şifre denemesi sınırı; sliding window. |
| POST auth/refresh | Token abuse | refresh | ~30/dk (token bucket) | 2 sn replenish | IP (sub=anon, UA) | AllowAnonymous olduğu için fiilen IP+UA; refresh sıklığı sınırı. |
| POST auth/logout | Düşük | (yok) | - | - | - | Auth gerekli; ek limit opsiyonel. |
| POST auth/logout-all | Düşük | (yok) | - | - | - | Aynı. |
| POST password/request-reset | Spam + mail | password-reset-request | 3 | 1 dk | IP | Mail gönderimi; az sayıda istek. |
| POST password/confirm | Token denemesi | password-reset-confirm | 10 | 1 dk | IP | Token brute force sınırı. |
| POST email-verify (request) | Spam + mail | email-verify-request | 3 | 1 dk | IP | Request ile aynı mantık. |
| POST email-verify (confirm) | Token denemesi | email-verify-confirm | 10 | 1 dk | IP | Confirm ile aynı mantık. |
| POST contact/message | Spam | contact | 10 | 1 dk | IP | Form spam; **policy var, endpoint’e bağlanmalı**. |
| GET me/sessions | Düşük | (yok) | - | - | - | Opsiyonel per-user limit. |
| DELETE me/sessions/* | Düşük | (yok) | - | - | - | Opsiyonel. |

Mevcut policy değerleri (login 5/dk, password-reset-request 3/dk, contact 10/dk, vb.) kurumsal ve savunulabilir; değişiklik önerisi yok. Sadece **contact** policy’sinin **kullanılması** eksik.

---

## 4. Kod seviyesi inceleme

### RateLimitingExtensions.cs

- **Yeterlilik:** Policy seti ve global limiter uygun. Sliding window / token bucket seçimi doğru. GetClientIp ile IP bazlı key; proxy için ForwardedHeaders (Program.cs’te) var, X-Forwarded-For kullanılıyorsa IP doğru alınmalı (gerekirse GetClientIp’te X-Forwarded-For fallback eklenebilir).
- **Okunabilirlik:** Policy’ler merkezi ve isimlendirme tutarlı.
- **Eksik:** Contact endpoint’ine policy bağlı değil (controller’da attribute yok).

### Controller / attribute eşlemesi

| Controller | Action | Beklenen policy | Mevcut |
|------------|--------|------------------|--------|
| AuthController | Login | login | ✓ EnableRateLimiting("login") |
| AuthController | Refresh | refresh | ✓ EnableRateLimiting("refresh") |
| AuthController | Logout | - | - |
| AuthController | LogoutAll | - | - |
| PasswordController | RequestReset | password-reset-request | ✓ |
| PasswordController | Confirm | password-reset-confirm | ✓ |
| ContactController | SendMessage | contact | **Yok** |
| (EmailVerification) | (request/confirm) | email-verify-request/confirm | Endpoint yok |

### Program.cs

- `AddAppRateLimiting()` ve `UseAppRateLimiting()` doğru yerde. Sıra: Routing → CORS → Rate limiting → Authentication → Authorization. Doğru.

---

## 5. Security hardening ek önerileri (rate limit dışı)

- **Login fail audit:** Zaten IAuditableRequest + AuditBehavior ile login denemesi audit’e yazılıyor; Result.Failure da artık Success=false ile loglanıyor. Ek: “login_failed” benzeri metric veya ayrı bir “security event” logu isteğe bağlı.
- **Refresh reject audit:** Refresh command audit’te; başarısız refresh’ler artık Success=false + FailureReason ile görünüyor. Yeterli.
- **Refresh token reuse:** Güvenlik standardında, bir refresh token tek seferlik kullanılıp rotate edilir; reuse tespit edilirse tüm oturumlar iptal edilir. Bu **handler/db** tarafında (RefreshToken entity, UsedAt, ReuseAttemptAt vb.) yapılmalı; rate limit bunun yerine geçmez. Mevcut handler’da reuse kontrolü varsa dokümantasyonda belirtilmeli.
- **Password reset / email verification spam görünürlüğü:** Audit’te RequestPayload maskeli; Action + Success + FailureReason ile “password_reset_request” / “email_verify_request” sayıları izlenebilir. İsteğe bağlı: 429 sayısını metric olarak açmak (zaten 429 body’de errorCode var).
- **Uniform response:** Login/refresh “invalid credentials” için 401 + aynı ProblemDetails formatı (enumeration’ı zor tamamen engellemez ama farklı mesaj vermemek iyi). Mevcut Result.ToActionResult standardı buna uygun.
- **Enumeration:** “User not found” ile “Wrong password” aynı 401 ve benzer süreyle yanıt verilmeli; mevcut tasarımda handler’lar tek tip hata mesajına yakınsa yeterli. Rate limit + uniform response birlikte enumeration’ı zorlaştırır.

---

## 6. Dosya bazlı değişiklik listesi

| Dosya | Değişiklik |
|-------|------------|
| **Backend.Veteriner.Api/Controllers/ContactController.cs** | `[EnableRateLimiting("contact")]` ekle; `using Microsoft.AspNetCore.RateLimiting`. ProducesResponseType 429 eklenebilir. |
| **Backend.Veteriner.Api/Middleware/RateLimitingExtensions.cs** | Zorunlu değil. İsteğe bağlı: GetClientIp’te X-Forwarded-For oku (proxy arkasında doğru IP). |
| **Email verification controller** (henüz yok) | Eklendiğinde ilgili action’lara `[EnableRateLimiting("email-verify-request")]` ve `[EnableRateLimiting("email-verify-confirm")]` ekle. |

---

## 7. Gerekli kod örneği

### ContactController – rate limiting eklenmesi

```csharp
// ContactController.cs
using Microsoft.AspNetCore.RateLimiting;

// SendMessage action üzerine:
[HttpPost("message")]
[AllowAnonymous]
[EnableRateLimiting("contact")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
public async Task<IActionResult> SendMessage(...)
```

---

## 8. Nihai aksiyon sırası

**Önce yapılacaklar**

1. **ContactController.SendMessage** üzerine `[EnableRateLimiting("contact")]` ekle; 429 ProducesResponseType ekle.
2. (İsteğe bağlı) **GetClientIp:** Proxy kullanılıyorsa `X-Forwarded-For` ilk değerini okuyup IP olarak kullan (güvenilir proxy listesi varsa).

**Sonra yapılacaklar**

3. Email verification public API eklendiğinde ilgili endpoint’lere `email-verify-request` ve `email-verify-confirm` policy’lerini bağla.
4. Logout-all / me/sessions revoke için per-user rate limit sadece ihtiyaç halinde (örn. 30/dk/user).

**Dokunulmayacaklar**

- Login/refresh/password policy limitleri ve key stratejisi.
- Global limiter (200/dk/IP).
- 429 response formatı ve Retry-After.
- Audit/correlation mevcut yapısı.

Bu doküman, risk-temelli ve mevcut mimariye uyumlu bir rate limiting/hardening özeti sunar; tek kritik düzeltme contact endpoint’ine policy bağlanmasıdır.
