# CQRS-11A — Staging rollout and rollback validation

Controlled validation plan for Appointment CQRS read-model cutover:

```text
Mode A (command-read) → Mode B (appointment-query) → Mode C (full-query) → Mode B → Mode A
```

**Scope:** staging or staging-like local validation only. No production deploy, no production secrets in repo.

## Infrastructure findings (repo)

| Question | Answer |
|----------|--------|
| Dedicated staging config in repo? | **No** dedicated staging host/IaC. Added `appsettings.Staging.json` with safe Mode A defaults (no connection strings). |
| Staging API instance count | **Unknown externally.** Repo documents **single instance** requirement for projector (`appointment-projection-operations.md`). Multi-instance without claim/lease → block Mode B/C rollout (CQRS-11D). |
| Command/Query DB separation | **Yes** by design. Distinct connection keys: `DefaultConnection` / `SqlServer` (command) and `QueryConnection` (query). Rebuild service rejects identical catalogs. |
| Migration strategy | **Manual** via `Backend.Veteriner.DbMigrator`: `migrate`, `migrate-query`. API startup does **not** auto-migrate. |
| Projection rebuild | `dotnet run --project src/Backend.Veteriner.DbMigrator -- rebuild-appointment-projections [--batch-size N]` |
| Feature flag change needs restart? | **Yes.** `IOptions<QueryReadModelsOptions>` and projector options are bound at startup. Env override requires process restart/redeploy. |
| Rollback by flags only? | **Partially.** Set `QueryReadModels:*` back to `false` + restart → Command read resumes immediately. Projector must stay **enabled** so Query DB stays current. |
| Staging secrets source | **Out of repo:** deployment platform env vars / secret store / user secrets (Development only). Never commit JWT key or SQL passwords. |
| Health externally reachable? | Depends on deployment. Endpoints: `/health/live`, `/health/ready`, `/health`. Staging should expose `/health/ready` to operators only. |
| Query DB backup/restore | **Not documented in repo.** Operational runbook item for staging ops team. |

## Environment variables

```text
ConnectionStrings__DefaultConnection
ConnectionStrings__QueryConnection
QueryReadModels__AppointmentsEnabled
QueryReadModels__DashboardAppointmentsEnabled
AppointmentProjection__Enabled
ASPNETCORE_ENVIRONMENT=Staging
```

### Safe staging default (Mode A)

```text
QueryReadModels__AppointmentsEnabled=false
QueryReadModels__DashboardAppointmentsEnabled=false
AppointmentProjection__Enabled=true
```

Mode B/C enabled **only** via explicit env override after health/parity checks.

## Startup diagnostics

API logs effective CQRS flags and DB **catalog names only** (no full connection strings):

```text
CQRS startup configuration. Environment=... ProjectionEnabled=... AppointmentsQueryReadEnabled=... DashboardQueryReadEnabled=... CommandDbCatalog=... QueryDbCatalog=...
```

## Rollout tooling

| Script | Purpose |
|--------|---------|
| `tests/load/tools/Invoke-CqrsStagedRollout.ps1` | Dry-run plan (default) or `-Apply` validation |
| `tests/load/tools/Test-CqrsStagedRollout.ps1` | Script/mode/rollback automation checks |
| `tests/load/tools/Test-CqrsLoadPreflight.ps1` | Health + flag + queue preflight |
| `tests/load/tools/Invoke-CqrsLoadDataReset.ps1` | Rebuild/parity prep (load-test DBs) |

### Dry-run (no changes)

```powershell
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 `
  -BaseUrl https://staging-api.example `
  -Mode appointment-query
```

### Apply validation (API already configured for target mode)

```powershell
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 `
  -BaseUrl https://staging-api.example `
  -Mode appointment-query `
  -CommandDatabase VetinityCommandDb_Staging `
  -QueryDatabase VetinityQueryDb_Staging `
  -Apply
```

### Full sequence plan

```powershell
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl <url> -Mode command-read -ShowSequence
```

### Rollback plans

```powershell
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl <url> -Mode command-read -ShowRollbackFrom full-query
.\tests\load\tools\Invoke-CqrsStagedRollout.ps1 -BaseUrl <url> -Mode command-read -ShowRollbackFrom appointment-query
```

## Per-mode read sources

| Mode | Appointment list/calendar | Dashboard appointment metrics |
|------|---------------------------|-------------------------------|
| A | Command DB | Command DB |
| B | Query DB | Command DB |
| C | Query DB | Query DB |

Verify via `/health/ready` → `appointment-projection.data.appointmentsReadEnabled` / `dashboardReadEnabled` and structured API logs (`Appointments list generated from query read-model` when Query path active).

## Rollback rules

1. **Do not** set `AppointmentProjection__Enabled=false` during rollback.
2. Rollback flags only; restart single API instance.
3. After rollback to A, Query projection continues; re-enable C later should **not** require rebuild if queue stayed clean.

## Query DB outage (Mode C)

**No automatic Command fallback** (by design).

Expected:

- `query-sql` health → Unhealthy
- Mode B/C read endpoints fail (not silent fallback)
- Commands still succeed; outbox accumulates until Query DB returns

Operational response:

```text
Detect outage → rollback flags to Mode A or B → restart → verify Command reads
→ restore Query DB → migrate-query → drain queue/rebuild parity → re-enable B/C
```

## Rebuild procedure (Query DB empty/corrupt)

1. Set read flags to Mode A (`AppointmentsEnabled=false`, `DashboardAppointmentsEnabled=false`).
2. Keep projector enabled OR pause only during rebuild window (see operations doc).
3. Ensure pending/dead-letter appointment outbox = 0.
4. `rebuild-appointment-projections`
5. Parity + duplicate/invalid checks (`Invoke-CqrsStagedRollout.ps1 -Apply` or `Invoke-CqrsLoadDataReset.ps1`)
6. Enable Mode B, validate, then Mode C.

## Staging-like local validation

When no real staging exists, use **LoadTest** databases and localhost API (same tooling as CQRS-10):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "LoadTest"
# Set connection strings + Jwt__Key via env (not committed)
.\tests\load\tools\Invoke-CqrsLoadMeasurement.ps1   # mode switch + perf (local)
.\tests\load\tools\Test-CqrsStagedRollout.ps1        # script automation
```

**Do not** report local LoadTest runs as production staging sign-off.

## Acceptance criteria (CQRS-11A)

See phase checklist in project tracking. Key gates:

- Single API/projector instance
- Health Healthy, queue `pending=retry=dead-letter=0`
- Mode-appropriate read sources
- Lifecycle create/reschedule/cancel smoke
- Rollback C→B→A without data loss; Query projection stays current
- No secrets in script output/logs

## Related docs

- [`appointment-projection-operations.md`](appointment-projection-operations.md)
- [`appointment-cqrs-load-test.md`](appointment-cqrs-load-test.md)
- [`cqrs-11b-operations-runbook.md`](cqrs-11b-operations-runbook.md)
