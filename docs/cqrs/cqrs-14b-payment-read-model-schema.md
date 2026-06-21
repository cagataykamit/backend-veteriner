# CQRS-14B — PaymentReadModel schema

**Tür:** Query DB schema/migration (yalnız tablo + index). **Production read davranışı değişmedi.**

**Ön durum:** 14A audit — payment list Query DB'ye taşınacak ilk yüzey; finance read-model (`PaymentDailyContributionReadModel`, `ClinicDailyPaymentStatsReadModel`) list için yeterli değil.

---

## 1. Bu fazda ne eklendi?

| Bileşen | Açıklama |
|---|---|
| `PaymentReadModel` | Query DB entity — per-payment list/search/recent satırı |
| `PaymentReadModelConfiguration` | EF config, index'ler, string/decimal limitleri |
| `QueryDbContext.PaymentReadModels` | DbSet kaydı |
| Migration `20260621074842_AddPaymentReadModel` | Query DB tablo + 4 index |
| `QueryReadModelConstraints` | `PaymentNotes` / `PaymentNotesNormalized` (4000) |
| Test | `QueryDbMigrationIntegrationTests` — tablo + index varlığı |

**Yapılmadı:** Projection processor, snapshot genişletme, backfill, parity, health, list routing, reader.

---

## 2. PaymentReadModel kolonları

| Kolon | Tip | Not |
|---|---|---|
| `PaymentId` | `Guid` PK | Command `Payments.Id` |
| `TenantId` | `Guid` | Tenant izolasyonu |
| `ClinicId` | `Guid` | Clinic scope |
| `ClientId` | `Guid` | FK benzeri (denormalize) |
| `ClientName` | `nvarchar(300)` | Display + arama |
| `ClientNameNormalized` | `nvarchar(300)` | LIKE/lookup |
| `PetId` | `Guid?` | Nullable |
| `PetName` | `nvarchar(200)?` | Nullable |
| `PetNameNormalized` | `nvarchar(200)?` | Nullable |
| `Amount` | `decimal(18,2)` | Finance config ile uyumlu |
| `Currency` | `nvarchar(3)` | ISO 4217 |
| `Method` | `int` | `PaymentMethod` enum değeri |
| `PaidAtUtc` | `datetime2` | List/recent sıralama |
| `Notes` | `nvarchar(4000)?` | Command `Payments.Notes` ile uyumlu |
| `NotesNormalized` | `nvarchar(4000)?` | Arama; **index yok** (geniş text) |
| `AppointmentId` | `Guid?` | Detail/report ileride |
| `ExaminationId` | `Guid?` | Detail/report ileride |
| `LastEventId` | `Guid` | Stale guard |
| `LastEventOccurredAtUtc` | `datetime2` | Event ordering |
| `LastProjectedAtUtc` | `datetime2` | Projection wall-clock |

---

## 3. Index gerekçeleri

| Index | Amaç |
|---|---|
| `IX_PaymentReadModels_TenantId_ClinicId_PaidAtUtc_PaymentId` (DESC PaidAtUtc, PaymentId) | `GET /payments` list + dashboard recent (clinic scope) |
| `IX_PaymentReadModels_TenantId_ClientId_PaidAtUtc` (DESC PaidAtUtc) | Client payment-summary recent (14I) |
| `IX_PaymentReadModels_TenantId_ClinicId_ClientNameNormalized` | Metin araması — client adı |
| `IX_PaymentReadModels_TenantId_ClinicId_PetNameNormalized` | Metin araması — pet adı |

**Eklenmedi:**

- `NotesNormalized` — 4000 char; LIKE taraması index maliyeti yüksek; 14D reader'da OR filtresi ile değerlendirilir.
- `Currency` tek başına — düşük seçicilik; list spec'te equality filtresi yeterli.

---

## 4. Kapsam dışı

- Payment projection processor / outbox / snapshot değişikliği
- Backfill, parity, health
- `GetPaymentsListQueryHandler` routing
- `IPaymentReadModelReader`
- Dashboard recent, report/export

---

## 5. Sonraki faz — 14C

- `PaymentProjectionSnapshot` genişletme (`ClientName`, `PetName`, `NotesNormalized`)
- `PaymentProjectionProcessor` → `PaymentReadModel` upsert (finance contribution ile aynı transaction veya genişletilmiş apply)
- Write-path enrichment (snapshot factory)

---

## 6. Manuel test listesi

```powershell
dotnet build --no-restore

dotnet test --no-restore --filter "FullyQualifiedName~QueryDbMigrationIntegrationTests"
```

Opsiyonel tam suite:

```powershell
dotnet test --no-restore
```

Query migration uygulama (local/staging):

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
```

---

## 7. Commit

**Commit atılmadı.** Kullanıcı onayı sonrası ayrı commit.
