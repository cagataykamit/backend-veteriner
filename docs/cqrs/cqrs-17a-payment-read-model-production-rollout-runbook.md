# CQRS-17A — Payment Read-Model Production Rollout Runbook

**Tür:** Operasyonel production rollout runbook. **Production kod, test, appsettings, migration veya feature flag değişmedi.**

**Ön durum (tamamlanan fazlar):**

- **CQRS-15O** — Payment search rollout readiness ([`cqrs-15o-payment-search-read-model-rollout-readiness.md`](cqrs-15o-payment-search-read-model-rollout-readiness.md))
- **CQRS-16A** — Payment GetById read-model decision audit ([`cqrs-16a-payment-getbyid-read-model-decision.md`](cqrs-16a-payment-getbyid-read-model-decision.md))
- **CQRS-16B** — Payment GetById Query DB route ([`cqrs-16b-payment-getbyid-read-model-route.md`](cqrs-16b-payment-getbyid-read-model-route.md))
- **CQRS-16C** — Payment GetById rollout readiness ([`cqrs-16c-payment-getbyid-rollout-readiness.md`](cqrs-16c-payment-getbyid-rollout-readiness.md))
- **IDOR / clinic isolation roadmap** — kapalı ([`idor-regression.md`](../security/idor-regression.md))

**İlgili altyapı dokümanları:** [`cqrs-14b-payment-read-model-schema.md`](cqrs-14b-payment-read-model-schema.md) · [`cqrs-14c-payment-read-model-projection.md`](cqrs-14c-payment-read-model-projection.md) · [`cqrs-14f-payment-list-backfill-parity-health.md`](cqrs-14f-payment-list-backfill-parity-health.md) · [`cqrs-14g-payment-list-rollout-acceptance.md`](cqrs-14g-payment-list-rollout-acceptance.md) · [`cqrs-15b-dashboard-recent-payments-read-model.md`](cqrs-15b-dashboard-recent-payments-read-model.md) · [`cqrs-15e-client-payment-summary-read-model.md`](cqrs-15e-client-payment-summary-read-model.md) · [`cqrs-15g-payment-report-read-model.md`](cqrs-15g-payment-report-read-model.md) · [`cqrs-13f-payment-finance-rollout-acceptance.md`](cqrs-13f-payment-finance-rollout-acceptance.md)

**Git doğrulama (2026-06-23, CQRS-17A başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** (tracked değişiklik yok; bu faz yalnızca yeni runbook ekler) |
| CQRS-16C commit | `243c8ec` — `docs(cqrs): add payment get by id rollout readiness` |
| CQRS-16B commit | `6f72702` — `feat(cqrs): route payment get by id through read model` |
| CQRS-15O commit | `7191426` — `test(cqrs): verify payment search rollout readiness` |
| Export search commit | `de383d1` — `feat(cqrs): enable payment export search through read model` |

---

## Current status

| Alan | Durum |
|---|---|
| Production read davranışı | **Değişmedi** — tüm payment read-model flag'leri default **false** |
| Query DB tablo | `PaymentReadModels` (14B migration; ayrı finance tabloları 13B) |
| Projection worker | `PaymentProjectionHostedService` → `IPaymentProjectionProcessor` (API process içi hosted service) |
| Backfill komutu | `backfill-payment-read-models` (DbMigrator) |
| Parity | `IPaymentReadModelParityReader.GetClinicParityAsync` |
| Health | `GET /health/ready` → entry `payment-projection` |
| Rollout dokümantasyonu | **Bu runbook** — operasyonel gate + sıra + smoke + rollback |

**Operatör özeti:** Payment read-model hattı kod olarak hazır. Production'da flag açmadan önce Query migration, projection, backfill, parity ve health gate'leri zorunludur. Read flag'ler kademeli açılır; her adımda smoke + startup log doğrulaması yapılır.

---

## Feature flag matrix

### Projection (Query DB doldurma / event tüketimi)

| Config yolu | Property | Default (tüm ortamlar) | Etki |
|---|---|---|---|
| `PaymentProjection` | `Enabled` | **false** | `PaymentProjectionHostedService` polling; outbox → `PaymentReadModels` + finance read-model upsert |

**Not:** Finance dashboard totals (`DashboardFinanceReadEnabled`) ayrı bir flag'dir ve bu runbook'un payment **list/detail/summary/report/export/search** yüzeylerinden bağımsızdır. Payment read-model rollout'u için projection açık olmalıdır; finance flag'i zorunlu değildir.

### Payment read-model routing (`QueryReadModels` section)

| Flag | Default | Endpoint / yüzey | Handler |
|---|---|---|---|
| `PaymentsListReadEnabled` | **false** | `GET /api/v1/payments` | `GetPaymentsListQueryHandler` |
| `DashboardRecentPaymentsReadEnabled` | **false** | `GET /api/v1/dashboard/finance-summary` (recent payments bölümü) | `GetDashboardFinanceSummaryQueryHandler` |
| `ClientPaymentSummaryReadEnabled` | **false** | `GET /api/v1/clients/{id}/payment-summary` | `GetClientPaymentSummaryQueryHandler` |
| `PaymentsReportReadEnabled` | **false** | `GET /api/v1/reports/payments` | `GetPaymentsReportQueryHandler` |
| `PaymentsReportExportReadEnabled` | **false** | `GET /api/v1/reports/payments/export` · `GET /api/v1/reports/payments/export-xlsx` | `ExportPaymentsReportQueryHandler` · `ExportPaymentsReportXlsxQueryHandler` (ortak `PaymentsReportExportPipeline`) |
| `PaymentsGetByIdReadEnabled` | **false** | `GET /api/v1/payments/{id}` | `GetPaymentByIdQueryHandler` |

**Explicit false doğrulandığı dosyalar (6):** `appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json`, `appsettings.Staging.json`, `appsettings.IntegrationTests.json`, `appsettings.LoadTest.json`.

### Bağımsız lookup flag'ler (Command path; Query path'i etkilemez)

| Flag | Default | Rol |
|---|---|---|
| `PaymentsSearchLookupEnabled` | **false** | Command DB path'te payment list/report/export search lookup kaynağı |
| `SharedSearchLookupEnabled` | **false** | Client/pet shared lookup (payment yüzeylerinden bağımsız) |

Query path seçildiğinde search resolution her zaman Query DB lookup reader'ları kullanır; yukarıdaki flag'ler Query path'i **etkilemez** (15O onaylı).

### Cross-flag bağımsızlık

Her payment read flag yalnızca kendi handler'ını etkiler. Örnek: `PaymentsGetByIdReadEnabled` list/report/export flag'lerinden bağımsızdır (`PaymentGetByIdReadFlagIsolationTests`).

---

## Pre-rollout gates

Deploy/flag açmadan **önce** tüm maddeler geçmelidir.

### 1. Migration ve Query DB erişimi

| Kontrol | Beklenen | Doğrulama |
|---|---|---|
| Query migration uygulandı mı? | `PaymentReadModels` tablo + indexler mevcut | `dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query` (bekleyen migration yok) |
| Command vs Query farklı catalog | Backfill guard geçer | Startup log: `CommandDbCatalog` ≠ `QueryDbCatalog` |
| Query connection string | Yapılandırılmış | Startup log: `QueryDbCatalog=(not-configured)` **olmamalı** |
| `PaymentReadModels` tablo var mı? | Query DB'de mevcut | SQL veya migration çıktısı |

**Migration notu:** Bu runbook yeni migration eklemez. Mevcut schema 14B (`PaymentReadModels`) + 15D (`ClinicName` enrichment) ile yeterlidir.

### 2. Projection servis / worker

| Kontrol | Beklenen |
|---|---|
| `PaymentProjection:Enabled=true` (gate sonrası) | `PaymentProjectionHostedService` polling başlar |
| Worker kapalıyken | Log: `PaymentProjection disabled via PaymentProjection:Enabled=false; background polling skipped.` |
| Outbox tüketimi | Yeni payment create/update event'leri `PaymentReadModels`'e yansır |

**Operasyon notu:** Projection API process içinde `IHostedService` olarak çalışır; ayrı worker deployment yoktur. Çoklu API instance + `ClaimingEnabled` senaryosu repoda tanımlı değilse tek instance projection tüketimi tercih edilir (appointment runbook pattern'i: [`cqrs-11b-operations-runbook.md`](cqrs-11b-operations-runbook.md)).

### 3. Log izleme

| Kaynak | Ne aranır |
|---|---|
| **Startup** | `CQRS startup configuration` — tüm flag'ler + `PaymentProjectionEnabled` + DB catalog'lar |
| **Application logs** | Query path hit mesajları (aşağıdaki Health and observability) |
| **Health** | `GET /health/ready` → `payment-projection` entry |
| **Projection worker** | `Payment projection tick failed.` (hata) · queue healthy mesajları health `data` alanında |

PII/secret startup log'a yazılmaz (`CqrsStartupConfigurationLogger`).

### 4. Client / Pet projection (search lookup bağımlılığı)

Payment search Query path, client/pet lookup read-model'lerine bağımlıdır (email/phone/species/breed). Rollout öncesi:

- Client/Pet projection backfill/parity InSync (ilgili client/pet CQRS runbook'ları)
- Lookup stale ise search sonuçları eksik olabilir (15O §6)

---

## Rollout sequence

**Altın kural:** Backfill + count/parity gate geçmeden **hiçbir** payment read flag açılmamalı. Query path seçildiğinde Command DB fallback yoktur.

### Faz 0 — Altyapı (read flag'ler kapalı)

```text
1. migrate-query
2. PaymentProjection:Enabled=true → API restart
3. backfill-payment-read-models
4. Count + clinic parity InSync doğrula
5. GET /health/ready → payment-projection Healthy (drift yok / kuyruk sağlıklı)
```

### Faz 1 — Read flag'ler (sırayla, her biri: config → restart → startup log → smoke)

| Sıra | Flag | Config yolu |
|---|---|---|
| 6 | `PaymentsListReadEnabled` | `QueryReadModels:PaymentsListReadEnabled=true` |
| 7 | `DashboardRecentPaymentsReadEnabled` | `QueryReadModels:DashboardRecentPaymentsReadEnabled=true` |
| 8 | `ClientPaymentSummaryReadEnabled` | `QueryReadModels:ClientPaymentSummaryReadEnabled=true` |
| 9 | `PaymentsReportReadEnabled` | `QueryReadModels:PaymentsReportReadEnabled=true` |
| 10 | `PaymentsReportExportReadEnabled` | `QueryReadModels:PaymentsReportExportReadEnabled=true` |
| 11 | `PaymentsGetByIdReadEnabled` | `QueryReadModels:PaymentsGetByIdReadEnabled=true` |

**Staging → Production:** Her flag staging'de smoke temiz olmadan production'a taşınmaz. Production'da aynı sıra korunur.

**Config uygulama:** Ortama göre appsettings overlay, environment variable (`QueryReadModels__PaymentsListReadEnabled=true`) veya secret store. Deploy/restart zorunlu.

### Backfill komutu (Faz 0, adım 3)

Repo'da mevcut komut adıyla çalıştırılmalı:

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --batch-size 500
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --tenant <guid>
```

- Exit code **0** — başarı
- Exit code **2** — count parity mismatch (flag açma; drift gider)
- **Idempotent:** tekrar çalıştırma duplicate satır üretmez; stale guard daha yeni projection event'ini ezmez

### Parity doğrulama (Faz 0, adım 4)

Programatik: `IPaymentReadModelParityReader.GetClinicParityAsync(tenantId, clinicId)` → `InSync=true` (count + row sample + recent ordering).

Drift tespit edilirse: flag açma → backfill yeniden çalıştır → projection lag/incident çöz → parity InSync olana kadar bekle.

---

## Per-flag smoke tests

Her flag açıldıktan sonra ilgili smoke çalıştırılır. **Single-clinic** kullanıcı veya tenant-wide admin ile test edin; multi-clinic ClinicAdmin (aktif klinik yok) bilinçli olarak Command DB fallback kullanır — Query path smoke için uygun değildir.

### After `PaymentsListReadEnabled=true`

| Kontrol | Beklenen |
|---|---|
| `GET /api/v1/payments` (single clinic, search boş) | 200; kayıt sayısı Command baseline ile uyumlu |
| Search dolu (single clinic) | 200; Query path (15L) |
| Log | `Payments list generated from Query DB read model` |
| Multi-clinic scope | Command DB fallback (Query log **yok**) |

### After `DashboardRecentPaymentsReadEnabled=true`

| Kontrol | Beklenen |
|---|---|
| `GET /api/v1/dashboard/finance-summary` (single clinic) | 200; `recentPayments` dolu (bilinen ödemeler) |
| Startup + response log | `DashboardRecentPaymentsReadEnabled=True`; structured log `RecentPayments={count}` |
| Tenant-wide / multi-clinic | Command DB path (recent payments Query log yok) |

### After `ClientPaymentSummaryReadEnabled=true`

| Kontrol | Beklenen |
|---|---|
| `GET /api/v1/clients/{id}/payment-summary` (single clinic) | 200; count/totals/recent dolu |
| Tenant-wide admin (aktif klinik yok) | 200; Query path tenant-wide (`ClinicScoped=false` log) |
| Log | `Client payment summary generated from Query DB read model` |

### After `PaymentsReportReadEnabled=true`

| Kontrol | Beklenen |
|---|---|
| `GET /api/v1/reports/payments?from=...&to=...` (single clinic veya tenant-wide) | 200; `totalCount` / `totalAmount` Command baseline ile uyumlu |
| Search dolu | 200; Query path (15M) |
| Log | `Payments report generated from Query DB read model` |

### After `PaymentsReportExportReadEnabled=true`

| Kontrol | Beklenen |
|---|---|
| `GET /api/v1/reports/payments/export?from=...&to=...` | 200 CSV; satır sayısı JSON rapor ile uyumlu |
| `GET /api/v1/reports/payments/export-xlsx?from=...&to=...` | 200 XLSX |
| Search dolu export | Filtrelenmiş satırlar; 50k cap (`PaymentsReportConstants.MaxExportRows = 50000`) aşılmamalı |
| Startup log | `PaymentsReportExportReadEnabled=True` |

Export pipeline'da ayrı `"generated from Query DB"` log satırı yok; doğrulama dosya indirme + satır sayısı + flag startup log ile yapılır.

### After `PaymentsGetByIdReadEnabled=true`

| Kontrol | Beklenen |
|---|---|
| `GET /api/v1/payments/{id}` (projected payment, authorized) | 200; DTO alanları dolu |
| Command'da var, Query'de yok ID | **404** `Payments.NotFound` (fallback yok) |
| Log | `Payment detail generated from Query DB read model` |
| Flag false rollback | Aynı ID **200** Command path |

---

## IDOR smoke checklist

Auth mantığı Command ve Query path'te paylaşılan inline guard ile uygulanır. HTTP integration testleri varsayılan `PaymentsGetByIdReadEnabled=false` ile Command baseline sağlar; **flag true + projected row** senaryoları staging manuel smoke zorunludur (16C).

Her read flag açıldıktan sonra ilgili yüzeyde aşağıdaki senaryolar doğrulanır (GetById için tüm maddeler zorunlu; list/report için scope guard mevcut semantik):

| Senaryo | Beklenen HTTP | Problem code |
|---|---|---|
| **Assigned clinic** — kullanıcı payment'ın kliniğine atanmış | **200** | — |
| **Unassigned clinic** — same-tenant, non-tenant-wide, kliniğe atanmamış | **404** | `Payments.NotFound` (Forbidden değil) |
| **Cross-tenant** payment ID | **404** | `Payments.NotFound` |
| **Tenant-wide** (Admin/Owner) cross-clinic erişim | **200** | — |
| **Active clinic mismatch** — aktif klinik ≠ kayıt kliniği | **404** | `Payments.NotFound` |
| **Permission yok** — `Payments.Read` yok | **403** | policy failure |

**Referans testler:** `PaymentDetailIdorIntegrationTests` (Command baseline) · `PaymentGetByIdQueryHandlerFeatureFlagTests` (Query path unit auth) · [`idor-regression.md`](../security/idor-regression.md) Full IDOR Regression filtresi.

---

## Backfill and parity checks

### Backfill nasıl çalışır?

```text
backfill-payment-read-models [--tenant <guid>] [--batch-size 500]
  → PaymentReadModelBackfillService.BackfillAsync
      → EnsureDistinctDatabasesAsync (Command ≠ Query catalog)
      → Command DB Payments batch (TenantId, Id sıralı)
      → Clients + Pets join (Command DB)
      → Query DB PaymentReadModels upsert (stale guard)
      → Global count parity kontrolü
```

Snapshot enrichment: `PaymentProjectionSnapshotFactory.Create(payment, client.FullName, clinic.Name, pet?.Name)` — projection processor ile birebir (15D `ClinicName` dahil).

### Idempotent mi?

| Durum | Davranış |
|---|---|
| Tekrar çalıştırma | PK upsert; duplicate satır yok |
| Daha yeni projection event'i | Stale guard → SkipStale; backfill ezmez |
| Backfill marker | `LastEventId = Guid.Empty`, `LastEventOccurredAtUtc = DateTime.MinValue` |

### Count parity nasıl doğrulanır?

1. **Backfill çıktısı:** mismatch → exit code **2**
2. **Health:** `readModelCountDrift = 0`, `readModelCountInSync = true` (`payment-projection` health `data`)
3. **Clinic parity:** `GetClinicParityAsync` → `CountInSync && RowSampleParityInSync && RecentOrderingInSync`

SQL (global count, operatör referansı):

```sql
-- Command DB
SELECT COUNT_BIG(*) FROM Payments;
-- Query DB
SELECT COUNT_BIG(*) FROM PaymentReadModels;
```

### Drift varsa ne yapılır?

1. Read flag'leri **açık tutma** veya **hemen kapat**
2. `backfill-payment-read-models` yeniden çalıştır
3. Projection kuyruk lag / dead-letter kontrol (`payment-projection` health)
4. Parity InSync + health Healthy olana kadar bekle
5. Flag'leri kademeli yeniden aç

### Client/Pet denormalized name riski

| Alan | Risk | Not |
|---|---|---|
| `ClientName` / `PetName` / `ClinicName` | Client/pet/clinic rename sonrası projection gecikmesi | Beklenen CQRS trade-off; payment event olmadan rename yansımaz |
| Search lookup alanları | Email/phone/species/breed lookup stale → eksik search | Client/Pet projection parity gerekli |
| Parity row sample | Backfill sonrası InSync | Rename sonrası field drift parity'de yakalanmayabilir (count parity korunur) |

GetById/list/report DTO'larında isimler projection'dan gelir; Query path'te Command client/pet lookup **yapılmaz**.

---

## Health and observability

### İzlenmesi gereken health check

**Endpoint:** `GET /health/ready`  
**Entry adı:** `payment-projection`

Değerlendirme: `PaymentProjectionHealthEvaluator` — finance kuyruk + read-model drift (en kötü seviye kazanır).

### Read-model drift gate

| projectionEnabled | paymentsListReadEnabled | Drift | Seviye |
|---|---|---|---|
| false | false | — | Drift **değerlendirilmez** (Healthy) |
| true | false | var | **Degraded** (catch-up penceresi) |
| * | true | var | **Unhealthy** |
| * | * | yok | **Healthy** |

`data` alanları (sinyal hesaplandığında): `paymentsListReadEnabled`, `readModelCommandPaymentCount`, `readModelCount`, `readModelCountDrift`, `readModelCountInSync` — mevcut `pendingCount`, `deadLetterCount`, `projectionEnabled` vb. ile birlikte.

**Not:** Field-level / recent ordering drift health'te değil **parity reader**'da değerlendirilir.

### Startup log kontrol listesi

Deploy/restart sonrası `CQRS startup configuration` satırında doğrula:

| Alan | Rollout başlangıcı | Her flag adımı |
|---|---|---|
| `PaymentProjectionEnabled` | **True** (Faz 0 sonrası) | True kalmalı |
| `PaymentsListReadEnabled` | False → True (adım 6) | Açılan flag **True** |
| `DashboardRecentPaymentsReadEnabled` | False → True (adım 7) | … |
| `ClientPaymentSummaryReadEnabled` | False → True (adım 8) | … |
| `PaymentsReportReadEnabled` | False → True (adım 9) | … |
| `PaymentsReportExportReadEnabled` | False → True (adım 10) | … |
| `PaymentsGetByIdReadEnabled` | False → True (adım 11) | … |
| `CommandDbCatalog` / `QueryDbCatalog` | Beklenen catalog adları | Değişmemeli |

### Projection lag / drift nasıl anlaşılır?

| Sinyal | Anlam |
|---|---|
| `pendingCount > 0` veya `oldestPendingAgeSeconds` yüksek | Projection lag |
| `deadLetterCount > 0` | **Unhealthy** (işlem gerekir) |
| `readModelCountDrift ≠ 0` | Count drift; read flag açıkken **Unhealthy** |
| `PaymentProjectionHealthDegraded` / `PaymentProjectionHealthUnhealthy` log | Operasyonel alert |
| `Payment projection tick failed.` | Processor exception; lag artabilir |

### Query path log mesajları (arama)

| Yüzey | Log mesajı (Information) |
|---|---|
| List | `Payments list generated from Query DB read model` |
| Dashboard recent | `Dashboard finance summary generated` (içinde `DashboardRecentPaymentsReadEnabled=True`) |
| Client summary | `Client payment summary generated from Query DB read model` |
| Report JSON | `Payments report generated from Query DB read model` |
| GetById | `Payment detail generated from Query DB read model` |

---

## No-fallback policy

Payment read-model Query path'lerinde **Command DB fallback yoktur**. Operasyonel olarak backfill/parity gate bu politikanın gerekçesidir.

| Koşul | Davranış |
|---|---|
| Query path seçildi | Yalnız Query DB reader çağrılır |
| Query DB boş / satır yok | List/report/export: **boş sonuç**; GetById: **`Payments.NotFound` (404)** |
| Reader exception | Exception propagate; Command DB tekrar denenmez |
| Multi-clinic scope (list/report/export/dashboard recent) | **Routing aşamasında** Command DB seçilir (Query path'e girilmez) — bu bir fallback değil, scope guard |
| `PaymentsSearchLookupEnabled` | Query path'i **etkilemez** |

**Operatör kuralı:** Backfill/parity gate geçmeden flag açmak false-negative (404/boş liste/eksik export) üretir. Projection kapalı + read flag açık → Query DB boş kalır → kullanıcı etkisi.

---

## Rollback plan

### Genel adımlar (her flag)

```text
1. İlgili QueryReadModels flag = false
2. Deploy / API restart
3. Startup log: flag False doğrula
4. Smoke: endpoint Command DB path'e döndü (Query log yok)
```

Kod geri alımı veya migration gerekmez. Anında etki: handler Command path seçer.

### Flag bazlı rollback

| Flag | Rollback etkisi | Diğer flag'ler |
|---|---|---|
| `PaymentsListReadEnabled` | List Command DB | Etkilenmez |
| `DashboardRecentPaymentsReadEnabled` | Recent payments Command DB | Totals/trend (`DashboardFinanceReadEnabled`) bağımsız |
| `ClientPaymentSummaryReadEnabled` | Summary Command DB | Etkilenmez |
| `PaymentsReportReadEnabled` | Report JSON Command DB | Export bağımsız |
| `PaymentsReportExportReadEnabled` | Export CSV/XLSX Command DB | Report JSON bağımsız kalabilir |
| `PaymentsGetByIdReadEnabled` | Detail Command DB | List/report/export etkilenmez |
| `PaymentProjection:Enabled` | Event tüketimi durur | Aşağıdaki risk |

### Kısmi rollback senaryoları

| Senaryo | Güvenli mi? | Not |
|---|---|---|
| Export flag false, report flag true | **Evet** | Bağımsız flag'ler (15O onaylı) |
| GetById false, diğerleri true | **Evet** | GetById yalnız detail handler |
| Projection false, read flag'ler true | **Hayır — yüksek risk** | Yeni payment'lar Query'ye yansımaz; lag/drift artar; read path 404/boş sonuç |
| Read flag false, projection true | **Evet** | Read-model sıcak tutulur; drift en fazla Degraded |

**Acil tam rollback (read yüzeyleri):** Tüm payment read flag'lerini false yap → restart → Command DB source of truth. Projection açık bırakılabilir (14F rollback notu).

---

## Known risks

| Risk | Etki | Azaltma |
|---|---|---|
| Query DB boş + read flag açık | False-negative NotFound / boş liste / sıfır rapor | Backfill + parity gate; flag sırasına uy |
| Search lookup stale | Eksik search sonuçları (email/phone/species/breed) | Client/Pet projection parity; 15O search smoke |
| Client/Pet/Clinic name denormalization stale | DTO'da eski isim | Kabul edilen trade-off; payment update event ile düzelir |
| Export memory-based, 50k cap, streaming yok | Büyük export OOM / bellek baskısı | Filtre daralt; operasyonel limit (15H/15I) |
| Tenant-wide report/export perf | Yüksek satır hacmi | İleride perf ölçümü; kademeli rollout |
| Flag sırası yanlış | Endpoint bazlı 404/eksik veri | Bu runbook sırasına uy |
| Projection lag / dead-letter | Stale read-model | Health monitoring; dead-letter → Unhealthy |
| GetById HTTP IDOR test gap (flag true) | CI Command baseline | Staging IDOR smoke zorunlu (16C) |
| Query DB outage | 5xx; fallback yok | Flag rollback; incident response |
| Multi-clinic Query search yok | ClinicAdmin → Command fallback | Bilinçli tasarım (15O) |

---

## Operator checklist

### Pre-deploy (Faz 0)

- [ ] `git status --short` temiz (deploy edilen commit doğrulandı)
- [ ] `migrate-query` — bekleyen migration yok
- [ ] Query DB erişimi + `PaymentReadModels` tablo mevcut
- [ ] `CommandDbCatalog` ≠ `QueryDbCatalog` (startup log)
- [ ] Client/Pet projection backfill/parity (search lookup bağımlılığı)
- [ ] `PaymentProjection:Enabled=true` → restart
- [ ] `backfill-payment-read-models` exit 0
- [ ] `GetClinicParityAsync` → InSync (hedef tenant/clinic)
- [ ] `/health/ready` → `payment-projection` Healthy
- [ ] Tüm payment read flag'ler **false** (baseline smoke)

### Her read flag adımı (6–11)

- [ ] Yalnız **bir** flag true yap
- [ ] Restart / deploy
- [ ] Startup log flag True
- [ ] Per-flag smoke (yukarıdaki tablo)
- [ ] IDOR smoke (GetById adımında tam checklist)
- [ ] 5–15 dk error rate / latency gözlem
- [ ] Staging temiz → production aynı adım

### Rollback hazırlığı

- [ ] Rollback owner atanmış
- [ ] Config false + restart prosedürü hazır
- [ ] Command DB baseline smoke senaryoları biliniyor

### CI test referansı (opsiyonel pre-deploy)

Repo'da mevcut test filtreleriyle çalıştırılmalı:

```powershell
dotnet build --no-restore

dotnet test tests/Backend.Veteriner.Application.Tests --no-restore --filter "FullyQualifiedName~PaymentList|FullyQualifiedName~PaymentsList|FullyQualifiedName~PaymentsReport|FullyQualifiedName~PaymentsReportExport|FullyQualifiedName~GetPaymentById|FullyQualifiedName~PaymentGetById|FullyQualifiedName~PaymentSearchQueryReadFlagIsolation"

dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~PaymentListRolloutAcceptance|FullyQualifiedName~PaymentReadModelParity|FullyQualifiedName~PaymentDetailIdorIntegrationTests|FullyQualifiedName~PaymentReadModelReaderIntegrationTests"
```

---

## Final decision

| Soru | Karar |
|---|---|
| Payment CQRS production rollout dokümanı hazır mı? | **Evet** — bu runbook (CQRS-17A) operasyonel gate, sıra, smoke, rollback ve riskleri tek yerde toplar |
| Hangi gate'ler geçmeden production flag açılmamalı? | **(1)** `migrate-query` + `PaymentReadModels` mevcut · **(2)** `PaymentProjection:Enabled=true` · **(3)** `backfill-payment-read-models` exit 0 · **(4)** clinic parity InSync · **(5)** `/health/ready` payment-projection Healthy · **(6)** Client/Pet lookup projection (search için) · **(7)** staging per-flag smoke + GetById IDOR smoke |
| CQRS-17B test-only acceptance gerekli mi? | **Hayır — zorunlu değil.** Mevcut acceptance set yeterli: `PaymentListRolloutAcceptanceIntegrationTests` (14G), search rollout testleri (15O / `7191426`), GetById routing + IDOR unit/integration (16B/16C, 35 test). CQRS-17B isteğe bağlı “full production rollout chain” integration testi eklenebilir; production flag açılışı için **blokör değildir**. Operasyonel güvence bu runbook'taki staging smoke + gate checklist ile sağlanır |

**Koşullu production onayı:** Tüm Faz 0 gate'leri + kademeli flag smoke (Faz 1, sıra 6→11) staging'de temiz olmadan production payment read flag'leri açılmamalıdır.

---

## İlgili commit'ler

| Commit | Mesaj |
|---|---|
| `de383d1` | `feat(cqrs): enable payment export search through read model` |
| `7191426` | `test(cqrs): verify payment search rollout readiness` (15O) |
| `6f72702` | `feat(cqrs): route payment get by id through read model` (16B) |
| `243c8ec` | `docs(cqrs): add payment get by id rollout readiness` (16C) |

**CQRS-17A:** Commit atılmadı (kullanıcı talimatı).
