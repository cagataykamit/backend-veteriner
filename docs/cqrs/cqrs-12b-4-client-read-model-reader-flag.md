# CQRS-12B-4 — Client Read-Model Reader + Feature Flag + Handler Routing

## Kapsam

Bu faz, client list/search endpoint'ini bir feature flag ile Query DB `ClientReadModels`
read-model'ine yönlendirir. Flag **kapalıyken** mevcut Command DB davranışı **birebir** korunur.

Bu fazın **dışında** (yapılmadı): client command handler değişikliği, `ClientProjectionProcessor`
değişikliği, Pet read-model, health/parity/acceptance script, route/auth/permission/tenant scope
davranışı, Query DB outage için otomatik fallback (rollback = flag kapatma).

## Feature flag

- **Ad:** `QueryReadModels:ClientsEnabled`
- **Default:** `false` (tüm `appsettings*.json` — base, Development, Production, Staging, LoadTest,
  IntegrationTests).
- `QueryReadModelsOptions.ClientsEnabled` (`Application/Common/Options`) — appointment/dashboard
  flag'leri ile aynı section.
- Startup log: `CqrsStartupConfigurationLogger` artık `ClientsQueryReadEnabled` değerini de yazar
  (PII/secret loglamadan).

## Reader

- **Abstraction:** `IClientReadModelReader` + `ClientListReadRequest(TenantId, Page, PageSize,
  SearchContainsLikePattern)` + `ClientListReadResult(Items, TotalCount)`
  (`Application/Clients/ReadModels`).
- **Implementation:** `ClientReadModelReader` (`Infrastructure/Query/Clients`), `QueryDbContext`
  üzerinden `AsNoTracking` okur.
- **Tenant scope:** her sorgu mutlaka `TenantId` ile filtrelenir.
- **Search:** command path (`ClientsByTenantPagedSpec` / `ClientsByTenantCountSpec`) ile aynı alan
  kümesi — `FullName`, `Email`, `Phone`, `PhoneNormalized` üzerinde `LIKE`. Pattern handler'da
  `ListQueryTextSearch.BuildContainsLikePattern` ile üretilir (command path ile aynı normalize +
  kaçış).
- **Pagination / sıralama:** deterministik `OrderBy(FullNameNormalized).ThenBy(ClientId)` →
  `Skip/Take`. `IX_ClientReadModels_TenantId_FullNameNormalized_ClientId` index'i ile uyumlu. Command
  path `FullName` (collation) sıralarken read-model `FullNameNormalized` kullanır — stabil ve
  tie-break'li.
- `TotalCount` filtre uygulanmış set üzerinden ayrı `CountAsync` ile alınır.

## Handler routing

`GetClientsListQueryHandler`:

1. `TenantId` yoksa `Tenants.ContextMissing` (her iki yolda da aynı, flag'ten önce).
2. `Page`/`PageSize` clamp (`page >= 1`, `pageSize 1..200`) — flag'ten önce, ortak.
3. Search pattern (`ListQueryTextSearch`) — ortak.
4. `ClientsEnabled == true` → `IClientReadModelReader.GetListAsync` (Query DB).
5. `ClientsEnabled == false` → mevcut Command DB yolu (`ClientsByTenantCountSpec` +
   `ClientsByTenantPagedSpec`), step/slow-query loglaması dahil **birebir** korunur.

Otomatik fallback **yoktur**: flag açıkken reader hata fırlatırsa exception yukarı propagate olur,
Command DB'ye düşmez (rollback flag kapatma ile yapılır).

## Response shape

Değişmedi: her iki yol da `PagedResult<ClientListItemDto>` döner; `ClientListItemDto(Id, TenantId,
CreatedAtUtc, FullName, Email, Phone)` aynıdır.

## DI

`AddInfrastructure`:

```csharp
services.AddScoped<IClientReadModelReader, ClientReadModelReader>();
```

## Testler

- **Unit** (`Application.Tests/Clients/Handlers`):
  - `GetClientsListQueryHandlerTests` — mevcut testler, handler yeni bağımlılıklarla (flag false)
    güncellendi; command path davranışı korunur.
  - `ClientQueryHandlerFeatureFlagTests` — flag false → command repo, flag true → reader; tenant +
    page + search reader'a doğru aktarılır; tenant eksikse reader çağrılmaz; reader hata atınca
    Command DB'ye fallback yok.
- **Integration** (`IntegrationTests/Query/Clients/ClientReadModelReaderIntegrationTests.cs`,
  `client-projection` collection, ayrı LocalDB query DB):
  - tenant isolation (yalnızca istenen tenant satırları).
  - empty result.
  - sıralama + pagination (total/count + sayfa içerikleri).
  - search full name / email / phone(normalized).

## Garanti

- Read path dışında production davranışı değişmedi.
- Flag default `false` → mevcut Command DB davranışı birebir.
- Projection / event / write davranışı değişmedi (`ClientProjectionProcessor` dokunulmadı).
- Appointment / dashboard read-model davranışı değişmedi.
- Commit atılmadı.
