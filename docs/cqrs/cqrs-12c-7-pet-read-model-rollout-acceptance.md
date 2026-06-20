# CQRS-12C-7 — Pet read-model rollout acceptance / final readiness

Pet read-model geçişinin (CQRS-12C-1..6) **operasyonel kapanışı**: rollout sırası, backfill sonrası
güvenli flag açma, rollback sırası, CI-safe ve manuel acceptance ayrımı, ve "Pet CQRS v1 tamamlandı"
checklist'i — tek sayfada.

**Kapsam:** docs + acceptance tooling kapanışı. Production code, pet command handler, projection/event
contract, `GetPetsListQueryHandler` routing, route/auth/permission/tenant scope **bu fazda değişmez**.
`QueryReadModels:PetsEnabled` default **`false`** kalır.

**İlgili dokümanlar:**

- [`cqrs-12c-4-pet-read-model-reader-flag.md`](cqrs-12c-4-pet-read-model-reader-flag.md) — reader + flag + routing
- [`cqrs-12c-5-pet-read-model-health-parity-smoke.md`](cqrs-12c-5-pet-read-model-health-parity-smoke.md) — health / parity / smoke
- [`cqrs-12c-6-pet-read-model-backfill.md`](cqrs-12c-6-pet-read-model-backfill.md) — backfill / rebuild
- [`cqrs-acceptance-runbook.md`](cqrs-acceptance-runbook.md) — appointment + client + pet birleşik runbook

---

## 1. Rollout sırası (PetsEnabled açmadan ÖNCE)

`Get-CqrsPetRolloutSequence` ile birebir; **flag açma en sonda**dır.

| # | Adım | Komut / aksiyon | Doğrulama |
|---|------|-----------------|-----------|
| 1 | `query-migration` | `dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query` | `PetReadModels` tablosu mevcut; bekleyen migration yok |
| 2 | `backfill` | `dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections` | Backfill başarılı; parity in-sync (mismatch → exit code `2`) |
| 3 | `parity-check` | `IPetReadModelParityReader` veya SQL `COUNT_BIG` | `CommandCount == QueryCount` (InSync) |
| 4 | `health-check` | `GET /health/ready` | overall Healthy; `pet-projection` Healthy; `deadLetterCount=0`; **`PetProjection:Enabled=true`** (default false) |
| 5 | `pets-enabled` | `QueryReadModels__PetsEnabled=true` → restart | startup log `PetsQueryReadEnabled=True`; read path Query DB |
| 6 | `smoke` | `GET /api/v1/pets` (list/search) | yalnızca istenen tenant satırları; parity korunur |

```text
migrate-query -> backfill-pet-projections -> parity (InSync) -> health (Healthy)
              -> PetsEnabled=true + restart -> smoke
```

> **Altın kural:** Backfill ve parity in-sync doğrulaması **flag açmadan önce** zorunludur. Aksi halde
> `PetReadModels` boş/eksikken `PetsEnabled=true` açılırsa liste eksik döner — **fallback yoktur**.

> **Projector notu:** `PetProjection:Enabled` production default **`false`**'tur. Rollout boyunca (özellikle
> adım 4–6) projector **açık** olmalıdır (`PetProjection__Enabled=true`); aksi halde yeni pet event'leri
> read-model'e yansımaz ve read flag açıkken health Degraded/Unhealthy olabilir.

### Backfill tenant bazlı

```text
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections --tenant <guid>
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections --batch-size 500
```

### Backfill exit code davranışı (DbMigrator)

Backfill sonrası **exit code 2** parity mismatch anlamına gelir; **exit code 0** başarı, **exit code 1** exception.

| Exit code | Anlam | Aksiyon |
|-----------|-------|---------|
| **0** | Başarılı; parity in-sync | `PetsEnabled` açmaya devam edilebilir |
| **2** | Backfill tamamlandı ama parity mismatch | `PetsEnabled` **açma**; parity/eksik satırları incele, backfill tekrar |
| **1** | Exception / hata | Log incele, düzelt, backfill tekrar |

Backfill **idempotent + non-destructive upsert**'tir; tekrar çalıştırınca duplicate üretmez. Daha yeni
gerçek event ile yazılmış satırı ezmez (baseline sentinel stale guard; bkz. 12C-6).

---

## 2. Rollback sırası (read path geri alma)

`Get-CqrsPetRollbackSequence` ile birebir. En hızlı geri alma flag kapatmadır.

| # | Adım | Komut / aksiyon | Doğrulama |
|---|------|-----------------|-----------|
| 1 | `pets-disabled` | `QueryReadModels__PetsEnabled=false` | read path Command DB'ye döner |
| 2 | `restart` | tek instance restart/deploy (flag'ler IOptions startup binding) | startup log `PetsQueryReadEnabled=False` |
| 3 | `health-check` | `GET /health/ready` | overall Healthy; `pet-projection` sağlıklı |
| 4 | `projection-disabled` *(opsiyonel)* | `PetProjection__Enabled=false` | yalnızca planlı pause |

```text
PetsEnabled=false -> restart -> health (Healthy)
                  -> (opsiyonel) PetProjection:Enabled=false
```

> **Altın kural:** Read flag rollback edilse bile `PetProjection__Enabled=true` **kalmalıdır**.
> Projector kapatılırsa Query DB geride kalır ve flag tekrar açılmadan önce yeniden **backfill** gerekebilir.
> `projection-disabled` yalnızca planlı pause içindir; read flag açıkken pending birikirse health
> Degraded/Unhealthy olur.

---

## 3. Health beklenti matrisi (rollout/rollback boyunca)

`PetProjectionHealthEvaluator` (C#) ve `Get-CqrsPetProjectionHealthExpectation` (PowerShell) aynı
öncelik sırasını taşır. Pet tarafında **tek read flag** (`PetsEnabled`) vardır; claim/lease yoktur.

| projectionEnabled | petsReadEnabled | pending/retry/dead-letter | Beklenen |
|-------------------|-----------------|---------------------------|----------|
| true | true | hepsi 0 | **Healthy** |
| * | * | deadLetter > 0 | **Unhealthy** |
| false | true | pending/retry > 0 | **Unhealthy** |
| false | true | hepsi 0 | **Degraded** |
| false | false | pending > 0 | **Healthy** |
| true | true | oldest pending age ≥ Unhealthy eşiği | **Unhealthy** |
| true | true | oldest pending age ≥ Degraded eşiği | **Degraded** |
| true | true | retry-waiting > 0 | **Degraded** |

Health `data` alanları (PII/secret yok): `pendingCount`, `retryWaitingCount`, `deadLetterCount`,
`oldestPendingAgeSeconds`, `nextRetryAtUtc`, `projectionEnabled`, `petsReadEnabled`.

---

## 4. CI-safe ve manuel acceptance ayrımı

### 4.1 Deterministik testler (CI-safe)

Live API / SQL / token / build **gerektirmez**. CI pipeline'da koşmalıdır.

| Test | Ne doğrular | Bağımlılık |
|------|-------------|-----------|
| `dotnet test --no-restore` | Domain + Application + Integration suite (pet health/parity/smoke/backfill dâhil) | Integration testleri SQL Server/localdb kullanır |
| `tests/load/tools/Test-CqrsPetRolloutAcceptance.ps1` | 12C-7 readiness: script parse, rollout (6) / rollback (4) sıraları, flag override invariant'ları, health beklenti matrisi, DbMigrator `backfill-pet-projections` varlığı, doküman + secret taraması | PowerShell 5.1+, repo |

> `Test-CqrsPetRolloutAcceptance.ps1` `backfill-pet-projections` komutunu **kaynaktan** (`DbMigrator/Program.cs`)
> doğrular; build veya çalışan API gerektirmez → CI'da deterministiktir.

### 4.2 Manuel / local acceptance (CI değil)

Çalışan API, SQL Server, migrate edilmiş Query DB ve genelde token gerektirir. Operatör staging/local
ortamda çalıştırır.

| Adım | Ne yapar | Gerektirir |
|------|----------|-----------|
| `migrate-query` + `backfill-pet-projections` | Query şema + read-model doldurma | DbMigrator, Command/Query DB |
| Parity doğrulama | `Pets` count == `PetReadModels` count | SQL erişimi / `IPetReadModelParityReader` |
| `GET /health/ready` | `pet-projection` Healthy | API ayakta |
| `PetProjection__Enabled=true` + `PetsEnabled=true` + restart + list smoke | read path Query DB'den, tenant izolasyonu | API, token |

---

## 5. Minimum acceptance checklist — "Pet CQRS v1 tamamlandı"

```text
[ ] CI: dotnet test --no-restore yeşil
[ ] CI: Test-CqrsPetRolloutAcceptance.ps1 yeşil
[ ] Query DB migrate edildi (migrate-query) — PetReadModels mevcut, pending migration yok
[ ] backfill-pet-projections çalıştırıldı — Success, parity in-sync (exit code 0)
[ ] Parity: Command Pets count == Query PetReadModels count (global + örnek tenant)
[ ] Health: GET /health/ready overall Healthy; pet-projection Healthy; deadLetter=0
[ ] PetProjection:Enabled=true (projector açık) rollout boyunca korundu
[ ] PetsEnabled=true + restart sonrası startup log PetsQueryReadEnabled=True
[ ] Smoke: list/search read-model'den, yalnızca istenen tenant satırları
[ ] Rollback provası: PetsEnabled=false -> restart -> health Healthy (read path Command DB)
[ ] PetProjection:Enabled rollback boyunca true kaldı (altın kural; opsiyonel pause hariç)
[ ] Secret taraması temiz; default PetsEnabled=false korunuyor
[ ] Rename propagation sınırı bilinçli: ClientFullName/SpeciesName/ColorName/BreedRefName yalnızca pet event veya backfill ile güncellenir
```

---

## 6. Flag referansı

| Config key | Env override | Etki | Default |
|------------|--------------|------|---------|
| `QueryReadModels:PetsEnabled` | `QueryReadModels__PetsEnabled` | Pet list/search → Query DB read-model | **false** |
| `PetProjection:Enabled` | `PetProjection__Enabled` | Pet outbox → read model projector | **false** |
| `PetProjectionHealth:*` | `PetProjectionHealth__*` | Health eşikleri (Degraded/Unhealthy/DeadLetter) | 10s / 30s / true |

---

## 7. Bilinen sınırlamalar (v1)

### Query DB boş/eksik + PetsEnabled=true

Command DB'de pet varken `PetReadModels` boş veya eksikse (backfill atlandı, projector kapalı vb.)
liste **eksik/boş** döner. Command DB'ye **otomatik fallback yoktur**. Rollback: `PetsEnabled=false`.

### Rename propagation yok

Read-model'deki denormalize alanlar yalnızca **pet create/update snapshot'ı** veya **backfill** ile
güncellenir; client/species/color/breed rename propagation bu sürümde yoktur:

- `ClientFullName`
- `SpeciesName`
- `ColorName`
- `BreedRefName`

---

## 8. Garanti

- `PetsEnabled` default **`false`** kaldı; production read davranışı değişmedi.
- Pet command handler, projection/event contract, `GetPetsListQueryHandler` routing **değişmedi**.
- Client/Appointment CQRS rollout/rollback davranışı **değişmedi** (ayrı sıra/script/doküman).
- Bu faz yalnızca docs + CI-safe acceptance tooling ekledi; **canlı DB reset / destructive işlem yoktur**.
- Mevcut testler kırılmadı.
- Commit atılmadı.
