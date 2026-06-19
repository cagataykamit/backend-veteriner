# CQRS-12B-3 — Client Projection Processor + ClientReadModel Writer Foundation

## Kapsam

Bu faz, CQRS-12B-2'de üretilen client integration event'lerini (`client.created.v1` /
`client.updated.v1`) Query DB `ClientReadModels` tablosuna **project eden** processor temelini ekler.

Bu fazın **dışında** (yapılmadı): `GetClientsListQueryHandler` routing/feature flag değişikliği,
`QueryReadModelsOptions.ClientsEnabled` flag, Client read-model reader, health/parity/acceptance
script, Pet projection, route/auth/permission/tenant scope davranışı.

## Processor / consumer

- **Processor:** `ClientProjectionProcessor` (`Infrastructure/Projections/Clients`),
  `IClientProjectionProcessor` (`Application/Projections/Clients`).
- **Worker:** `ClientProjectionHostedService` (background; `ClientProjection:Enabled` ile kontrol;
  varsayılan polling, dolu batch'te drain, boş batch'te idle).
- **Options:** `ClientProjectionOptions` (`ClientProjection` section): `Enabled`, `BatchSize`,
  `LoopIntervalSeconds`, `ConsumerName`.
- **Consumer adı:** `client-read-model-v1` — appointment'tan (`appointment-read-model-v1`) ayrıdır.
  `ProcessedProjectionEvents` PK'si `(EventId, ConsumerName)` olduğundan idempotency bağımsızdır.

## Sadece client event'lerini claim eder

Batch seçimi `OutboxMessageQueryFilters.ClientIntegrationEventsOnly` ile yapılır (yalnızca bilinen
client tipleri). Generic `OutboxProcessor` zaten `ExcludingProjectionIntegrationEvents` ile bu tipleri
hariç tutar; appointment processor da yalnızca kendi tiplerini claim eder. Böylece:

- Appointment projection davranışı **değişmez**.
- Client mesajları generic worker tarafından dead-letter edilmez.
- Email / domain event / appointment mesajları client processor tarafından **işlenmez**
  (`NonClientOutboxMessage_Should_NotBeConsumedByClientProcessor`).

## Idempotency / dedup

`ProcessedProjectionEvents`'e `(EventId, client-read-model-v1)` satırı, read-model upsert'i ile **aynı
Query DB transaction'ı** içinde yazılır. Önce fast-path `AnyAsync` kontrolü, sonra UNIQUE PK insert
yarış koruması (`2601/2627` → duplicate). Aynı event tekrar gelirse read-model'e dokunulmaz.

## Stale / out-of-order koruması (sequence alanı yok)

Client event'lerinde per-aggregate sequence **yoktur**. Bu yüzden ordering anahtarı olarak event'in
`OccurredAtUtc` değeri kullanılır:

- `ClientReadModel`'e **additive** `LastEventOccurredAtUtc` kolonu eklendi
  (migration: `20260619203849_AddClientReadModelLastEventOccurredAtUtc`, `datetime2`,
  `defaultValue = 0001-01-01`).
- Upsert: mevcut satırda `incoming.OccurredAtUtc < existing.LastEventOccurredAtUtc` ise event
  **stale** kabul edilir; read-model verisi **korunur**, yalnızca dedup satırı yazılır ve outbox
  processed işaretlenir (yeniden işlenmez). Aksi halde tüm alanlar overwrite edilir ve
  `LastEventOccurredAtUtc` güncellenir.
- **EventId tek başına ordering için kullanılmaz** — yalnızca idempotency/dedup içindir.
- `LastProjectedAtUtc` projection wall-clock'tur (`TimeProvider`), event zamanından ayrı tutulur.

Bu, "newer update önce, older event sonra gelirse yeni veri ezilmez" senaryosunu kapatır
(`StaleEvent_Should_NotOverwriteNewerReadModelData`). Outbox ordering (`CreatedAtUtc, Id`) sağlıklı
akışta zaten doğru sırayı verir; `LastEventOccurredAtUtc` guard'ı out-of-order/retry/replay'e karşı
ek savunmadır.

## Bilinmeyen event type davranışı

- Tasarım gereği bilinmeyen tip (ör. `client.unknown.v1`) `ClientIntegrationEventsOnly` filtresinden
  geçmez → client processor tarafından **claim edilmez**, dokunulmadan pending kalır, exception üretmez
  (`UnknownClientEventType_Should_NotBeClaimed_And_RemainUntouched`).
- Savunma amaçlı: claim edilmiş bir mesaj registry/dispatch'te çözülemezse
  `UnknownClientIntegrationEventTypeException` → retry'siz **kontrollü dead-letter** (asla başarılı
  olamayacağı için).
- Bozuk payload (geçersiz JSON) gibi geçici/operasyonel hatalar `OutboxRetryHelper` ile retry → eşik
  sonrası dead-letter (`InvalidJson_Should_Retry_And_NotMarkOutboxProcessed`).

## DI

`AddInfrastructure`:

```csharp
services.Configure<ClientProjectionOptions>(configuration.GetSection(ClientProjectionOptions.SectionName));
services.AddScoped<IClientProjectionProcessor, ClientProjectionProcessor>();
services.AddHostedService<ClientProjectionHostedService>();
```

## Testler

`tests/.../Projections/Clients/ClientProjectionIntegrationTests.cs` (ayrı command/query LocalDB;
hosted servisler kapalı; `ProcessBatchAsync` doğrudan çağrılır):

- create → `ClientReadModel` insert + outbox processed + dedup satırı.
- update → mevcut satır güncellenir (tek satır).
- duplicate → idempotent (tek read-model, tek dedup, retry/dead-letter yok).
- stale/out-of-order → yeni veri ezilmez; stale event yine tüketilir.
- unknown type → claim edilmez, pending kalır.
- invalid json → retry, processed olmaz.
- non-client (email) mesaj → client processor tüketmez.

## Garanti

- Read path değişmedi (`GetClientsListQueryHandler` ve diğer query handler'lar dokunulmadı).
- `QueryReadModelsOptions`'a `ClientsEnabled` flag eklenmedi.
- Appointment projection davranışı değişmedi.
- Migration additive'dir (yalnızca yeni kolon; mevcut veri/şema bozulmaz).
- Commit atılmadı.
