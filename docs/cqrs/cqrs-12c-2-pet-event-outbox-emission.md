# CQRS-12C-2 — Pet Read-Model Event / Outbox Emission

## Kapsam

Bu faz Pet create/update işlemlerinden Pet read-model projection'ı için gerekli integration
event/outbox mesajlarını **üretir**. Yalnızca **event contract + outbox emission + test** fazıdır.

Bu fazın **dışında** (yapılmadı): projection processor, Query DB `PetReadModels`'e veri yazma,
`GetPetsListQueryHandler` değişikliği, feature flag, health/parity/smoke, backfill/rebuild,
PetDeleted event, Client/Appointment projection refactor, route/auth/permission/tenant scope davranışı.

## Event contract

Client/Appointment integration event deseni birebir takip edildi (adapter → `IOutboxBuffer` →
`OutboxSaveChangesInterceptor` → `OutboxMessages` tablosu).

### `PetProjectionSnapshot` (`Application/Pets/IntegrationEvents`)

`PetReadModels` kolonlarıyla **birebir** hizalıdır (projection metadata hariç):

| Snapshot alanı | PetReadModel kolonu |
|----------------|---------------------|
| `PetId` | `PetId` (PK) |
| `TenantId` | `TenantId` |
| `ClientId` | `ClientId` |
| `ClientFullName` | `ClientFullName` |
| `ClientFullNameNormalized` | `ClientFullNameNormalized` |
| `Name` | `Name` |
| `NameNormalized` | `NameNormalized` |
| `SpeciesId` | `SpeciesId` |
| `SpeciesName` | `SpeciesName` |
| `SpeciesNameNormalized` | `SpeciesNameNormalized` |
| `BreedId` | `BreedId` |
| `Breed` | `Breed` |
| `BreedRefName` | `BreedRefName` |
| `ColorId` | `ColorId` |
| `ColorName` | `ColorName` |
| `ColorNameNormalized` | `ColorNameNormalized` |
| `Gender` | `Gender` (int?) |
| `BirthDate` | `BirthDate` |
| `Weight` | `Weight` |

> `CreatedAtUtc` **eklenmedi** — Pet domain'de canonical kaynak yok (CQRS-12C-1 kararı).
> `LastEventId` / `LastEventOccurredAtUtc` / `LastProjectedAtUtc` projection processor tarafından
> doldurulacaktır (`LastEventId` event payload'ındaki `EventId`'den,
> `LastEventOccurredAtUtc` event `OccurredAtUtc`'sinden). Bu fazda yazılmaz.

### Event tipleri (`OutboxMessages.Type`, `nvarchar(64)`)

- `pet.created.v1` → `PetCreatedIntegrationEvent(EventId, OccurredAtUtc, Current)`
- `pet.updated.v1` → `PetUpdatedIntegrationEvent(EventId, OccurredAtUtc, Current)`

Event metadata: `EventId` (Guid, idempotency/dedup için), `EventType` (outbox `Type` kolonu),
`OccurredAtUtc` (UTC), ve snapshot içindeki `TenantId` + `PetId`.

**Sequence/ordering alanı eklenmedi.** Client ile aynı gerekçe: yalnızca Create/Update vardır,
processor bu fazda yazılmamaktadır ve read-model bir upsert'tür.

### Adapter / registry (`Infrastructure/Outbox`)

- `IPetIntegrationEventOutbox` / `PetIntegrationEventOutbox`
- `PetIntegrationEventTypeRegistry`
- `UnknownPetIntegrationEventTypeException`

## Outbox emission nerede yapıldı

- `CreatePetCommandHandler`: tüm tenant/client/species/breed/color/duplicate kontrolleri geçtikten,
  `AddAsync` çağrıldıktan **sonra** ve `SaveChangesAsync`'ten **önce** `pet.created.v1` enqueue edilir.
- `UpdatePetCommandHandler`: `UpdateDetails` + `UpdateAsync` sonrası, `SaveChangesAsync` öncesi
  `pet.updated.v1` enqueue edilir.

Emission **aynı transaction sınırındadır**: `EnqueueAsync` yalnızca scoped buffer'a yazar; gerçek
`OutboxMessages` satırı `AppDbContext.SaveChangesAsync` sırasında `OutboxSaveChangesInterceptor`
tarafından aynı SaveChanges içinde eklenir. Command başarısız olursa handler erken döner ve enqueue
**hiç çağrılmaz** → outbox satırı oluşmaz.

## Denormalize alan üretimi

`PetProjectionSnapshotFactory.Create(pet, client, species, breedRef?, colorRef?)`:

- **ClientFullName** — command handler'da doğrulanmış `Client.FullName`
- **ClientFullNameNormalized** — `Client.NormalizeFullNameForDuplicateCheck` (Client deseni)
- **NameNormalized** — `pet.Name.Trim().ToLowerInvariant()` (duplicate spec ile uyumlu)
- **SpeciesName** — doğrulanmış `Species.Name`
- **SpeciesNameNormalized** — `species.Name.Trim().ToLowerInvariant()`
- **BreedRefName** — `BreedId` varsa handler'da yüklenen `Breed.Name`; yoksa null
- **ColorName / ColorNameNormalized** — `ColorId` varsa handler'da yüklenen `PetColor.Name`; yoksa null
- **Gender** — `(int?)pet.Gender`

Rename propagation (Client/Species/Color/Breed adı değişince mevcut Pet satırlarını güncelleme) bu fazda **yapılmaz**.

## PetDeleted event neden eklenmedi

Pet tarafında delete command/endpoint implemente edilmemiş. CQRS-12C-2 kapsamı dışında olduğu için
`pet.deleted.v1` **eklenmedi**. Silme davranışı netleştiğinde ayrı fazda değerlendirilmelidir.

## Generic OutboxProcessor ile etkileşim

`OutboxMessageQueryFilters` genişletildi: appointment + client + **pet** integration event tipleri
"projection integration events" olarak gruplandı.

- `PetIntegrationEventsOnly` eklendi
- `ProjectionIntegrationEventTypeValues` pet tiplerini içerir
- `IsProjectionIntegrationEvent` pet tiplerini tanır
- `OutboxProcessor` mevcut `ExcludingProjectionIntegrationEvents` + `IsProjectionIntegrationEvent`
  guard'ını kullanmaya devam eder → pet event'leri generic worker tarafından tüketilmez / dead-letter olmaz

Pet projection processor henüz olmadığından pet projection outbox mesajlarının **pending kalması beklenir**.

## Garanti

- Production read path değişmedi (`GetPetsListQueryHandler` dokunulmadı).
- Query DB `PetReadModels` tablosuna yazma yok.
- Client/Appointment projection davranışı korundu.
- PII loglama eklenmedi.
