# CQRS-15J — Payment export CSV/XLSX Query DB route

**Tür:** Feature flag routing + Query DB export reader. **Schema/migration/projection/backfill/parity/health/DTO/export contract değişmedi.**

**Ön durum:** CQRS-15G payment report JSON Query DB route'u açtı; CQRS-15H export safety audit ve CQRS-15I export safety hardening tamamlandı. Export CSV/XLSX bu faz öncesinde her zaman Command DB'deydi.

**İlgili dokümanlar:** [`cqrs-15i-payment-export-safety-hardening.md`](cqrs-15i-payment-export-safety-hardening.md) · [`cqrs-15h-payment-export-safety-performance-audit.md`](cqrs-15h-payment-export-safety-performance-audit.md) · [`cqrs-15g-payment-report-read-model.md`](cqrs-15g-payment-report-read-model.md)

---

## 1. Özet

Payment export CSV/XLSX için JSON report'tan **bağımsız** yeni feature flag ile Query DB `PaymentReadModels` okuma yolu eklendi. Flag kapalıyken mevcut Command DB export davranışı birebir korunur. Flag açık + search boş + scope represent edilebiliyorsa (tek klinik veya tenant-wide) export Query DB'den okunur; search dolu veya multi-clinic scope Command DB fallback kullanır.

---

## 2. Flag adı ve default

```json
"QueryReadModels": {
  "PaymentsReportExportReadEnabled": false
}
```

Tüm ortam dosyalarında (6 appsettings) explicit **false**. Production export yolu değişmedi.

JSON report flag'i (`PaymentsReportReadEnabled`) export'u etkilemez; export yalnızca `PaymentsReportExportReadEnabled` kullanır.

---

## 3. CSV/XLSX routing behavior

CSV (`ExportPaymentsReportQueryHandler`) ve XLSX (`ExportPaymentsReportXlsxQueryHandler`) ortak `PaymentsReportExportPipeline.LoadAsync` kullanır.

| Koşul | Kaynak |
|---|---|
| `PaymentsReportExportReadEnabled = false` | **Command DB** (mevcut path, birebir) |
| `true` + search **boş/whitespace** + scope **tek kliniğe** çözüldü | **Query DB** `PaymentReadModels` |
| `true` + search **boş/whitespace** + scope **tenant-wide** (Admin/Owner) | **Query DB** (clinic filtresi yok) |
| `true` + search **dolu** | **Command DB** (search parity 15K'ya bırakıldı) |
| `true` + scope **multi-clinic** (ClinicAdmin, aktif klinik yok) | **Command DB** |
| Scope resolve **hata** | Validation failure (Query DB'ye gidilmez) |

Query path seçildiğinde:
- `PaymentsReportSearchResolution` **çalışmaz**
- Command repository `Count` / `List` **çalışmaz**
- `PaymentsReportItemMapping` Command hydration **kullanılmaz** (read-model denormalize alanları doğrudan map edilir)
- Command DB **fallback yok**

---

## 4. Search guard

- `ListQueryTextSearch.Normalize(search) is null` → Query path uygun (boş/whitespace dahil)
- Search dolu → Command DB fallback (bilinçli guard, sessiz fallback değil)

---

## 5. Scope guard

15G/15E ile aynı kural (`TryGetRepresentableQueryClinicScope`):

- `SingleClinicId` dolu → Query DB clinic filtreli
- `AccessibleClinicIds` null (tenant-wide Admin/Owner) → Query DB clinic filtresiz
- `AccessibleClinicIds` dolu (multi-clinic ClinicAdmin) → Command DB fallback

---

## 6. Query DB reader davranışı

`IPaymentsReportExportReadModelReader` / `PaymentsReportExportReadModelReader`:

- Kaynak: yalnız `PaymentReadModels`
- Filtreler: date range, clinic, client, pet, method (currency filtresi yok)
- Search desteklenmez
- Sıralama: `PaidAtUtc DESC, PaymentId DESC` (export Command spec ile birebir)
- Count: SQL `COUNT(*)`
- Limit aşımında items belleğe çekilmez; pipeline mevcut `Payments.ReportExportTooManyRows` hatasını döner
- Denormalize alanlar: `ClientName`, `PetName`, `ClinicName` — Command DB lookup/hydration yok

---

## 7. 50k limit davranışı

- Limit: `PaymentsReportConstants.MaxExportRows` = **50.000** (değişmedi)
- Query path: reader count döndürür; pipeline count > 50.000 ise aynı validation error
- 49.999 / 50.000 allowed; 50.001 rejected
- Items yalnızca count limit geçerse çekilir (reader içinde count > MaxExportRows ise items boş)

---

## 8. Export contract unchanged

- CSV/XLSX response contract, dosya adı, header, delimiter, timezone format değişmedi
- Writer'lar (`PaymentsCsvWriter`, `PaymentsXlsxWriter`) değişmedi
- Endpoint response shape aynı

---

## 9. JSON report flag bağımsızlığı

| Flag | Etkilediği yüzey |
|---|---|
| `PaymentsReportReadEnabled` | Yalnız GET `/api/v1/reports/payments` JSON |
| `PaymentsReportExportReadEnabled` | Yalnız export CSV/XLSX |

Birbirlerinden bağımsızdır; cross-routing yoktur.

---

## 10. Kalan riskler

| Risk | Açıklama |
|---|---|
| XLSX memory | 50k satır hâlâ belleğe alınır (Query path Command yükünü azaltır, bellek riskini çözmez) |
| Streaming/paging yok | Export tam liste belleğe yüklenir |
| Search dolu → Command DB | Search parity gap (15K) |
| Multi-clinic → Command fallback | ClinicAdmin tenant-wide export Query DB'de temsil edilmez |

---

## 11. Sonraki faz

- **CQRS-15K** — Payment search parity gap
- **CQRS-15L** (opsiyonel) — Streaming/paged export

---

## 12. Eklenen/değişen bileşenler

| Bileşen | Açıklama |
|---|---|
| `QueryReadModelsOptions.PaymentsReportExportReadEnabled` | Yeni flag, default false |
| `IPaymentsReportExportReadModelReader` | Export Query DB abstraction |
| `PaymentsReportExportReadRequest` / `PaymentsReportExportReadResult` | Export reader sözleşmeleri |
| `PaymentsReportExportReadModelReader` | SQL COUNT + unpaged items |
| `PaymentsReportExportPipeline` | Export routing + 50k guard (Query/Command) |
| Export handlers | Flag + reader injection |
| `appsettings*.json` (6) | Explicit false |
| `CqrsStartupConfigurationLogger` | Startup log satırı |
| Testler | Routing, options, integration reader |

**Yapılmadı:** Schema/migration, `PaymentReadModel` alan değişikliği, search parity, streaming/paging, export limit değişikliği, writer refactor.
