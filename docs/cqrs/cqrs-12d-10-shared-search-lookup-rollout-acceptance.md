# CQRS-12D-10 — Shared + payments search lookup rollout / acceptance

**Tür:** Operasyonel kapanış — rollout sırası, rollback prosedürü, precondition checklist, flag
kombinasyonları, manuel smoke komutları ve CI-safe acceptance script.

**Kapsam:** docs + PowerShell acceptance tooling. **Production C# değiştirilmedi.** Handler, reader,
projection, backfill, migration, appsettings default ve route/auth/permission/tenant scope **bu fazda
değişmez**.

**Production default (değişmedi):**

| Ayar | Default |
|---|---|
| `QueryReadModels:SharedSearchLookupEnabled` | `false` |
| `QueryReadModels:PaymentsSearchLookupEnabled` | `false` |

Her iki bayrak da `false` iken tüm search lookup yolları **Command DB** kullanır (12D öncesi davranış).

**İlgili faz dokümanları:**

- [`cqrs-12d-1-client-pet-shared-lookup-audit.md`](cqrs-12d-1-client-pet-shared-lookup-audit.md)
- [`cqrs-12d-3-shared-search-lookup-examinations-pilot.md`](cqrs-12d-3-shared-search-lookup-examinations-pilot.md)
- [`cqrs-12d-4-shared-search-lookup-clinical-lists.md`](cqrs-12d-4-shared-search-lookup-clinical-lists.md)
- [`cqrs-12d-5-shared-search-lookup-appointments.md`](cqrs-12d-5-shared-search-lookup-appointments.md)
- [`cqrs-12d-6-payments-shared-search-lookup-audit.md`](cqrs-12d-6-payments-shared-search-lookup-audit.md)
- [`cqrs-12d-7-payments-list-search-lookup-routing.md`](cqrs-12d-7-payments-list-search-lookup-routing.md)
- [`cqrs-12d-8-payments-report-search-lookup-routing.md`](cqrs-12d-8-payments-report-search-lookup-routing.md)
- [`cqrs-12d-9-payments-export-search-lookup-routing.md`](cqrs-12d-9-payments-export-search-lookup-routing.md)
- [`cqrs-12b-7-client-read-model-rollout-acceptance.md`](cqrs-12b-7-client-read-model-rollout-acceptance.md)

---

## 1. Blast radius (tek sayfada)

| Flag | Etkilenen yüzeyler | Lookup deseni |
|---|---|---|
| `SharedSearchLookupEnabled` | Examinations, treatments, vaccinations, hospitalizations, lab-results, prescriptions list; appointments list (**yalnız `AppointmentsEnabled=false` Command DB yolu**) | Strateji A — pet text → `searchPetIds` (SharedSearchPetIdsLookup) |
| `PaymentsSearchLookupEnabled` | Payment list, payment report, CSV export, XLSX export | Strateji B — ayrı `searchClientIds` + `searchPetIds` |

> **Finansal not:** Payment export en kritik yüzeydir. `PaymentsSearchLookupEnabled` açılmadan önce list
> ve report parity smoke geçilmelidir (12D-7 → 12D-8 → 12D-9 sırası).

---

## 2. Precondition checklist (bayrak açmadan ÖNCE)

`Get-CqrsSharedSearchLookupPreconditionSequence` ile birebir. **Her iki lookup bayrağı da kapalıyken**
tamamlanmalıdır.

| # | Adım | Komut / aksiyon | Doğrulama |
|---|------|-----------------|-----------|
| 1 | `command-migration` | `dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate` | Command schema güncel |
| 2 | `query-migration` | `dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query` | `ClientReadModels` + `PetReadModels` mevcut |
| 3 | `backfill-client` | `dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections` | Başarılı; mismatch → exit `2` |
| 4 | `backfill-pet` | `dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections` | Başarılı; mismatch → exit `2` |
| 5 | `parity-client` | `IClientReadModelParityReader` / SQL `COUNT_BIG` | Command == Query (InSync) |
| 6 | `parity-pet` | `IPetReadModelParityReader` / SQL `COUNT_BIG` | Command == Query (InSync) |
| 7 | `health-check` | `GET /health/ready` | overall Healthy; `client-projection` + `pet-projection` Healthy; `deadLetterCount=0` |

```text
migrate -> migrate-query
       -> backfill-client-projections + backfill-pet-projections
       -> parity (Client + Pet InSync)
       -> health (Healthy)
       -> (sonra rollout adımları)
```

Tenant bazlı backfill:

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections --tenant <guid>
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections --tenant <guid>
```

> **Altın kural:** Client/Pet read-model boş veya stale iken lookup bayrağı açılırsa arama **sessizce
> eksik sonuç** döndürebilir. **Otomatik Command DB fallback yoktur.**

---

## 3. Rollout sırası (deterministik)

Operatör her adımda environment override uygular, **tek API instance restart** eder, startup log
doğrular, ardından smoke çalıştırır.

| # | Adım | Environment | Smoke / doğrulama |
|---|------|-------------|-------------------|
| 1 | Precondition (§2) | Her iki flag `false` | backfill + parity + health |
| 2 | `shared-search-enabled` | `QueryReadModels__SharedSearchLookupEnabled=true` | startup log; clinical + appointments smoke |
| 3 | `smoke-clinical` | (flag zaten true) | 6 klinik liste endpoint'i `?search=` |
| 4 | `smoke-appointments` | (flag zaten true) | `GET /api/v1/appointments?search=` |
| 5 | `payments-search-enabled` | `QueryReadModels__PaymentsSearchLookupEnabled=true` | startup log; payment smoke grubu |
| 6 | `smoke-payments-list` | (flag zaten true) | `GET /api/v1/payments?search=` |
| 7 | `smoke-payments-report` | (flag zaten true) | `GET /api/v1/reports/payments?search=` |
| 8 | `smoke-payments-export-csv` | (flag zaten true) | `GET /api/v1/reports/payments/export?search=` |
| 9 | `smoke-payments-export-xlsx` | (flag zaten true) | `GET /api/v1/reports/payments/export-xlsx?search=` |

```text
preconditions (both flags false)
  -> SharedSearchLookupEnabled=true + restart
  -> clinical smoke + appointments smoke
  -> PaymentsSearchLookupEnabled=true + restart
  -> payment list -> report -> CSV export -> XLSX export smoke
```

### Minimum smoke endpointleri

| Grup | Endpoint |
|---|---|
| Clinical | `GET /api/v1/examinations`, `/treatments`, `/vaccinations`, `/hospitalizations`, `/lab-results`, `/prescriptions` |
| Appointments | `GET /api/v1/appointments` |
| Payments | `GET /api/v1/payments`, `/api/v1/reports/payments`, `/export`, `/export-xlsx` |

Ortak query parametreleri: `Page=1&PageSize=5&clinicId={clinicId}&search={term}` (report/export için
`fromUtc` / `toUtc` eklenir). Smoke script varsayılan `search=smoke` kullanır — **PII loglanmaz**.

---

## 4. Rollback sırası

En hızlı geri alma: ilgili bayrağı `false` yap + **restart** (IOptions startup binding).

| # | Adım | Environment | Not |
|---|------|-------------|-----|
| 1 | `payments-search-disabled` *(opsiyonel kısmi)* | `PaymentsSearchLookupEnabled=false` | Finansal yüzeyler önce; Shared flag açık kalabilir |
| 2 | `shared-search-disabled` | `SharedSearchLookupEnabled=false` | Klinik + appointments lookup Command DB |
| 3 | `restart` | Tek instance | Startup log doğrula |
| 4 | `health-check` | — | `/health/ready` Healthy |

```text
(partial) PaymentsSearchLookupEnabled=false -> restart
(full)    SharedSearchLookupEnabled=false   -> restart -> health
```

Projector'lar (`ClientProjection`, `PetProjection`) rollback sırasında **açık kalmalıdır** — aksi halde
Query DB geride kalır ve bayrak tekrar açılmadan önce yeniden backfill gerekir (12B/12C altın kural).

---

## 5. Flag kombinasyonları

| Shared | Payments | Clinical lookup | Appointments lookup | Payments lookup |
|:---:|:---:|---|---|---|
| false | false | Command DB | Command DB (Command path) | Command DB |
| true | false | Query DB PetReadModels | Query DB pet (Command path) | Command DB |
| false | true | Command DB | Command DB | Query DB Client+Pet read-models |
| true | true | Query DB | Query DB pet (Command path) | Query DB Client+Pet read-models |

- **Production default:** ilk satır (her iki flag `false`).
- **Önerilen rollout:** satır 2 → smoke → satır 4 (satır 3 tek başına önerilmez).
- **`AppointmentsEnabled=true`:** appointments list tamamen Query DB read-model yolunu kullanır;
  `SharedSearchLookupEnabled` o yolda **etkisizdir** (12D-5).

---

## 6. Query DB boş / stale riski

| Durum | Davranış |
|---|---|
| Flag `true` + read-model boş/stale | Arama sonucu eksik/boş olabilir (**sessiz veri eksiltme**) |
| Query DB outage | Lookup başarısız veya boş; **otomatik Command DB fallback yok** |
| Rollback | İlgili flag `false` + restart |
| Ön-koşul | §2 backfill + parity in-sync + projection health Healthy |

Export (`PaymentsSearchLookupEnabled=true`) finansal bütünlük açısından en yüksek risk taşır: eksik
id kümesi sayfasız CSV/XLSX'te **eksik satır** üretebilir.

---

## 7. Acceptance script

**Dosyalar:**

- `tests/load/tools/CqrsSharedSearchLookupRolloutAcceptanceCommon.ps1` — sequence + smoke helpers
- `tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1` — CI-safe + optional live `-Apply`

### CI-safe (varsayılan)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1
```

Parse, sequence invariant, flag override, DbMigrator backfill varlığı, doc taraması. Live API gerekmez.

### Yardım

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1 -Help
```

### Plan (JSON)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1 -ShowPlan
```

### Live smoke (`-Apply`)

Script **environment değiştirmez**; operatör override + restart sonrası doğrular.

```powershell
# Örnek: precondition health
powershell -NoProfile -ExecutionPolicy Bypass -File tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1 `
  -Apply -Step precondition-health -BaseUrl https://localhost:7173

# Örnek: shared flag açıldıktan sonra (env'de Shared=true, Payments=false)
$env:QueryReadModels__SharedSearchLookupEnabled = "True"
$env:QueryReadModels__PaymentsSearchLookupEnabled = "False"
# ... API'yi bu env ile restart ...
powershell -NoProfile -ExecutionPolicy Bypass -File tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1 `
  -Apply -Step shared-search-enabled -BaseUrl https://localhost:7173 -TokenFile tests/load/tokens/load-test-tokens.json

# Örnek: payments flag açıldıktan sonra (her iki flag true)
powershell -NoProfile -ExecutionPolicy Bypass -File tests/load/tools/Test-CqrsSharedSearchLookupRolloutAcceptance.ps1 `
  -Apply -Step payments-search-enabled -BaseUrl https://localhost:7173 -TokenFile tests/load/tokens/load-test-tokens.json
```

**`-Apply -Step` değerleri:**

| Step | Doğrulama |
|---|---|
| `precondition-health` | `/health/ready` client + pet projection Healthy |
| `shared-search-enabled` | env Shared=true, Payments=false + clinical + appointments smoke |
| `smoke-clinical` | 6 klinik liste endpoint |
| `smoke-appointments` | appointments list |
| `payments-search-enabled` | env her iki flag true + tüm payment smoke |
| `smoke-payments-list` | payment list only |
| `smoke-payments-report` | payment report only |
| `smoke-payments-export-csv` | CSV export only |
| `smoke-payments-export-xlsx` | XLSX export only |
| `smoke-payments-all` | list + report + export + xlsx |
| `rollback-payments-search` | env Payments=false (Shared=true) |
| `rollback-shared-search` | env her iki flag false |

**Parametreler:**

| Parametre | Açıklama |
|---|---|
| `-BaseUrl` | API taban URL (default `https://localhost:7173`) |
| `-TokenFile` | Bearer token JSON; slot `01` `clinicId` zorunlu |
| `-SearchTerm` | Smoke arama metni (default `smoke`; PII kullanmayın) |
| `-Apply` | Live doğrulama modu |
| `-ShowPlan` | Rollout plan JSON |
| `-Help` | Kullanım |

Flag snapshot: script `QueryReadModels__SharedSearchLookupEnabled` ve
`QueryReadModels__PaymentsSearchLookupEnabled` process environment değerlerini raporlar. Runtime API
değerleri restart sonrası startup log'dan doğrulanmalıdır
(`CqrsStartupConfigurationLogger`: `SharedSearchLookupEnabled=…`, `PaymentsSearchLookupEnabled=…`).

Başarısız adım → **exit code 1**. Yanıt gövdeleri loglanmaz (yalnızca HTTP status + path).

---

## 8. Manuel dotnet test komutları (parity / regression)

Full suite zorunlu değil; rollout öncesi/sonrası önerilen filtreler:

```powershell
# Shared lookup (clinical + appointments)
dotnet test --no-restore --filter "SharedSearchLookup"

# Payments lookup (list + report + export)
dotnet test --no-restore --filter "PaymentsSearchLookup|PaymentExportSearchLookup|PaymentReportSearchLookup|PaymentListSearchLookup"

# Geniş payment regression
dotnet test --no-restore --filter "Payments"

# Full suite (opsiyonel, release gate)
dotnet test --no-restore
```

---

## 9. CQRS-12D acceptance checklist (kapanış)

- [ ] Command DB migration güncel
- [ ] Query DB migration güncel (`ClientReadModels`, `PetReadModels`)
- [ ] Client backfill tamam + parity in-sync
- [ ] Pet backfill tamam + parity in-sync
- [ ] `client-projection` + `pet-projection` health Healthy
- [ ] `SharedSearchLookupEnabled=true` + clinical smoke geçti
- [ ] Appointments smoke geçti (Command DB path veya bilinçli `AppointmentsEnabled` notu)
- [ ] `PaymentsSearchLookupEnabled=true` + payment list smoke geçti
- [ ] Payment report smoke + total/totalAmount spot-check
- [ ] CSV + XLSX export smoke + MaxExportRows davranışı doğrulandı
- [ ] Rollback prosedürü dokümante edildi ve test ortamında denendi
- [ ] Production default (`false`/`false`) değişmedi

---

## 10. Bu fazda değişmeyenler

- Production C# (handler, reader, projection, health evaluator)
- appsettings default (`SharedSearchLookupEnabled=false`, `PaymentsSearchLookupEnabled=false`)
- Migration / backfill kodu
- Route, auth, permission, tenant/clinic scope
- Otomatik fallback davranışı (yok)
