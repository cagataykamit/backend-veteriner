# CQRS-15I — Payment export safety hardening

**Tür:** Test + dokümantasyon. **Production kod değişmedi.**

**Ön durum:**

- CQRS-15G — JSON report `PaymentsReportReadEnabled` ile Query DB route (search boş + representable scope).
- CQRS-15H — Export CSV/XLSX audit: ortak pipeline, ≤50k bellek yükü, count validation, streaming yok.
- Export hâlâ **Command DB** üzerinde; Query DB export route yok.

**İlgili dokümanlar:** [`cqrs-15h-payment-export-safety-performance-audit.md`](cqrs-15h-payment-export-safety-performance-audit.md) · [`cqrs-15g-payment-report-read-model.md`](cqrs-15g-payment-report-read-model.md) · [`cqrs-12d-9-payments-export-search-lookup-routing.md`](cqrs-12d-9-payments-export-search-lookup-routing.md)

---

## 1. Özet

Bu fazda payment export CSV/XLSX **production davranışı değiştirilmedi**. Mevcut güvenlik/performance sınırları unit testler ve bu doküman ile netleştirildi.

| Konu | Mevcut davranış (değişmedi) |
|---|---|
| Kaynak | Command DB (`IReadRepository<Payment>` + hydration) |
| Pipeline | `PaymentsReportExportPipeline.LoadAsync` (CSV + XLSX ortak) |
| 50k tavan | `ReportsSharedLimits.MaxExportRows = 50_000` |
| Limit uygulaması | Count validation; `total > 50_000` → `Payments.ReportExportTooManyRows` |
| Query DB route | **Yok** |
| JSON flag etkisi | Export `PaymentsReportReadEnabled` okumaz |

---

## 2. 50k boundary davranışı

| Count | Sonuç |
|---|---|
| 49.999 | İzin verilir — `ListAsync` + mapping + writer çalışır |
| 50.000 | İzin verilir — tam sınır dahil |
| 50.001 | Red — `Payments.ReportExportTooManyRows` |

**Not:** Limit `>` ile uygulanır (`total > MaxExportRows`). Query `Take` veya writer guard değildir.

---

## 3. Pipeline sırası (count validation)

```text
PaymentsReportQueryValidation
  → PaymentsReportSearchResolution (search varsa)
  → PaymentsFilteredCountSpec
  → if total > 50_000 → FAIL (ListAsync çağrılmaz)
  → PaymentsFilteredOrderedForReportSpec (sayfasız)
  → PaymentsReportItemMapping
  → PaymentsCsvWriter / PaymentsXlsxWriter
```

**15I test garantileri:**

- Validation hatasında search lookup ve count çalışmaz.
- Search resolution, count öncesinde çalışır.
- Limit aşımında `ListAsync`, item mapping batch lookup'ları ve writer input üretimi yapılmaz.
- CSV ve XLSX handler aynı pipeline safety davranışını paylaşır.

---

## 4. CSV/XLSX ortak pipeline safety

Her iki export handler yalnızca writer ve dosya uzantısında ayrılır; limit ve sıralama `PaymentsReportExportPipeline` içinde tek noktada uygulanır.

**Search lookup flag:** Export yalnızca `PaymentsSearchLookupEnabled` okur (lookup kaynağı Command spec vs Query reader). JSON report flag'inden bağımsızdır.

---

## 5. Writer contract notları

### CSV (`PaymentsCsvWriter`)

- UTF-8 BOM, ayırıcı `;` (Excel TR).
- Başlıklar: `Tarih;Klinik;Müşteri;Hayvan;Tutar;Para Birimi;Ödeme Yöntemi;Not`
- Tarih: UTC → Europe/Istanbul, `dd.MM.yyyy HH:mm` (`tr-TR`).
- Tutar: `0.##` Turkish culture.
- Kaçış: `;`, `"`, `\r`, `\n` içeren alanlar çift tırnak ile sarılır.
- Teknik ID kolonları export dosyasında yok.

### XLSX (`PaymentsXlsxWriter`)

- Sheet: `"Odemeler"`.
- Aynı Türkçe başlık seti (8 kolon).
- Tarih hücresi: yerel Istanbul, `dd.MM.yyyy HH:mm`.
- Tutar: `#,##0.00`.
- Boş liste: yalnızca başlık satırı.
- AutoFilter (satır varsa), freeze row 1, column width max 55.

---

## 6. Export scope / Command route guard (15J öncesi)

| Senaryo | Export davranışı |
|---|---|
| Search dolu | Command search resolution path (`PaymentsListSearchResolution`) |
| `PaymentsReportReadEnabled = true` | Export yine Command DB — JSON flag export'u etkilemez |
| Tenant-wide (clinic yok) | Scope resolver tenant-wide; Command count/list |
| Multi-clinic ClinicAdmin | `AccessibleClinicIds` ile Command fallback |
| Clinic context mismatch | `Payments.ClinicContextMismatch` — DB erişimi yok |

Query DB export route **bu fazda eklenmedi**.

---

## 7. Eklenen test kapsamı

| Dosya | Kapsam |
|---|---|
| `PaymentsReportExportPipelineTests` | 50k boundary (49_999/50_000/50_001), sıralama, mapping guard |
| `PaymentsReportExportScopeGuardTests` | Command route, scope, JSON flag bağımsızlığı, search Command path |
| `ExportPaymentsReportQueryHandlerTests` | 50k boundary + mapping guard (CSV handler) |
| `ExportPaymentsReportXlsxQueryHandlerTests` | 50k boundary + mapping guard (XLSX handler) |
| `PaymentsCsvWriterTests` | Boş liste, kaçış, Türkçe başlık/method |
| `PaymentsXlsxWriterTests` | Boş liste, 8 kolon başlık, tek satır smoke |
| Mevcut | `ExportPaymentsReportPaymentsSearchLookupFeatureFlagTests`, integration smoke |

**Kasıtlı olarak yazılmadı:** 50k gerçek entity, bellek/OOM, streaming, HTTP 400 integration (unit mock yeterli).

---

## 8. Kalan riskler

| Risk | Not |
|---|---|
| XLSX ClosedXML bellek | 50k satırda workbook tam bellekte; dominant risk |
| Streaming/paging yok | Limit tavan sağlar, bellek yükünü çözmez |
| Search dolu Command DB | Lookup + Payment OR filtresi Command'da kalır |
| 50k hâlâ yüksek | Tam 50k DTO + writer peak memory kabul edilmiş trade-off |
| Timeout / large file | Integration load testi yok |

---

## 9. CQRS-15J hazır kriterleri

15J (Payment export Query DB route) için ön koşullar bu fazda karşılandı:

1. 50k boundary davranışı test ile sabitlendi.
2. Pipeline sırası ve limit guard dokümante/test edildi.
3. Export'un JSON flag'inden bağımsız Command route'u doğrulandı.
4. Writer contract regression testleri eklendi.
5. Production davranış değişmedi — 15J sadece routing ekler.

**Önerilen sonraki faz:** **CQRS-15J** — `PaymentsReportExportReadEnabled` + guard'lı Query DB route (search boş + representable scope).

---

## 10. Kapsam dışı (bu faz)

- Production kod / handler / pipeline / writer değişikliği
- Query DB route, feature flag, reader, schema/migration
- Export limit veya response contract değişikliği
- Streaming/paging implementasyonu
- Test çalıştırma, commit
