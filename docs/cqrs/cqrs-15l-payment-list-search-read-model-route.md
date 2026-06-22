# CQRS-15L — Payment list Query DB search route

**Tür:** Payment list search parity + Query DB routing. **Schema/migration/projection/backfill/report/export değişmedi.**

**Ön durum:** CQRS-14E payment list Query route (search boş); CQRS-15K audit Seçenek B (Query lookup + PaymentReadModels filtre) kararını verdi.

**İlgili dokümanlar:** [`cqrs-15k-payment-search-parity-gap-audit.md`](cqrs-15k-payment-search-parity-gap-audit.md) · [`cqrs-14e-payment-list-routing.md`](cqrs-14e-payment-list-routing.md)

---

## 1. Özet

`PaymentsListReadEnabled=true` + **single clinic scope** iken search dolu istekler artık Query DB `PaymentReadModels` reader üzerinden okunur. Search resolution yalnız Query DB lookup reader'ları kullanır; Command DB `PaymentsListSearchResolution` Query path'te **çalışmaz**.

Report JSON ve export CSV/XLSX **değişmedi** (search dolu → Command DB guard devam).

---

## 2. Routing behavior

`GET /api/v1/payments` → `GetPaymentsListQueryHandler`

| Koşul | Kaynak |
|---|---|
| `PaymentsListReadEnabled = false` | **Command DB** (mevcut, birebir) |
| `true` + search boş/whitespace + `SingleClinicId` | **Query DB** (14E davranışı, değişmedi) |
| `true` + search dolu + `SingleClinicId` | **Query DB** (15L — yeni) |
| `true` + multi-clinic scope (`SingleClinicId` null + `AccessibleClinicIds` dolu) | **Command DB** fallback |
| Query path seçildi | Command DB fallback **yok** |
| Query lookup/reader throw | Exception propagate, fallback **yok** |
| Query DB no matches | Boş `PagedResult`, fallback **yok** |

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
2. `PaymentsListQuerySearchResolution.ResolveSearchIdsAsync` → `IClientReadModelLookupReader` + `IPetReadModelLookupReader` (tenant-only)
3. `PaymentsListReadRequest` → pattern + `SearchMatchClientIds` + `SearchMatchPetIds`
4. `PaymentsListReadModelReader` → clinic filtreli OR search + SQL COUNT + paging

Command DB spec/reader **çağrılmaz**. `PaymentsSearchLookupEnabled` Query list path'i **etkilemez** (lookup her zaman Query reader).

---

## 5. Scope guard

- Query path: yalnız `SingleClinicId` dolu iken (`TenantId` + `ClinicId` zorunlu).
- Multi-clinic (`AccessibleClinicIds` dolu, `SingleClinicId` null) → Command DB (15L kapsam dışı).
- Tenant-wide list desteklenmez (`Payments.ClinicScopeRequired` — değişmedi).
- Lookup tenant-only; clinic izolasyonu `PaymentReadModels.ClinicId` filtresinde.

---

## 6. No schema/migration

- `PaymentReadModel` alan/index değişmedi.
- `PaymentsListReadRequest` genişletildi: `SearchMatchClientIds`, `SearchMatchPetIds` (opsiyonel).

---

## 7. No report/export change

| Yüzey | Search dolu |
|---|---|
| Report JSON (`PaymentsReportReadEnabled`) | Command DB |
| Export CSV/XLSX (`PaymentsReportExportReadEnabled`) | Command DB |

---

## 8. Remaining gaps

| Gap | Durum |
|---|---|
| Report JSON search Query DB | Sonraki faz (15M) |
| Export search Query DB | Sonraki faz (15N) |
| Multi-clinic Query search | Command fallback (list + report + export) |
| Client/Pet lookup stale → eksik search sonucu | Operasyonel risk; Client/Pet parity/health ön-koşul |
| Command path (`PaymentsListReadEnabled=false`) | Hâlâ `PaymentsSearchLookupEnabled` ile lookup kaynağı seçer |

---

## 9. Eklenen/değişen bileşenler

| Bileşen | Değişiklik |
|---|---|
| `PaymentsListQuerySearchResolution` | Yeni — Query-only lookup |
| `PaymentsListReadRequest` | `SearchMatchClientIds`, `SearchMatchPetIds` |
| `PaymentsListReadModelReader` | Lookup ID + direct OR search |
| `GetPaymentsListQueryHandler` | Search dolu Query routing |
| `QueryReadModelsOptions` | XML doc güncellendi |
| Testler | Routing, resolution, reader integration |

---

## 10. Sonraki fazlar

1. **CQRS-15M** — Payment report JSON search Query DB route
2. **CQRS-15N** — Payment export search Query DB route

---

## 11. Rollback

`PaymentsListReadEnabled=false` + restart → search dolu istekler Command DB'ye döner. Kod geri alımı gerekmez.

---

## 12. Manuel test (referans)

```bash
dotnet build --no-restore
dotnet test --no-restore --filter "FullyQualifiedName~PaymentList"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsList"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsSearchLookup"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsListRead"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReport"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReportExport"
```

---

## 13. Garanti

Report/export handler/pipeline değişmedi. Schema/migration/projection/backfill/parity değişmedi. Commit/test bu fazda çalıştırılmadı.
