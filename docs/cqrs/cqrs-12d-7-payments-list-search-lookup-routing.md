# CQRS-12D-7 — PaymentsSearchLookupEnabled rollout: payment list (Strategy B)

**Tür:** Handler routing. 12D-6 audit deseni `GetPaymentsListQueryHandler`'a uygulandı.
Yeni flag: `QueryReadModels:PaymentsSearchLookupEnabled` (default false).

## Uygulanan handler

| Handler | Endpoint |
|---|---|
| `GetPaymentsListQueryHandler` | `GET /payments` |

## Kapsam dışı (bu faz)

| Alan | Neden |
|---|---|
| `GetPaymentsReportQueryHandler` | 12D-8 |
| `ExportPaymentsReportQueryHandler` / XLSX | 12D-9 |
| `PaymentsReportSearchResolution` | Rapor/export ortak helper — sonraki faz |
| `PaymentsReportItemMapping` | id-hydration Command DB'de kalır |
| `SharedSearchLookupEnabled` | Strateji A; payments Strateji B kullanır |

## Handler routing özeti

```text
searchPattern != null iken:
  PaymentsSearchLookupEnabled=false
    → ClientsByTenantTextSearchSpec → searchClientIds
    → PetsByTenantTextFieldsSearchSpec → searchPetIds

  PaymentsSearchLookupEnabled=true
    → IClientReadModelLookupReader.ResolveClientIdsByTextSearchAsync → searchClientIds
    → IPetReadModelLookupReader.ResolvePetIdsByPetTextFieldsAsync → searchPetIds

Aggregate count/list + post-page name hydration → Command DB (değişmedi)
```

Ortak helper: `Application/Payments/PaymentsListSearchResolution.cs`

**Not:** `SharedSearchPetIdsLookup` / `ResolvePetIdsByTextSearchAsync` **kullanılmaz** — Strateji A
client adını pet id'ye katar; payments ayrı `searchClientIds` + `searchPetIds` ister.

## Clinic scope

- Lookup: tenant-only (`ClientTextSearchLookupRequest.TenantId`, `PetTextFieldsSearchLookupRequest.TenantId`).
- Clinic scope: mevcut `IClinicReadScopeResolver` + `PaymentsFiltered*Spec` (değişmedi).

## Query DB boş/stale

- Flag true + boş Client/Pet read-models → arama sonucu eksik/boş olabilir.
- **Fallback yok.** Rollback: `PaymentsSearchLookupEnabled=false` + restart.

## Flag

| Ayar | Default |
|---|---|
| `QueryReadModels:PaymentsSearchLookupEnabled` | `false` |

Startup log: `CqrsStartupConfigurationLogger` → `PaymentsSearchLookupEnabled={...}`

## Testler

- `PaymentsListSearchResolutionTests` — helper unit
- `GetPaymentsListQueryHandlerPaymentsSearchLookupFeatureFlagTests` — flag routing
- `GetPaymentsListQueryHandlerTests` — constructor güncellendi (regresyon)
- `PaymentListSearchLookupSmokeIntegrationTests` — tenant isolation + no-fallback + client/pet path smoke

## Manuel test

```powershell
dotnet test --no-restore --filter "Payments"
```

Ardından full suite:

```powershell
dotnet test --no-restore
```

Opsiyonel smoke:

```powershell
dotnet test tests/Backend.Veteriner.IntegrationTests/Backend.Veteriner.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PaymentListSearchLookupSmokeIntegrationTests"
```

## Rollout notu

Payment list için lookup açmadan önce Client + Pet read-model backfill/parity/health ön-koşulu
geçerlidir. Rapor/export ayrı fazlarda (12D-8/9) aynı flag ile genişletilebilir; prod enable sırası:
liste → rapor → export.
