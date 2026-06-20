# CQRS-12C-3 — Pet Read-Model Projection Processor

## Kapsam

Bu faz Pet integration event'lerini (`pet.created.v1` / `pet.updated.v1`) Command DB outbox'tan okuyup
Query DB `PetReadModels` tablosuna **idempotent** ve **stale-safe** şekilde uygular.

Bu fazın **dışında** (yapılmadı): `GetPetsListQueryHandler` değişikliği, `PetsEnabled` feature flag,
pet read-model reader, health/parity/smoke, backfill/rebuild, rename propagation, route/auth/permission
değişiklikleri.

## Processor / hosted service

| Bileşen | Konum |
|---------|--------|
| `IPetProjectionProcessor` | `Application/Projections/Pets` |
| `PetProjectionApplyResult` | `Application/Projections/Pets` |
| `PetProjectionProcessor` | `Infrastructure/Projections/Pets` |
| `PetProjectionHostedService` | `Infrastructure/Projections/Pets` |
| `PetProjectionOptions` | `Infrastructure/Projections/Pets` |

### Consumer name

`ProcessedProjectionEvents.ConsumerName` = **`pet-read-model-v1`** (varsayılan; `PetProjection:ConsumerName` ile override edilebilir).

Appointment (`appointment-read-model-v1`) ve Client (`client-read-model-v1`) consumer'larından bağımsızdır;
PK `(EventId, ConsumerName)` sayesinde dedup çakışmaz.

### Hosted service davranışı

- `PetProjection:Enabled` **default `false`** (`appsettings.json`); açık ortamda explicit enable gerekir.
- `Enabled=false` iken hosted service poll etmez; processor manuel/test çağrısına açık kalır.
- Dolu batch sonrası drain (beklemeden tekrar); boş batch'te `LoopIntervalSeconds` kadar idle.

## İşleme akışı

1. Outbox'tan yalnızca `PetIntegrationEventsOnly` filtresiyle `pet.created.v1` / `pet.updated.v1` claim edilir.
2. JSON deserialize → `PetIntegrationEventTypeRegistry` ile tip çözümleme.
3. Query DB transaction:
   - `ProcessedProjectionEvents` INSERT (PK conflict → duplicate skip)
   - `PetReadModels` upsert (insert veya update)
4. Command DB outbox mesajı `ProcessedAtUtc` ile işaretlenir.

## Stale / idempotency stratejisi

### Idempotency

- `(EventId, ConsumerName)` composite PK ile aynı event ikinci kez uygulanmaz.
- Fast-path: transaction öncesi `ProcessedProjectionEvents` kontrolü.
- Race: INSERT conflict → duplicate skip, read-model'e dokunulmaz.

### Stale / out-of-order

Pet event'lerinde per-aggregate sequence yok (Client ile aynı). Ordering anahtarı **`OccurredAtUtc`**:

- Mevcut satır yoksa → insert.
- `incoming.OccurredAtUtc >= existing.LastEventOccurredAtUtc` → snapshot alanları güncellenir.
- `incoming.OccurredAtUtc < existing.LastEventOccurredAtUtc` → **read-model korunur**; event yine de
  processed/dedup olarak işaretlenir (outbox consumed, stale log).

### Metadata alanları

| PetReadModel kolonu | Kaynak |
|---------------------|--------|
| `LastEventId` | Event `EventId` |
| `LastEventOccurredAtUtc` | Event `OccurredAtUtc` |
| `LastProjectedAtUtc` | Projection wall-clock (`TimeProvider`) |

`CreatedAtUtc` **yok** — projection set etmez (Pet domain'de canonical kaynak yok).

## Bad JSON / unknown type

Client projection deseniyle uyumlu:

| Durum | Davranış |
|-------|----------|
| **Bad JSON** | Exception → `OutboxRetryHelper.ApplyFailure` → retry/backoff; max retry sonrası dead-letter |
| **Unknown pet type** (filtre dışı) | Claim edilmez; outbox untouched |
| **Known type ama registry dışı payload** | `UnknownPetIntegrationEventTypeException` → retry yapmadan dead-letter |

Generic `OutboxProcessor` pet event'lerini zaten `ExcludingProjectionIntegrationEvents` ile tüketmez.

## Rename propagation neden bu fazda yok

`PetReadModels` içindeki `ClientFullName`, `SpeciesName`, `ColorName`, `BreedRefName` **denormalize**
tutulur ancak yalnızca `pet.created.v1` / `pet.updated.v1` snapshot'ından güncellenir.

- Client adı değişince mevcut pet satırları **güncellenmez**.
- Species/Color/Breed referans adı değişince mevcut pet satırları **güncellenmez**.

Bu bilinçli bir sınır: rename propagation ayrı faz/backfill gerektirir; bu fazda scope dışı bırakıldı.

## Konfigürasyon (`appsettings.json`)

```json
"PetProjection": {
  "Enabled": false,
  "BatchSize": 50,
  "LoopIntervalSeconds": 2,
  "ConsumerName": "pet-read-model-v1"
}
```

## Garanti

- Production read path değişmedi (`GetPetsListQueryHandler` dokunulmadı).
- Client/Appointment projection davranışı korundu.
- Feature flag / read routing yok.
- PII loglama eklenmedi.
