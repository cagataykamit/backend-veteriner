# CQRS-12B-5 — Client read-model health / parity / smoke

## Kapsam

Client read-model'i (CQRS-12B-1..4 ile kurulan) **operasyonel olarak gözlemlenebilir** hale getirir:

- Client projection queue health (`/health/ready` → `client-projection`).
- Command DB `Clients` ile Query DB `ClientReadModels` satır sayısı parity okuması.
- Flag açık/kapalı smoke testleri.
- Rollback runbook.

Bu fazın **dışında** (yapılmadı): client command handler değişikliği, projection event contract
değişikliği, `GetClientsListQueryHandler` routing mantığının yeniden yazılması, Pet read-model,
**backfill/rebuild** (CQRS-12B-6), route/auth/permission/tenant scope davranışı, Query DB outage için
otomatik Command fallback. Flag default `false` kalır.

## Health

Appointment health deseniyle hizalı, hosting-neutral ve test edilebilir:

- `ClientProjectionStatus` (`Application/Projections/Clients`) — salt okunur queue durumu
  (pending / retry-waiting / dead-letter / oldest pending age / next retry / query DB reachable /
  pending migrations / projection enabled).
- `IClientProjectionStatusReader` + `ClientProjectionStatusReader` (`Infrastructure/Projections/Clients`)
  — Command DB outbox'tan **yalnızca** client integration event tiplerini
  (`OutboxMessageQueryFilters.ClientIntegrationEventsOnly`) sayar; appointment/generic akışa dokunmaz.
  Query DB durumu mevcut `IQueryDatabaseStatusReader` ile paylaşılır.
- `ClientProjectionHealthEvaluator` (saf fonksiyon) + `ClientProjectionHealthLevel` /
  `ClientProjectionHealthEvaluation`.
- `ClientProjectionHealthOptions` (`ClientProjectionHealth` section; default Degraded=10s,
  Unhealthy=30s, DeadLetterIsUnhealthy=true). Tüm `appsettings*` (base/Dev/Staging/Prod/IntegrationTests)
  appointment ile aynı eşiklerle eklendi.
- `ClientProjectionHealthCheck` (`Api/Health`) → `/health/ready` içinde `client-projection` entry.

### Health kuralları (öncelik sırası)

1. Query DB erişilemiyor → **Unhealthy**.
2. Query DB bekleyen migration → **Unhealthy**.
3. Dead-letter > 0 (ve `DeadLetterIsUnhealthy`) → **Unhealthy**.
4. `ClientsEnabled=true` ama projection disabled:
   - pending/retry varsa → **Unhealthy**, yoksa → **Degraded**.
5. Oldest pending age ≥ Unhealthy eşiği → **Unhealthy**; ≥ Degraded eşiği → **Degraded**.
6. retry-waiting > 0 → **Degraded**.
7. Aksi halde → **Healthy**.

### Health `data` alanları (PII/secret yok)

`pendingCount`, `retryWaitingCount`, `deadLetterCount`, `oldestPendingAgeSeconds`, `nextRetryAtUtc`,
`projectionEnabled`, `clientsReadEnabled`.

## Parity

Command DB `Clients` ile Query DB `ClientReadModels` satır sayısı karşılaştırması:

- `ClientReadModelParityResult` (`CommandCount`, `QueryCount`, `Difference`, `AbsoluteDifference`,
  `InSync`, `ScopeTenantId`).
- `ClientReadModelParityEvaluator` — saf, deterministik karar mantığı.
- `IClientReadModelParityReader` + `ClientReadModelParityReader` (`Infrastructure/Query/Clients`):
  `GetGlobalParityAsync` ve `GetTenantParityAsync(tenantId)` (`AsNoTracking`, salt okuma).

Client domain'inde **silme/soft-delete yoktur**; bu yüzden tüm event'ler projeksiyon edildiğinde
beklenen durum `InSync == true`'dur.

### Production SQL parity (operatör)

```sql
-- Command DB
SELECT COUNT_BIG(*) FROM Clients;                 -- (opsiyonel) WHERE TenantId = @tenant
-- Query DB
SELECT COUNT_BIG(*) FROM ClientReadModels;        -- (opsiyonel) WHERE TenantId = @tenant
```

Sayılar eşitse parity sağlanmış. Fark pozitifse read-model **geride**dir (bkz. backfill notu).

## Smoke

`GetClientsListQueryHandler` doğru veri yolundan okuyor ve tenant izolasyonunu koruyor mu:

- **Flag kapalı** (`ClientsEnabled=false`): read-model boş olsa bile Command DB'den yanıt verir.
- **Flag açık** (`ClientsEnabled=true`): projeksiyon sonrası Query DB read-model'inden yanıt verir;
  yalnızca istenen tenant satırları.
- **Flag açık + read-model boş**: Command DB'ye **fallback yok** → boş sonuç (rollback = flag kapatma).

## Testler (deterministik, CI-safe)

- **Unit** (`Application.Tests`):
  - `Projections/Clients/ClientProjectionHealthEvaluatorTests` — tüm health seviyeleri + data payload.
  - `Clients/ReadModels/ClientReadModelParityEvaluatorTests` — in-sync / behind / ahead / scope.
- **Integration** (`IntegrationTests`, `client-projection` collection, ayrı LocalDB; hosted servisler
  kapalı, processor manuel çağrılır):
  - `Projections/Clients/ClientProjectionHealthIntegrationTests` — `/health/ready` `client-projection`
    güvenli data alanları; status reader pending; degraded (pending age); unhealthy (dead-letter);
    degraded (flag açık + projection kapalı).
  - `Query/Clients/ClientReadModelParityIntegrationTests` — projeksiyon sonrası tenant parity in-sync;
    tenant izolasyonu; **read-model geride** (projeksiyon yapılmadan).
  - `Query/Clients/ClientReadModelSmokeIntegrationTests` — flag kapalı → Command DB; flag açık →
    read-model; flag açık + boş read-model → fallback yok.

Canlı API / SQL / k6 gerektiren bir test eklenmedi; suite deterministik kalır.

## Rollback runbook

Read path geri alma sırası (en hızlıdan):

1. **Flag kapat:** `QueryReadModels__ClientsEnabled=false`. Read path anında Command DB'ye döner.
2. **(Gerekirse) projection durdur:** `ClientProjection__Enabled=false`. Yalnızca planlı pause için;
   read flag açıkken pending birikirse health Degraded/Unhealthy olur.
3. **Restart:** flag'ler `IOptions` startup binding olduğundan API restart zorunlu.
4. **Health kontrol:** `GET /health/ready` → overall Healthy; `client-projection.data`:
   `projectionEnabled`, `pendingCount`, `retryWaitingCount`, `deadLetterCount`.
5. **Parity kontrol:** Command `Clients` count == Query `ClientReadModels` count (yukarıdaki SQL veya
   `IClientReadModelParityReader`).

**Altın kural:** Read flag rollback edilse bile, read-model'i güncel tutmak için
`ClientProjection__Enabled=true` bırakmak tercih edilir; projector kapatılırsa Query DB geride kalır
ve flag tekrar açılmadan önce **backfill** (12B-6) gerekebilir.

## Backfill neden bu fazda yapılmadı

Mevcut/legacy client satırları read-model'e ancak yeni create/update event'leriyle yansır. Var olan
satırlar için **toplu backfill/rebuild** ayrı bir konudur (CQRS-12B-6). Bu yüzden:

- Canlı ortamda `ClientReadModels` boş/eksikken `ClientsEnabled=true` açılırsa parity **fail eder** ve
  liste eksik döner — bu beklenen durumdur, fallback **yoktur**.
- `ClientReadModelParityIntegrationTests` "read-model geride" senaryosu bu durumu deterministik olarak
  belgeler.
- Üretimde flag açmadan önce 12B-6 backfill çalıştırılmalı ve parity in-sync doğrulanmalıdır.

## Garanti

- Flag default `false`; read path dışında production davranışı değişmedi.
- Projection / event / write davranışı değişmedi (`ClientProjectionProcessor` dokunulmadı).
- Appointment CQRS health/parity davranışı değişmedi (ayrı evaluator/status reader/health entry).
- Mevcut testler kırılmadı.
- Commit atılmadı.
