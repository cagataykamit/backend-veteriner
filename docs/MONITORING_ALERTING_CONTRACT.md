# İzleme ve uyarı sözleşmesi (Aşama 9, Dilim 5 — kod tarafı)

**Kapsam:** Yapılandırılmış log + korelasyon kimliği, sağlayıcıdan bağımsız hata raporlama soyutlaması, outbox uyarı eşikleri, temel operasyon metrikleri, gösterge paneli veri kaynağı.
**Kapsam dışı (kullanıcı hesabı/anahtarı gerekir):** hata izleme hizmeti hesabı (Sentry, Application Insights vb.), log toplama hizmeti, OTLP toplayıcı/gösterge paneli kurulumu, uptime izleyici ve uyarı kanalı (e-posta/SMS/Teams). Bunlar bu belgede yalnız **bağlanma noktası** olarak tanımlıdır.
**Migration:** yok. **Yeni HTTP ucu:** yok (veri kaynağı `/health/ready` JSON'ıdır).
Mevcut tasarım: [`cqrs/cqrs-11c-monitoring-alerts.md`](cqrs/cqrs-11c-monitoring-alerts.md) (appointment projeksiyonu). Bu belge onu tamamlar, yerine geçmez.

## 1. Korelasyon kimliği

- Başlık: `X-Correlation-ID` (`Correlation.HeaderName`).
- Gelen değer **yalnız** `A-Z a-z 0-9 - _ . :` karakterlerinden oluşuyor ve ≤ 64 karaktersa kabul edilir; aksi halde (log enjeksiyonu / şişirme önlemi) yeni kimlik üretilir. Kural tek yerde: `Correlation.Normalize`.
- Kimlik yanıt başlığına, `HttpContext.Items`'a, Serilog `LogContext` (`CorrelationId`, `TraceId`) ve geçerli `Activity` etiketine (`correlation.id`) yazılır; ProblemDetails `correlationId` alanı aynı değerdir.
- Production günlükleri `CompactJsonFormatter` ile konsola tek satır JSON yazar (`appsettings.Production.json`); `CorrelationId` ve `TraceId` her satırda bulunur.

## 2. Hata raporlama soyutlaması

- Arayüz: `IErrorReporter.Report(Exception, ErrorReportContext)` (`Application/Common/Abstractions`). `ErrorReportContext`: `TraceId`, `CorrelationId`, `StatusCode`.
- Çağrı noktası: genel istisna işleyicisi (`UseGlobalExceptionHandler`), yalnız **beklenmeyen (5xx)** hatalar için. Doğrulama, 401, 404 ve iptal (499) raporlanmaz.
- Varsayılan uygulama: `LoggingErrorReporter` — yapılandırılmış `Error` günlüğü + `ops.errors.unhandled.total` sayacı. Dış hizmete veri göndermez.
- Sağlayıcı bağlama: ilgili sağlayıcı için `IErrorReporter` uygulaması yazılıp DI'da `LoggingErrorReporter` yerine (veya yanına) kaydedilir. **Hesap/DSN gerekir; bu dilimde yapılmadı.** DSN/anahtar yalnız gizli depoya girer, repoya yazılmaz.
- OpenTelemetry iz kaydı zaten istisnaları (`RecordException = true`) OTLP'ye taşır; OTLP uç noktası `OTEL_EXPORTER_OTLP_ENDPOINT` ile verilir (yoksa Development dışında dışa aktarım kapalı).

## 3. Outbox uyarı eşikleri

`OutboxHealth` yapılandırma bölümü (`OutboxHealthOptions`), `/health/ready` içindeki `outbox` kontrolünü besler. Değerlendirme kuralı tek yerde: `OutboxHealthEvaluator`.

| Anahtar | Varsayılan | Anlam |
|---|---|---|
| `PendingDegradedThreshold` | 500 | Bekleyen (işlenmemiş, dead-letter olmayan) mesaj sayısı bunu **aşarsa** Degraded (eski sabit ile aynı) |
| `DegradedAfterSeconds` | 120 | En eski bekleyen mesajın yaşı ≥ bu ise Degraded |
| `UnhealthyAfterSeconds` | 600 | En eski bekleyen mesajın yaşı ≥ bu ise Unhealthy |
| `DeadLetterIsUnhealthy` | true | Dead-letter sayısı > 0 ise Unhealthy |

Önem sırası: dead-letter (Unhealthy) → en eski yaş ≥ Unhealthy → en eski yaş ≥ Degraded → bekleyen sayısı eşiği → Healthy. `/health/ready` Degraded ve Unhealthy'yi 503 döndürür (mevcut davranış korunur). Geri bekleme (retry) yükünü hesaba katmak için varsayılan yaş eşikleri worker döngüsünden (5 sn) belirgin büyüktür.

Projeksiyon gecikme eşikleri değişmedi: `AppointmentProjectionHealth`, `ClientProjectionHealth`, `PetProjectionHealth`, `PaymentProjectionHealth` (`DegradedAfterSeconds`/`UnhealthyAfterSeconds`).

## 4. Metrikler

Meter: `Backend.Veteriner.Operations` (OTel'e kayıtlı). Etiket: yalnız düşük kardinalite; tenant/kullanıcı/kimlik/URL/mesaj etiketi **yok**.

| Enstrüman | Tür | Etiket | Açıklama |
|---|---|---|---|
| `ops.errors.unhandled.total` | Counter | `exception_type` (CLR tür adı) | Raporlanan beklenmeyen hata |
| `ops.outbox.pending` | ObservableGauge | — | Bekleyen outbox mesajı |
| `ops.outbox.dead_letter` | ObservableGauge | — | Dead-letter mesajı |
| `ops.outbox.oldest_pending_age` | ObservableGauge (s) | — | En eski bekleyen yaşı |

Outbox göstergeleri son `/health/ready` sonucundan beslenen bellek içi anlık görüntüden okunur (geri çağrıda SQL çalışmaz). `/health/ready` çağrılmıyorsa değerler eskir: **uptime izleyici bu ucu düzenli çağırmalıdır** (≤ 1 dk).
HTTP istek sayısı/süresi, runtime ve appointment projeksiyon metrikleri mevcut ASP.NET Core / Runtime / `Vetinity.Cqrs.Appointments` enstrümanlarından gelir.

## 5. Gösterge paneli veri kaynağı

`GET /health/ready` JSON'ı (`status`, `results.<kontrol>.{status, description, duration, data}`). `outbox` kontrolünün `data` alanı: `pendingCount`, `deadLetterCount`, `oldestPendingAgeSeconds`. Projeksiyon kontrollerinin `data` alanları mevcut sözleşmedeki gibidir. Yeni yönetim ucu eklenmedi; kimlik doğrulamasız uç yalnız sayaç/durum döndürür, kişisel veri içermez.

## 6. Önerilen uyarı kuralları (kanal kurulduğunda)

| Uyarı | Koşul | Şiddet |
|---|---|---|
| Hazır değil | `/health/ready` 503 ≥ 2 dk | Yüksek |
| Canlı değil | `/health/live` yanıtsız ≥ 1 dk | Kritik |
| Outbox dead-letter | `ops.outbox.dead_letter` > 0 | Yüksek |
| Outbox gecikmesi | `ops.outbox.oldest_pending_age` ≥ 600 sn | Yüksek |
| Beklenmeyen hata | `ops.errors.unhandled.total` artış oranı > 5/5 dk | Orta |
| Projeksiyon gecikmesi | `*-projection` kontrolü Unhealthy | Yüksek |

## 7. Doğrulanmayanlar / kullanıcı işi

- Gerçek bir OTLP toplayıcısı, hata izleme hizmeti, uptime izleyici ve uyarı kanalı bu dilimde kurulmadı/denenmedi; yukarıdaki eşikler ve uyarı kuralları pilot verisiyle ayarlanmamıştır.
- Metrik dışa aktarım adları toplayıcı yapılandırmasına göre değişebilir; alarm kurarken gerçek adı toplayıcıda doğrulayın.
