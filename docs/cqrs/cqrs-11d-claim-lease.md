# CQRS-11D: Claim / Lease ve Güvenilir Ordering

Bu belge CQRS-11D alt fazlarının ortak tasarımını tanımlar. **CQRS-11D-2A** yalnızca ordering temelini uygular; claim/lease sonraki alt fazlardadır.

## Delivery semantiği

Appointment read-model projection, Command DB outbox üzerinden **at-least-once** teslim alır. Her başarılı appointment mutasyonu tam olarak bir integration event üretir. Projection tüketicisi (`appointment-read-model-v1`) event’i işlerken Query DB tarafında dedup ve sıralama kuralları uygulanır (11D-2C).

## Üç doğruluk katmanı

1. **Command DB — mutation sequence:** `Appointments.MutationSequence` monoton artar; EF optimistic concurrency token’dır.
2. **Outbox metadata:** `OutboxMessages.AppointmentId` + `AppointmentSequence` claim/ordering sorguları için kolon metadata’sıdır (payload JSON parse edilmez).
3. **Query DB — processed dedup:** `ProcessedProjectionEvents (EventId, ConsumerName)` fence sağlar (11D-2C).

## MutationSequence

| Durum | Değer |
|-------|-------|
| Yeni entity (constructor) | `0` |
| İlk başarılı mutasyon (create dahil) | `1` |
| Her sonraki başarılı mutasyon | `önceki + 1` |
| Migration sonrası mevcut satırlar | `0` (backfill) |
| Başarısız validation / domain no-op | artmaz |

`MutationSequence` yalnızca sayaç değildir; `IsConcurrencyToken()` ile SQL `WHERE MutationSequence = @original` kontrolü yapılır. Çakışmada API **409** (`Appointments.ConcurrencyConflict`) döner.

## Event metadata

Tüm `appointment.*.v1` integration event kayıtları şunu taşır:

```text
AppointmentId        (Current.AppointmentId)
AppointmentSequence  (domain mutasyon anındaki MutationSequence)
```

`CreatedAtUtc` veya `Guid` sıralama için kullanılmaz.

## Outbox invariant (yeni binary)

Appointment integration eventleri için:

```text
AppointmentId IS NOT NULL
AppointmentSequence IS NOT NULL
AppointmentSequence > 0
```

Filtrelenmiş unique index:

```text
UNIQUE (AppointmentId, AppointmentSequence)
WHERE AppointmentId IS NOT NULL AND AppointmentSequence IS NOT NULL
```

Appointment dışı outbox tipleri (`email`, vb.) metadata alanlarını `NULL` bırakır.

## Migration varsayımları

- **Drain-first:** Production deploy öncesi projection queue boşaltılır.
- Tarihî `OutboxMessages` satırları JSON backfill yapılmaz; `AppointmentId` / `AppointmentSequence` `NULL` kalır.
- Mevcut `Appointments` satırları `MutationSequence = 0` ile backfill edilir; ilk yeni mutasyon `sequence = 1` üretir.
- Tarihî event sayısı tahmin edilmez.

## Mixed-version rollout uyarısı

Migration uygulandıktan sonra **eski binary** hâlâ appointment write kabul ederse, ürettiği yeni `OutboxMessages` satırlarında `AppointmentId` ve `AppointmentSequence` kolonları **null kalır**. Bu satırlar 11D-2B+ claim/lease sorgusu tarafından güvenli şekilde işlenemez; per-appointment ordering ve atomik claim için gerekli metadata eksiktir.

Bu nedenle **migration ile yeni binary deploy arasında eski API appointment write kabul etmemelidir.**

Rollout sırasında:

- Queue drain tamamlandıktan sonra appointment **write trafiği durdurulmalı** veya eski API instance **tamamen kapatılmalıdır**.
- Eski ve yeni appointment writer instance’ları **aynı anda çalıştırılmamalıdır**.

Tarihî, zaten `ProcessedAtUtc` dolu processed satırların `AppointmentId` / `AppointmentSequence` alanlarının null kalması sorun değildir; drain-first migration varsayımıyla bu satırlar claim sorgusuna girmeyecektir. Risk yalnızca migration sonrası üretilen **yeni pending** appointment event’lerinin metadata’sız yazılmasıdır.

## Henüz uygulanmayan (11D-2B+)

- Claim kolonları, `ClaimToken`, worker ID
- Atomik claim SQL / lease expiry
- Multi-instance processor
- `LastAppliedAppointmentSequence` (Query read model)

## Transaction atomikliği

`OutboxSaveChangesInterceptor` appointment güncellemesi ile outbox insert’ini aynı `SaveChanges` transaction’ında birleştirir. Concurrency conflict veya rollback durumunda ne entity mutasyonu ne de outbox satırı kalıcı olmaz.
