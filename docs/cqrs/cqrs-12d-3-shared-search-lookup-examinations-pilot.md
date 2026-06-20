# CQRS-12D-3 — SharedSearchLookupEnabled pilot: GetExaminationsListQueryHandler

**Tür:** Pilot handler routing. Yalnızca **examination list search lookup** (pet id çözümlemesi)
Query DB read-model'e taşındı. Examination asıl verisi Command DB'den okunmaya devam eder.

## Flag

| Ayar | Değer |
|---|---|
| **Ad** | `QueryReadModels:SharedSearchLookupEnabled` |
| **Default** | `false` (tüm appsettings) |
| **Startup log** | `SharedSearchLookupEnabled={value}` |

`ClientsEnabled` / `PetsEnabled` / `AppointmentsEnabled` bayraklarından **bağımsızdır**.

## Handler routing özeti

`GetExaminationsListQueryHandler` içinde yalnızca `searchPattern != null` iken çalışan
**ön-arama adımı** flag'e göre dallanır:

| Flag | Search pet-id çözümlemesi |
|---|---|
| `false` | `ListSearchPetIds.ResolveForAggregateListAsync` → Command DB (`ClientsByTenantTextSearchSpec`, `PetsByTenantTextFieldsSearchSpec`, `PetsByTenantForClientIdsSpec`) |
| `true` | `IPetReadModelLookupReader.ResolvePetIdsByTextSearchAsync` → Query DB `PetReadModels` |

**Değişmeyen adımlar (her iki flag değerinde Command DB):**

- Examination count/list (`ExaminationsFilteredCountSpec`, `ExaminationsFilteredPagedSpec`)
- Sayfa sonrası pet/client isim hydration (`PetsByTenantIdsNameClientSpec`, `ClientsByTenantIdsNameSpec`)
- Clinic scope çözümlemesi (`IClinicReadScopeResolver`)
- Response DTO, sıralama, pagination

## Clinic scope nasıl korunur?

- Client/pet **lookup tenant-only** kalır (`TenantId` zorunlu); klinik filtresi lookup'a uygulanmaz.
- Klinik scope **examination aggregate query** tarafında uygulanmaya devam eder:
  `effectiveClinicId` / `accessibleClinicIds` → `ExaminationsFiltered*Spec`.
- Bu, 12D-1 audit bulgusuyla uyumludur: lookup tenant genelinde pet id kümesi üretir; klinik
  izolasyonu examination satırlarında korunur.

## Query DB boş/stale davranışı

- `SharedSearchLookupEnabled=true` iken read-model boş/stale ise lookup eksik pet id döndürür.
- Examination listesi **eksik/boş** olabilir (client adı aramasında visit reason eşleşmiyorsa).
- **Command DB fallback yok.** Rollback: `SharedSearchLookupEnabled=false` + restart.
- Query DB outage → exception propagate; otomatik fallback yok.

## Search boş/null

`ListQueryTextSearch.Normalize` null dönerse lookup **çağrılmaz** (mevcut davranış).

## Rollout ön-koşulu (operasyonel)

Pilot açılmadan önce ilgili tenant(lar) için:

1. `backfill-pet-projections` (+ gerekirse client backfill — lookup pet read-model kullanır)
2. Pet parity in-sync
3. `pet-projection` health Healthy
4. Sonra `QueryReadModels__SharedSearchLookupEnabled=true`

## Kapsam dışı (bu faz)

- Diğer aggregate list handler'ları (appointments, treatments, …)
- Payments/report/export
- Post-page name hydration Query DB'ye taşıma
- Otomatik fallback

## Testler

- `GetExaminationsListQueryHandlerSharedSearchLookupFeatureFlagTests` (unit)
- `ExaminationSharedSearchLookupSmokeIntegrationTests` (integration)
- Mevcut `GetExaminationsListQueryHandlerTests` regresyon
