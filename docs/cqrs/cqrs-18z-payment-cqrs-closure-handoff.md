# CQRS-18Z — Payment CQRS Closure / Handoff

**Tür:** Kapanış / devir notu. **Production kod, test, appsettings, migration veya feature flag değişmedi.**

**Tamamlanan Payment CQRS fazları:**

| Faz | Konu | Commit |
|---|---|---|
| CQRS-15O | Payment search rollout readiness | `7191426` |
| CQRS-16A | Payment GetById read-model decision audit | `70008aa` |
| CQRS-16B | Payment GetById Query DB route | `6f72702` |
| CQRS-16C | Payment GetById rollout readiness | `243c8ec` |
| CQRS-17A | Payment read-model production rollout runbook | `a3e8ae8` |
| CQRS-17C | Payment CQRS release readiness | `4005f5b` |
| CQRS-18A | Payment read-model performance/index audit | `251f69a` |
| IDOR / clinic isolation roadmap | Kapalı | [`idor-regression.md`](../security/idor-regression.md) |

**Referans dokümanlar:** [`cqrs-18a-payment-read-model-performance-audit.md`](cqrs-18a-payment-read-model-performance-audit.md) · [`cqrs-17c-payment-cqrs-release-readiness.md`](cqrs-17c-payment-cqrs-release-readiness.md) · [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](cqrs-17a-payment-read-model-production-rollout-runbook.md) · [`cqrs-16c-payment-getbyid-rollout-readiness.md`](cqrs-16c-payment-getbyid-rollout-readiness.md) · [`cqrs-14f-payment-list-backfill-parity-health.md`](cqrs-14f-payment-list-backfill-parity-health.md)

**Git doğrulama (2026-06-23, CQRS-18Z başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** |
| CQRS-18A commit | `251f69a` — `docs(cqrs): audit payment read model performance` |
| CQRS-17C commit | `4005f5b` — `docs(cqrs): add payment cqrs release readiness` |
| CQRS-17A commit | `a3e8ae8` — `docs(cqrs): add payment read model rollout runbook` |
| CQRS-16B commit | `6f72702` — `feat(cqrs): route payment get by id through read model` |

---

## Current status

- **Payment CQRS read-model hattı kod açısından tamamlandı.** Schema, projection, backfill, parity, health, 6 read yüzeyi routing, search Query path, GetById route ve test kapsamı implement edildi.
- **Production default behavior değişmedi** — tüm payment read flag'leri ve `PaymentProjection:Enabled` default **false** (6/6 `appsettings*.json` dosyasında explicit false).
- **Production açılışı operasyonel flag/rollout kararıdır;** kod path hazır, davranış değişikliği config + restart ile yapılır.
- **Rollout için CQRS-17C Go/No-Go checklist geçilmelidir** — migration, projection, backfill, parity, health, staging smoke ve GetById IDOR smoke olmadan production read flag açılmamalı.

| Alan | Durum |
|---|---|
| Query DB tablo | `PaymentReadModels` (14B + 15D `ClinicName`) |
| Projection | `PaymentProjectionHostedService` (API içi) |
| Read routing | 6 bağımsız `QueryReadModels` flag |
| Operasyon | CQRS-17A runbook + CQRS-17C checklist |
| Performance audit | CQRS-18A — rollout blokörü yok |
| Development hattı | **Kapanabilir** — kalan iş operasyon + opsiyonel 18B/18C |

---

## Completed read surfaces

| # | Yüzey | Endpoint | Handler | Reader | Flag |
|---|---|---|---|---|---|
| 1 | **Payment list** | `GET /api/v1/payments` | `GetPaymentsListQueryHandler` | `PaymentsListReadModelReader` | `PaymentsListReadEnabled` |
| 2 | **Dashboard recent payments** | `GET /api/v1/dashboard/finance-summary` (recent) | `GetDashboardFinanceSummaryQueryHandler` | `DashboardRecentPaymentsReadModelReader` | `DashboardRecentPaymentsReadEnabled` |
| 3 | **Client payment summary** | `GET /api/v1/clients/{id}/payment-summary` | `GetClientPaymentSummaryQueryHandler` | `ClientPaymentSummaryReadModelReader` | `ClientPaymentSummaryReadEnabled` |
| 4 | **Payment report JSON** | `GET /api/v1/reports/payments` | `GetPaymentsReportQueryHandler` | `PaymentsReportReadModelReader` | `PaymentsReportReadEnabled` |
| 5 | **Payment export CSV/XLSX** | `GET /api/v1/reports/payments/export` · `export-xlsx` | `ExportPaymentsReportQueryHandler` · `ExportPaymentsReportXlsxQueryHandler` | `PaymentsReportExportReadModelReader` | `PaymentsReportExportReadEnabled` |
| 6 | **Payment GetById** | `GET /api/v1/payments/{id}` | `GetPaymentByIdQueryHandler` | `PaymentGetByIdReadModelReader` | `PaymentsGetByIdReadEnabled` |

**Altyapı (read flag'lerden önce):** Query migration, `PaymentProjection:Enabled`, `backfill-payment-read-models`, `IPaymentReadModelParityReader`, `GET /health/ready` → `payment-projection`.

**Search:** List, report JSON ve export Query path'te search destekli (15L/15M/15N); Query path client/pet lookup read-model kullanır.

---

## Release readiness decision

| Soru | Karar |
|---|---|
| Kod release-ready mi? | **Evet** — handler, reader, flag, projection, backfill, parity, health, test ve runbook tamam |
| Production davranış bugün değişti mi? | **Hayır** — tüm flag'ler default false |
| Flag açma neye bağlı? | **Staging/production operasyonel gate'ler** (aşağıdaki §Production rollout gates) |
| CQRS-18B/C rollout öncesi zorunlu mu? | **Hayır** — CQRS-18A onaylı |

**Koşul:** Query DB migration, projection, backfill, parity, health ve staging smoke geçmeden **hiçbir** production payment read flag açılmamalı.

---

## Production rollout gates

Deploy/flag açmadan önce CQRS-17C Go checklist:

- [ ] `dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query` — `PaymentReadModels` mevcut
- [ ] `PaymentProjection:Enabled=true` → API restart
- [ ] `backfill-payment-read-models` exit **0** (parity InSync)
- [ ] `IPaymentReadModelParityReader.GetClinicParityAsync` → InSync
- [ ] `GET /health/ready` → `payment-projection` Healthy; `readModelCountDrift = 0`
- [ ] Client/Pet projection backfill/parity (search lookup bağımlılığı)
- [ ] Kademeli flag açma (17A sıra 6→11) + per-flag staging smoke
- [ ] GetById IDOR smoke (flag true + projected row — staging manuel)

**Rollout sırası:** [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](cqrs-17a-payment-read-model-production-rollout-runbook.md) · checklist: [`cqrs-17c-payment-cqrs-release-readiness.md`](cqrs-17c-payment-cqrs-release-readiness.md)

---

## Feature flag matrix

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

**Bağımsız (Payment read Query path'i etkilemez):**

| Flag | Not |
|---|---|
| `DashboardFinanceReadEnabled` | Finance totals/trend — recent payments'tan bağımsız |
| `PaymentsSearchLookupEnabled` | Command path lookup; Query path her zaman Query DB lookup |
| `SharedSearchLookupEnabled` | Client/pet shared lookup |

**Startup doğrulama:** `CQRS startup configuration` log satırı (`CqrsStartupConfigurationLogger`).

**Config örneği:** `QueryReadModels__PaymentsListReadEnabled=true` — deploy/restart zorunlu.

---

## No-fallback policy

Payment read-model Query path'lerinde **Command DB fallback yoktur.** Bilinçli tasarım kararı; operasyonel güvence backfill/parity/health gate ile sağlanır.

| Koşul | Davranış |
|---|---|
| Query path seçildi | Yalnız Query DB reader çağrılır |
| Query DB boş / satır yok | List/report/export/summary → **boş sonuç** / sıfır totals; GetById → **`Payments.NotFound` (404)** |
| Reader exception | Exception propagate; Command DB tekrar denenmez |
| Multi-clinic scope | Routing aşamasında Command DB seçilir (scope guard; fallback değil) |
| Projection kapalı + read flag açık | Yüksek risk — yeni kayıtlar Query'ye yansımaz |

**Operatör kuralı:** Backfill/parity gate geçmeden flag açmak false-negative (404/boş liste/eksik export) üretir.

---

## IDOR / tenant-clinic safety

- **IDOR roadmap kapandı** — clinic assignment write guard'ları + [`idor-regression.md`](../security/idor-regression.md) regression checklist mevcut.
- Payment Query DB read path'leri **tenant/clinic scope açısından kabul edildi** (16C, 17C, integration/unit testler).
- **GetById:** Reader `TenantId + PaymentId` lookup; satır sonrası paylaşılan `TryGetClinicAccessFailureAsync` post-load clinic access check korunur.
- **Mevcut semantik korunur:**

| Senaryo | HTTP | Problem code |
|---|---|---|
| Assigned clinic | **200** | — |
| Unassigned clinic (same-tenant, non-tenant-wide) | **404** | `Payments.NotFound` |
| Cross-tenant payment ID | **404** | `Payments.NotFound` |
| Tenant-wide (Admin/Owner) cross-clinic | **200** | — |
| Active clinic mismatch | **404** | `Payments.NotFound` |
| `Payments.Read` permission yok | **403** | policy failure |

**Bilinen gap:** HTTP integration testleri varsayılan `PaymentsGetByIdReadEnabled=false` ile Command baseline; **staging IDOR smoke (flag true)** zorunlu.

---

## Performance decision

CQRS-18A özeti ([`cqrs-18a-payment-read-model-performance-audit.md`](cqrs-18a-payment-read-model-performance-audit.md)):

| Konu | Karar |
|---|---|
| Clinic-scoped yüzeyler (list, dashboard recent, clinic report/export, GetById) | Mevcut indexler **yeterli** |
| Tenant-wide report/export | **Orta seviye index riski** — Query DB'de `(TenantId, PaidAtUtc, PaymentId)` yok; Command DB'de karşılık var |
| Şu an migration/index | **Yok** — ölçüm olmadan index ekleme |
| CQRS-18B | **Şu an gerekli değil** — ölçüm sonrası koşullu |
| CQRS-18C export streaming | Teknik borç; **rollout blokörü değil** |
| Payment CQRS rollout vs CQRS-18 | **Rollout CQRS-18 beklemeden yapılabilir** |

---

## Deferred technical debt

| Borç | Faz / not | Rollout blokörü? |
|---|---|---|
| CQRS-18B — optional tenant-wide `(TenantId, PaidAtUtc DESC, PaymentId DESC)` index | Ölçüm sonrası migration | **Hayır** |
| CQRS-18C — export streaming / paged export design | Memory-based export, ClosedXML | **Hayır** (50k cap) |
| Client/Pet search lookup freshness monitoring | Parity/health yakalamaz; operasyonel izleme | **Hayır** (gate: client/pet backfill) |
| Export memory-based writer, 50k cap | 15H/18A documented | **Hayır** |
| Tenant-wide report/export p95 / logical reads izleme | Production rollout sonrası | **Hayır** |

---

## What not to do yet

- **CQRS-18B'yi ölçüm olmadan başlatma** — staging/production execution plan + logical reads olmadan index migration açma.
- **CQRS-18C'yi rollout stabil olmadan başlatma** — export streaming design, operasyonel baseline olmadan erken.
- **CQRS-19 next module migration'a production rollout/monitoring netleşmeden geçme** — Payment hattı operasyonel olarak doğrulanmadan yeni modül CQRS seçimi yapma.
- **Yeni Query DB path açarken IDOR/scope audit yapmadan ilerleme** — her yeni read yüzey scope + auth parity gerektirir.
- **Backfill/parity gate atlamadan read flag açma** — no-fallback policy false-negative üretir.
- **Projection kapalıyken read flag'leri açık bırakma** — drift/lag artar.

---

## When to open CQRS-18B

Optional tenant-wide report/export index migration fazını aç:

- Staging veya production'da **tenant-wide report/export p95** kötüleşirse
- **Logical reads** clinic-scoped baseline'ın belirgin üzerindeyse (operasyonel eşik)
- Execution plan **clustered/index scan** gösteriyorsa (seek yok)
- **Büyük tenant** (100k+ payment) report/export belirgin yavaşsa
- CQRS-18A §Measurement plan tamamlandıysa ve sonuç index gap'i doğruluyorsa

**Önerilen index:** `(TenantId, PaidAtUtc DESC, PaymentId DESC)` on `PaymentReadModels`.

---

## When to open CQRS-18C

Export streaming design fazını aç:

- Export **kullanım hacmi** production'da artarsa
- **50k cap** operasyonel olarak yetersiz kalırsa (iş kuralı değişikliği ayrı karar)
- Export endpoint'lerinde **memory pressure** / OOM / yüksek GC gözlemlenirse
- XLSX (ClosedXML) peak memory endişe verici profil gösterirse
- **Payment rollout sonrası 2–4 hafta** izleme tamamlandıysa ve stabil baseline varsa

---

## When to open CQRS-19

Next module CQRS selection/migration fazını aç:

- **Payment rollout tamamlandıysa** (staging → production kademeli flag smoke)
- Production/staging **monitoring stabil** (health drift, projection lag, error rate, latency)
- **No-fallback read path** davranışı operasyonel olarak doğrulandıysa (false-negative olay yok veya yönetildi)
- **IDOR regression** checklist korunuyorsa (yeni write path'lerde clinic assignment)
- CQRS-18B/C **acil blokör değilse** veya ayrı track'te yönetiliyorsa

---

## Final decision

| Soru | Karar |
|---|---|
| **Payment CQRS development hattı kapanabilir mi?** | **Evet** — kod, test, runbook, release checklist ve performance audit tamam; kalan iş operasyon + opsiyonel borç |
| **Production rollout için hazır mı?** | **Evet — kod açısından.** Operasyonel Go/No-Go (17C) staging'de geçilmeden production flag açılmamalı |
| **CQRS-18B hemen açılmalı mı?** | **Hayır** — ölçüm sonrası koşullu |
| **CQRS-18C hemen açılmalı mı?** | **Hayır** — rollout stabilizasyonu + izleme sonrası |
| **CQRS-19 hemen açılmalı mı?** | **Hayır** — payment production rollout ve monitoring netleşmeden |
| **Sonraki önerilen çalışma** | **(1)** CQRS-17A/17C operasyonel rollout (staging → production) · **(2)** Production izleme (health, drift, latency, tenant-wide report/export) · **(3)** İzleme sonuçlarına göre CQRS-18B ve/veya 18C · **(4)** Payment rollout stabil olduktan sonra CQRS-19 |

**Handoff özeti:** Payment CQRS read-model hattı geliştirme tarafında **kapalı** kabul edilir. Devralan ekip operasyon runbook'u ([17A](cqrs-17a-payment-read-model-production-rollout-runbook.md)) ve release checklist'i ([17C](cqrs-17c-payment-cqrs-release-readiness.md)) ile production flag açma sürecini yürütür. Performans ve export borcu ayrı faz kapılarıyla takip edilir ([18A](cqrs-18a-payment-read-model-performance-audit.md)).

**CQRS-18Z:** Commit atılmadı (kullanıcı talimatı).
