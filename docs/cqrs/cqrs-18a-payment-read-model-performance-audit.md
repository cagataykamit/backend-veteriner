# CQRS-18A — Payment Read-Model Performance Audit

**Tür:** Performance / index / query risk audit. **Production kod, test, appsettings, migration, feature flag değişmedi.**

**Ön durum (tamamlanan fazlar):**

| Faz | Durum | Commit |
|---|---|---|
| CQRS-15O — Payment search rollout readiness | Tamamlandı | `7191426` |
| CQRS-16A/B/C — Payment GetById Query DB route + rollout readiness | Tamamlandı | `70008aa` · `6f72702` · `243c8ec` |
| CQRS-17A — Payment read-model production rollout runbook | Tamamlandı | `a3e8ae8` |
| CQRS-17C — Payment CQRS release readiness checklist | Tamamlandı | `4005f5b` |
| IDOR / clinic isolation roadmap | **Kapalı** | [`idor-regression.md`](../security/idor-regression.md) |

**İlgili dokümanlar:** [`cqrs-17c-payment-cqrs-release-readiness.md`](cqrs-17c-payment-cqrs-release-readiness.md) · [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](cqrs-17a-payment-read-model-production-rollout-runbook.md) · [`cqrs-14b-payment-read-model-schema.md`](cqrs-14b-payment-read-model-schema.md) · [`cqrs-14f-payment-list-backfill-parity-health.md`](cqrs-14f-payment-list-backfill-parity-health.md) · [`cqrs-15f-payment-report-export-read-model-strategy.md`](cqrs-15f-payment-report-export-read-model-strategy.md) · [`cqrs-15h-payment-export-safety-performance-audit.md`](cqrs-15h-payment-export-safety-performance-audit.md) · [`cqrs-15o-payment-search-read-model-rollout-readiness.md`](cqrs-15o-payment-search-read-model-rollout-readiness.md)

**Git doğrulama (2026-06-23, CQRS-18A başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** |
| CQRS-17C commit | `4005f5b` — `docs(cqrs): add payment cqrs release readiness` |
| CQRS-17A commit | `a3e8ae8` — `docs(cqrs): add payment read model rollout runbook` |
| CQRS-16C commit | `243c8ec` — `docs(cqrs): add payment get by id rollout readiness` |
| CQRS-16B commit | `6f72702` — `feat(cqrs): route payment get by id through read model` |
| CQRS-15O commit | `7191426` — `test(cqrs): verify payment search rollout readiness` |

---

## Current status

- **Payment CQRS read-model hattı release-ready** (CQRS-17C onaylı); bu audit implementasyon değildir.
- **Mevcut index seti clinic-scoped yüzeyler için yeterli** kabul edilir; tenant-wide report/export için **ölçüm sonrası** opsiyonel index değerlendirmesi gerekir.
- **Export memory-based tasarım** bilinen teknik borçtur; **production rollout blokörü değildir** (50k cap + mevcut guard).
- **CQRS-18B (index)** ve **CQRS-18C (export streaming design)** rollout **öncesinde zorunlu değildir**.

| Alan | Audit sonucu |
|---|---|
| Query DB tablo | `PaymentReadModels` — 4 nonclustered index + PK (`PaymentReadModelConfiguration`) |
| Clinic-scoped query yüzeyleri | Index uyumu **yüksek** |
| Tenant-wide report/export | Index gap **orta risk** — Command DB'de `(TenantId, PaidAtUtc)` var, Query DB'de yok |
| Search lookup freshness | **Operasyonel risk orta** — parity/health yakalamaz |
| Backfill/parity/health | Rollout gate için yeterli; büyük tenant'ta süre maliyeti izlenmeli |
| Production rollout vs CQRS-18 | **Rollout CQRS-18 beklemeden yapılabilir** |

---

## Existing query surfaces

| # | Yüzey | Endpoint | Reader | Flag |
|---|---|---|---|---|
| 1 | Payment list | `GET /api/v1/payments` | `PaymentsListReadModelReader` | `PaymentsListReadEnabled` |
| 2 | Dashboard recent payments | `GET /api/v1/dashboard/finance-summary` (recent) | `DashboardRecentPaymentsReadModelReader` | `DashboardRecentPaymentsReadEnabled` |
| 3 | Client payment summary | `GET /api/v1/clients/{id}/payment-summary` | `ClientPaymentSummaryReadModelReader` | `ClientPaymentSummaryReadEnabled` |
| 4 | Payment report JSON | `GET /api/v1/reports/payments` | `PaymentsReportReadModelReader` | `PaymentsReportReadEnabled` |
| 5 | Payment export CSV/XLSX | `GET /api/v1/reports/payments/export` · `export-xlsx` | `PaymentsReportExportReadModelReader` | `PaymentsReportExportReadEnabled` |
| 6 | Payment GetById | `GET /api/v1/payments/{id}` | `PaymentGetByIdReadModelReader` | `PaymentsGetByIdReadEnabled` |
| 7 | Payment search (list/report/export) | Yukarıdaki yüzeyler + search param | `PaymentsListQuerySearchResolution` + lookup reader'lar | İlgili read flag (Query path) |
| 8 | Backfill / parity / health | DbMigrator + `/health/ready` | `PaymentReadModelBackfillService` · `PaymentReadModelParityReader` · `PaymentReadModelHealthReader` | Altyapı |

**Scope guard (ortak):** Multi-clinic ClinicAdmin (`AccessibleClinicIds` dolu) → Command DB path. Query path yalnız single-clinic veya tenant-wide (`ClinicId` filtresi yok).

---

## Existing index coverage

### `PaymentReadModels` üzerindeki indexler

Kaynak: `PaymentReadModelConfiguration` · migration `20260621074842_AddPaymentReadModel`

| Index | Kolonlar | Sıralama | Amaç (14B) |
|---|---|---|---|
| `PK_PaymentReadModels` | `PaymentId` | — | GetById PK seek |
| `IX_PaymentReadModels_TenantId_ClinicId_PaidAtUtc_PaymentId` | `TenantId, ClinicId, PaidAtUtc, PaymentId` | PaidAtUtc ↓, PaymentId ↓ | List, dashboard recent, clinic-scoped report/export |
| `IX_PaymentReadModels_TenantId_ClientId_PaidAtUtc` | `TenantId, ClientId, PaidAtUtc` | PaidAtUtc ↓ | Client payment summary |
| `IX_PaymentReadModels_TenantId_ClinicId_ClientNameNormalized` | `TenantId, ClinicId, ClientNameNormalized` | ASC | Search — direct client name LIKE |
| `IX_PaymentReadModels_TenantId_ClinicId_PetNameNormalized` | `TenantId, ClinicId, PetNameNormalized` | ASC | Search — direct pet name LIKE |

**Bilinçli olarak eklenmeyenler (14B):**

- `NotesNormalized` — 4000 char; LIKE maliyeti yüksek
- `Currency` tek başına — düşük seçicilik
- `(TenantId, PaidAtUtc, PaymentId)` — tenant-wide report için **henüz eklenmedi** (15F/15H önerisi)

### Command DB karşılaştırması

Command `Payments` tablosunda `IX_Payments_TenantId_PaidAtUtc` mevcut (`PaymentConfiguration`). Tenant-wide Admin/Owner report/export Query path'te bu index'in Query DB karşılığı **yoktur**.

### Yüzey → index eşlemesi (özet)

| Yüzey | Beklenen index kullanımı | Yeterlilik |
|---|---|---|
| List (single clinic) | `TenantId+ClinicId+PaidAtUtc+PaymentId` | **Yeterli** |
| Dashboard recent (Take=10) | Aynı index, TOP N | **Yeterli** |
| Client summary (tenant-wide) | `TenantId+ClientId+PaidAtUtc` | **Yeterli** |
| Client summary (clinic-scoped) | Client index + residual `ClinicId` filter | **Kısmen yeterli** (yüksek hacimde ek index faydalı olabilir; blokör değil) |
| Report/export (single clinic + date range) | Clinic index + `PaidAtUtc` range seek | **Yeterli** |
| Report/export (tenant-wide + date range) | TenantId equality + PaidAtUtc range; **ClinicId yok** | **Kısmen yeterli / risk** — tenant geneli scan veya suboptimal plan |
| GetById | PK seek (`PaymentId`) + `TenantId` residual | **Yeterli** |
| Search (direct fields) | Name normalized indexler (prefix/equality); Notes/Currency **index yok** | **Kısmen** — Notes/Currency LIKE scan beklenir |
| Search (lookup IDs) | `ClientId IN` / `PetId IN` — clinic index üzerinde OR filtresi | **Değişken** — lookup ID sayısına bağlı |

---

## Surface-by-surface analysis

### 1. Payment list

| Alan | Değer |
|---|---|
| **Reader** | `PaymentsListReadModelReader` |
| **Filtreler** | `TenantId`, `ClinicId` (zorunlu); opsiyonel `ClientId`, `PetId`, `Method`, `PaidFromUtc`, `PaidToUtc`, search OR |
| **Sort** | `PaidAtUtc DESC`, `PaymentId DESC` |
| **Pagination** | `Page` / `PageSize` (1–200, default 20) |
| **Query shape** | `COUNT(*)` + sayfalı `SELECT` (2 round-trip) |
| **Index ihtiyacı** | Clinic composite index **yeterli** |
| **Worst-case** | Search dolu + Notes/Currency LIKE → clinic kapsamında scan; multi-clinic → Command path (Query değil) |
| **Risk** | **Düşük** (clinic-scoped, capped page) |

### 2. Dashboard recent payments

| Alan | Değer |
|---|---|
| **Reader** | `DashboardRecentPaymentsReadModelReader` |
| **Filtreler** | `TenantId`, `ClinicId` |
| **Sort / cap** | `PaidAtUtc DESC`, `PaymentId DESC`; `Take = 10` (`DashboardFinanceSummaryConstants.RecentPaymentsTake`) |
| **Query shape** | Tek `SELECT TOP 10` |
| **Index ihtiyacı** | Clinic composite index **yeterli** |
| **Worst-case** | Yok (sabit küçük cap) |
| **Risk** | **Düşük** |

### 3. Client payment summary

| Alan | Değer |
|---|---|
| **Reader** | `ClientPaymentSummaryReadModelReader` |
| **Filtreler** | `TenantId`, `ClientId`; opsiyonel `ClinicId` veya `AccessibleClinicIds` (tenant-wide admin) |
| **Sort / cap** | Recent: `PaidAtUtc DESC`, `PaymentId DESC`, `RecentTake = 10` |
| **Query shape** | `COUNT` + `MAX(PaidAtUtc)` + `GROUP BY Currency SUM` + recent `TOP 10` (4 round-trip; satırlar belleğe toplu alınmaz) |
| **Index ihtiyacı** | Tenant-wide: `TenantId+ClientId+PaidAtUtc` **yeterli**. Clinic-scoped: aynı index + `ClinicId` residual filter |
| **Worst-case** | Çok ödemeli tek client + clinic filter → index seek sonrası yüksek residual filter |
| **Risk** | **Düşük–orta** (blokör değil; 15C/15E ile uyumlu) |

### 4. Payment report JSON

| Alan | Değer |
|---|---|
| **Reader** | `PaymentsReportReadModelReader` |
| **Filtreler** | `TenantId`; opsiyonel `ClinicId`; `ClientId`, `PetId`, `Method`; zorunlu `FromUtc`–`ToUtc` (max 93 gün); search OR |
| **Sort / pagination** | `PaidAtUtc DESC`, `PaymentId DESC`; `PageSize` ≤ 200 |
| **Query shape** | `COUNT` + `SUM(Amount)` + sayfalı items (3 round-trip) |
| **Index ihtiyacı** | Single clinic: clinic index **yeterli**. Tenant-wide: **tenant-level PaidAtUtc index eksik** |
| **Worst-case** | Tenant-wide Admin, 93 gün, büyük tenant → geniş `TenantId` scan + date filter + aggregate |
| **Risk** | **Orta** (tenant-wide); **Düşük** (single clinic) |

### 5. Payment export CSV/XLSX

| Alan | Değer |
|---|---|
| **Reader** | `PaymentsReportExportReadModelReader` |
| **Filtreler** | Report JSON ile aynı |
| **Sort / cap** | `PaidAtUtc DESC`, `PaymentId DESC`; **sayfalama yok**; max **50.000** satır (`PaymentsReportConstants.MaxExportRows`) |
| **Query shape** | `COUNT` + tam liste `ToListAsync` (2 round-trip; tüm satırlar belleğe) |
| **Index ihtiyacı** | Report JSON ile aynı |
| **Worst-case** | 50k satır tenant-wide export → DB scan + 50k entity + DTO + writer bellek |
| **Risk** | **Yüksek** (bellek); **Orta** (DB — tenant-wide index gap) |

### 6. Payment GetById

| Alan | Değer |
|---|---|
| **Reader** | `PaymentGetByIdReadModelReader` |
| **Filtreler** | `TenantId` + `PaymentId` |
| **Query shape** | Tek satır `FirstOrDefault` — PK seek |
| **Index ihtiyacı** | PK **yeterli** |
| **Worst-case** | Yok (O(1) satır) |
| **Risk** | **Düşük** |

### 7. Payment search (Query path)

| Alan | Değer |
|---|---|
| **Resolution** | `PaymentsListQuerySearchResolution` → `IClientReadModelLookupReader` + `IPetReadModelLookupReader` |
| **Payment filter** | OR: normalized name LIKE, Notes/Currency LIKE, `ClientId IN`, `PetId IN` |
| **Lookup queries** | Client: `ClientReadModels` tenant text search (FullName, Email, Phone, PhoneNormalized). Pet: `PetReadModels` text fields |
| **Index ihtiyacı** | Payment: name indexler; Notes/Currency unindexed. Lookup: client/pet kendi index setleri |
| **Worst-case** | Geniş pattern + büyük lookup ID kümesi + OR → plan karmaşıklığı; stale lookup → **eksik sonuç** (false negative) |
| **Risk** | **Orta** (perf + freshness) |

### 8. Backfill / parity

| Alan | Değer |
|---|---|
| **Backfill** | Command `Payments` batch (500), join Clients/Pets/Clinics, Query upsert; global count parity |
| **Parity** | `GetClinicParityAsync`: clinic-scoped count + recent 50 sample + ordering |
| **Health** | Global `LongCount` Command vs Query; drift gate `PaymentsListReadEnabled` ile |
| **Worst-case** | Milyon+ payment tenant → backfill süresi; parity per-clinic 4+ query + Command join |
| **Risk** | **Operasyonel orta** (süre); rollout gate için yeterli |

---

## Tenant-wide report/export risk

### Problem

Tenant-wide Admin/Owner report ve export Query path'te `ClinicId` filtresi **uygulanmaz** (`PaymentsReportReadModelReader` / `PaymentsReportExportReadModelReader`: yalnız `TenantId` + date range + opsiyonel filtreler).

Mevcut index `(TenantId, ClinicId, PaidAtUtc, PaymentId)` bu sorgu şeklinde **leading column ClinicId** gerektirir; tenant-wide sorguda optimizer:

- `TenantId` equality ile tüm tenant satırlarını tarayabilir, veya
- Farklı plan seçerek yüksek **logical reads** üretebilir.

Command DB'de karşılık: `IX_Payments_TenantId_PaidAtUtc` — tenant-wide date-range sorguları için daha uygun.

### Önerilen opsiyonel index (CQRS-18B adayı)

```text
(TenantId, PaidAtUtc DESC, PaymentId DESC)
```

**Bu fazda eklenmez.** Karar ölçüme bağlıdır (aşağıdaki §Measurement plan).

### Azaltma (rollout öncesi/ sırasında)

- Kademeli flag açma (17A sırası)
- Staging'de büyük tenant senaryosu smoke
- Production'da tenant-wide report/export latency + logical reads izleme
- Gerekirse operasyonel: tarih aralığını daraltma, export yerine sayfalı JSON

---

## Search lookup freshness risk

### Mekanizma

Query path search, email/phone/species/breed eşleşmeleri için **Client/Pet lookup read-model**'lerine bağımlıdır (`PaymentsListQuerySearchResolution`). Bu alanlar `PaymentReadModels`'e denormalize edilmemiştir (15K/15O tasarımı).

### Stale lookup etkisi

| Durum | Sonuç |
|---|---|
| Client/Pet projection gecikmesi | Email/phone/species/breed ile arama **eksik satır** dönebilir (false negative) |
| Direct alanlar (clientName, petName, notes, currency) | Payment projection'dan gelir; lookup'tan bağımsız |
| Payment count parity InSync | Lookup freshness **garanti etmez** |
| Payment parity row sample | Lookup alanlarını **kapsamaz** |
| Health (`payment-projection`) | Payment count drift; client/pet lookup drift **yok** |

### Yakalama

| Mekanizma | Lookup stale yakalar mı? |
|---|---|
| `GetClinicParityAsync` | **Hayır** (payment count/sample/ordering) |
| `PaymentProjectionHealthEvaluator` | **Hayır** (global payment count drift) |
| Client/Pet projection parity/health (ayrı hat) | **Evet** (rollout gate — 17C/17A) |
| Staging search smoke (direct + lookup alanları) | **Kısmen** (anlık doğrulama) |

### Operasyonel risk

**Orta.** Rollout öncesi client/pet backfill/parity InSync zorunludur. Production'da client/pet projection lag izlenmeli; lookup stale şüphesinde payment search false-negative olabilir (veri kaybı değil, eksik liste).

---

## Export memory risk

### Mevcut tasarım

- `PaymentsReportExportPipeline` → `PaymentsReportExportReadModelReader.GetExportAsync` → tüm eşleşen satırlar `ToListAsync`
- Cap: **50.000** satır (`ReportsSharedLimits.MaxExportRows`); aşımda `Payments.ReportExportTooManyRows`
- CSV: `StringBuilder` + UTF-8 BOM `byte[]`
- XLSX: ClosedXML workbook bellekte
- **Streaming yok** (15H onaylı)

### Production blocker mı?

**Hayır** — bilinçli tasarım trade-off:

- 50k hard cap operasyonel üst sınır
- Query path hydration lookup kaldırır ama **50k DTO + writer bellek yükü kalır**
- Mevcut Command path ile aynı bellek profili sınıfı
- CQRS-17C rollout onayı bu borcu **kabul edilmiş** out-of-scope olarak işaretler

### CQRS-18C ne zaman açılmalı?

| Tetikleyici | Öncelik |
|---|---|
| Production rollout sonrası export endpoint memory/latency / OOM gözlemi | **Yüksek** |
| Staging'de 50k XLSX export bellek profili endişe verici | **Orta** |
| Proaktif iyileştirme (rollout stabil olduktan sonra) | **Normal** |

**Öneri:** CQRS-18C design fazını **production kademeli rollout tamamlandıktan ve ilk 2–4 haftalık izleme verisi toplandıktan sonra** aç. Rollout **öncesinde** zorunlu değil.

---

## Backfill and parity performance

### Backfill (`PaymentReadModelBackfillService`)

| Konu | Değerlendirme |
|---|---|
| **Idempotency** | PK upsert + stale guard — **güvenli tekrar** |
| **Batch** | Default 500; offset pagination `OrderBy TenantId, Id` |
| **Büyük veri** | O(n) Command scan; tenant başına `--tenant` ile bölünebilir |
| **Join maliyeti** | Batch başına Clients + Clinics + Pets lookup (Command DB) |
| **Transaction** | Batch başına Query transaction — uzun lock riski düşük |
| **Parity gate** | Global count; mismatch → exit code **2** |

**Rollout izleme:** Backfill süresi (`DurationMs` log), büyük tenant'ta maintenance window planla.

### Parity (`PaymentReadModelParityReader`)

| Konu | Değerlendirme |
|---|---|
| **Kapsam** | Tenant + clinic; recent sample **50** (`PaymentReadModelParityDefaults.RecentSampleSize`) |
| **Maliyet** | 2× count + 2× recent TOP 50 + Command side client/pet join (recent sample) |
| **Sıklık** | Rollout gate + operasyonel spot-check; sürekli polling değil |
| **Eksik** | Lookup freshness, tenant-wide report perf, field rename drift (count korunur) |

### Health (`PaymentReadModelHealthReader` + `PaymentProjectionHealthEvaluator`)

| Konu | Değerlendirme |
|---|---|
| **Maliyet** | 2× global `LongCount` (ready check per poll) |
| **Drift gate** | `PaymentsListReadEnabled=true` + drift → **Unhealthy** |
| **Not** | Yalnız `PaymentsListReadEnabled` health sinyalinde; diğer read flag'ler açıkken de count drift kritik |

**Production rollout sırasında izle:** `readModelCountDrift`, `pendingCount`, `deadLetterCount`, export/report p95 latency, Query DB CPU/logical reads.

---

## Measurement plan

Repo'da hazır SQL perf scripti **yoktur**. Aşağıdaki ölçümler **staging/production Query DB** üzerinde execution plan, duration ve logical reads ile yapılmalıdır.

### Ölçülecek query'ler

| # | Yüzey | Senaryo |
|---|---|---|
| 1 | List | Single clinic, search boş, page 1, pageSize 200 |
| 2 | List | Single clinic, search dolu (client name + lookup email) |
| 3 | Dashboard recent | Single clinic, TOP 10 |
| 4 | Client summary | Tenant-wide admin, yüksek ödemeli client |
| 5 | Client summary | Clinic-scoped, aynı client |
| 6 | Report JSON | Single clinic, 93 gün, page 1 |
| 7 | Report JSON | **Tenant-wide**, 93 gün, page 1 |
| 8 | Report JSON | Tenant-wide + search dolu |
| 9 | Export | Single clinic, 93 gün, ~10k satır |
| 10 | Export | **Tenant-wide**, 93 gün, ~10k–50k satır |
| 11 | GetById | Random PK, cold/hot cache |
| 12 | Search lookup | Client + pet lookup text search (tenant-wide pattern) |
| 13 | Backfill | `--tenant` büyük tenant, batch 500 süre |
| 14 | Health | Global count parity (2× COUNT_BIG) |

### Parametre matrisi

| Boyut | Değerler |
|---|---|
| Scope | Single clinic vs **tenant-wide** vs multi-clinic (Command baseline) |
| Date range | 7 gün vs 93 gün (max) |
| Search | Boş vs direct field vs lookup field (email/phone) |
| Volume | Küçük tenant (&lt;10k payment) vs **büyük tenant** (&gt;100k–500k+) |
| Pagination | pageSize 20 vs 200 vs export full (≤50k) |

### SQL Server ölçüm yöntemi

DB tarafında **execution plan**, **duration** ve **logical reads** ile ölçülmeli:

```sql
-- Örnek: tenant-wide report count shape (parametreler operatör tarafından değiştirilir)
SET STATISTICS IO, TIME ON;

SELECT COUNT_BIG(*)
FROM PaymentReadModels
WHERE TenantId = @TenantId
  AND PaidAtUtc >= @FromUtc
  AND PaidAtUtc <= @ToUtc;

-- Actual execution plan'da index seek vs scan doğrulanmalı
SET STATISTICS IO, TIME OFF;
```

**Karar eşiği (öneri — operasyonel kalibrasyon gerekir):**

| Metrik | Tenant-wide report/export için CQRS-18B tetikleyici (rehber) |
|---|---|
| Logical reads | Clinic-scoped baseline'ın **10× üzeri** aynı date range |
| Duration (p95) | &gt; 2–3 s (API SLA'ya göre ayarla) |
| Plan | Clustered/index **scan** + yüksek row estimate; `(TenantId, PaidAtUtc, PaymentId)` seek yok |

### Staging smoke (repo komutları)

```powershell
dotnet build --no-restore

dotnet test tests/Backend.Veteriner.Application.Tests --no-restore --filter "FullyQualifiedName~PaymentList|FullyQualifiedName~PaymentsList|FullyQualifiedName~PaymentsReport|FullyQualifiedName~PaymentsReportExport|FullyQualifiedName~GetPaymentById|FullyQualifiedName~PaymentGetById|FullyQualifiedName~PaymentSearchQueryReadFlagIsolation"

dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~PaymentListRolloutAcceptance|FullyQualifiedName~PaymentReadModelParity|FullyQualifiedName~PaymentDetailIdorIntegrationTests|FullyQualifiedName~PaymentReadModelReaderIntegrationTests"
```

Integration testler **fonksiyonel** doğrulama sağlar; production-scale perf ölçümü **değildir**.

---

## Optional index decision

### `(TenantId, PaidAtUtc DESC, PaymentId DESC)` — tenant-wide report/export

| Soru | Karar |
|---|---|
| Şu an eklenmeli mi? | **Hayır** |
| CQRS-18B gerekli mi? | **Koşullu — ölçüm sonrası** |
| Hangi ölçüm sonucuna bağlı? | Tenant-wide report/export staging veya production'da yüksek logical reads / scan plan / p95 latency (§Measurement plan eşikleri) |
| Clinic-scoped yüzeyler blokör mü? | **Hayır** — mevcut index yeterli |
| `(TenantId, ClinicId, ClientId, PaidAtUtc)` | **Opsiyonel düşük öncelik** — yalnız clinic-scoped client summary yoğun müşteride ölçüm sonrası |

### Diğer index adayları (düşük öncelik)

| Index | Karar |
|---|---|
| `NotesNormalized` | **Eklenmemeli** (14B gerekçesi geçerli) |
| `Currency` | **Gerekmez** |
| Multi-clinic `IN (...)` | **Pratik değil** — Command fallback bilinçli |

---

## Recommendations

1. **Rollout:** CQRS-17A/17C runbook'una göre kademeli flag aç; CQRS-18B/C **beklemeden** devam edilebilir.
2. **Staging ölçüm:** Büyük tenant fixture ile tenant-wide report (93 gün) + export (10k+) execution plan kaydı al.
3. **Production izleme:** Query path log hacmi, endpoint latency, `payment-projection` health drift, export memory (process working set).
4. **Lookup gate:** Client/Pet projection parity rollout öncesi zorunlu; production'da ayrı client/pet health izle.
5. **CQRS-18B:** Yalnız staging/production ölçümü tenant-wide scan doğrulursa index migration fazı aç.
6. **CQRS-18C:** Rollout stabilizasyonu + export bellek gözlemi sonrası streaming/paged export design.
7. **Operasyonel limit:** Export 50k cap korun; tenant-wide büyük export için JSON sayfalama alternatifini dokümante et (17A Known risks ile uyumlu).

---

## Final decision

| Soru | Karar |
|---|---|
| **Mevcut indexler yeterli mi?** | **Clinic-scoped yüzeyler için evet.** Tenant-wide report/export için **kısmen** — gap bilinen, orta risk |
| **Tenant-wide report/export index riski var mı?** | **Evet — orta.** Command DB'de tenant index var, Query DB'de yok; ölçüm öncesi rollout blokörü **değil** |
| **CQRS-18B (optional index) gerekli mi?** | **Şu an hayır.** **Ölçüm sonrası gerekli olabilir** — tenant-wide p95/logical reads eşik aşımı |
| **Şu an index/migration eklenmeli mi?** | **Hayır** |
| **CQRS-18C export streaming design gerekli mi?** | **Tasarım fazı gerekli** (teknik borç), ancak **rollout öncesi blokör değil**; stabil rollout + izleme sonrası açılmalı |
| **Payment CQRS rollout CQRS-18 beklemeden yapılabilir mi?** | **Evet** — 17C release readiness geçerli; CQRS-18 post-rollout perf/tech-debt hattıdır |

**CQRS-18A sonraki adım:** Production/staging ölçüm planını uygula → sonuçlara göre CQRS-18B (index) ve/veya CQRS-18C (export streaming design) faz kararı.

**CQRS-18A:** Commit atılmadı (kullanıcı talimatı).
