# Deneme (trial) kısıtları API sözleşmesi — TRIAL-004

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-001 madde 5 (trial hesabı günlük kullanımın büyük bölümünü deneyimler; gerçek dış sistem işlemleri kısıtlanır), backlog `TRIAL-004` (kısıtlar kullanıcıya açık gösterilir), ürün kararı (kullanıcı, 2026-10-24, "Standart" seviye). İlgili: `IMPORT_API_CONTRACT.md` §4.1, `REGISTERS_API_CONTRACT.md`, `DAILY_TRANSACTIONS_REPORT_API_CONTRACT.md`, `OUTSTANDING_BALANCE_REPORT_API_CONTRACT.md`, `BACKEND-CONTRACT-STANDARD.md` §27A.4 (rapor export tavanı), `AUTH_TENANT_CONTRACT.md` (abonelik durumu). Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme (uygulandı). **Migration yok**, yeni izin yok, yeni uç yok; mevcut uçların deneme kiracısındaki davranışı değişir. Frontend bu belgeye göre sınır mesajlarını gösterir ve abonelik çağrısına yönlendirir.

---

## 1) Kararlar (kullanıcı, 2026-10-24)

| # | Karar |
|---|---|
| K1 | Kısıtlar yalnız **deneme** kiracısında geçerlidir. Deneme = abonelik kaydının saklanan durumu `Trialing`. Süresi bitmiş deneme (etkin durum `ReadOnly`, yazma zaten kapalı) da ücretli değildir; okuma/dışa aktarma kısıtı sürer. Kısıt **abonelikle kalkar** (`ActivatePaidPlan` → `Active`). Abonelik kaydı olmayan kiracı güvenli taraf olarak kısıtlı sayılır. |
| K2 | **Hatırlatma e-postası:** kiracı başına **UTC gün** içinde en fazla **20** e-posta kuyruğa alınır. Fazlası gönderilmez; dispatch log'a `Skipped` + neden `TrialDailyLimit` yazılır. |
| K3 | **Rapor dışa aktarımı (CSV/XLSX/PDF):** eşleşen satır **> 1.000** ise `422 Trial.ExportLimitExceeded` (PDF REPORT-007 ile eklendi, aynı kural; `REPORT_PDF_EXPORT_API_CONTRACT.md`). Ücretli kiracıda tavan `ReportsSharedLimits.MaxExportRows` (50.000) ve modülün kendi `*.ExportTooManyRows` kodu değişmeden kalır. |
| K4 | **İçe aktarma:** dosyada **tablo başına > 500** veri satırı ise `422 Trial.ImportLimitExceeded` (parti oluşturulmaz). Ücretli kiracıda `Imports:Limits.MaxRowsPerTable` (10.000) ve `Imports.TooManyRows` (400) değişmez. Parti toplamı (25.000) ve diğer tavanlar her kiracıda aynıdır. |
| K5 | HBS/e-belge ve ödeme sağlayıcı entegrasyonları üründe yok; kısıt gerekmez. SMS yok. |
| K6 | Kısıt kullanıcıya **açık** gösterilir: `code` `Trial.` önekli, `detail` "Deneme sürümünde … sınırı; abonelikle kalkar" biçiminde. |

## 2) Tek kaynak (kod)

- Sabitler ve mesajlar: `Application/Tenants/TrialLimits.cs` (`ReminderEmailsPerDay = 20`, `ExportMaxRows = 1_000`, `ImportMaxRowsPerTable = 500`, `ReminderDailyLimitSkipReason = "TrialDailyLimit"`).
- Karar: `ITrialLimitPolicy` / `TrialLimitPolicy` (scoped; kiracı başına istek içinde tek sorgu). `IsTrialAsync(tenantId)`, `GetExportRowLimitAsync(tenantId)` → `ExportRowLimit(MaxRows, IsTrial)`; `ExportRowLimit.Ensure(total, paidErrorCode)` aşım hatasını üretir.
- Uygulama noktaları: rapor export pipeline'ları ve `RegisterReportRunner` (tavanı politikadan alır, okuyucuya `take` olarak da verir), `ImportUploadReader.Read(upload, trialTenant)` + `ImportFileLimits.ForTrial()`, `ReminderProcessorService` (günlük bütçe: o UTC gün `Status != Skipped` log sayısı).
- HTTP eşlemesi: `ResultExtensions.MapStatusCode` — `trial.*` → **422**.

## 3) Etkilenen uçlar ve kodlar

### 3.1 Rapor dışa aktarımı — `Trial.ExportLimitExceeded` (422)

Tüm CSV/XLSX/PDF export uçları (`/export`, `/export-xlsx`, `/export-pdf`); JSON/sayfalı uçlar etkilenmez. PDF ücretli kiracıda ayrıca 5.000 satırla sınırlıdır (`ExportRowLimit.ForFormat`).

| Uç | Ücretli kod (değişmedi, 400) |
|---|---|
| `GET /reports/payments/export`, `/export-xlsx` | `Payments.ReportExportTooManyRows` |
| `GET /reports/appointments/export`, `/export-xlsx` | `Appointments.ReportExportTooManyRows` |
| `GET /reports/examinations/export`, `/export-xlsx` | `Examinations.ReportExportTooManyRows` |
| `GET /reports/vaccinations/export`, `/export-xlsx` | `Vaccinations.ReportExportTooManyRows` |
| `GET /reports/outstanding-balances/export`, `/export-xlsx` | `OutstandingBalances.ReportExportTooManyRows` |
| Defter export uçları (`/reports/registers/*/export`, `/export-xlsx`; muayene, reçete, aşı, ilaç, narkotik, aşı bilgi, HBS) | `Registers.ExportTooManyRows` |
| `GET /reports/daily-transactions/export`, `/export-xlsx` | `Registers.ExportTooManyRows` |

Kural: önce `Count`; deneme kiracısında eşleşen satır **> 1.000** → 422; satırlar belleğe çekilmez, dosya üretilmez. Kasa özeti (`/reports/cash-summary/*`) satır dışa aktarımı değildir, kısıt yoktur.

Yanıt (örnek):

```json
{
  "status": 422,
  "title": "İş kuralı ihlali",
  "detail": "Deneme sürümünde dışa aktarma en fazla 1000 satır sınırı; abonelikle kalkar. Filtreyi daraltın.",
  "type": "https://httpstatuses.io/422",
  "instance": "/api/v1/reports/payments/export",
  "code": "Trial.ExportLimitExceeded",
  "traceId": "...", "correlationId": "...", "timestampUtc": "..."
}
```

### 3.2 İçe aktarma — `Trial.ImportLimitExceeded` (422)

| Uç | Davranış |
|---|---|
| `POST /imports/batches` | Dosya okunurken herhangi bir tabloda veri satırı **> 500** → 422 `Trial.ImportLimitExceeded`; parti oluşturulmaz, tekrar/mükerrer kontrolü bu adımdan öncedir (aynı `requestId` ile tekrar da aynı hatayı verir). |

`detail`: `"Deneme sürümünde içe aktarma tablo başına en fazla 500 satır sınırı (Müşteriler tablosu aşıyor); abonelikle kalkar."` (parantez içi tablo adı: Müşteriler / Hastalar / Aşılar). Ücretli kiracıda `Imports.TooManyRows` (400) ve mesajı değişmez.

### 3.3 Hatırlatma e-postaları — HTTP hatası yok

Arka plan işleyicisi; API yanıtı yoktur. Görünürlük `GET /reminders/logs` üzerinden: atlanan kayıt `status = Skipped`, `lastError = "TrialDailyLimit"`. Sayaç: kiracının o **UTC gün** oluşturulmuş `Skipped` dışı dispatch log sayısı (kuyruğa alınan + gönderilen + gönderimi başarısız). Randevu ve aşı hatırlatmaları aynı günlük bütçeyi paylaşır; sıra: önce randevu, sonra aşı, her biri vade sırasına göre. Atlanan hatırlatma tekilleştirme anahtarı yazıldığı için **sonraki gün yeniden denenmez** (bilinen davranış, K2).

## 4) Sınır tablosu

| Kısıt | Deneme | Ücretli | Kod (deneme) | HTTP |
|---|---|---|---|---|
| Hatırlatma e-postası / UTC gün / kiracı | 20 | sınırsız | log `TrialDailyLimit` | — |
| Export satır (CSV/XLSX) | 1.000 | 50.000 | `Trial.ExportLimitExceeded` | 422 |
| Export satır (PDF) | 1.000 | 5.000 | `Trial.ExportLimitExceeded` | 422 |
| İçe aktarma tablo başına satır | 500 | 10.000 (`Imports:Limits`) | `Trial.ImportLimitExceeded` | 422 |

## 5) Frontend etkisi

- Export ve içe aktarma hata işleyicilerine `422` + `code` `Trial.*` dalı: `detail` metnini olduğu gibi göster, yanına "Aboneliğe geç" eylemi (abonelik özeti/checkout akışı). Mevcut `*.ExportTooManyRows` / `Imports.TooManyRows` (400) dalları değişmedi.
- Hatırlatma günlükleri ekranında `lastError = "TrialDailyLimit"` için okunur etiket: "Deneme günlük e-posta sınırı (20) doldu".
- Deneme kiracısı olduğu `GET /tenants/subscription/summary` (`status = Trialing`) ile bilinir; isteğe bağlı olarak export/içe aktarma ekranlarında önceden sınır bilgisi gösterilebilir.

## 6) Testler

- Unit: `TrialLimitPolicyTests` (Trialing/expired-trial → kısıtlı; Active → kısıtsız; kayıt yok → kısıtlı; istek içi tek sorgu; `ExportRowLimit.Ensure` kodları), `ExportAppointmentsReportQueryHandlerTests` (1.001 satır: deneme 422 kodu ve mesaj, ücretli geçer), `ImportUploadReaderTests` (501 satır: deneme `Trial.ImportLimitExceeded`, ücretli okur).
- Entegrasyon: `ReportsEndpointTests` (deneme kiracısı 1.001 ödeme → CSV/XLSX 422 + `code` + `detail`; ücretli → 200, 1.001 satır), `ReminderProcessorServiceTests` (21 aday: deneme 20 kuyruk + 1 `Skipped/TrialDailyLimit`, ikinci turda yeni e-posta yok; ücretli 21 kuyruk).
