# CQRS-12B-7 — Client read-model rollout acceptance / final readiness

Client read-model geçişinin (CQRS-12B-1..6) **operasyonel kapanışı**: rollout sırası, backfill sonrası
güvenli flag açma, rollback sırası, CI-safe ve manuel acceptance ayrımı, ve "Client CQRS v1 tamamlandı"
checklist'i — tek sayfada.

**Kapsam:** docs + acceptance tooling kapanışı. Production code, client command handler, projection/event
contract, `GetClientsListQueryHandler` routing, route/auth/permission/tenant scope **bu fazda değişmez**.
`QueryReadModels:ClientsEnabled` default **`false`** kalır.

**İlgili dokümanlar:**

- [`cqrs-12b-4-client-read-model-reader-flag.md`](cqrs-12b-4-client-read-model-reader-flag.md) — reader + flag + routing
- [`cqrs-12b-5-client-read-model-health-parity-smoke.md`](cqrs-12b-5-client-read-model-health-parity-smoke.md) — health / parity / smoke
- [`cqrs-12b-6-client-read-model-backfill.md`](cqrs-12b-6-client-read-model-backfill.md) — backfill / rebuild
- [`cqrs-acceptance-runbook.md`](cqrs-acceptance-runbook.md) — appointment + client birleşik runbook

---

## 1. Rollout sırası (ClientsEnabled açmadan ÖNCE)

`Get-CqrsClientRolloutSequence` ile birebir; **flag açma en sonda**dır.

| # | Adım | Komut / aksiyon | Doğrulama |
|---|------|-----------------|-----------|
| 1 | `query-migration` | `dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query` | `ClientReadModels` tablosu mevcut; bekleyen migration yok |
| 2 | `backfill` | `dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections` | Backfill başarılı; parity in-sync (mismatch → exit code `2`) |
| 3 | `parity-check` | `IClientReadModelParityReader` veya SQL `COUNT_BIG` | `CommandCount == QueryCount` (InSync) |
| 4 | `health-check` | `GET /health/ready` | overall Healthy; `client-projection` Healthy; `deadLetterCount=0` |
| 5 | `clients-enabled` | `QueryReadModels__ClientsEnabled=true` → restart | startup log `ClientsQueryReadEnabled=True`; read path Query DB |
| 6 | `smoke` | `GET /api/v1/clients` (list/search) | yalnızca istenen tenant satırları; parity korunur |

```text
migrate-query -> backfill-client-projections -> parity (InSync) -> health (Healthy)
              -> ClientsEnabled=true + restart -> smoke
```

> **Altın kural:** Backfill ve parity in-sync doğrulaması **flag açmadan önce** zorunludur. Aksi halde
> `ClientReadModels` boş/eksikken `ClientsEnabled=true` açılırsa liste eksik döner — **fallback yoktur**.

### Backfill tenant bazlı

```text
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections --tenant <guid>
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections --batch-size 500
```

Backfill **idempotent + non-destructive upsert**'tir; tekrar çalıştırınca duplicate üretmez, daha yeni
event ile yazılmış satırı ezmez (bkz. 12B-6). Bu yüzden canlı projection akışıyla aynı anda çalışabilir.

---

## 2. Rollback sırası (read path geri alma)

`Get-CqrsClientRollbackSequence` ile birebir. En hızlı geri alma flag kapatmadır.

| # | Adım | Komut / aksiyon | Doğrulama |
|---|------|-----------------|-----------|
| 1 | `clients-disabled` | `QueryReadModels__ClientsEnabled=false` | read path Command DB'ye döner |
| 2 | `restart` | tek instance restart/deploy (flag'ler IOptions startup binding) | startup log `ClientsQueryReadEnabled=False` |
| 3 | `health-check` | `GET /health/ready` | overall Healthy; `client-projection` sağlıklı |
| 4 | `projection-disabled` *(opsiyonel)* | `ClientProjection__Enabled=false` | yalnızca planlı pause |

```text
ClientsEnabled=false -> restart -> health (Healthy)
                     -> (opsiyonel) ClientProjection:Enabled=false
```

> **Altın kural:** Read flag rollback edilse bile `ClientProjection__Enabled=true` **kalmalıdır**.
> Projector kapatılırsa Query DB geride kalır ve flag tekrar açılmadan önce yeniden **backfill** gerekebilir.
> `projection-disabled` yalnızca planlı pause içindir; read flag açıkken pending birikirse health
> Degraded/Unhealthy olur.

---

## 3. Health beklenti matrisi (rollout/rollback boyunca)

`ClientProjectionHealthEvaluator` (C#) ve `Get-CqrsClientProjectionHealthExpectation` (PowerShell) aynı
öncelik sırasını taşır. Client tarafında **tek read flag** (`ClientsEnabled`) vardır; claim/lease yoktur.

| projectionEnabled | clientsReadEnabled | pending/retry/dead-letter | Beklenen |
|-------------------|--------------------|---------------------------|----------|
| true | true | hepsi 0 | **Healthy** |
| * | * | deadLetter > 0 | **Unhealthy** |
| false | true | pending/retry > 0 | **Unhealthy** |
| false | true | hepsi 0 | **Degraded** |
| false | false | pending > 0 | **Healthy** |
| true | true | oldest pending age ≥ Unhealthy eşiği | **Unhealthy** |
| true | true | oldest pending age ≥ Degraded eşiği | **Degraded** |
| true | true | retry-waiting > 0 | **Degraded** |

Health `data` alanları (PII/secret yok): `pendingCount`, `retryWaitingCount`, `deadLetterCount`,
`oldestPendingAgeSeconds`, `nextRetryAtUtc`, `projectionEnabled`, `clientsReadEnabled`.

---

## 4. CI-safe ve manuel acceptance ayrımı

### 4.1 Deterministik testler (CI-safe)

Live API / SQL / token / build **gerektirmez**. CI pipeline'da koşmalıdır.

| Test | Ne doğrular | Bağımlılık |
|------|-------------|-----------|
| `dotnet test --no-restore` | Domain + Application + Integration suite (client health/parity/smoke/backfill dâhil) | Integration testleri SQL Server/localdb kullanır |
| `tests/load/tools/Test-CqrsClientRolloutAcceptance.ps1` | 12B-7 readiness: script parse, rollout (6) / rollback (4) sıraları, flag override invariant'ları, health beklenti matrisi, DbMigrator `backfill-client-projections` varlığı, doküman + secret taraması | PowerShell 5.1+, repo |

> `Test-CqrsClientRolloutAcceptance.ps1` `backfill-client-projections` komutunu **kaynaktan** (`DbMigrator/Program.cs`)
> doğrular; build veya çalışan API gerektirmez → CI'da deterministiktir.

### 4.2 Manuel / local acceptance (CI değil)

Çalışan API, SQL Server, migrate edilmiş Query DB ve genelde token gerektirir. Operatör staging/local
ortamda çalıştırır.

| Adım | Ne yapar | Gerektirir |
|------|----------|-----------|
| `migrate-query` + `backfill-client-projections` | Query şema + read-model doldurma | DbMigrator, Command/Query DB |
| Parity doğrulama | `Clients` count == `ClientReadModels` count | SQL erişimi / `IClientReadModelParityReader` |
| `GET /health/ready` | `client-projection` Healthy | API ayakta |
| `ClientsEnabled=true` + restart + list smoke | read path Query DB'den, tenant izolasyonu | API, token |

---

## 5. Minimum acceptance checklist — "Client CQRS v1 tamamlandı"

```text
[ ] CI: dotnet test --no-restore yeşil
[ ] CI: Test-CqrsClientRolloutAcceptance.ps1 yeşil
[ ] Query DB migrate edildi (migrate-query) — ClientReadModels mevcut, pending migration yok
[ ] backfill-client-projections çalıştırıldı — Success, parity in-sync (exit code 0)
[ ] Parity: Command Clients count == Query ClientReadModels count (global + örnek tenant)
[ ] Health: GET /health/ready overall Healthy; client-projection Healthy; deadLetter=0
[ ] ClientsEnabled=true + restart sonrası startup log ClientsQueryReadEnabled=True
[ ] Smoke: list/search read-model'den, yalnızca istenen tenant satırları
[ ] Rollback provası: ClientsEnabled=false -> restart -> health Healthy (read path Command DB)
[ ] ClientProjection:Enabled rollback boyunca true kaldı (altın kural)
[ ] Secret taraması temiz; default ClientsEnabled=false korunuyor
```

---

## 6. Flag referansı

| Config key | Env override | Etki | Default |
|------------|--------------|------|---------|
| `QueryReadModels:ClientsEnabled` | `QueryReadModels__ClientsEnabled` | Client list/search → Query DB read-model | **false** |
| `ClientProjection:Enabled` | `ClientProjection__Enabled` | Client outbox → read model projector | true |
| `ClientProjectionHealth:*` | `ClientProjectionHealth__*` | Health eşikleri (Degraded/Unhealthy/DeadLetter) | 10s / 30s / true |

---

## 7. Garanti

- `ClientsEnabled` default **`false`** kaldı; production read davranışı değişmedi.
- Client command handler, projection/event contract, `GetClientsListQueryHandler` routing **değişmedi**.
- Appointment CQRS rollout/rollback davranışı **değişmedi** (ayrı sıra/script/doküman).
- Bu faz yalnızca docs + CI-safe acceptance tooling ekledi; **canlı DB reset / destructive işlem yoktur**.
- Mevcut testler kırılmadı.
- Commit atılmadı.
