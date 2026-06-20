# CQRS-12D-4 — SharedSearchLookupEnabled rollout: non-financial clinical Strategy A lists

**Tür:** Handler routing genişletmesi. 12D-3 pilot deseni kalan Strategy A klinik aggregate listelerine
uygulandı. Yeni flag eklenmedi; mevcut `QueryReadModels:SharedSearchLookupEnabled` yeniden kullanıldı.

## Uygulanan handlerlar

| Handler | Endpoint |
|---|---|
| `GetTreatmentsListQueryHandler` | `GET /treatments` |
| `GetVaccinationsListQueryHandler` | `GET /vaccinations` |
| `GetHospitalizationsListQueryHandler` | `GET /hospitalizations` |
| `GetLabResultsListQueryHandler` | `GET /lab-results` |
| `GetPrescriptionsListQueryHandler` | `GET /prescriptions` |

12D-3 pilot (`GetExaminationsListQueryHandler`) aynı flag ile zaten aktifti; lookup mantığı
`SharedSearchPetIdsLookup` ortak helper'a taşındı.

## Kapsam dışı (bu faz)

| Handler / alan | Neden |
|---|---|
| `GetAppointmentsListQueryHandler` | Ayrı `AppointmentsEnabled` read-model routing; çift bayrak etkileşimi riski (12D-1 audit) |
| Payments list / report / export | Strateji B + finansal blast radius → 12D-5 (veya sonraki finans fazı) |
| Dashboard handlerları | Farklı lookup desenleri (count/recent/id-hydration) |
| Report search helper'ları | Payment/appointment report path; finans/kapsam dışı |

## Handler routing özeti

`searchPattern != null` iken:

```text
SharedSearchLookupEnabled=false  →  ListSearchPetIds (Command DB)
SharedSearchLookupEnabled=true   →  IPetReadModelLookupReader.ResolvePetIdsByTextSearchAsync (Query DB)
```

Ortak helper: `Application/Common/SharedSearchPetIdsLookup.cs`

Asıl aggregate count/list + post-page name hydration **Command DB'de kalır** (12D-3 ile aynı).

## Clinic scope

- Lookup: tenant-only (`PetTextSearchLookupRequest.TenantId`).
- Clinic scope: mevcut `IClinicReadScopeResolver` + aggregate `*Filtered*Spec` (değişmedi).

## Query DB boş/stale

- Flag true + read-model boş → lookup eksik pet id → arama sonucu eksik/boş olabilir.
- **Fallback yok.** Rollback: `SharedSearchLookupEnabled=false` + restart.

## Flag

| Ayar | Default |
|---|---|
| `QueryReadModels:SharedSearchLookupEnabled` | `false` |

## Testler

- `SharedSearchPetIdsLookupTests` — ortak helper unit
- `ClinicalListSharedSearchLookupFeatureFlagTests` — 5 handler × flag false/true (theory)
- Mevcut `Get*ListQueryHandlerTests` — constructor güncellendi
- `TreatmentSharedSearchLookupSmokeIntegrationTests` — tenant isolation + no-fallback smoke
- Mevcut `ExaminationSharedSearchLookupSmokeIntegrationTests` — regresyon (examination pilot)

## Manuel test

```powershell
dotnet test --no-restore
```

Opsiyonel smoke alt kümesi:

```powershell
dotnet test tests/Backend.Veteriner.IntegrationTests/Backend.Veteriner.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~SharedSearchLookupSmokeIntegrationTests"
```

## Rollout notu

Tüm Strategy A klinik listeler (examinations dahil) aynı flag ile açılır/kapanır. Açmadan önce
pet read-model backfill + parity + health ön-koşulu geçerlidir (12D-3 rollout notu ile aynı).
