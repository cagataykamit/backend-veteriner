# Rapor PDF dışa aktarma — API sözleşmesi (REPORT-007)

Backend tek doğruluk kaynağıdır. Kaynak: backlog REPORT-007 (ürün deposu), TRIAL-004 (`TRIAL_LIMITS_API_CONTRACT.md`), ADR-003 (Rapor Merkezi). Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** uygulandı, dal `feat/rapor-favori-filtre`. Migration yok, yeni izin yok. Her uç, aynı raporun CSV/XLSX ucuyla **aynı izin, aynı filtre ve aynı satırlar**.

## 1) Uçlar

CSV/XLSX ucu olan her satır raporunun yanına `/export-pdf` eklendi (sayfa yok, `Content-Type: application/pdf`, dosya adı CSV ile aynı kalıp, uzantı `.pdf`):

| Uç | İzin | Dosya adı |
|---|---|---|
| `GET /reports/payments/export-pdf` | `Payments.Read` | `tahsilat-raporu-{from}-{to}.pdf` |
| `GET /reports/appointments/export-pdf` | `Appointments.Read` | `randevu-raporu-…` |
| `GET /reports/examinations/export-pdf` | `Examinations.Read` | `muayene-raporu-…` |
| `GET /reports/vaccinations/export-pdf` | `Vaccinations.Read` | `asi-raporu-…` |
| `GET /reports/outstanding-balances/export-pdf` | `Charges.Read` | `borc-raporu-{bugün}.pdf` |
| `GET /reports/registers/{examinations, prescriptions, vaccines, medications, controlled-substances, vaccine-info, hbs-prescriptions, hbs-vaccinations}/export-pdf` | defterin kendi izni | defterin CSV öneki + `{bugün}.pdf` |
| `GET /reports/daily-transactions/export-pdf` | `Payments.Read` + `Charges.Read` | `gunluk-islem-dokumu-{bugün}.pdf` |
| `GET /reports/cash-summary/export-pdf` | `Payments.Read` | `kasa-ozeti-{bugün}.pdf` |

## 2) Belge biçimi

- A4 yatay; üstte rapor adı, altında `Dönem: gg.aa.yyyy – gg.aa.yyyy · N kayıt` (anlık durum raporunda yalnız kayıt sayısı); sütun başlığı her sayfada tekrarlanır; altbilgi `Vetinity · Oluşturma: gg.aa.yyyy SS:dd · Sayfa X / Y` (İstanbul saati).
- Sütunlar ve değer türleri CSV/XLSX ile aynı sütun tanımından (`RegisterColumn`) gelir; gruplu defter başlığı `Grup · Başlık`. Tarih İstanbul günü, tutar `#.##0,00`, sayılar sağa yaslı. Teknik kimlik sütunu yok.
- Yazı tipi gömülür (Türkçe harfler). Sunucuda kurulu yazı tipi kullanılır, depoya dosya konmaz: Windows Arial, Linux DejaVu Sans / Liberation Sans, macOS Arial. Hiçbiri yoksa PDF ucu 500 döner (CSV/XLSX etkilenmez).
- Kütüphane: PDFsharp-MigraDoc **6.2.4 (MIT)**. 6.5.0 ve sonrası AGPL-3.0'dır; sürüm sabittir, Dependabot yükseltmesi engellidir (`.github/dependabot.yml`).

## 3) Satır tavanı (tek yer: `ExportRowLimit.ForFormat`)

| Kiracı | CSV/XLSX | PDF | Aşım kodu |
|---|---|---|---|
| Deneme (`Trialing`, süresi bitmiş deneme, abonelik kaydı yok) | 1.000 | 1.000 | `Trial.ExportLimitExceeded` (422) |
| Ücretli (`Active`) | 50.000 | **5.000** (`ReportsSharedLimits.MaxPdfExportRows`) | modülün `*.ExportTooManyRows` kodu (400), mesaj "Dışa aktarma en fazla 5000 satır destekler; filtreyi daraltın." |

Kural: önce sayım; tavan aşılıyorsa satırlar belleğe çekilmez, dosya üretilmez. Kasa özeti satır dökümü değildir, tavanı yoktur. PDF tavanı yazdırılabilir belge sınırıdır (~5.000 satır ≈ 150 sayfa); daha büyük döküm için CSV/XLSX.

## 4) Testler

- Application: `ReportPdfWriterTests` (PDF imzası, gömülü yazı tipi, çok sayfa, boş liste, defter dosya adı, dönem metni, `ForFormat` tavanları ve kodları). Yazı tipi bulunmayan makinede PDF üretim testleri atlanır.
- Entegrasyon: `ReportsEndpointTests` (ödeme PDF 200 + gömülü yazı tipi; deneme 1.001 satır PDF 422; ücretli 5.001 satır PDF 400, CSV 200), `RegistersIntegrationTests` / `BatchRegistersIntegrationTests` / `DailyTransactionsReportIntegrationTests` (PDF uçları izin, kiracı, klinik kapsamı ve tarih doğrulamasında CSV/XLSX ile aynı döngülerde; muayene defteri ve gün sonu PDF içeriği).
