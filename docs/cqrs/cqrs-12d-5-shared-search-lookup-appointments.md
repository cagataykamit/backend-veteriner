# CQRS-12D-5 — SharedSearchLookupEnabled rollout: appointments list (Command DB path)

**Tür:** Handler routing genişletmesi. 12D-3/12D-4 deseni `GetAppointmentsListQueryHandler` Command DB
yoluna uygulandı. Yeni flag eklenmedi; mevcut `QueryReadModels:SharedSearchLookupEnabled` yeniden kullanıldı.

## Uygulanan handler

| Handler | Endpoint | Etkilenen yol |
|---|---|---|
| `GetAppointmentsListQueryHandler` | `GET /appointments` | Yalnız `AppointmentsEnabled=false` → `HandleFromCommandDbAsync` |

## Kapsam dışı (bu faz)

| Alan | Neden |
|---|---|
| `HandleFromQueryReadModelAsync` (`AppointmentsEnabled=true`) | Appointment read-model (CQRS-11) kendi arama/hydration akışını kullanır; shared lookup bu yola bağlanmaz |
| Appointment projection processor / schema / backfill | Out of scope |
| Payments list/report/export | Finansal blast radius |
| Dashboard handlerları | Farklı lookup desenleri |

## Handler routing özeti

```text
AppointmentsEnabled=true
  → HandleFromQueryReadModelAsync (değişmedi)
  → IAppointmentReadModelReader.GetListAsync(searchPattern dahil)
  → SharedSearchLookupEnabled bu yolda kullanılmaz

AppointmentsEnabled=false
  → HandleFromCommandDbAsync
  → searchPattern != null iken:
       SharedSearchLookupEnabled=false  →  ListSearchPetIds (Command DB)
       SharedSearchLookupEnabled=true   →  IPetReadModelLookupReader (Query DB PetReadModels)
  → aggregate count/list + post-page hydration Command DB'de kalır
```

Ortak helper: `Application/Common/SharedSearchPetIdsLookup.cs` (12D-4 ile aynı).

## İki flag etkileşimi

| AppointmentsEnabled | SharedSearchLookupEnabled | Search pet-id lookup |
|---|---|---|
| false | false | Command DB (`ListSearchPetIds`) |
| false | true | Query DB (`IPetReadModelLookupReader`) |
| true | false | Yok — read-model reader kendi aramasını yapar |
| true | true | Yok — read-model reader kendi aramasını yapar |

Blast radius ayrıdır: `AppointmentsEnabled` tüm listeyi Query DB'ye taşır; `SharedSearchLookupEnabled`
yalnız Command DB fallback yolundaki ön-arama adımını etkiler.

## Clinic scope

- Lookup: tenant-only (`PetTextSearchLookupRequest.TenantId`).
- Clinic scope: mevcut `effectiveClinicId` + `AppointmentsFiltered*Spec` (değişmedi).

## Query DB boş/stale

- `AppointmentsEnabled=false` + `SharedSearchLookupEnabled=true` + boş PetReadModels → arama sonucu eksik/boş olabilir.
- **Fallback yok.** Rollback: `SharedSearchLookupEnabled=false` + restart (veya `AppointmentsEnabled=true` ile tam read-model yolu).

## Flag default

| Ayar | Default |
|---|---|
| `QueryReadModels:SharedSearchLookupEnabled` | `false` |
| `QueryReadModels:AppointmentsEnabled` | `false` |

Production default davranış değişmedi.

## Testler

- `GetAppointmentsListQueryHandlerSharedSearchLookupFeatureFlagTests` — flag routing + iki bayrak kombinasyonu
- `GetAppointmentsListQueryHandlerTests` / `AppointmentQueryHandlerFeatureFlagTests` — constructor güncellendi
- `AppointmentSharedSearchLookupSmokeIntegrationTests` — tenant isolation + no-fallback smoke (`AppointmentsEnabled=false`)

## Manuel test

```powershell
dotnet test --no-restore --filter "Appointments"
```

Ardından full suite:

```powershell
dotnet test --no-restore
```

Opsiyonel smoke alt kümesi:

```powershell
dotnet test tests/Backend.Veteriner.IntegrationTests/Backend.Veteriner.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~AppointmentSharedSearchLookupSmokeIntegrationTests"
```

## Rollout notu

Appointment list için shared lookup yalnız Command DB yolunda anlamlıdır. `AppointmentsEnabled=true` ortamında
bu flag etkisizdir. Açmadan önce pet read-model backfill + parity ön-koşulu geçerlidir (12D-3 rollout notu ile aynı).
