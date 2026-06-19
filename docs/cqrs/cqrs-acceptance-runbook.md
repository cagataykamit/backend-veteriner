# CQRS-11F — Appointment read-model acceptance & CI runbook

Appointment CQRS read-model rollout sürecinin **üretime hazırlık** özeti. Hangi test/script CI'da koşar, hangisi manuel/local acceptance gerektirir, hangi sırayla, ve hata durumunda rollback adımları — tek sayfada.

**Kapsam:** docs + tooling kapanış. Production code, domain read model, route/auth/permission/clinic scope **bu fazda değişmez**.

**İlgili dokümanlar:**

- [`cqrs-11a-staging-rollout.md`](cqrs-11a-staging-rollout.md) — Mode A/B/C staged rollout
- [`cqrs-11b-operations-runbook.md`](cqrs-11b-operations-runbook.md) — tam operasyon runbook (incident, rebuild, parity)
- [`cqrs-11c-monitoring-alerts.md`](cqrs-11c-monitoring-alerts.md) — metrikler / alert eşikleri
- [`cqrs-11d-two-instance-acceptance.md`](cqrs-11d-two-instance-acceptance.md) — iki instance claim/lease acceptance
- [`cqrs-11e-rollout-rollback-acceptance.md`](cqrs-11e-rollout-rollback-acceptance.md) — staged rollout / rollback acceptance
- [`appointment-cqrs-load-test.md`](appointment-cqrs-load-test.md), [`load-test-database-setup.md`](load-test-database-setup.md)

---

## 1. Test sınıflandırması

İki kategori vardır. **Hangisinin nerede koştuğunu karıştırmayın.**

### 1.1 Deterministik testler (CI-safe)

Live API, çalışan SQL Server veya token dosyası **gerektirmez**. Yalnızca parse, plan, mode tanımı, health beklenti mantığı ve doküman/secret kontrolü yapar. CI pipeline'da koşmalıdır.

| Test | Ne doğrular | Bağımlılık |
|------|-------------|-----------|
| `dotnet test --no-restore` | Domain + Application + Integration suite | Integration testleri SQL Server kullanır (CI DB veya localdb); ~10 dk |
| `tests/load/tools/Test-CqrsStagedRollout.ps1` | 11A/11B/11C readiness: script parse, mode tanımları, rollback planı, runbook/monitoring doküman + secret taraması, DbMigrator `rebuild-appointment-projections` komutu | PowerShell 5.1+, repo; `dotnet run DbMigrator -- help` (build gerekir) |
| `tests/load/tools/Test-CqrsTwoInstanceAcceptance.ps1` | 11D script parse, token partition, k6 lifecycle özet ayrıştırma, worker participation, processed-events delta mantığı | PowerShell 5.1+, repo |
| `tests/load/tools/Test-CqrsRolloutAcceptance.ps1` | 11E acceptance sequence, projection-disabled override, health beklenti matrisi, rollback dokümantasyonu | PowerShell 5.1+, repo |
| `tests/load/tools/Test-CqrsClientRolloutAcceptance.ps1` | 12B-7 client readiness: rollout (6)/rollback (4) sıraları, flag override invariant'ları, client health beklenti matrisi, DbMigrator `backfill-client-projections` varlığı, doküman/secret taraması | PowerShell 5.1+, repo |

> **Not (determinism garantisi):** `Test-CqrsStagedRollout.ps1` içindeki canlı `preflight-mode-mismatch-fails` kontrolü, **token dosyası varsa ve API erişilebilirse** çalışır. API erişilemiyorsa (CI veya API kapalı dev makinesi) bu kontrol **atlanır (skip = pass)**; token dosyası yoksa da atlanır. Böylece bu test gerçek CI'da deterministiktir.

### 1.2 Manuel / local acceptance testleri (CI değil)

Çalışan API instance(ları), SQL Server, k6, token dosyası ve genelde LoadTest DB reset gerektirir. **CI'da koşturulmaz**; staging veya local LoadTest ortamında operatör çalıştırır.

| Test / komut | Ne yapar | Gerektirir |
|--------------|----------|-----------|
| `Invoke-CqrsStagedRollout.ps1 -Apply` | Mode A/B/C canlı smoke + SQL parity | API ayakta (hedef mode), token dosyası, sqlcmd, Command/Query DB |
| `Invoke-CqrsRolloutAcceptance.ps1 -Step <step> -Apply` | 11E tek adım canlı doğrulama (projection pause/resume dâhil) | API ayakta, token, sqlcmd, DB |
| `Invoke-CqrsTwoInstanceAcceptance.ps1 -Apply` | 11D iki instance claim/lease acceptance | 7173 + 7174 iki API instance, k6, token, sqlcmd, DB |
| `Invoke-CqrsTwoInstanceAcceptance.ps1 -Reset -ConfirmReset -Apply` | Yukarıdakine ek **destructive LoadTest DB reset** | Yukarıdakiler + onay |
| `appointment-projection-lag.js` (k6) | Projection lag workload | k6, API, token |
| `Prepare-LoadTestTokens.ps1` | Token dosyası üretir | API ayakta, kullanıcı seed |
| `Invoke-CqrsLoadDataReset.ps1` | **Yalnızca LoadTest DB** reset | sqlcmd, LoadTest DB |

---

## 2. Çalıştırma sırası

### 2.1 CI (her commit / PR)

```text
1. dotnet test --no-restore
2. .\tests\load\tools\Test-CqrsStagedRollout.ps1
3. .\tests\load\tools\Test-CqrsTwoInstanceAcceptance.ps1
4. .\tests\load\tools\Test-CqrsRolloutAcceptance.ps1
5. .\tests\load\tools\Test-CqrsClientRolloutAcceptance.ps1   # CQRS-12B client read-model
```

Hepsi başarısızlıkta exit code != 0 döner (`Test-CqrsTwoInstanceAcceptance.ps1` throw eder; diğerleri `exit 1`). Sıra önemsizdir; paralel de koşabilir.

> Repoda hazır CI pipeline dosyası yoktur. Yukarıdaki dört adım, eklenecek pipeline'ın "cqrs-acceptance" job'ını oluşturur. PowerShell adımları Windows runner gerektirir; `dotnet test` cross-platform'dur.

### 2.2 Local / staging manuel acceptance (sırayla)

```text
A. Önkoşul
   - LoadTest DB migrate (DbMigrator migrate + migrate-query)
   - Prepare-LoadTestTokens.ps1  -> tests/load/.tokens/clinic-tokens.json
   - k6 ve sqlcmd PATH'te

B. Tek instance staged rollout (11A/11E)
   1. API'yi Mode A ile başlat (bkz. §4 flag tablosu)
   2. Invoke-CqrsRolloutAcceptance.ps1 -BaseUrl https://localhost:7173 -ShowPlan
   3. Her adımda: override uygula -> tek instance restart -> -Apply
      command-read -> appointment-query -> full-query
      -> projection-disabled -> projection-reenabled
      -> rollback-appointment-query -> rollback-command-read

C. İki instance claim/lease acceptance (11D, opsiyonel)
   1. 7173 + 7174 iki API instance (ClaimingEnabled=true)
   2. Invoke-CqrsTwoInstanceAcceptance.ps1 ... (dry-run)
   3. ... -Apply (gerekirse -Reset -ConfirmReset)
```

---

## 3. Rollback runbook (operasyonel — sade)

Detaylı incident akışı için [`cqrs-11b-operations-runbook.md`](cqrs-11b-operations-runbook.md) Bölüm 7–13. Hızlı operasyonel sıra:

### 3.1 Read model flag kapatma (en hızlı geri alma)

```text
Mode C -> B :  QueryReadModels__DashboardAppointmentsEnabled=false
Mode B -> A :  QueryReadModels__AppointmentsEnabled=false  (dashboard zaten false)
HER ZAMAN   :  AppointmentProjection__Enabled=true  (projector AÇIK kalır)
```

### 3.2 Projection flag kapatma (planlı pause)

```text
AppointmentProjection__Enabled=false
# read flags istenen modda kalabilir; read flags AÇIK + pending birikirse health Unhealthy olur
```

### 3.3 Restart

```text
Tek API instance restart/deploy (flag'ler IOptions startup binding -> restart zorunlu)
İki instance senaryosunda tek seferde tek instance restart
```

### 3.4 Health kontrol

```text
GET /health/ready -> overall Healthy
appointment-projection.data: projectionEnabled, pendingCount, retryWaitingCount, deadLetterCount
query-sql -> Healthy
Doğrulama scripti: Invoke-CqrsRolloutAcceptance.ps1 -Step <hedef> -Apply
```

### 3.5 Queue drain

```text
Projection AÇIK iken pending = retry = dead-letter = 0 olana kadar bekle
Invoke-CqrsRolloutAcceptance.ps1 -Step projection-reenabled -Apply -PollTimeoutSeconds 120
```

### 3.6 SQL parity

```text
Appointments (Command) count == AppointmentReadModels (Query) count
duplicate AppointmentId = 0, invalid Guid = 0, ScheduledEndUtc >= ScheduledAtUtc
queue clean
Script: Invoke-CqrsStagedRollout.ps1 -Mode <mode> -Apply  (Get-CqrsStagedParityReport)
```

**Altın kural:** Rollback sırasında `AppointmentProjection__Enabled=true` kalır. Projector kapatılırsa Query DB geride kalır ve sonraki Mode B/C açılışında rebuild gerekebilir.

---

## 4. Flag referansı

| Config key | Env override | Etki |
|------------|--------------|------|
| `QueryReadModels:AppointmentsEnabled` | `QueryReadModels__AppointmentsEnabled` | List + calendar -> Query DB |
| `QueryReadModels:DashboardAppointmentsEnabled` | `QueryReadModels__DashboardAppointmentsEnabled` | Dashboard appointment -> Query DB |
| `AppointmentProjection:Enabled` | `AppointmentProjection__Enabled` | Outbox -> read model projector |
| `AppointmentProjection:ClaimingEnabled` | `AppointmentProjection__ClaimingEnabled` | Multi-instance claim/lease (11D); production default **false** |

| Mode | Appointments | Dashboard | Projection |
|------|-------------|-----------|-----------|
| A (command-read) | false | false | true |
| B (appointment-query) | true | false | true |
| C (full-query) | true | true | true |

---

## 5. Ortam önkoşulları ve kırılgan varsayımlar

Script'lerde makineye-özel sabit **path yoktur**; tüm yollar `$PSScriptRoot` / repo root / temp üzerinden çözülür. Ancak aşağıdaki **ortam varsayımları** vardır:

| Varsayım | Nerede | Not / override |
|----------|--------|----------------|
| HTTPS port 7173 (primary), 7174 (secondary) | `Test-CqrsStagedLocalApiSingleInstance`, `Get-CqrsTwoInstanceListenerProbe` | Local single/two-instance probe için **sabit**. Farklı port kullanılırsa probe atlanır/yanılır; local acceptance'ı bu portlarla çalıştırın. |
| Default Command/Query DB: `VetinityCommandDb_LoadTest` / `VetinityQueryDb_LoadTest` | Invoke-* `-CommandDatabase` / `-QueryDatabase` | Parametre ile override edilebilir. Command ve Query catalog **farklı** olmalı (script enforce eder). |
| `ServerInstance = localhost` | parity/processed-events SQL | `-ServerInstance` ile override. |
| `sqlcmd` PATH'te + DB erişimi (Trusted_Connection) | tüm SQL kontrolleri | Yoksa Invoke-* `-Apply` parity adımı hata verir. Deterministik testler sqlcmd gerektirmez. |
| `k6` PATH'te | workload (11D, lag) | Yoksa workload job fail. |
| `curl.exe` PATH'te | API smoke (`Invoke-CqrsStagedApiRequest`) | Windows 10+ dâhili. |
| Token dosyası: `tests/load/.tokens/clinic-tokens.json` | `Resolve-CqrsLoadTokenFile` | `.gitignore`'da; CI'da yoktur. `Prepare-LoadTestTokens.ps1` ile üretilir. |
| Non-localhost host izni | host guard | `CQRS_LOAD_ALLOWED_HOST` env ile tek host whitelist. |

Secret (JWT key, SQL password, connection string, token) **commit edilmez**; tüm script çıktıları `Test-CqrsStagedOutputContainsSecrets` ile taranır ve secret tespitinde throw eder.

---

## 6. Script isim referansı (tutarlılık)

| Faz | Common | Invoke (canlı) | Test (deterministik) |
|-----|--------|----------------|----------------------|
| 11A/11B staged | `CqrsStagedRolloutCommon.ps1` | `Invoke-CqrsStagedRollout.ps1` | `Test-CqrsStagedRollout.ps1` |
| 11D two-instance | `CqrsTwoInstanceAcceptanceCommon.ps1` | `Invoke-CqrsTwoInstanceAcceptance.ps1` | `Test-CqrsTwoInstanceAcceptance.ps1` |
| 11E rollout/rollback | `CqrsRolloutAcceptanceCommon.ps1` | `Invoke-CqrsRolloutAcceptance.ps1` | `Test-CqrsRolloutAcceptance.ps1` |

`Invoke-*` varsayılan **dry-run**; `-Apply` ile canlı çalışır. `Test-*` her zaman deterministik ve CI-safe.

---

## 7. Üretime hazırlık checklist

```text
[ ] CI: dotnet test --no-restore yeşil
[ ] CI: Test-CqrsStagedRollout.ps1 yeşil (38/38)
[ ] CI: Test-CqrsTwoInstanceAcceptance.ps1 yeşil
[ ] CI: Test-CqrsRolloutAcceptance.ps1 yeşil
[ ] Local: Mode A->B->C->pause->resume->rollback acceptance geçti
[ ] Local: (gerekiyorsa) iki instance claim/lease acceptance geçti
[ ] Rollback runbook §3 operatör tarafından prova edildi
[ ] Secret taraması temiz; token/results .gitignore'da
[ ] Monitoring alert eşikleri (11C) bağlandı
```

---

## 8. Client read-model (CQRS-12B)

Appointment read-model'den **bağımsız** ikinci CQRS read-model. Health/parity/smoke için bkz.
[`cqrs-12b-5-client-read-model-health-parity-smoke.md`](cqrs-12b-5-client-read-model-health-parity-smoke.md);
backfill için [`cqrs-12b-6-client-read-model-backfill.md`](cqrs-12b-6-client-read-model-backfill.md);
**rollout/rollback acceptance ve final readiness** için
[`cqrs-12b-7-client-read-model-rollout-acceptance.md`](cqrs-12b-7-client-read-model-rollout-acceptance.md).

| Config key | Env override | Etki |
|------------|--------------|------|
| `QueryReadModels:ClientsEnabled` | `QueryReadModels__ClientsEnabled` | Client list/search → Query DB read-model (default **false**) |
| `ClientProjection:Enabled` | `ClientProjection__Enabled` | Client outbox → read model projector (default true) |
| `ClientProjectionHealth:*` | `ClientProjectionHealth__*` | Health eşikleri (Degraded/Unhealthy/DeadLetter) |

- **Health:** `/health/ready` → `client-projection` entry (appointment-projection ile aynı `data`
  şeması: `pendingCount`, `retryWaitingCount`, `deadLetterCount`, `oldestPendingAgeSeconds`,
  `projectionEnabled`, `clientsReadEnabled`).
- **Parity:** Command `Clients` count == Query `ClientReadModels` count (`IClientReadModelParityReader`
  veya SQL `COUNT_BIG`). Client'ta silme yoktur → tüm event'ler işlendiğinde in-sync.
- **Rollback:** `QueryReadModels__ClientsEnabled=false` → restart → health → parity. Projector açık
  kalır.
- **Backfill:** Mevcut client satırlarını Query `ClientReadModels`'e idempotent dolduran backfill
  **CQRS-12B-6** ile eklendi (bkz. [`cqrs-12b-6-client-read-model-backfill.md`](cqrs-12b-6-client-read-model-backfill.md)).
  `ClientsEnabled=true` açmadan **önce** çalıştırılmalı ve parity in-sync doğrulanmalıdır; aksi halde
  liste eksik döner (fallback yok).

  ```text
  dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections
  dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections --tenant <guid>
  ```

### 8.1 Client rollout sırası (CQRS-12B-7)

ClientsEnable açma **en sonda**; backfill + parity önce. Tam tablo ve checklist için
[`cqrs-12b-7-...`](cqrs-12b-7-client-read-model-rollout-acceptance.md).

```text
migrate-query -> backfill-client-projections -> parity (InSync) -> health (Healthy)
              -> QueryReadModels__ClientsEnabled=true + restart -> list/search smoke
```

### 8.2 Client rollback sırası

```text
QueryReadModels__ClientsEnabled=false -> restart -> health (Healthy)
                                      -> (opsiyonel, planlı pause) ClientProjection__Enabled=false
```

**Altın kural:** Read flag rollback edilse bile `ClientProjection__Enabled=true` kalır; aksi halde Query
DB geride kalır ve flag tekrar açılmadan önce yeniden backfill gerekebilir.
