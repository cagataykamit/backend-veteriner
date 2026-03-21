# Observability Kapanış Analizi

**Tarih:** 2025-03-11  
**Kapsam:** Auth closure / hardening sonrası – health, correlation, enrichment, outbox görünürlüğü

---

## 1. Genel değerlendirme

- **Mevcut yapı büyük ölçüde kurumsal seviyede.** Health check’ler (DB, Outbox) anlamlı; correlation middleware zinciri var; request enrichment log’a ClientIp/UserId/UserEmail ekliyor; audit ClientContext üzerinden CorrelationId alıyor; OutboxProcessor retry/dead-letter logluyor.
- **Tespit edilen eksikler:**
  1. **Correlation zinciri tutarsızlığı:** `UseStatusCodePages` içinde correlation id, `ctx.Items["CorrelationId"]` ile okunuyor; oysa `CorrelationIdMiddleware` değeri `ctx.Items["X-Correlation-ID"]` (Correlation.HeaderName) ile yazıyor. Sonuç: 401/403/404/429 gibi status code sayfalarında response body’deki `correlationId` genelde TraceIdentifier’a düşüyor; zincir kırılıyor.
  2. **HttpAuditContext.CorrelationId:** Sadece `TraceIdentifier` dönüyor; middleware’ın ürettiği correlation id ile aynı değil. Audit aslında `IClientContext` (ClientContext) kullandığı için audit kayıtları doğru; ancak IAuditContext kullanan başka yerler varsa tutarsızlık riski.
  3. **Outbox health:** Pending/dead sayısı ve Degraded/Unhealthy ayrımı iyi; `PendingThreshold` sabit (500). İsteğe bağlı: config’den okuma, “stale” (en eski pending mesaj yaşı) ile Degraded.
  4. **Health/ready çıktısı:** Sadece status + entry status/description/duration; pending/dead sayıları JSON’da yok. İsteğe bağlı: entry data ile sayıları döndürmek.

Büyük mimari değişiklik gerekmiyor; bir tutarlılık düzeltmesi (correlation key) ve isteğe bağlı iyileştirmeler yeterli.

---

## 2. Dosya bazlı analiz

### DatabaseHealthCheck.cs
- **Ne yapıyor:** `CanConnectAsync` ile DB bağlantısı kontrolü; başarısızsa Unhealthy + exception.
- **Eksik:** Sadece “bağlanabiliyor mu”; gerçek bir sorgu (örn. `SELECT 1`) yok. Çoğu senaryoda yeterli; ağır timeout’larda bazen CanConnectAsync bile başarılı olabilir (false negative nadir).
- **Risk:** Düşük.
- **Öneri:** Mevcut hali kabul edilebilir. İsteğe bağlı: `await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct)` ile “gerçek sorgu” eklenebilir.

### OutboxHealthCheck.cs
- **Ne yapıyor:** Pending (ProcessedAtUtc == null && DeadLetterAtUtc == null) ve dead-letter sayıları; dead > 0 => Unhealthy; pending > 500 => Degraded; aksi halde Healthy. Mesajda pending/dead sayıları yazılıyor.
- **Eksik:** Threshold 500 sabit (config yok). “Stale” (örn. en eski pending’in CreatedAtUtc’si > 1 saat) kontrolü yok.
- **Risk:** Düşük.
- **Öneri:** Mevcut mantık yeterli. İsteğe bağlı: PendingThreshold’u config’den (Outbox:PendingWarningThreshold) okumak; isteğe bağlı stale eşiği (örn. 1 saat) ile Degraded.

### CorrelationIdMiddleware.cs
- **Ne yapıyor:** Request header’dan X-Correlation-ID alır veya yeni GUID üretir; `context.Items[Correlation.HeaderName]` (X-Correlation-ID) ile saklar; response OnStarting’de header’a yazar; Serilog’a CorrelationId property basar.
- **Eksik:** Yok; davranış doğru.
- **Risk:** Yok.
- **Öneri:** Dokunma.

### CorrelationIdExtensions.cs
- **Ne yapıyor:** UseCorrelationId extension.
- **Öneri:** Dokunma.

### RequestEnrichmentMiddleware.cs
- **Ne yapıyor:** ClientIp (RemoteIpAddress + X-Forwarded-For fallback), UserId ve UserEmail (claim’lerden); Serilog LogContext’e ClientIp, UserId, UserEmail ekliyor.
- **Eksik:** Route/Method zaten HttpContext’te; audit tarafı ClientContext’ten alıyor. Email log’da “anonymous” ile maskelenmiş; PII riski düşük.
- **Risk:** Düşük.
- **Öneri:** Dokunma.

### RequestEnrichmentExtensions.cs
- **Ne yapıyor:** UseRequestEnrichment.
- **Öneri:** Dokunma.

### ClientContext.cs
- **Ne yapıyor:** IpAddress, UserAgent, Path, Method, CorrelationId (Items[Correlation.HeaderName] ?? TraceIdentifier), UserId (claim’den). AuditBehavior bunu kullanıyor.
- **Eksik:** Yok.
- **Öneri:** Dokunma.

### HttpAuditContext.cs
- **Ne yapıyor:** UserId, Route, HttpMethod, IpAddress, UserAgent, CorrelationId (sadece TraceIdentifier).
- **Eksik:** CorrelationId middleware’daki ile aynı değil; Items[Correlation.HeaderName] kullanılmıyor.
- **Risk:** Orta (IAuditContext başka yerde correlation bekliyorsa).
- **Öneri:** CorrelationId’yi ClientContext ile aynı kaynaktan al: `Items[Correlation.HeaderName] ?? TraceIdentifier`. Böylece tek kaynak olur.

### OutboxProcessor.cs
- **Ne yapıyor:** Periyodik batch, retry + exponential backoff + jitter, dead-letter after MaxRetryCount; dead-letter’da Error, retry’da Warning log (Type, Id, RetryCount).
- **Eksik:** Döngü bazlı “pending count” veya heartbeat log yok; backlog trendi sadece health check’ten görülür.
- **Risk:** Düşük.
- **Öneri:** Mevcut log yeterli. İsteğe bağlı: her N döngüde bir info log ile pending sayısı (teşhis için).

### Program.cs (UseStatusCodePages)
- **Eksik:** `ctx.Items["CorrelationId"]` kullanılıyor; middleware `Items["X-Correlation-ID"]` yazıyor. Sonuç: 401/403/404/429 body’de correlationId çoğu zaman TraceIdentifier.
- **Risk:** Orta (istek zinciri takibinde kırık).
- **Öneri:** Correlation id’yi middleware ile aynı key ile oku: `ctx.Items[Correlation.HeaderName]` (using Backend.Veteriner.Application.Common.Constants eklenmeli).

---

## 3. Health check değerlendirme tablosu

| Check | Şu an ne yapıyor | Eksik nokta | Risk seviyesi | Önerilen iyileştirme |
|-------|-------------------|-------------|---------------|----------------------|
| **DatabaseHealthCheck** | CanConnectAsync ile bağlantı testi; exception’da Unhealthy. | Gerçek sorgu (SELECT 1) yok; çok nadir false negative. | Düşük | Opsiyonel: ExecuteSqlRawAsync("SELECT 1"). |
| **OutboxHealthCheck** | Pending ve dead count; dead>0 => Unhealthy; pending>500 => Degraded; description’da sayılar. | PendingThreshold sabit; stale (yaş) kontrolü yok. | Düşük | Opsiyonel: threshold config; isteğe bağlı stale eşiği. |
| **Liveness (/health/live)** | Predicate = false => hiç check yok, 200. | - | - | Kalabilir (minimal liveness). |
| **Readiness (/health/ready)** | Tüm check’ler; 503 on Degraded/Unhealthy; JSON’da status + results (entry status, description, duration). | Entry’lerde “data” (pending/dead sayıları) yok. | Düşük | Opsiyonel: health check’lerde Data ekleyip response’ta göstermek. |

---

## 4. Correlation / enrichment değerlendirmesi

### Correlation zinciri
- **Request:** Header X-Correlation-ID varsa kullanılır, yoksa GUID üretilir.
- **Middleware:** Items["X-Correlation-ID"], response header, Serilog CorrelationId.
- **ClientContext:** Items[Correlation.HeaderName] ?? TraceIdentifier → audit’e gidiyor, doğru.
- **UseStatusCodePages:** Items["CorrelationId"] aranıyor → **bulunmuyor**; fallback Request.Headers veya TraceId. Düzeltme: Items[Correlation.HeaderName] kullanılmalı.
- **HttpAuditContext:** CorrelationId = TraceIdentifier → middleware ile aynı değil. Düzeltme: Items[Correlation.HeaderName] ?? TraceIdentifier.

### Request enrichment
- **Log:** ClientIp, UserId, UserEmail (Serilog LogContext). Route/Method/Path audit’te ClientContext’ten geliyor.
- **Tutarlılık:** İyi; gereksiz PII yok (email “anonymous” olabiliyor).
- **Eksik alan:** Kritik değil; istenirse RequestId/TraceId de LogContext’e eklenebilir (zaten TraceIdentifier var).

---

## 5. Outbox observability değerlendirmesi

- **Backlog büyümesi:** OutboxHealthCheck pending count ile görülüyor; Degraded/Unhealthy ile uyarı veriyor.
- **Stale mesaj:** En eski pending’in yaşı şu an health’te yok; isteğe bağlı eklenebilir.
- **Dead-letter yoğunluğu:** Dead count health description’da; dead > 0 => Unhealthy.
- **Retry storm:** OutboxProcessor her retry’da Warning log (Type, Id, RetryCount); toplu retry görünür.
- **Processor log’ları:** Dead-letter’da Error, retry’da Warning; operasyonel teşhis için yeterli. İsteğe bağlı: periyodik info ile pending count.

---

## 6. Operasyonel teşhis senaryoları

| Senaryo | Görünürlük | Not |
|--------|------------|-----|
| DB erişimi bozuldu | DatabaseHealthCheck Unhealthy; exception mesajı. | Yeterli. |
| Outbox backlog büyüdü | OutboxHealthCheck Degraded/Healthy description’da pending sayısı. | Yeterli. |
| Outbox processor takıldı | Loop exception’da "Outbox loop error" Error log; uygulama ayakta ama mesaj işlenmez, backlog artar → health Degraded/Unhealthy. | Yeterli. |
| Aynı request zinciri (audit + log) | CorrelationId middleware’da set, ClientContext ile audit’e yazılıyor; log’da CorrelationId Serilog’da. UseStatusCodePages düzeltilirse 401/404 vb. body’de de aynı id. | Düzeltme sonrası tam. |
| Mutasyon neden başarısız | Audit’te Success=false, FailureReason; RequestPayload maskeli. | Yeterli. |
| 429 / auth failure / business failure / exception | Audit’te Action, Success, FailureReason; 429 rate limit response’ta errorCode. Log’da CorrelationId ile eşleşir. | Yeterli. |

---

## 7. Güçlü yönler

- Health check’ler anlamlı (DB gerçekten bağlantı, Outbox dead/pending ve Degraded/Unhealthy ayrımı).
- Correlation middleware tek kaynak (Items + header + Serilog); ClientContext audit ile uyumlu.
- Request enrichment log’da ClientIp, UserId, UserEmail; PII sınırlı.
- Audit’te CorrelationId, Route, Method, ActorUserId; Result failure audit’te Success=false.
- OutboxProcessor retry/dead-letter log’ları net; backoff/jitter var.
- Readiness’ta tüm check’ler çalışıyor; 503 ile orchestration uyumlu.

---

## 8. Eksikler / riskler

- **Correlation:** UseStatusCodePages’ta Items key yanlış → 401/403/404/429 body’de correlationId zinciri kırık. **Düzeltilmeli.**
- **HttpAuditContext.CorrelationId:** TraceIdentifier only → IAuditContext tüketicileri için middleware ile aynı değer olmalı. **Düzeltilmeli.**
- Outbox PendingThreshold sabit; stale (yaş) yok; health response’ta sayılar sadece description’da (parse gerekir). İsteğe bağlı iyileştirmeler.

---

## 9. Nihai aksiyon listesi

**Önce yapılacaklar (tutarlılık)**

1. **Program.cs – UseStatusCodePages:** Correlation id’yi `ctx.Items[Correlation.HeaderName]` ile oku. `using Backend.Veteriner.Application.Common.Constants;` ekle; `correlationId` atamasında `ctx.Items.TryGetValue(Correlation.HeaderName, out var v)` kullan.
2. **HttpAuditContext.cs:** `CorrelationId` implementasyonunu ClientContext ile aynı yap: `_httpContextAccessor.HttpContext?.Items[Correlation.HeaderName]?.ToString() ?? _httpContextAccessor.HttpContext?.TraceIdentifier`. Gerekirse `using Backend.Veteriner.Application.Common.Constants`.

**Sonra yapılacaklar (isteğe bağlı)**

3. OutboxHealthCheck: PendingThreshold’u config’den (Outbox:PendingWarningThreshold) okumak.
4. OutboxHealthCheck: Stale eşiği (örn. en eski pending > 1 saat) => Degraded.
5. DatabaseHealthCheck: İsteğe bağlı SELECT 1.
6. Health response’ta entry Data ile pending/dead sayılarını döndürmek (mevcut results yapısına data eklenebilir).
7. OutboxProcessor: Her N döngüde bir info log ile pending count (opsiyonel).

**Dokunulmayacaklar**

- CorrelationIdMiddleware, CorrelationIdExtensions, RequestEnrichment (middleware + extensions), ClientContext, AuditBehavior, OutboxProcessor temel davranışı, liveness/readiness endpoint tasarımı.

---

## 10. Örnek kod (yalnızca gerekli düzeltmeler)

### Program.cs – correlation id okuma

```csharp
// Üstte using ekle:
using Backend.Veteriner.Application.Common.Constants;

// UseStatusCodePages içinde correlationId ataması:
var correlationId =
    ctx.Items.TryGetValue(Correlation.HeaderName, out var cidVal) ? cidVal?.ToString() :
    ctx.Request.Headers.TryGetValue(Correlation.HeaderName, out var cidHeader) ? cidHeader.ToString() :
    traceId;
```

### HttpAuditContext.cs – CorrelationId

```csharp
using Backend.Veteriner.Application.Common.Constants;

// CorrelationId property:
public string? CorrelationId =>
    _httpContextAccessor.HttpContext?.Items[Correlation.HeaderName]?.ToString()
    ?? _httpContextAccessor.HttpContext?.TraceIdentifier;
```

Bu iki değişiklik correlation zincirini kapatır ve IAuditContext’i middleware ile uyumlu hale getirir; mimari değişiklik yoktur.
