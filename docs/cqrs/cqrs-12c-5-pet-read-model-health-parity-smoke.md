# CQRS-12C-5 — Pet read-model health / parity / smoke

## Kapsam

Pet read-model'i (CQRS-12C-1..4 ile kurulan) **operasyonel olarak gözlemlenebilir** hale getirir:

- Pet projection queue health (`/health/ready` → `pet-projection`).
- Command DB `Pets` ile Query DB `PetReadModels` satır sayısı parity okuması.
- Flag açık/kapalı smoke testleri (CQRS-12C-4'te başlatıldı; bu fazda health/parity ile birlikte belgelenir).
- Rollback runbook.

Bu fazın **dışında** (yapılmadı): pet command handler değişikliği, projection event contract değişikliği,
`GetPetsListQueryHandler` routing mantığının yeniden yazılması, **backfill/rebuild** (CQRS-12C-6),
**rollout acceptance script** (CQRS-12C-7), route/auth/permission/tenant scope davranışı, Query DB outage
için otomatik Command fallback. Flag default `false` kalır.

## Health

Client projection health deseniyle hizalı, hosting-neutral ve test edilebilir:

- `PetProjectionStatus` (`Application/Projections/Pets`) — salt okunur queue durumu
  (pending / retry-waiting / dead-letter / oldest pending age / next retry / query DB reachable /
  pending migrations / projection enabled).
- `IPetProjectionStatusReader` + `PetProjectionStatusReader` (`Infrastructure/Projections/Pets`)
  — Command DB outbox'tan **yalnızca** pet integration event tiplerini
  (`OutboxMessageQueryFilters.PetIntegrationEventsOnly`) sayar; client/appointment/generic akışa dokunmaz.
  Query DB durumu mevcut `IQueryDatabaseStatusReader` ile paylaşılır.
- `PetProjectionHealthEvaluator` (saf fonksiyon) + `PetProjectionHealthLevel` /
  `PetProjectionHealthEvaluation`.
- `PetProjectionHealthOptions` (`PetProjectionHealth` section; default Degraded=10s,
  Unhealthy=30s, DeadLetterIsUnhealthy=true). Tüm `appsettings*` (base/Dev/Staging/Prod/IntegrationTests)
  client ile aynı eşiklerle eklendi.
- `PetProjectionHealthCheck` (`Api/Health`) → `/health/ready` içinde `pet-projection` entry.

### Health kuralları (öncelik sırası)

1. Query DB erişilemiyor → **Unhealthy**.
2. Query DB bekleyen migration → **Unhealthy**.
3. Dead-letter > 0 (ve `DeadLetterIsUnhealthy`) → **Unhealthy**.
4. `PetsEnabled=true` ama projection disabled:
   - pending/retry varsa → **Unhealthy**, yoksa → **Degraded**.
5. Oldest pending age ≥ Unhealthy eşiği → **Unhealthy**; ≥ Degraded eşiği → **Degraded**.
6. retry-waiting > 0 → **Degraded**.
7. Aksi halde → **Healthy**.

### Health `data` alanları (PII/secret yok)

`pendingCount`, `retryWaitingCount`, `deadLetterCount`, `oldestPendingAgeSeconds`, `nextRetryAtUtc`,
`projectionEnabled`, `petsReadEnabled`.

## Parity

Command DB `Pets` ile Query DB `PetReadModels` satır sayısı karşılaştırması:

- `PetReadModelParityResult` (`CommandCount`, `QueryCount`, `Difference`, `AbsoluteDifference`,
  `InSync`, `ScopeTenantId`).
- `PetReadModelParityEvaluator` — saf, deterministik karar mantığı.
- `IPetReadModelParityReader` + `PetReadModelParityReader` (`Infrastructure/Query/Pets`):
  `GetGlobalParityAsync` ve `GetTenantParityAsync(tenantId)` (`AsNoTracking`, salt okuma).

Pet domain'inde **silme/soft-delete yoktur**; bu yüzden tüm event'ler projeksiyon edildiğinde
beklenen durum `InSync == true`'dur.

### Production SQL parity (operatör)

```sql
-- Command DB
SELECT COUNT_BIG(*) FROM Pets;                 -- (opsiyonel) WHERE TenantId = @tenant
-- Query DB
SELECT COUNT_BIG(*) FROM PetReadModels;        -- (opsiyonel) WHERE TenantId = @tenant
```

Sayılar eşitse parity sağlanmış. Fark pozitifse read-model **geride**dir (bkz. backfill notu).

## Smoke

`GetPetsListQueryHandler` doğru veri yolundan okuyor ve tenant izolasyonunu koruyor mu
(CQRS-12C-4'te eklenen `PetReadModelSmokeIntegrationTests`):

- **Flag kapalı** (`PetsEnabled=false`): read-model boş olsa bile Command DB'den yanıt verir.
- **Flag açık** (`PetsEnabled=true`): projeksiyon sonrası Query DB read-model'inden yanıt verir;
  yalnızca istenen tenant satırları.
- **Flag açık + read-model boş**: Command DB'ye **fallback yok** → boş sonuç (rollback = flag kapatma).

## Testler (deterministik, CI-safe)

- **Unit** (`Application.Tests`):
  - `Projections/Pets/PetProjectionHealthEvaluatorTests` — tüm health seviyeleri + data payload.
  - `Pets/ReadModels/PetReadModelParityEvaluatorTests` — in-sync / behind / ahead / scope.
- **Integration** (`IntegrationTests`, `pet-projection` collection, ayrı LocalDB; hosted servisler
  kapalı, processor manuel çağrılır):
  - `Projections/Pets/PetProjectionHealthIntegrationTests` — `/health/ready` `pet-projection`
    güvenli data alanları; status reader pending; client/appointment event sayılmaz; degraded
    (pending age); unhealthy (dead-letter); degraded (flag açık + projection kapalı).
  - `Query/Pets/PetReadModelParityIntegrationTests` — projeksiyon sonrası tenant/global parity in-sync;
    tenant izolasyonu; **read-model geride** (projeksiyon yapılmadan).
  - `Query/Pets/PetReadModelSmokeIntegrationTests` — flag kapalı → Command DB; flag açık →
    read-model; flag açık + boş read-model → fallback yok.

Canlı API / SQL / k6 gerektiren bir test eklenmedi; suite deterministik kalır.

## Rollback runbook

Read path geri alma sırası (en hızlıdan):

1. **Flag kapat:** `QueryReadModels__PetsEnabled=false`. Read path anında Command DB'ye döner.
2. **(Gerekirse) projection durdur:** `PetProjection__Enabled=false`. Yalnızca planlı pause için;
   read flag açıkken pending birikirse health Degraded/Unhealthy olur.
3. **Restart:** flag'ler `IOptions` startup binding olduğundan API restart zorunlu.
4. **Health kontrol:** `GET /health/ready` → overall Healthy; `pet-projection.data`:
   `projectionEnabled`, `pendingCount`, `retryWaitingCount`, `deadLetterCount`.
5. **Parity kontrol:** Command `Pets` count == Query `PetReadModels` count (yukarıdaki SQL veya
   `IPetReadModelParityReader`).

**Altın kural:** Read flag rollback edilse bile, read-model'i güncel tutmak için
`PetProjection__Enabled=true` bırakmak tercih edilir; projector kapatılırsa Query DB geride kalır
ve flag tekrar açılmadan önce **backfill** (12C-6) gerekebilir.

## Backfill (CQRS-12C-6 — bu fazda yapılmadı)

Mevcut/legacy pet satırları read-model'e ancak yeni create/update event'leriyle yansır. Var olan
satırlar için **toplu backfill/rebuild** CQRS-12C-6 konusudur. Bu yüzden:

- Canlı ortamda `PetReadModels` boş/eksikken `PetsEnabled=true` açılırsa parity **fail eder** ve
  liste eksik döner — bu beklenen durumdur, fallback **yoktur**.
- `PetReadModelParityIntegrationTests` "read-model geride" senaryosu bu durumu deterministik olarak
  belgeler.
- Üretimde flag açmadan **önce** 12C-6 backfill çalıştırılmalı ve parity in-sync doğrulanmalıdır.

### Rename propagation (bilinçli sınırlama)

Pet read-model'deki denormalize alanlar (`ClientFullName`, `SpeciesName`, `ColorName`, `BreedRefName`)
yalnızca pet create/update snapshot'ıyla güncellenir; client/species/color rename propagation bu
fazlarda yoktur.

## Garanti

- Flag default `false`; read path dışında production davranışı değişmedi.
- Projection / event / write davranışı değişmedi (`PetProjectionProcessor` dokunulmadı).
- Client/Appointment CQRS health/parity davranışı değişmedi (ayrı evaluator/status reader/health entry).
- Mevcut testler kırılmadı.
