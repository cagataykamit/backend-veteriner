# CQRS-12D-8 — PaymentsSearchLookupEnabled rollout: payment report (Strategy B)

**Tür:** Report search resolution routing. 12D-7 list deseni `GetPaymentsReportQueryHandler` +
`PaymentsReportSearchResolution` flag overload'a uygulandı. Export pipeline **değişmedi**.

## Uygulanan yüzey

| Bileşen | Değişiklik |
|---|---|
| `PaymentsReportSearchResolution` | Yeni flag overload; export için eski 5-param imza korundu |
| `GetPaymentsReportQueryHandler` | `PaymentsSearchLookupEnabled` + lookup reader routing |

## Kapsam dışı (bu faz)

| Alan | Neden |
|---|---|
| `PaymentsReportExportPipeline` | 12D-9 |
| `ExportPaymentsReportQueryHandler` / XLSX | 12D-9 |
| Export path `ResolveSearchAsync(tenant, search, clients, pets)` | Command DB; 12D-9'da flag bağlanacak |
| `PaymentsReportItemMapping` | id-hydration Command DB'de kalır |
| `SharedSearchLookupEnabled` / `SharedSearchPetIdsLookup` | Payments Strateji B — kullanılmaz |

## Resolution routing özeti

```text
GetPaymentsReportQueryHandler
  → PaymentsReportSearchResolution.ResolveSearchAsync(..., flag, readers, ...)

searchPattern != null:
  PaymentsSearchLookupEnabled=false
    → ClientsByTenantTextSearchSpec → searchClientIds
    → PetsByTenantTextFieldsSearchSpec → searchPetIds

  PaymentsSearchLookupEnabled=true
    → PaymentsListSearchResolution.ResolveSearchIdsAsync (12D-7 helper)
    → IClientReadModelLookupReader.ResolveClientIdsByTextSearchAsync
    → IPetReadModelLookupReader.ResolvePetIdsByPetTextFieldsAsync

Report aggregate (Command DB, değişmedi):
  PaymentsFilteredCountSpec → TotalCount
  PaymentsFilteredAmountsSpec → TotalAmount
  PaymentsFilteredPagedSpec → Items page
  PaymentsReportItemMapping → name hydration
```

Export pipeline hâlâ eski 5-param `ResolveSearchAsync` → **her zaman Command DB**.

## Client/Pet id ayrımı

Strateji B korundu: `searchClientIds` ve `searchPetIds` ayrı çözülür; aynı id seti count,
amounts ve paged sorgularına verilir → `TotalCount` / `TotalAmount` tutarlılığı korunur.

## Clinic scope

- Lookup: tenant-only.
- Report scope: mevcut `PaymentsReportQueryValidation` + `IClinicReadScopeResolver` + aggregate specs.

## Query DB boş/stale

Flag true + boş read-models → report `TotalCount` / `TotalAmount` eksik olabilir. **Fallback yok.**
Rollback: `PaymentsSearchLookupEnabled=false` + restart.

## Flag

| Ayar | Default |
|---|---|
| `QueryReadModels:PaymentsSearchLookupEnabled` | `false` |

(12D-7'de eklendi; bu faz yalnız report routing.)

## Testler

- `PaymentsReportSearchResolutionTests` — resolution + export legacy path
- `GetPaymentsReportQueryHandlerPaymentsSearchLookupFeatureFlagTests` — handler flag routing + totals
- `GetPaymentsReportQueryHandlerTests` — constructor güncellendi
- `PaymentReportSearchLookupSmokeIntegrationTests` — tenant isolation + total count/amount smoke

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
dotnet test tests/Backend.Veteriner.IntegrationTests/Backend.Veteriner.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PaymentReportSearchLookupSmokeIntegrationTests"
```

## Rollout notu

Prod'da report lookup açmadan önce list (12D-7) parity doğrulaması + Client/Pet backfill ön-koşulu.
Export (12D-9) ayrı enable kapısı olarak kalmalı.
