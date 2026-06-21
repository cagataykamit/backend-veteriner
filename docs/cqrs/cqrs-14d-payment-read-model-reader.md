# CQRS-14D — Payment read-model list reader

**Tür:** Query DB reader altyapısı. **Production list davranışı değişmedi** (handler routing / flag yok).

**Ön durum:** 14B — `PaymentReadModels` tablo/migration; 14C — projection upsert + snapshot enrichment.

---

## 1. Bu fazda ne eklendi?

| Bileşen | Açıklama |
|---|---|
| `IPaymentsListReadModelReader` | Application abstraction — `GetListAsync(PaymentsListReadRequest)` |
| `PaymentsListReadRequest` | TenantId + ClinicId zorunlu; handler ile aynı filtre/paging parametreleri |
| `PaymentsListReadResult` | `Items` (`PaymentListItemDto`) + `TotalCount` |
| `PaymentsListReadModelReader` | `QueryDbContext.PaymentReadModels` üzerinden `AsNoTracking` okuma |
| DI | `AddScoped<IPaymentsListReadModelReader, PaymentsListReadModelReader>()` |
| Testler | `PaymentReadModelReaderIntegrationTests` — doğrudan read-model seed |

**Yapılmadı:** `GetPaymentsListQueryHandler` routing, `PaymentsListEnabled` flag, backfill/parity/health, dashboard recent, report/export, migration, frontend.

---

## 2. Desteklenen filtreler

| Filtre | Davranış |
|---|---|
| `TenantId` | Zorunlu — her sorguda uygulanır |
| `ClinicId` | Zorunlu — tenant-wide list **desteklenmez** (handler ile uyumlu) |
| `ClientId` | Opsiyonel eşitlik filtresi |
| `PetId` | Opsiyonel eşitlik filtresi |
| `Method` | Opsiyonel `PaymentMethod` eşitlik filtresi |
| `PaidFromUtc` | `PaidAtUtc >= value` |
| `PaidToUtc` | `PaidAtUtc <= value` |
| `SearchContainsLikePattern` | Handler'da `ListQueryTextSearch` ile üretilen `%term%` pattern |

### Search alanları (Query DB denormalize)

OR birleşimi:

- `ClientNameNormalized` — `LIKE`
- `PetNameNormalized` — null-safe `LIKE`
- `NotesNormalized` — null-safe `LIKE`
- `Currency` — `LIKE` (command spec ile uyumlu)

Command DB path ayrıca client/pet lookup ile email/telefon/breed vb. eşleştirir; read-model path **yalnızca** yukarıdaki denormalize alanları kullanır. Client/pet isim hydration için Command DB'ye gidilmez.

---

## 3. Sıralama ve paging

- Sıralama: `PaidAtUtc DESC`, `PaymentId DESC` (command `PaymentsListFilteredPagedSpec` ile aynı).
- Index: `IX_PaymentReadModels_TenantId_ClinicId_PaidAtUtc_PaymentId`.
- Paging: ayrı `CountAsync` + `Skip/Take`; `Page`/`PageSize` handler'da `Math.Max(1, page)` ve `Math.Clamp(1, 200)` ile normalize edilir — reader ham değerleri kullanır (14E routing'de handler normalize eder).

---

## 4. Scope / tenant / clinic izolasyonu

- Her sorgu `TenantId AND ClinicId` ile sınırlıdır.
- Cross-tenant veya cross-clinic satır dönmez.
- Command DB fallback **yok** — yalnızca `PaymentReadModels`.

---

## 5. DTO mapping

`PaymentListItemDto`: `PaymentId`, `ClinicId`, `ClientId`, `ClientName`, `PetId`, `PetName` (null pet → boş string), `Amount`, `Currency`, `Method`, `PaidAtUtc`.

---

## 6. Kapsam dışı kalanlar

- Handler routing ve `QueryReadModels:PaymentsListEnabled` (14E).
- Client email/phone / pet breed search (command lookup path'e özgü).
- Backfill, parity, health.
- Dashboard recent payments, report/export.

---

## 7. Sonraki faz — 14E notu

- `GetPaymentsListQueryHandler` içinde `PaymentsListEnabled` flag ile reader routing.
- `QueryReadModelsOptions.PaymentsListEnabled` + appsettings default `false`.
- Search pattern handler'da üretilir; reader'a `SearchContainsLikePattern` olarak aktarılır.
- Clinic scope resolver sonrası `effectiveClinicId` reader request'e geçirilir.

---

## 8. Manuel test listesi

Visual Studio Test Explorer veya terminal:

```powershell
dotnet build --no-restore
dotnet test --no-restore --filter "FullyQualifiedName~PaymentReadModelReader"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentProjection"
```

Integration test sınıfı: `Backend.IntegrationTests.Query.Payments.PaymentReadModelReaderIntegrationTests`
