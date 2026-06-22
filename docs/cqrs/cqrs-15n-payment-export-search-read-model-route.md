# CQRS-15N — Payment export CSV/XLSX Query DB search route

**Tür:** Payment export CSV/XLSX search parity + Query DB routing. **Schema/migration/projection/backfill/list/JSON report değişmedi.**

**Ön durum:** CQRS-15J export Query route (search boş); CQRS-15L payment list search Query route; CQRS-15M payment report JSON search Query route.

**İlgili dokümanlar:** [`cqrs-15j-payment-export-read-model-route.md`](cqrs-15j-payment-export-read-model-route.md) · [`cqrs-15l-payment-list-search-read-model-route.md`](cqrs-15l-payment-list-search-read-model-route.md) · [`cqrs-15m-payment-report-search-read-model-route.md`](cqrs-15m-payment-report-search-read-model-route.md)

---

## 1. Özet

`PaymentsReportExportReadEnabled=true` + **representable scope** (tek klinik veya tenant-wide) iken search dolu CSV/XLSX export istekleri artık Query DB `PaymentReadModels` reader üzerinden okunur. Search resolution yalnız Query DB lookup reader'ları kullanır; Command DB `PaymentsReportSearchResolution` Query path'te **çalışmaz**.

Payment list ve JSON report **değişmedi**.

---

## 2. CSV/XLSX routing behavior

`ExportPaymentsReportQueryHandler` (CSV) ve `ExportPaymentsReportXlsxQueryHandler` (XLSX) ortak `PaymentsReportExportPipeline` kullanır — davranış birebir aynıdır.

| Koşul | Kaynak |
|---|---|
| `PaymentsReportExportReadEnabled = false` | **Command DB** (mevcut, birebir) |
| `true` + search boş/whitespace + representable scope | **Query DB** (15J davranışı, değişmedi) |
| `true` + search dolu + single clinic | **Query DB** (15N — yeni) |
| `true` + search dolu + tenant-wide | **Query DB** (15N — yeni) |
| `true` + search dolu + multi-clinic scope | **Command DB** fallback |
| Query path seçildi | Command DB fallback **yok** |
| Query lookup/reader throw | Exception propagate, fallback **yok** |
| Query DB no matches | Boş export: `Count=0`, `Items` empty; CSV/XLSX writer mevcut empty behavior |

---

## 3. Search fields covered

Query DB search OR mantığı (15L/15M ile aynı):

| Kaynak | Alanlar |
|---|---|
| **Direct** (`PaymentReadModels`) | `ClientNameNormalized`, `PetNameNormalized`, `NotesNormalized`, `Currency` |
| **Client lookup** → `ClientId IN (...)` | FullName, Email, Phone, PhoneNormalized |
| **Pet lookup** → `PetId IN (...)` | Name, Breed, SpeciesName, BreedRefName |

Email/telefon/tür/ırk **PaymentReadModel'e eklenmedi**; lookup ID filtre ile kapatıldı.

---

## 4. Lookup reader strategy

Search dolu Query export path:

1. `ListQueryTextSearch.Normalize` + `BuildContainsLikePattern`
2. `PaymentsListQuerySearchResolution.ResolveSearchIdsAsync` → `IClientReadModelLookupReader` + `IPetReadModelLookupReader` (tenant-only; 15L/15M ile paylaşılan)
3. `PaymentsReportExportReadRequest` → pattern + `SearchMatchClientIds` + `SearchMatchPetIds`
4. `PaymentsReportExportReadModelReader` → scope filtreli OR search + SQL COUNT + full ordered list

Command DB spec/reader/hydration **çağrılmaz**. `PaymentsSearchLookupEnabled` Query export path'i **etkilemez**. `PaymentsReportReadEnabled` ve `PaymentsListReadEnabled` export route'u **etkilemez**.

---

## 5. Scope guard

- Query path: tek klinik (`ClinicId` dolu) veya tenant-wide (`ClinicId` null, `AccessibleClinicIds` null).
- Multi-clinic (`AccessibleClinicIds` dolu, `SingleClinicId` null) → Command DB (15N kapsam dışı).
- Lookup tenant-only; clinic izolasyonu `PaymentReadModels.ClinicId` filtresinde.
- Tenant-wide search aynı tenant içinde tüm klinik kayıtlarını export edebilir.

---

## 6. 50k limit behavior

- Search filtreli count üzerinden uygulanır (`PaymentsReportConstants.MaxExportRows = 50_000`).
- Query path count 50.000 → allowed; items çekilir, writer çalışır.
- Query path count 50.001 → `Payments.ReportExportTooManyRows`; items çekilmez, writer çalışmaz.
- Command path limit davranışı değişmedi.

---

## 7. No schema/migration

- `PaymentReadModel` alan/index değişmedi.
- `PaymentsReportExportReadRequest` genişletildi: `SearchContainsLikePattern`, `SearchMatchClientIds`, `SearchMatchPetIds` (opsiyonel).

---

## 8. JSON report / list behavior unchanged

| Yüzey | Search dolu |
|---|---|
| Export CSV/XLSX (`PaymentsReportExportReadEnabled`) | **Query DB** (15N) |
| Report JSON (`PaymentsReportReadEnabled`) | **Query DB** (15M, değişmedi) |
| Payment list (`PaymentsListReadEnabled`) | **Query DB** (15L, değişmedi) |

---

## 9. Remaining gaps

| Gap | Durum |
|---|---|
| Client/Pet lookup stale → eksik search sonucu | Operasyonel risk; Client/Pet parity/health ön-koşul |
| Multi-clinic Query search | Command fallback (export + report + list) |
| XLSX/CSV memory risk | Tüm satırlar belleğe; streaming/paging yok |
| Streaming/paged export | Sonraki tasarım fazı |

---

## 10. Eklenen/değişen bileşenler

| Bileşen | Değişiklik |
|---|---|
| `PaymentsReportExportReadRequest` | Search pattern + lookup ID alanları |
| `PaymentsReportExportReadModelReader` | Lookup ID + direct OR search; COUNT search filtreli |
| `PaymentsReportExportPipeline` | Search dolu Query routing; `PaymentsListQuerySearchResolution` paylaşımı |
| `QueryReadModelsOptions` | XML doc güncellendi |
| Testler | Export routing, reader integration search parity, feature flag isolation |

---

## 11. Sonraki faz

**CQRS-15O** — Payment search rollout final regression / readiness

Opsiyonel:
- Shared resolver rename (`PaymentsListQuerySearchResolution` → `PaymentsQuerySearchResolution`) ayrı küçük refactor fazı
- Streaming/paged export tasarımı

---

## 12. Rollback

`PaymentsReportExportReadEnabled=false` + restart → search dolu export istekleri Command DB'ye döner. Kod geri alımı gerekmez.

---

## 13. Manuel test (referans)

```bash
dotnet build --no-restore
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReportExport"
dotnet test --no-restore --filter "FullyQualifiedName~ExportPaymentsReport"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReportExportRead"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsCsvWriter"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsXlsxWriter"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReport"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReportRead"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentList"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsList"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsSearchLookup"
```

---

## 14. Garanti

Payment list değişmedi. JSON report değişmedi. Schema/migration/projection/backfill/parity değişmedi. Writer contract değişmedi. Commit/test bu fazda çalıştırılmadı.
