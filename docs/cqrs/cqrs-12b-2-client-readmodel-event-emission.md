# CQRS-12B-2 — Client Read-Model Event / Outbox Emission

## Kapsam

Bu faz Client create/update işlemlerinden Client read-model projection'ı için gerekli integration
event/outbox mesajlarını **üretir**. Yalnızca **event contract + outbox emission + test** fazıdır.

Bu fazın **dışında** (yapılmadı): projection processor, Query DB `ClientReadModel`'e veri yazma,
`GetClientsListQueryHandler` değişikliği, feature flag, health check/parity/acceptance script,
Pet read-model/event, Delete event, route/auth/permission/tenant scope davranışı.

## Event contract

Appointment integration event deseni birebir takip edildi (adapter → `IOutboxBuffer` →
`OutboxSaveChangesInterceptor` → `OutboxMessages` tablosu).

### `ClientProjectionSnapshot` (`Application/Clients/IntegrationEvents`)

`ClientReadModels` kolonlarıyla **birebir** hizalıdır:

| Snapshot alanı | ClientReadModel kolonu |
|----------------|------------------------|
| `ClientId` | `ClientId` (PK) |
| `TenantId` | `TenantId` |
| `FullName` | `FullName` |
| `FullNameNormalized` | `FullNameNormalized` |
| `Email` | `Email` |
| `Phone` | `Phone` |
| `PhoneNormalized` | `PhoneNormalized` |
| `CreatedAtUtc` | `CreatedAtUtc` |

> `LastEventId` / `LastProjectedAtUtc` kolonları projection processor tarafından doldurulacaktır
> (`LastEventId` event payload'ındaki `EventId`'den). Bu fazda yazılmaz.

### Event tipleri (`OutboxMessages.Type`, `nvarchar(64)`)

- `client.created.v1` → `ClientCreatedIntegrationEvent(EventId, OccurredAtUtc, Current)`
- `client.updated.v1` → `ClientUpdatedIntegrationEvent(EventId, OccurredAtUtc, Current)`

Event metadata: `EventId` (Guid, idempotency/dedup için), `EventType` (outbox `Type` kolonu),
`OccurredAtUtc` (UTC), ve snapshot içindeki `TenantId` + `ClientId`.

**Sequence/ordering alanı eklenmedi.** Appointment'ta `MutationSequence` + `OutboxMessages.AppointmentSequence`
çok aşamalı yaşam döngüsü (create/update/reschedule/cancel/complete) için strict per-aggregate sıralama
sağlar ve esas olarak projection processor tarafından kullanılır. Client'ta yalnızca Create/Update vardır,
processor bu fazda yazılmamaktadır ve read-model bir upsert'tür; bu nedenle command DB üzerinde yeni
ordering kolonu/migration eklemek kapsam dışıdır. İleride Client projection processor sıralamayı
`CreatedAtUtc` + `EventId` ile yapabilir.

### Adapter / registry (`Infrastructure/Outbox`)

- `IClientIntegrationEventOutbox` / `ClientIntegrationEventOutbox` — event'i JSON'a serialize edip
  `IOutboxBuffer`'a yazar (DB/transaction başlatmaz). Appointment adapter'ının aynısı; tek fark
  ordering alanı taşımaz (`AppointmentId`/`AppointmentSequence` null kalır, diğer outbox tipleri gibi).
- `ClientIntegrationEventTypeRegistry` — `Type` → payload CLR tipi eşlemesi (reflection yok).
- `UnknownClientIntegrationEventTypeException`.

## Outbox emission nerede yapıldı

- `CreateClientCommandHandler`: tüm tenant/duplicate kontrolleri geçtikten, `AddAsync` çağrıldıktan
  **sonra** ve `SaveChangesAsync`'ten **önce** `client.created.v1` enqueue edilir.
- `UpdateClientCommandHandler`: `UpdateDetails` + `UpdateAsync` sonrası, `SaveChangesAsync` öncesi
  `client.updated.v1` enqueue edilir.

Emission **aynı transaction sınırındadır**: `EnqueueAsync` yalnızca scoped buffer'a yazar; gerçek
`OutboxMessages` satırı `AppDbContext.SaveChangesAsync` sırasında `OutboxSaveChangesInterceptor`
tarafından aynı SaveChanges içinde eklenir. Command başarısız olursa (tenant context yok, tenant
pasif, duplicate, client bulunamadı) handler erken döner ve enqueue **hiç çağrılmaz** → outbox satırı oluşmaz.

## Generic OutboxProcessor ile etkileşim

`OutboxProcessor` (email/domain event işleyen generic worker) bilinmeyen tipte mesaj için
`NotSupportedException` fırlatıp retry/dead-letter uygular. Client event'lerinin yanlışlıkla bu worker
tarafından tüketilip dead-letter olmasını önlemek için:

- `OutboxMessageQueryFilters` genişletildi: appointment + client integration event tipleri
  "projection integration events" olarak gruplandı; `ExcludingProjectionIntegrationEvents`,
  `ClientIntegrationEventsOnly`, `IsProjectionIntegrationEvent` eklendi.
- `OutboxProcessor` artık `ExcludingProjectionIntegrationEvents` + `IsProjectionIntegrationEvent`
  guard'ı kullanır. Appointment davranışı korunur; client event'leri ileride yazılacak dedike
  Client projection processor için tabloda **işlenmemiş** kalır.

## Consumer/projection adı

Bu fazda processor yazılmadı. İleride Client projection consumer adı appointment'tan (`appointment-read-model-v1`)
**ayrı** olacak şekilde (`client-read-model-v1` gibi) tasarlanmalıdır; `ProcessedProjectionEvents` PK'si
`(EventId, ConsumerName)` olduğundan ayrı consumer adıyla bağımsız idempotency sağlanır.

## Delete event neden eklenmedi

Client için silme komutu/handler/endpoint/permission **yoktur** (yalnızca Create/Update; CQRS-12B-1
foundation notunda da teyit edilmiştir). Domain'de soft-delete (`IsDeleted`/`ISoftDelete`) de yoktur.
Bu nedenle `ClientDeleted` event'i bu fazda **eklenmedi** (talimat: "Delete event yazma, eğer Client
delete command yoksa").

## Normalizasyon (yeni kırılganlık eklenmedi)

`ClientProjectionSnapshotFactory.Create(Client)` snapshot'ı aggregate'ten üretir; ek DB erişimi yok:

- `FullNameNormalized` = `Client.NormalizeFullNameForDuplicateCheck` (trim + invariant lower) — mevcut
  duplicate-check normalizer'ıyla aynı.
- `Phone` / `PhoneNormalized` = aggregate üzerindeki değerler (`TurkishMobilePhone`, `905XXXXXXXXX`).
- `Email` = aggregate üzerindeki normalize e-posta (trim + invariant lower).

Yeni random/locale-bağımlı normalizasyon eklenmedi. Event üretimi deterministiktir (aynı aggregate →
aynı snapshot); EventId/OccurredAtUtc command anında üretilir, duplicate event üretilmez.

## Testler

- `ClientCommandHandlerOutboxEmissionTests` (Application.Tests): create/update → doğru tip + payload
  (TenantId/ClientId/FullName/normalized/Email/Phone), `EventId != Empty`, `OccurredAtUtc.Kind == Utc`,
  enqueue + SaveChanges `Times.Once`; tenant missing / duplicate / not-found → enqueue **hiç** olmaz.
- `ClientProjectionSnapshotFactoryTests` (Application.Tests): alan eşlemesi, null opsiyoneller, determinizm.
- `ClientIntegrationEventSerializationTests` (Application.Tests): tip benzersizliği + JSON round-trip.
- `ClientIntegrationEventInfrastructureTests` (IntegrationTests): registry resolve/reject, adapter buffer
  enqueue (DB'siz), unknown/too-long/payload-mismatch reddi.

## Garanti

- Production read path değişmedi (`GetClientsListQueryHandler` ve diğer query handler'lar dokunulmadı).
- Route/auth/permission/tenant scope davranışı değişmedi.
- Mevcut Client testleri (constructor'a yeni bağımlılık eklenerek) güncellendi ve geçer.
- Commit atılmadı.
