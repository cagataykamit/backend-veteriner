# Appointment projection operations

Operational runbook for the physical CQRS path: **Command DB → Outbox → AppointmentProjectionProcessor → Query DB**.

## Deploy order

1. Apply Command DB migrations (`DbMigrator` or EF migrate on command database).
2. Apply Query DB migrations (`migrate-query` / QueryDbContext migrations).
3. Deploy API with `QueryReadModels:*` flags **false** (default).
4. Ensure exactly **one** API instance has `AppointmentProjection:Enabled=true`.
5. Verify `/health/ready` includes `appointment-projection` as **Healthy**.
6. Enable read flags only after lag is acceptable (see cutover below).

## Health and lag

- **Endpoint:** `GET /health/ready` — check `appointment-projection` entry.
- **Healthy:** Query DB reachable, no pending Query migrations, no dead-letter, no pending appointment events (or oldest pending age below `AppointmentProjectionHealth:DegradedAfterSeconds`).
- **Degraded:** Pending backlog aging, retry-waiting, or read flags enabled while projector disabled.
- **Unhealthy:** Query DB down, pending Query migrations, dead-letter (when `DeadLetterIsUnhealthy=true`), or backlog age ≥ `UnhealthyAfterSeconds`.

Structured batch logs (`Appointment projection batch completed`) include `ProcessedCount`, `FailedCount`, `DeadLetteredCount`, `DurationMs`, `OldestPendingAgeMs`.

## Pending / dead-letter inspection

Use Command DB `OutboxMessages` filtered by known types in `AppointmentIntegrationEventTypes.All` (not prefix wildcards):

- **Pending (ready):** `ProcessedAtUtc IS NULL`, `DeadLetterAtUtc IS NULL`, `NextAttemptAtUtc IS NULL OR <= UTC now`
- **Retry-waiting:** `NextAttemptAtUtc > UTC now`
- **Dead-letter:** `DeadLetterAtUtc IS NOT NULL`

`IAppointmentProjectionStatusReader` exposes the same aggregates for tooling/tests.

## Rebuild

1. Stop API instances that run the projector (or set `AppointmentProjection:Enabled=false` on all).
2. Run `IAppointmentProjectionRebuildService.RebuildAsync` (DbMigrator/maintenance job).
3. Confirm Query read-model counts match Command appointments for the tenant scope.
4. Restart single projector instance; process live outbox tail.

Rebuild clears `ProcessedProjectionEvents`; live events after rebuild append normally.

## Projector single instance

`SemaphoreSlim` protects **one process only**. Multiple replicas with `AppointmentProjection:Enabled=true` can pick the same outbox head; idempotency limits damage but causes duplicate work and retry noise.

**Production rule:** run the projector on **one** instance until claim/lease (future phase).

## Feature flag cutover

### `QueryReadModels:AppointmentsEnabled`

- **false:** list/calendar use Command DB (default).
- **true:** list/calendar use Query DB; no silent fallback on failure.

Cutover when: projector healthy, pending appointment outbox ≈ 0, rebuild parity verified.

### `QueryReadModels:DashboardAppointmentsEnabled`

Independent of `AppointmentsEnabled`. Same rules for dashboard appointment-derived fields only.

### Rollback

Set flags back to **false** — Command read paths resume immediately. Projector may keep writing Query DB; **do not delete** Query data for rollback.

## Query DB outage

- Commands succeed; outbox rows accumulate.
- Projector retries; outbox stays unprocessed until Query DB returns.
- Health → **Unhealthy**; flag=true reads fail (by design, no Command fallback).
- Recovery: restore Query DB, run pending Query migrations, let projector drain backlog; confirm health returns to Healthy.

## Dead-letter

Do **not** manually set `ProcessedAtUtc` on appointment outbox rows without understanding event payload and Query state.

Investigate `LastError`, fix root cause (schema, data, Query connectivity), then requeue or replay via supported tooling. Dead-letter blocks strict ordering for subsequent events until resolved.

## Manual “mark processed” risks

Marking outbox processed while Query DB was not updated creates **permanent drift**. Marking Query `ProcessedProjectionEvents` without outbox alignment causes duplicate-skip or stuck head depending on order.

Always prefer: fix Query → let projector retry → verify read-model row and outbox processed timestamp together.

## Rebuild / projector stop

Not strictly required for every rebuild, but recommended: pause projector during full rebuild to avoid racing live events with bulk delete/insert.

## Related tests

- Projector: `AppointmentProjectionIntegrationTests`, `AppointmentProjectionOrderingIntegrationTests`
- Outbox E2E: `AppointmentCommandOutboxIntegrationTests`
- Eventual consistency: `AppointmentEventualConsistencyIntegrationTests`, `AppointmentProjectionHostedServiceIntegrationTests`
- Query parity: `AppointmentListCalendarQueryParityIntegrationTests`, `DashboardAppointmentQueryParityIntegrationTests`
