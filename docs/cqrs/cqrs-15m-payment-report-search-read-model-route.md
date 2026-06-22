# CQRS-15M — Payment report JSON Query DB search route

**Tür:** Payment report JSON search parity + Query DB routing. **Schema/migration/projection/backfill/export değişmedi.**

**Ön durum:** CQRS-15G report JSON Query route (search boş); CQRS-15L payment list search Query route; CQRS-15K audit Seçenek B kararı.

**İlgili dokümanlar:** [`cqrs-15g-payment-report-read-model.md`](cqrs-15g-payment-report-read-model.md) · [`cqrs-15l-payment-list-search-read-model-route.md`](cqrs-15l-payment-list-search-read-model-route.md) · [`cqrs-15k-payment-search-parity-gap-audit.md`](cqrs-15k-payment-search-parity-gap-audit.md)

---

## 1. Özet

`PaymentsReportReadEnabled=true` + **representable scope** (tek klinik veya tenant-wide) iken search dolu istekler artık Query DB `PaymentReadModels` reader üzerinden okunur. Search resolution yalnız Query DB lookup reader'ları kullanır; Command DB `PaymentsReportSearchResolution` Query path'te **çalışmaz**.

Export CSV/XLSX ve payment list **değişmedi** (export search dolu → Command DB guard devam).

---

## 2. Routing behavior

`GET /api/v1/reports/payments` → `GetPaymentsReportQueryHandler`

| Koşul | Kaynak |
|---|---|
| `PaymentsReportReadEnabled = false` | **Command DB** (mevcut, birebir) |
| `true` + search boş/whitespace + representable scope | **Query DB** (15G davranışı, değişmedi) |
| `true` + search dolu + single clinic | **Query DB** (15M — yeni) |
| `true` + search dolu + tenant-wide | **Query DB** (15M — yeni) |
| `true` + search dolu + multi-clinic scope | **Command DB** fallback |
| Query path seçildi | Command DB fallback **yok** |
| Query lookup/reader throw | Exception propagate, fallback **yok** |
| Query DB no matches | Boş rapor: `TotalCount=0`, `TotalAmount=0`, `Items` empty |

---

## 3. Search fields covered

Query DB search OR mantığı:

| Kaynak | Alanlar |
|---|---|
| **Direct** (`PaymentReadModels`) | `ClientNameNormalized`, `PetNameNormalized`, `NotesNormalized`, `Currency` |
| **Client lookup** → `ClientId IN (...)` | FullName, Email, Phone, PhoneNormalized |
| **Pet lookup** → `PetId IN (...)` | Name, Breed, SpeciesName, BreedRefName |

Email/telefon/tür/ırk **PaymentReadModel'e eklenmedi**; lookup ID filtre ile kapatıldı.

---

## 4. Lookup reader strategy

Search dolu Query path:

1. `ListQueryTextSearch.Normalize` + `BuildContainsLikePattern`
2. `PaymentsListQuerySearchResolution.ResolveSearchIdsAsync` → `IClientReadModelLookupReader` + `IPetReadModelLookupReader` (tenant-only; 15L ile paylaşılan)
3. `PaymentsReportReadRequest` → pattern + `SearchMatchClientIds` + `SearchMatchPetIds`
4. `PaymentsReportReadModelReader` → scope filtreli OR search + SQL COUNT + SQL SUM + paging

Command DB spec/reader **çağrılmaz**. `PaymentsSearchLookupEnabled` Query report path'i **etkilemez** (lookup her zaman Query reader).

---

## 5. Scope guard

- Query path: tek klinik (`ClinicId` dolu) veya tenant-wide (`ClinicId` null, `AccessibleClinicIds` null).
- Multi-clinic (`AccessibleClinicIds` dolu, `SingleClinicId` null) → Command DB (15M kapsam dışı).
- Lookup tenant-only; clinic izolasyonu `PaymentReadModels.ClinicId` filtresinde.
- Tenant-wide search aynı tenant içinde tüm klinik kayıtlarını görebilir.

---

## 6. TotalCount / TotalAmount behavior

- `TotalCount`: search filtresi uygulanmış küme üzerinde SQL `COUNT(*)`.
- `TotalAmount`: aynı filtrelenmiş küme üzerinde SQL `SUM(Amount)` (boşsa 0).
- Paging: search filtresi sonrası `PaidAtUtc DESC, PaymentId DESC`.

---

## 7. No schema/migration

- `PaymentReadModel` alan/index değişmedi.
- `PaymentsReportReadRequest` genişletildi: `SearchContainsLikePattern`, `SearchMatchClientIds`, `SearchMatchPetIds` (opsiyonel).

---

## 8. No export change

| Yüzey | Search dolu |
|---|---|
| Report JSON (`PaymentsReportReadEnabled`) | **Query DB** (15M) |
| Export CSV/XLSX (`PaymentsReportExportReadEnabled`) | Command DB (değişmedi) |

---

## 9. Remaining gaps

| Gap | Durum |
|---|---|
| Export search Query DB | Sonraki faz (15N) |
| Multi-clinic Query search | Command fallback (report + export) |
| Client/Pet lookup stale → eksik search sonucu | Operasyonel risk; Client/Pet parity/health ön-koşul |
| Command path (`PaymentsReportReadEnabled=false`) | Hâlâ `PaymentsSearchLookupEnabled` ile lookup kaynağı seçer |

---

## 10. Eklenen/değişen bileşenler

| Bileşen | Değişiklik |
|---|---|
| `PaymentsReportReadRequest` | Search pattern + lookup ID alanları |
| `PaymentsReportReadModelReader` | Lookup ID + direct OR search; COUNT/SUM search filtreli |
| `GetPaymentsReportQueryHandler` | Search dolu Query routing; `PaymentsListQuerySearchResolution` paylaşımı |
| `QueryReadModelsOptions` | XML doc güncellendi |
| Testler | Routing, reader integration search parity |

---

## 11. Sonraki faz

**CQRS-15N** — Payment export search Query DB route

---

## 12. Rollback

`PaymentsReportReadEnabled=false` + restart → search dolu istekler Command DB'ye döner. Kod geri alımı gerekmez.

---

## 13. Manuel test (referans)

```bash
dotnet build --no-restore
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReportRead"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReport"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsSearchLookup"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentList"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsList"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReportExport"
```

---

## 14. Garanti

Export handler/pipeline değişmedi. Payment list değişmedi. Schema/migration/projection/backfill/parity değişmedi. Commit/test bu fazda çalıştırılmadı.
