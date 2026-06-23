# CQRS-17C — Payment CQRS Release Readiness

**Tür:** Final release readiness checklist. **Production kod, test, appsettings, migration veya feature flag değişmedi.**

**Ön durum (tamamlanan fazlar):**

| Faz | Durum | Commit |
|---|---|---|
| CQRS-15O — Payment search rollout readiness | Tamamlandı | `7191426` |
| CQRS-16A — Payment GetById read-model decision audit | Tamamlandı | `70008aa` |
| CQRS-16B — Payment GetById Query DB route | Tamamlandı | `6f72702` |
| CQRS-16C — Payment GetById rollout readiness | Tamamlandı | `243c8ec` |
| CQRS-17A — Payment read-model production rollout runbook | Tamamlandı | `a3e8ae8` |
| CQRS-17B — Full production rollout chain integration test | **Zorunlu değil** — mevcut testler + staging smoke yeterli |
| IDOR / clinic isolation roadmap | **Kapalı** | [`idor-regression.md`](../security/idor-regression.md) |

**Referans runbook:** [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](cqrs-17a-payment-read-model-production-rollout-runbook.md)

**Git doğrulama (2026-06-23, CQRS-17C başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** |
| CQRS-17A commit | `a3e8ae8` — `docs(cqrs): add payment read model rollout runbook` |
| CQRS-16C commit | `243c8ec` — `docs(cqrs): add payment get by id rollout readiness` |
| CQRS-16B commit | `6f72702` — `feat(cqrs): route payment get by id through read model` |
| CQRS-15O commit | `7191426` — `test(cqrs): verify payment search rollout readiness` |

---

## Current status

- **Payment CQRS read-model migration hattı release-ready kabul edilebilir.**
- **Production default behavior değişmedi** — tüm payment read flag'leri ve `PaymentProjection:Enabled` default **false** (6/6 `appsettings*.json` dosyasında explicit false).
- **Production'a açma config/operasyon kararıdır; kod path hazırdır.**

| Alan | Durum |
|---|---|
| Query DB tablo | `PaymentReadModels` (14B + 15D enrichment) |
| Projection worker | `PaymentProjectionHostedService` (API process içi) |
| Read routing | 6 bağımsız flag; handler'lar implement edildi |
| Test kapsamı | List/report/export/search/GetById rollout + IDOR unit/integration |
| Operasyonel runbook | CQRS-17A (gate, sıra, smoke, rollback) |

---

## Release scope

Bu release checklist'in kapsadığı yüzeyler:

| # | Yüzey | Endpoint | Flag |
|---|---|---|---|
| 1 | Payment list | `GET /api/v1/payments` | `PaymentsListReadEnabled` |
| 2 | Dashboard recent payments | `GET /api/v1/dashboard/finance-summary` (recent bölümü) | `DashboardRecentPaymentsReadEnabled` |
| 3 | Client payment summary | `GET /api/v1/clients/{id}/payment-summary` | `ClientPaymentSummaryReadEnabled` |
| 4 | Payment report JSON | `GET /api/v1/reports/payments` | `PaymentsReportReadEnabled` |
| 5 | Payment export CSV/XLSX | `GET /api/v1/reports/payments/export` · `GET /api/v1/reports/payments/export-xlsx` | `PaymentsReportExportReadEnabled` |
| 6 | Payment GetById | `GET /api/v1/payments/{id}` | `PaymentsGetByIdReadEnabled` |

**Altyapı (read flag'lerden önce):** Query migration, `PaymentProjection:Enabled`, backfill, parity, health.

---

## Out of scope

Aşağıdakiler bu release checklist kapsamı **dışındadır**:

- Export streaming (memory-based export, 50k cap — mevcut tasarım)
- Yeni index/migration
- Yeni module CQRS migration (appointments, clients, pets vb. ayrı hatlar)
- Payment lifecycle/delete/refund/void yeni command'ları
- **CQRS-18** — performance/index çalışmaları
- **CQRS-19** — next module selection
- `DashboardFinanceReadEnabled` (finance totals/trend — payment read yüzeylerinden bağımsız)
- `PaymentsSearchLookupEnabled` / `SharedSearchLookupEnabled` (Command path lookup; Query path'i etkilemez)

---

## Required commands

Deploy/flag açmadan önce repo'daki **gerçek komut adlarıyla** çalıştırılmalıdır:

### Checklist

- [ ] **Query DB migration**
  ```powershell
  dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
  ```
  Beklenen: bekleyen migration yok; `PaymentReadModels` tablo mevcut.

- [ ] **Payment read-model backfill**
  ```powershell
  dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models
  dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --batch-size 500
  dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --tenant <guid>
  ```
  Beklenen: exit code **0**; count parity InSync. Exit code **2** → flag açma.

- [ ] **Count/parity check**
  - Backfill çıktısı: `Parity in-sync : True`
  - Programatik: `IPaymentReadModelParityReader.GetClinicParityAsync` → `InSync=true` (count + row sample + recent ordering)
  - SQL referans (operatör):
    ```sql
    -- Command DB
    SELECT COUNT_BIG(*) FROM Payments;
    -- Query DB
    SELECT COUNT_BIG(*) FROM PaymentReadModels;
    ```

- [ ] **Health/readiness check**
  ```text
  GET /health/ready → entry: payment-projection → Healthy
  ```
  `readModelCountDrift = 0`, `readModelCountInSync = true`, `deadLetterCount = 0`.

- [ ] **Targeted smoke tests** (CI — opsiyonel pre-deploy gate)
  ```powershell
  dotnet build --no-restore

  dotnet test tests/Backend.Veteriner.Application.Tests --no-restore --filter "FullyQualifiedName~PaymentList|FullyQualifiedName~PaymentsList|FullyQualifiedName~PaymentsReport|FullyQualifiedName~PaymentsReportExport|FullyQualifiedName~GetPaymentById|FullyQualifiedName~PaymentGetById|FullyQualifiedName~PaymentSearchQueryReadFlagIsolation"

  dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~PaymentListRolloutAcceptance|FullyQualifiedName~PaymentReadModelParity|FullyQualifiedName~PaymentDetailIdorIntegrationTests|FullyQualifiedName~PaymentReadModelReaderIntegrationTests"
  ```

- [ ] **IDOR smoke tests** — staging manuel (GetById flag true + projected row zorunlu; bkz. §IDOR)

**Client/Pet projection bağımlılığı:** Payment search Query path client/pet lookup read-model'lerine bağımlıdır. Rollout öncesi client/pet backfill/parity InSync olmalıdır (repo'daki mevcut komut adlarıyla: `backfill-client-projections`, `backfill-pet-projections`).

---

## Required flags

Tüm flag'ler **default false** (`appsettings.json`, `Development`, `Production`, `Staging`, `IntegrationTests`, `LoadTest`).

| Config yolu | Property | Default | Rol |
|---|---|---|---|
| `PaymentProjection` | `Enabled` | **false** | Outbox → `PaymentReadModels` projection worker |
| `QueryReadModels` | `PaymentsListReadEnabled` | **false** | Payment list Query DB routing |
| `QueryReadModels` | `DashboardRecentPaymentsReadEnabled` | **false** | Dashboard recent payments Query DB routing |
| `QueryReadModels` | `ClientPaymentSummaryReadEnabled` | **false** | Client payment summary Query DB routing |
| `QueryReadModels` | `PaymentsReportReadEnabled` | **false** | Payment report JSON Query DB routing |
| `QueryReadModels` | `PaymentsReportExportReadEnabled` | **false** | Payment export CSV/XLSX Query DB routing |
| `QueryReadModels` | `PaymentsGetByIdReadEnabled` | **false** | Payment GetById Query DB routing |

**Startup doğrulama:** Deploy/restart sonrası `CQRS startup configuration` log satırında tüm flag'ler + `PaymentProjectionEnabled` + `CommandDbCatalog` / `QueryDbCatalog` (`CqrsStartupConfigurationLogger`).

**Config uygulama:** Environment variable örneği: `QueryReadModels__PaymentsListReadEnabled=true`. Deploy/restart zorunlu.

---

## Rollout order

**Altın kural:** Backfill + parity gate geçmeden **hiçbir** payment read flag açılmamalı.

| # | Adım | Aksiyon | Doğrulama |
|---|---|---|---|
| 1 | Deploy code | Tüm read flag'ler **false** | Production davranış değişmez |
| 2 | Query migration | `migrate-query` | `PaymentReadModels` mevcut |
| 3 | Enable projection | `PaymentProjection:Enabled=true` | — |
| 4 | Restart/redeploy | API restart | — |
| 5 | Confirm startup logs | `PaymentProjectionEnabled=True`; read flag'ler False | `CommandDbCatalog` ≠ `QueryDbCatalog` |
| 6 | Payment backfill | `backfill-payment-read-models` | Exit 0 |
| 7 | Confirm parity / InSync | `GetClinicParityAsync` + backfill çıktısı | InSync=true |
| 8 | Confirm health ready | `GET /health/ready` | `payment-projection` Healthy |
| 9 | Enable list | `PaymentsListReadEnabled=true` → restart | Startup log True |
| 10 | Smoke list | §Smoke tests — List | Query log + parity |
| 11 | Enable dashboard recent | `DashboardRecentPaymentsReadEnabled=true` → restart | Startup log True |
| 12 | Smoke dashboard | §Smoke tests — Dashboard | Recent payments dolu |
| 13 | Enable client summary | `ClientPaymentSummaryReadEnabled=true` → restart | Startup log True |
| 14 | Smoke client summary | §Smoke tests — Client summary | Count/totals/recent |
| 15 | Enable report JSON | `PaymentsReportReadEnabled=true` → restart | Startup log True |
| 16 | Smoke report JSON | §Smoke tests — Report | totalCount/totalAmount |
| 17 | Enable export | `PaymentsReportExportReadEnabled=true` → restart | Startup log True |
| 18 | Smoke CSV/XLSX export | §Smoke tests — Export | Satır sayısı JSON ile uyumlu |
| 19 | Enable GetById | `PaymentsGetByIdReadEnabled=true` → restart | Startup log True |
| 20 | Smoke GetById + IDOR | §Smoke tests — GetById + §IDOR | 200/404/403 senaryoları |
| 21 | Final smoke | Tüm açık flag'lerle entegre smoke | 5–15 dk error rate gözlem |

**Staging → Production:** Her flag staging'de smoke temiz olmadan production'a taşınmaz.

---

## Smoke tests

Her yüzey için kısa checklist (happy path, empty query, search/filter parity, tenant/clinic scope, no-fallback):

### Payment list (`PaymentsListReadEnabled`)

- [ ] **Happy path** — single clinic, search boş → 200; kayıt sayısı Command baseline ile uyumlu
- [ ] **Search/filter parity** — search dolu, single clinic → 200; Query path
- [ ] **Empty query** — sonuç yok → boş liste (404 değil)
- [ ] **Tenant/clinic scope** — multi-clinic → Command DB path (Query log **yok**)
- [ ] **No-fallback** — Query path seçildi, Query boş → boş liste; Command fallback **yok**
- [ ] Log: `Payments list generated from Query DB read model`

### Dashboard recent payments (`DashboardRecentPaymentsReadEnabled`)

- [ ] **Happy path** — single clinic → 200; `recentPayments` dolu
- [ ] **Empty query** — Query boş → recent payments boş
- [ ] **Tenant/clinic scope** — tenant-wide / multi-clinic → Command DB path
- [ ] **No-fallback** — Query path, boş read-model → boş recent list
- [ ] Log: `DashboardRecentPaymentsReadEnabled=True`; `RecentPayments={count}`

### Client payment summary (`ClientPaymentSummaryReadEnabled`)

- [ ] **Happy path** — single clinic → 200; count/totals/recent dolu
- [ ] **Tenant-wide admin** — aktif klinik yok → 200; Query path (`ClinicScoped=false`)
- [ ] **Empty query** — Query boş → count 0 / totals boş / recent boş
- [ ] **Tenant/clinic scope** — multi-clinic ClinicAdmin → Command DB path
- [ ] **No-fallback** — Query path, boş → sıfır/boş summary
- [ ] Log: `Client payment summary generated from Query DB read model`

### Payment report JSON (`PaymentsReportReadEnabled`)

- [ ] **Happy path** — single clinic veya tenant-wide → 200; `totalCount` / `totalAmount` uyumlu
- [ ] **Search/filter parity** — search dolu → filtrelenmiş sonuçlar
- [ ] **Empty query** — eşleşme yok → boş/zero rapor
- [ ] **Tenant/clinic scope** — multi-clinic → Command DB path
- [ ] **No-fallback** — Query path, boş → boş/zero rapor
- [ ] Log: `Payments report generated from Query DB read model`

### Payment export CSV/XLSX (`PaymentsReportExportReadEnabled`)

- [ ] **Happy path** — CSV 200; XLSX 200; satır sayısı JSON rapor ile uyumlu
- [ ] **Search/filter parity** — search dolu export → filtrelenmiş satırlar
- [ ] **Empty query** — eşleşme yok → boş export
- [ ] **Tenant/clinic scope** — multi-clinic → Command DB path
- [ ] **No-fallback** — Query path, boş → boş dosya
- [ ] 50k cap (`MaxExportRows = 50000`) aşılmamalı
- [ ] Startup log: `PaymentsReportExportReadEnabled=True`

### Payment GetById (`PaymentsGetByIdReadEnabled`)

- [ ] **Happy path** — projected payment, authorized → 200; DTO alanları dolu
- [ ] **Empty Query DB** — Command'da var, Query'de yok → **404** `Payments.NotFound`
- [ ] **Tenant/clinic scope** — bkz. §IDOR
- [ ] **No-fallback** — Query row yok → NotFound; Command fallback **yok**
- [ ] Log: `Payment detail generated from Query DB read model`
- [ ] Rollback: flag false → aynı ID **200** Command path

---

## IDOR / tenant-clinic safety

- **IDOR roadmap kapandı** — clinic assignment write guard'ları + regression checklist mevcut ([`idor-regression.md`](../security/idor-regression.md)).
- Payment detail/list/report/export Query DB path'leri **tenant/clinic scope** ile korunmalıdır.
- Auth mantığı Command ve Query path'te paylaşılan inline guard ile uygulanır.

| Senaryo | Beklenen HTTP | Problem code |
|---|---|---|
| **Assigned clinic** — kullanıcı payment'ın kliniğine atanmış | **200** | — |
| **Unassigned clinic** — same-tenant, non-tenant-wide, kliniğe atanmamış | **404** | `Payments.NotFound` (Forbidden değil) |
| **Cross-tenant** payment ID | **404** | `Payments.NotFound` |
| **Tenant-wide** (Admin/Owner) cross-clinic erişim | **200** | — |
| **Active clinic mismatch** — aktif klinik ≠ kayıt kliniği | **404** | `Payments.NotFound` |
| **Permission yok** — `Payments.Read` yok | **403** | policy failure |

**Test referansları:** `PaymentDetailIdorIntegrationTests` (Command baseline) · `PaymentGetByIdQueryHandlerFeatureFlagTests` (Query path unit auth) · `PaymentReadModelReaderIntegrationTests.GetById_*`

**Bilinen gap:** HTTP integration testleri varsayılan `PaymentsGetByIdReadEnabled=false` ile Command baseline sağlar. **Staging IDOR smoke (flag true + projected row) zorunludur.**

---

## No-fallback safety

- **Query path seçildi ise Command DB fallback yok.**
- Bu bilinçli bir tasarım kararıdır; operasyonel güvence backfill/parity gate ile sağlanır.
- **Backfill/parity gate geçmeden flag açılırsa false-negative riski:**
  - GetById → **404** (`Payments.NotFound`)
  - List/report/export/summary → **boş sonuç** / sıfır totals
- Reader exception → propagate; Command DB tekrar denenmez.
- Multi-clinic scope'ta list/report/export/dashboard recent → routing aşamasında Command DB seçilir (scope guard; fallback değil).
- **Projection kapalı + read flag açık → yüksek risk:** Yeni payment'lar Query'ye yansımaz; read path 404/boş sonuç üretir.

---

## Rollback checklist

### Her flag için (genel)

- [ ] İlgili `QueryReadModels:*ReadEnabled` → **false**
- [ ] Restart/redeploy
- [ ] Startup log: flag **False** doğrula
- [ ] Command DB smoke: endpoint eski davranışa döndü (Query log **yok**)
- [ ] Sorun devam ederse: projection/backfill/parity kontrolü

### Flag bazlı etki

| Flag | Rollback sonucu | Diğer flag'ler |
|---|---|---|
| `PaymentsListReadEnabled` | List → Command DB | Etkilenmez |
| `DashboardRecentPaymentsReadEnabled` | Recent payments → Command DB | Finance totals bağımsız |
| `ClientPaymentSummaryReadEnabled` | Summary → Command DB | Etkilenmez |
| `PaymentsReportReadEnabled` | Report JSON → Command DB | Export bağımsız kalabilir |
| `PaymentsReportExportReadEnabled` | Export → Command DB | Report JSON bağımsız kalabilir |
| `PaymentsGetByIdReadEnabled` | Detail → Command DB | List/report/export etkilenmez |
| `PaymentProjection:Enabled` | Event tüketimi durur | **Read flag'ler açık kalmamalı** |

### Kısmi rollback (güvenli senaryolar)

- [ ] Export kapatılıp report açık kalabilir (`PaymentsReportExportReadEnabled=false`, `PaymentsReportReadEnabled=true`)
- [ ] GetById kapatılıp diğer route'lar açık kalabilir
- [ ] **Projection kapatılırsa read flag'ler açık kalmamalı** — yeni kayıtlar Query'ye yansımaz; drift/lag artar

**Acil tam rollback:** Tüm payment read flag'ler false → restart → Command DB source of truth. Projection açık bırakılabilir (read-model sıcak tutulur).

---

## Go / No-Go checklist

### Go (production flag açma onayı)

- [ ] Query migration tamam (`migrate-query`; `PaymentReadModels` mevcut)
- [ ] Projection healthy (`PaymentProjection:Enabled=true`; outbox tüketimi çalışıyor)
- [ ] Backfill success (`backfill-payment-read-models` exit 0)
- [ ] Parity InSync (`GetClinicParityAsync` + count parity)
- [ ] Health ready (`GET /health/ready` → `payment-projection` Healthy; drift yok)
- [ ] Startup flags doğru (`CQRS startup configuration` log)
- [ ] Staging per-flag smoke tests pass (sıra 9→20)
- [ ] IDOR smoke pass (GetById adımında tam checklist)
- [ ] Client/Pet projection parity (search lookup bağımlılığı)
- [ ] Rollback tested or documented (bu checklist §Rollback)

### No-Go (flag açma — dur)

- [ ] Parity mismatch (backfill exit 2 veya `InSync=false`)
- [ ] Health degraded/unhealthy (`readModelCountDrift ≠ 0` + read flag açık)
- [ ] Query DB count mismatch (Command vs Query global count)
- [ ] Staging IDOR smoke fail
- [ ] Startup flag logs belirsiz veya beklenen değerlerden sapma
- [ ] Projection dead-letter / lag (`deadLetterCount > 0`, yüksek `pendingCount`)
- [ ] `QueryDbCatalog=(not-configured)` startup log'da

---

## Final decision

| Soru | Karar |
|---|---|
| **Payment CQRS release readiness tamam mı?** | **Evet** — kod, test, runbook (17A) ve bu checklist (17C) ile migration hattı release-ready kabul edilebilir |
| **Production flag açma kod açısından hazır mı?** | **Evet** — tüm handler/reader/flag/projection/backfill/parity/health path'leri implement edildi; default false ile production davranışı değişmedi |
| **Hangi koşullar sağlanmadan açılmamalı?** | **(1)** `migrate-query` + tablo mevcut · **(2)** `PaymentProjection:Enabled=true` · **(3)** `backfill-payment-read-models` exit 0 · **(4)** parity InSync · **(5)** health Healthy · **(6)** Client/Pet lookup projection (search) · **(7)** staging kademeli flag smoke + GetById IDOR smoke |
| **Sonraki önerilen faz?** | **Production rollout sonrası izleme** öncelikli (health drift, projection lag, error rate, latency). Ardından **CQRS-18** (performance/index) planlanabilir. **CQRS-19** (next module) production rollout tamamlanmadan başlatılmamalı |

**Koşullu production onayı:** Tüm Go checklist maddeleri staging'de temiz olmadan production payment read flag'leri açılmamalıdır.

---

## İlgili dokümanlar

- [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](cqrs-17a-payment-read-model-production-rollout-runbook.md) — operasyonel detay
- [`cqrs-16c-payment-getbyid-rollout-readiness.md`](cqrs-16c-payment-getbyid-rollout-readiness.md) — GetById readiness
- [`cqrs-15o-payment-search-read-model-rollout-readiness.md`](cqrs-15o-payment-search-read-model-rollout-readiness.md) — search rollout
- [`cqrs-14f-payment-list-backfill-parity-health.md`](cqrs-14f-payment-list-backfill-parity-health.md) — backfill/parity/health
- [`idor-regression.md`](../security/idor-regression.md) — IDOR regression

**CQRS-17C:** Commit atılmadı (kullanıcı talimatı).
