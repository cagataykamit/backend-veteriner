# CQRS-12C-6 — Pet read-model backfill / rebuild

## Kapsam

CQRS-12C-1..5 ile kurulan pet read-model'ine, **mevcut Command DB `Pets` kayıtlarını** Query DB
`PetReadModels` tablosuna **idempotent** biçimde dolduran/yeniden oluşturan backfill mekanizması
ekler. Bu olmadan `PetsEnabled=true` açılırsa read-model eksik/boş döner (fallback yok), bu yüzden
backfill **flag açmadan önceki önkoşuldur**.

Bu fazın **dışında** (yapılmadı): `GetPetsListQueryHandler` routing değişikliği, `PetsEnabled`
default'unu değiştirme, pet command handler / event contract değişikliği, `PetProjectionProcessor`
değişikliği, health/parity/smoke davranışı değişikliği, route/auth/permission/tenant scope
davranışı, canlı API/k6 rollout acceptance (CQRS-12C-7).

## Yaklaşım — non-destructive idempotent upsert

Client backfill (CQRS-12B-6) ile aynı desen: **upsert** (`PetId` PK üzerinden).

- Query tablosu **silinmez**; eksik satır insert, mevcut satır update edilir.
- Backfill **canlı projection akışıyla aynı anda** çalışabilir.
- Komponentler:
  - `PetReadModelBackfillPlanner` (Application, **saf**) — timestamp ve insert/update/skip kararı.
  - `PetReadModelBackfillService` (Infrastructure) — Command DB'den batch okuma + Query upsert.
  - `IPetReadModelBackfillService` / `PetReadModelBackfillResult`.
  - DbMigrator komutu: `backfill-pet-projections`.

Snapshot üretimi mevcut `PetProjectionSnapshotFactory` ile yapılır (Client/Species/Breed/Color
join'leri Command DB'den). Upsert alan eşlemesi `PetProjectionProcessor` ile aynıdır.

## Timestamp / `LastEventOccurredAtUtc` stratejisi

Pet domain'inde **`CreatedAtUtc` / `UpdatedAtUtc` yoktur** (Client backfill stratejisi birebir
kopyalanmaz).

Seçilen strateji:

```
LastEventOccurredAtUtc = DateTime.MinValue (UTC kind) — BackfillBaselineOccurredAtUtc
LastProjectedAtUtc     = backfill wall-clock (TimeProvider)
LastEventId            = Guid.Empty (BackfillEventId)
```

Gerekçe:

- Backfill snapshot'ının gerçek event zamanı yoktur; wall-clock kullanmak race'te gerçek event'lerin
  ezilmesine yol açabilir.
- Minimum UTC sentinel, handler'da `DateTime.UtcNow` ile üretilen `pet.created.v1` /
  `pet.updated.v1` event'lerinin **her zaman** backfill satırını ezebilmesini garanti eder.
- Mevcut satır baseline (`LastEventOccurredAtUtc == sentinel`) ise re-run güvenli **Update** yapar
  (Command DB değişikliklerini yansıtır).
- Mevcut satır daha yeni gerçek event ile yazılmışsa **SkipStale** (veri korunur).

`PetReadModel`'de `CreatedAtUtc` kolonu yoktur; backfill bunu kullanmaz.

## Idempotency

- Anahtar `PetId` (read-model PK). Tekrar çalıştırma **duplicate üretmez**.
- Karar (`PetReadModelBackfillPlanner.Decide`):
  - satır yok → **Insert**
  - `backfillOccurredAt >= existing.LastEventOccurredAtUtc` → **Update** (eşitlik dâhil; re-run güvenli)
  - `backfillOccurredAt <  existing.LastEventOccurredAtUtc` → **SkipStale**
- Aynı veriyle re-run, yalnızca `LastProjectedAtUtc`'yi tazeler; satır sayısı değişmez.

## Race condition değerlendirmesi

1. **Stale guard ortak**: `PetProjectionProcessor` ve backfill aynı ordering kuralını kullanır.
2. **ProcessedProjectionEvents'e dokunulmaz**: Backfill sahte event yazmaz; dedup bozulmaz.
3. **Batch transaction**: Her batch kendi transaction'ında commit edilir; yarıda kalırsa yeniden
   çalıştırılabilir.

Sıralama:

- Event önce, backfill sonra → backfill sentinel daha eski → **SkipStale**.
- Backfill önce (baseline), event sonra → event daha yeni → satır güncellenir (gerçek `LastEventId`).

## Denormalize alanlar

Backfill Command DB'den güncel snapshot üretir (`PetProjectionSnapshotFactory`):

- `ClientFullName`, `ClientFullNameNormalized` — `Clients` join
- `SpeciesName`, `SpeciesNameNormalized` — `Species` navigation
- `Breed`, `BreedId`, `BreedRefName` — pet + `BreedRef` navigation
- `ColorName`, `ColorNameNormalized` — `ColorRef` navigation

Rename propagation event tabanlı değildir; backfill mevcut Command DB değerleriyle denormalize
alanları tazeler.

## Tenant güvenliği

- `--tenant <guid>` verilirse yalnızca o tenant'ın `Pets` satırları okunur/yazılır; parity de tenant
  kapsamlıdır.
- TenantId upsert sırasında snapshot'tan yazılır.

## DbMigrator komutu

```text
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections --batch-size 500
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections --tenant <guid>
```

- Default batch size: 500
- Exit code **2** parity in-sync değilse (`PetsEnabled` açmadan önce düzelt)
- Exit code **1** exception

## Testler (deterministik, CI-safe)

- **Unit**: `PetReadModelBackfillPlannerTests` — baseline sentinel, insert/update/skip kararları.
- **Integration** (`pet-projection` collection):
  - `PetReadModelBackfillIntegrationTests` — boş query doldurma, parity, idempotent re-run, command
    değişikliği update, tenant scope, ProcessedProjectionEvents yazmaz, stale guard, global parity,
    denormalize alanlar.

## Rollout önkoşulu

```text
migrate-query -> backfill-pet-projections -> parity (InSync) -> health (Healthy)
              -> QueryReadModels__PetsEnabled=true + restart -> list/search smoke
```

Detay: [`cqrs-acceptance-runbook.md`](cqrs-acceptance-runbook.md) §9.

## Garanti

- `PetsEnabled` default `false` kaldı.
- Projection / event / write / health davranışı değişmedi.
- Client/Appointment backfill bozulmadı.
