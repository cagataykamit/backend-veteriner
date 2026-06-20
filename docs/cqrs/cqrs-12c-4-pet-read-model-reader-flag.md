# CQRS-12C-4 — Pet Read-Model Reader + Feature Flag + Handler Routing

## Kapsam

Bu faz, pet list/search endpoint'ini bir feature flag ile Query DB `PetReadModels`
read-model'ine yönlendirir. Flag **kapalıyken** mevcut Command DB davranışı **birebir** korunur.

Bu fazın **dışında** (yapılmadı): `PetProjectionProcessor` değişikliği, pet event contract
değişikliği, pet command handler değişikliği, health/parity/acceptance script, backfill/rebuild,
route/auth/permission/tenant scope davranışı, Query DB outage için otomatik fallback (rollback =
flag kapatma).

## Feature flag

- **Ad:** `QueryReadModels:PetsEnabled`
- **Default:** `false` (tüm `appsettings*.json` — base, Development, Production, Staging, LoadTest,
  IntegrationTests).
- `QueryReadModelsOptions.PetsEnabled` (`Application/Common/Options`) — appointment/dashboard/client
  flag'leri ile aynı section.
- Startup log: `CqrsStartupConfigurationLogger` artık `PetsQueryReadEnabled` değerini de yazar
  (PII/secret loglamadan).

## Reader

- **Abstraction:** `IPetReadModelReader` + `PetListReadRequest(TenantId, Page, PageSize, ClientId?,
  SpeciesId?, SearchContainsLikePattern?)` + `PetListReadResult(Items, TotalCount)`
  (`Application/Pets/ReadModels`).
- **Implementation:** `PetReadModelReader` (`Infrastructure/Query/Pets`), `QueryDbContext`
  üzerinden `AsNoTracking` okur.
- **Tenant scope:** her sorgu mutlaka `TenantId` ile filtrelenir.
- **Search:** command path (`PetsByTenantPagedSpec` / `PetsByTenantCountSpec`) ile aynı alan
  kümesi — `Name`, `Breed`, `SpeciesName`, `BreedRefName` ve sahip adı (`ClientFullName`;
  command path'te ayrı client text lookup ile eşlenen pet id'leri). `ColorName` command path'te
  aranmadığı için read-model'de de aranmaz. Pattern handler'da
  `ListQueryTextSearch.BuildContainsLikePattern` ile üretilir.
- **Pagination / sıralama:** command path ile uyumlu `OrderBy(Name).ThenBy(SpeciesName).ThenBy(PetId)`
  → `Skip/Take`.
- `TotalCount` filtre uygulanmış set üzerinden ayrı `CountAsync` ile alınır.
- `PetReadModel`'de `CreatedAtUtc` yok; reader bunu kullanmaz.

## Handler routing

`GetPetsListQueryHandler`:

1. `TenantId` yoksa `Tenants.ContextMissing` (her iki yolda da aynı, flag'ten önce).
2. `Page`/`PageSize` clamp (`page >= 1`, `pageSize 1..200`) — flag'ten önce, ortak.
3. Search pattern (`ListQueryTextSearch`) — ortak.
4. `PetsEnabled == true` → `IPetReadModelReader.GetListAsync` (Query DB).
5. `PetsEnabled == false` → mevcut Command DB yolu (`PetsByTenantCountSpec` +
   `PetsByTenantPagedSpec` + client text search side-query), **birebir** korunur.

Otomatik fallback **yoktur**: flag açıkken reader hata fırlatırsa exception yukarı propagate olur,
Command DB'ye düşmez (rollback flag kapatma ile yapılır).

## Query DB boş + flag açık

Command DB'de pet kayıtları varken Query DB `PetReadModels` boş/eksik ise (projeksiyon henüz
çalışmadı, backfill yapılmadı vb.) liste **boş/eksik** döner. Command DB'ye otomatik fallback
**yapılmaz**. Rollback: `QueryReadModels:PetsEnabled=false`.

## Response shape

Değişmedi: her iki yol da `PagedResult<PetListItemDto>` döner; `PetListItemDto` alanları aynıdır
(`Weight` null ise `0`).

## DI

`AddInfrastructure`:

```csharp
services.AddScoped<IPetReadModelReader, PetReadModelReader>();
```

## Testler

- **Unit** (`Application.Tests/Pets/Handlers`):
  - `GetPetsListQueryHandlerTests` — flag false (default); command path davranışı korunur.
  - `PetQueryHandlerFeatureFlagTests` — flag false → command repo, flag true → reader; tenant +
    page + client/species filter + search reader'a doğru aktarılır; LIKE kaçışı; tenant eksikse
    reader çağrılmaz; reader hata atınca Command DB'ye fallback yok.
- **Integration** (`IntegrationTests/Query/Pets/`, `pet-projection` collection):
  - `PetReadModelReaderIntegrationTests` — tenant isolation, empty result, sıralama + pagination,
    search (name/breed/species/breedRef/client), clientId/speciesId filtreleri.
  - `PetReadModelSmokeIntegrationTests` — flag off → command DB; flag on + projection → read model;
    flag on + boş read model → boş sonuç (fallback yok).

## Production default

`PetsEnabled=false` olduğu sürece production davranışı **değişmez** (Command DB path).
