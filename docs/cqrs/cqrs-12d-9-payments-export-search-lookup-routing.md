# CQRS-12D-9 — PaymentsSearchLookupEnabled rollout: payment export (CSV/XLSX)

**Tür:** Export search resolution routing. 12D-7/12D-8 deseni CSV ve XLSX export pipeline'a
uygulandı. Liste ve report handler'ları **değişmedi**.

## Uygulanan yüzey

| Bileşen | Değişiklik |
|---|---|
| `PaymentsReportExportPipeline.LoadAsync` | Flag + lookup reader parametreleri; flag-aware resolution |
| `ExportPaymentsReportQueryHandler` | Reader + `IOptions` + pipeline flag geçişi |
| `ExportPaymentsReportXlsxQueryHandler` | Aynı (ortak pipeline) |
| `PaymentsReportSearchResolution` | Legacy 5-param imza kaldırıldı; tek flag-aware overload (report + export) |

## Kapsam dışı (bu faz)

| Alan | Durum |
|---|---|
| `GetPaymentsListQueryHandler` | 12D-7 — değişmedi |
| `GetPaymentsReportQueryHandler` | 12D-8 — değişmedi |
| `PaymentsReportItemMapping` | id-hydration Command DB — değişmedi |
| `PaymentsCsvWriter` / `PaymentsXlsxWriter` | Kolon/shape — değişmedi |
| `SharedSearchLookupEnabled` / `SharedSearchPetIdsLookup` | Kullanılmaz |

## Export routing özeti

```text
ExportPaymentsReportQueryHandler / ExportPaymentsReportXlsxQueryHandler
  → PaymentsReportExportPipeline.LoadAsync(..., PaymentsSearchLookupEnabled, readers, ...)
  → PaymentsReportSearchResolution.ResolveSearchAsync (flag-aware)
  → PaymentsListSearchResolution.ResolveSearchIdsAsync (12D-7 helper)

PaymentsSearchLookupEnabled=false
  → ClientsByTenantTextSearchSpec + PetsByTenantTextFieldsSearchSpec

PaymentsSearchLookupEnabled=true
  → IClientReadModelLookupReader.ResolveClientIdsByTextSearchAsync
  → IPetReadModelLookupReader.ResolvePetIdsByPetTextFieldsAsync

Export data (Command DB, değişmedi):
  PaymentsFilteredCountSpec → MaxExportRows tavan kontrolü
  PaymentsFilteredOrderedForReportSpec → sayfasız satır kümesi
  PaymentsReportItemMapping → CSV/XLSX mapping
```

CSV ve XLSX **aynı pipeline**'ı paylaşır; yalnız son writer farklıdır.

## Client/Pet id ayrımı

Strateji B korundu: `searchClientIds` ve `searchPetIds` ayrı çözülür. Aynı id seti count +
ordered list sorgularına verilir → export satır bütünlüğü korunur.

## MaxExportRows

`PaymentsReportConstants.MaxExportRows` tavanı **count sonrası** uygulanır (değişmedi). Flag
routing yalnızca search id çözümlemesini etkiler; tavan mantığı aynı kalır.

## Clinic scope

- Lookup: tenant-only.
- Export scope: mevcut `PaymentsReportQueryValidation` + `IClinicReadScopeResolver` + aggregate specs.

## Query DB boş/stale

Flag true + boş read-models → export **eksik satır** üretebilir (sessiz veri eksiltme). **Fallback
yok.** Rollback: `PaymentsSearchLookupEnabled=false` + restart.

## Blast radius notu

Tek flag (`PaymentsSearchLookupEnabled`) list (12D-7), report (12D-8) ve export (12D-9) yüzeylerini
birlikte açar/kapatır. Prod enable sırası önerisi: list parity → report parity → export bütünlük
doğrulaması.

## Flag

| Ayar | Default |
|---|---|
| `QueryReadModels:PaymentsSearchLookupEnabled` | `false` |

## Testler

- `ExportPaymentsReportPaymentsSearchLookupFeatureFlagTests` — CSV/XLSX flag routing + MaxExportRows
- `PaymentsReportSearchResolutionTests` — güncellendi (legacy imza kaldırıldı)
- `ExportPaymentsReportQueryHandlerTests` / `ExportPaymentsReportXlsxQueryHandlerTests` — constructor
- `PaymentExportSearchLookupSmokeIntegrationTests` — CSV/XLSX smoke + tenant isolation

## Manuel test

```powershell
dotnet test --no-restore --filter "PaymentsReport"
```

Geniş payment test:

```powershell
dotnet test --no-restore --filter "Payments"
```

Full suite:

```powershell
dotnet test --no-restore
```

Opsiyonel smoke:

```powershell
dotnet test tests/Backend.Veteriner.IntegrationTests/Backend.Veteriner.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PaymentExportSearchLookupSmokeIntegrationTests"
```

## Rollout notu

Export finansal bütünlük açısından en kritik yüzey. Flag açmadan önce Client + Pet read-model
backfill/parity/health + list/report parity geçilmelidir.
