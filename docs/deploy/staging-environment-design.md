# Staging Environment Design

**Tür:** Deploy environment design audit (DEPLOY-0). **Production kod, test, config, migration değişmedi.**

**Amaç:** Payment CQRS rollout'u production'a taşımadan önce staging ortamının minimum altyapı, config ve operasyonel gereksinimlerini netleştirmek.

**İlgili dokümanlar:** [`cqrs-18z-payment-cqrs-closure-handoff.md`](../cqrs/cqrs-18z-payment-cqrs-closure-handoff.md) · [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md) · [`cqrs-17c-payment-cqrs-release-readiness.md`](../cqrs/cqrs-17c-payment-cqrs-release-readiness.md) · [`cqrs-11a-staging-rollout.md`](../cqrs/cqrs-11a-staging-rollout.md) · [`README.md`](../../README.md)

**Repo bulguları (2026-06-23):**

| Bileşen | Durum |
|---|---|
| `appsettings.Staging.json` | Mevcut — connection string boş; güvenli CQRS default'ları |
| `Dockerfile` / `docker-compose` | **Yok** |
| Dedicated staging IaC / host tanımı | **Yok** (repo dışı operasyon kararı) |
| DbMigrator | Mevcut — manuel migration/backfill CLI |
| API auto-migrate | **Yok** — startup migration/seed çalıştırmaz |
| Health endpoints | `/health/live`, `/health/ready`, `/health` |
| CQRS rollout tooling | `tests/load/tools/Invoke-CqrsStagedRollout.ps1` (appointment odaklı; payment için 17A runbook esas) |

---

## Required services

Staging için **minimum** çalışma zamanı bileşenleri:

| # | Servis | Rol | Zorunlu? |
|---|---|---|---|
| 1 | **Microsoft SQL Server** | Command DB + Query DB barındırır | **Evet** |
| 2 | **Backend.Veteriner.Api** | HTTP API, outbox worker, projection hosted service'ler (API process içi) | **Evet** |
| 3 | **DbMigrator** (CLI) | Şema migration + backfill (deploy adımı; sürekli servis değil) | **Evet** (deploy sırasında) |
| 4 | **Secret/config store** | Connection string, JWT key, SMTP vb. (repo dışı) | **Evet** |
| 5 | **Reverse proxy / TLS** | IIS, nginx veya cloud LB (HTTPS) | Önerilir |
| 6 | **Log depolama** | Serilog file sink (Production şablonu: `/var/log/...`) veya platform log | Önerilir |
| 7 | **SMTP** | Reminder/mail özellikleri kullanılacaksa | Opsiyonel (Payment CQRS için zorunlu değil) |

**Not:** Projection worker'lar (`PaymentProjectionHostedService`, client/appointment/pet projection) **ayrı worker deployment değildir** — API process içinde `IHostedService` olarak çalışır. Repo'da payment projection için `ClaimingEnabled` default **false**; staging'de başlangıçta **tek API instance** tercih edilir ([`cqrs-11a-staging-rollout.md`](../cqrs/cqrs-11a-staging-rollout.md)).

Payment CQRS search Query path için **Client + Pet** lookup read-model'lerine bağımlıdır — staging'de ilgili projection/backfill gate'leri geçilmelidir (aşağıda §Payment CQRS rollout readiness).

---

## Database topology

### Command DB vs Query DB ayrımı

| Rol | EF Context | Connection key | İçerik (özet) |
|---|---|---|---|
| **Command DB** | `AppDbContext` | `DefaultConnection` (tercih) veya `SqlServer` | `Payments`, outbox, operasyonel truth |
| **Query DB** | `QueryDbContext` | `QueryConnection` | `PaymentReadModels`, client/pet read-model, finance read-model |

Kaynak: `DependencyInjection.cs` — Command için `DefaultConnection` yoksa `SqlServer` fallback; Query için **`QueryConnection` zorunlu**.

Backfill servisleri (`PaymentReadModelBackfillService` vb.) **aynı catalog** tespit ederse hata verir — Command ve Query **farklı database adları** olmalıdır.

### Aynı SQL Server instance, iki ayrı database yeterli mi?

**Evet — başlangıç için yeterli ve repo'nun varsayılan desenidir.**

- Development: aynı sunucu, `VetinityCommandDb` + `VetinityQueryDb`
- Production şablonu: aynı `localhost` sunucu, iki farklı catalog
- Load test: `VetinityCommandDb_LoadTest` + `VetinityQueryDb_LoadTest`

Ayrı SQL Server instance veya ayrı managed DB **zorunlu değildir**; ölçek/operasyon ihtiyacına göre ileride ayrılabilir.

### Önerilen staging database adları (repo dışı konvansiyon)

CQRS-11A örneği:

```text
VetinityCommandDb_Staging
VetinityQueryDb_Staging
```

README production şablonu:

```text
VetinityCommandDb
VetinityQueryDb
```

Staging için `_Staging` suffix veya ayrı sunucu — **operasyon ekibi kararı**; repo sabit ad tanımlamaz (`appsettings.Staging.json` connection string'leri boş).

---

## Configuration and environment variables

### Connection string isimleri

| Key | Kullanım | Zorunlu |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Command DB (önerilen) | **Evet** (veya `SqlServer`) |
| `ConnectionStrings:SqlServer` | Command DB alternatif (Production şablonu) | `DefaultConnection` yoksa |
| `ConnectionStrings:QueryConnection` | Query DB | **Evet** |

**Staging notu:** `appsettings.Staging.json` `DefaultConnection` + `QueryConnection` kullanır (boş). Production şablonu yalnızca `SqlServer` + `QueryConnection` içerir — staging deploy'da **tercihen `DefaultConnection`** set edin veya `SqlServer` fallback'i bilinçli kullanın.

### Ortam

```text
ASPNETCORE_ENVIRONMENT=Staging
```

DbMigrator için aynı ortam:

```text
DOTNET_ENVIRONMENT=Staging
```

### Minimum staging environment variables (secret store / platform config)

```text
# Database
ConnectionStrings__DefaultConnection=Server=<host>;Database=VetinityCommandDb_Staging;User Id=...;Password=...;TrustServerCertificate=True;MultipleActiveResultSets=True
ConnectionStrings__QueryConnection=Server=<host>;Database=VetinityQueryDb_Staging;User Id=...;Password=...;TrustServerCertificate=True;MultipleActiveResultSets=True

# Auth (API çalışması için)
Jwt__Key=<staging-signing-key-min-32-chars>

# App
App__BaseUrl=https://staging-api.example.com
```

### Payment CQRS rollout sırasında override edilecek flag'ler

Flag değişikliği **process restart** gerektirir (`IOptions` startup bind).

```text
PaymentProjection__Enabled=true

# Kademeli (17A sırası — her biri ayrı deploy/restart + smoke)
QueryReadModels__PaymentsListReadEnabled=true
QueryReadModels__DashboardRecentPaymentsReadEnabled=true
QueryReadModels__ClientPaymentSummaryReadEnabled=true
QueryReadModels__PaymentsReportReadEnabled=true
QueryReadModels__PaymentsReportExportReadEnabled=true
QueryReadModels__PaymentsGetByIdReadEnabled=true
```

Search lookup bağımlılığı için (Query path):

```text
ClientProjection__Enabled=true
PetProjection__Enabled=true
```

Staging şablonunda `ClientProjection:Enabled=true`, **`PetProjection:Enabled=false`** — payment search rollout öncesi pet projection açılmalı + `backfill-pet-projections` çalıştırılmalı.

### Diğer config (API genel çalışması)

| Alan | Not |
|---|---|
| `Outbox:Enabled` | Base `appsettings.json` true; staging overlay'de yok |
| `RateLimiting:Enabled` | Staging'de **true** |
| `Serilog` | Staging overlay minimum level; base'den file/console devralınabilir |
| `Billing:*`, `Smtp:*` | Staging sandbox değerleri operasyon kararı |

**Secret kuralı:** JWT key, SQL parola, SMTP parola **repoda tutulmaz** ([`README.md`](../../README.md)).

---

## CQRS flags

### Staging başlangıç durumu (`appsettings.Staging.json`)

Payment CQRS rollout **öncesi** tüm payment flag'leri **false** olmalıdır — repo şablonu bunu sağlar:

| Config yolu | Staging default | Payment rollout başlangıcı |
|---|---|---|
| `PaymentProjection:Enabled` | **false** | **false** (Faz 0'da true yapılır) |
| `QueryReadModels:PaymentsListReadEnabled` | **false** | **false** |
| `QueryReadModels:DashboardRecentPaymentsReadEnabled` | **false** | **false** |
| `QueryReadModels:ClientPaymentSummaryReadEnabled` | **false** | **false** |
| `QueryReadModels:PaymentsReportReadEnabled` | **false** | **false** |
| `QueryReadModels:PaymentsReportExportReadEnabled` | **false** | **false** |
| `QueryReadModels:PaymentsGetByIdReadEnabled` | **false** | **false** |

Diğer `QueryReadModels` flag'leri staging'de de **false** (appointment/client/pet list query routing kapalı).

**İlk deploy:** Repo şablonu ile deploy → tüm payment read flag'ler kapalı → production davranışı korunur.

---

## Migration and backfill commands

### DbMigrator nasıl çalışır?

- Proje: `src/Backend.Veteriner.DbMigrator`
- Generic Host; `AddInfrastructure(configuration)` ile API ile **aynı** connection string çözümlemesi
- Development'ta User Secrets destekli; Staging'de ortam değişkenleri / platform secret
- Komut sonu exit code: **0** başarı, **1** hata, **2** parity mismatch (backfill komutları)

```powershell
$env:DOTNET_ENVIRONMENT = "Staging"
# veya deploy pipeline'da ASPNETCORE_ENVIRONMENT=Staging ile aynı appsettings.Staging.json yüklenir
```

### İlk staging kurulum sırası (Command + Query şema)

```powershell
$env:DOTNET_ENVIRONMENT = "Staging"

# 1) Command DB şema
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate

# 2) Query DB şema (PaymentReadModels dahil)
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query

# 3) İsteğe bağlı — ilk tenant/izin verisi
dotnet run --project src/Backend.Veteriner.DbMigrator -- seed
```

API startup **migration çalıştırmaz** — log: `API startup does not run EF migrations or database seeding`.

### Payment CQRS Faz 0 (read flag'ler kapalıyken)

```powershell
$env:DOTNET_ENVIRONMENT = "Staging"

# Client/Pet lookup (search bağımlılığı) — projection açık + backfill
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections

# Payment list read-model
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --batch-size 500
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --tenant <guid>
```

**Beklenen:** `Parity in-sync : True`, exit code **0**. Exit code **2** → flag açma.

**Önkoşul:** API'de `PaymentProjection:Enabled=true` + restart (outbox tüketimi); ardından backfill tekrar doğrulanabilir.

Opsiyonel finance tabloları (dashboard finance totals — payment list'ten bağımsız):

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-finance-projections
```

### Alternatif EF komutu (command DB only)

```powershell
dotnet ef database update --project src/Backend.Veteriner.Infrastructure --startup-project src/Backend.Veteriner.Api
```

Query DB için resmi yol **`migrate-query`** komutudur.

---

## Health checks

### Endpoint'ler

| Endpoint | Amaç |
|---|---|
| `GET /health/live` | Liveness — predicate yok (her zaman healthy) |
| `GET /health/ready` | Readiness — **tüm** registered check'ler |
| `GET /health` | Tam rapor |

Kaynak: `WebApplicationExtensions.cs`

### `/health/ready` kayıtlı check'ler

| Entry | Anlam |
|---|---|
| `sql` | Command DB bağlantısı |
| `query-sql` | Query DB bağlantı + **bekleyen migration yok** |
| `outbox` | Outbox dead-letter / pending |
| `appointment-projection` | Appointment projection kuyruk |
| `client-projection` | Client projection kuyruk |
| `pet-projection` | Pet projection kuyruk |
| `payment-projection` | Payment finance kuyruk + **PaymentReadModels count drift** |

### Payment CQRS doğrulama

```http
GET /health/ready
```

Beklenen (Faz 0 sonrası, read flag'ler kapalı):

- HTTP **200** (veya degraded/unhealthy durumda **503**)
- `payment-projection` entry **Healthy** (veya projection kapalıyken queue sinyalleri exposed)
- `readModelCountDrift = 0`, `readModelCountInSync = true` (sinyal hesaplandığında)
- `deadLetterCount = 0`
- `query-sql` **Healthy** — bekleyen Query migration yok

Read flag açıkken (`PaymentsListReadEnabled=true`) count drift → **Unhealthy** ([`PaymentProjectionHealthEvaluator`](../cqrs/cqrs-14f-payment-list-backfill-parity-health.md)).

Response JSON: `status`, `results.<name>.status`, `description`, `data` (PII-safe).

---

## Logging and observability

### Serilog (base `appsettings.json`)

- Console + rolling file (`logs/log-.ndjson`, compact JSON)
- Production şablonu: `/var/log/backend-veteriner/log-.txt`
- Staging overlay: minimum level override; WriteTo base'den devralınabilir

### Startup'ta aranacak satır

`CqrsStartupConfigurationLogger` — tek Information satırı:

```text
CQRS startup configuration. Environment=Staging ProjectionEnabled=... PaymentProjectionEnabled=... AppointmentsQueryReadEnabled=... DashboardQueryReadEnabled=... DashboardFinanceReadEnabled=... DashboardRecentPaymentsReadEnabled=... ClientsQueryReadEnabled=... PetsQueryReadEnabled=... SharedSearchLookupEnabled=... PaymentsSearchLookupEnabled=... PaymentsListReadEnabled=... ClientPaymentSummaryReadEnabled=... PaymentsReportReadEnabled=... PaymentsReportExportReadEnabled=... PaymentsGetByIdReadEnabled=... CommandDbCatalog=... QueryDbCatalog=...
```

**Doğrulama checklist:**

| Alan | İlk deploy | Payment Faz 0 | Her read flag adımı |
|---|---|---|---|
| `Environment` | `Staging` | `Staging` | `Staging` |
| `CommandDbCatalog` | Staging command DB adı | Aynı | Aynı |
| `QueryDbCatalog` | Staging query DB adı | Aynı | Aynı |
| `QueryDbCatalog=(not-configured)` | **Olmamalı** | **Olmamalı** | **Olmamalı** |
| `CommandDbCatalog` ≠ `QueryDbCatalog` | **Evet** | **Evet** | **Evet** |
| `PaymentProjectionEnabled` | False → **True** | **True** | True |
| `Payments*ReadEnabled` | **False** | False | İlgili flag **True** |

### Query path hit logları (flag açık smoke)

| Yüzey | Log mesajı |
|---|---|
| List | `Payments list generated from Query DB read model` |
| Dashboard recent | `Dashboard finance summary generated` (içinde `DashboardRecentPaymentsReadEnabled=True`) |
| Client summary | `Client payment summary generated from Query DB read model` |
| Report JSON | `Payments report generated from Query DB read model` |
| GetById | `Payment detail generated from Query DB read model` |

Export: ayrı Query log satırı yok — satır sayısı + flag startup log ile doğrulanır (17A).

### Performance diagnostics

`PerformanceDiagnostics:Enabled=true` (base appsettings) — slow SQL/handler uyarıları staging'de faydalı.

---

## Rollback model

Payment CQRS rollback **config + restart** ile anında etki; kod geri alımı veya migration rollback gerekmez.

### Genel adımlar

```text
1. İlgici QueryReadModels:*ReadEnabled = false
2. API restart / redeploy
3. Startup log: flag False doğrula
4. Smoke: endpoint Command DB path (Query log yok)
```

### Flag bazlı etki

| Flag kapatılırsa | Etki |
|---|---|
| `PaymentsListReadEnabled` | List → Command DB |
| `DashboardRecentPaymentsReadEnabled` | Recent payments → Command DB |
| `ClientPaymentSummaryReadEnabled` | Summary → Command DB |
| `PaymentsReportReadEnabled` | Report JSON → Command DB |
| `PaymentsReportExportReadEnabled` | Export → Command DB |
| `PaymentsGetByIdReadEnabled` | Detail → Command DB |
| `PaymentProjection:Enabled` | Event tüketimi durur — **read flag'ler açık kalmamalı** |

### Acil tam rollback (read yüzeyleri)

Tüm payment read flag'ler **false** → restart → Command DB source of truth. Projection açık bırakılabilir (read-model sıcak tutulur).

### Kısmi rollback (güvenli)

- Export kapat, report açık kalabilir
- GetById kapat, diğerleri açık kalabilir
- **Projection kapat + read flag açık → yüksek risk**

Environment variable örneği:

```text
QueryReadModels__PaymentsListReadEnabled=false
```

---

## Payment CQRS rollout readiness

Staging'de production'a geçmeden önce [`cqrs-17c-payment-cqrs-release-readiness.md`](../cqrs/cqrs-17c-payment-cqrs-release-readiness.md) Go checklist uygulanmalıdır.

### Operasyonel gate sırası (17A)

```text
Faz 0 (read flag'ler kapalı):
  1. migrate + migrate-query
  2. ClientProjection + PetProjection enabled (search için)
  3. backfill-client-projections + backfill-pet-projections
  4. PaymentProjection:Enabled=true → API restart
  5. backfill-payment-read-models → exit 0, Parity in-sync
  6. GetClinicParityAsync InSync (hedef tenant/clinic)
  7. GET /health/ready → payment-projection Healthy

Faz 1 (kademeli — her adım: config → restart → startup log → smoke):
  8.  PaymentsListReadEnabled=true
  9.  DashboardRecentPaymentsReadEnabled=true
  10. ClientPaymentSummaryReadEnabled=true
  11. PaymentsReportReadEnabled=true
  12. PaymentsReportExportReadEnabled=true
  13. PaymentsGetByIdReadEnabled=true (+ IDOR smoke)
```

### Staging smoke checklist (production öncesi)

**Altyapı**

- [ ] Command/Query catalog farklı; startup log doğrulandı
- [ ] Query migration güncel; `query-sql` health healthy
- [ ] Backfill exit 0; count parity InSync
- [ ] `payment-projection` healthy; drift = 0

**Her read flag (single-clinic veya tenant-wide test kullanıcı)**

- [ ] Happy path 200
- [ ] Search/filter parity (search dolu senaryolar — list/report/export)
- [ ] Empty query → boş sonuç (404 değil; GetById hariç)
- [ ] Multi-clinic → Command path (Query log yok)
- [ ] No-fallback: Query boş → boş/NotFound; Command fallback yok

**GetById IDOR (flag true, staging manuel — zorunlu)**

- [ ] Assigned clinic → 200
- [ ] Unassigned clinic → 404 `Payments.NotFound`
- [ ] Cross-tenant → 404
- [ ] Tenant-wide admin cross-clinic → 200
- [ ] Permission yok → 403

**Rollback drill**

- [ ] Flag false + restart → Command baseline smoke

**İzleme (5–15 dk per flag)**

- [ ] Error rate, latency, `payment-projection` health, Query path log hacmi

---

## Recommended first deployment shape

### Başlangıç topolojisi (düşük maliyet, repo ile uyumlu)

```text
┌─────────────────────────────────────────────┐
│  Single VPS / Windows Server / Linux VM      │
│  ┌─────────────────────────────────────┐    │
│  │  IIS (reverse proxy) veya Kestrel    │    │
│  │  Backend.Veteriner.Api (1 instance)  │    │
│  │  ASPNETCORE_ENVIRONMENT=Staging      │    │
│  └─────────────────────────────────────┘    │
│  ┌─────────────────────────────────────┐    │
│  │  SQL Server (single instance)        │    │
│  │  ├─ VetinityCommandDb_Staging        │    │
│  │  └─ VetinityQueryDb_Staging          │    │
│  └─────────────────────────────────────┘    │
└─────────────────────────────────────────────┘

Deploy adımları (CI/CD veya manuel):
  publish Api → configure secrets → migrate → migrate-query → seed? → restart
  Payment CQRS Faz 0: projection + backfill → health gate → kademeli flag
```

| Soru | Öneri |
|---|---|
| Tek VPS/IIS/Docker ile başlamak mümkün mü? | **Evet** — repo Docker tanımlamaz; IIS+Kestrel veya Linux+systemd+Kestrel yeterli |
| Ayrı app service gerekir mi? | **Hayır** (başlangıç) — tek API instance |
| Ayrı SQL Server gerekir mi? | **Hayır** (başlangıç) — aynı instance, **iki ayrı database zorunlu** |
| Multi-instance API? | Payment projection `ClaimingEnabled=false` → **tek instance** tercih; scale öncesi 11D/claiming değerlendirmesi |

### Production'a geçiş

Staging smoke + rollback drill temiz olmadan production flag açılmamalı. Production şablonu (`appsettings.Production.json`) aynı flag default'larına sahiptir; secret'lar platformda doldurulur.

---

## Open questions

Repo'da **tanımlı değil** — operasyon ekibi kararı gerekir:

| # | Konu | Not |
|---|---|---|
| 1 | Staging host URL / DNS | `App:BaseUrl` operatör doldurur |
| 2 | Staging DB sunucusu (managed vs self-hosted) | Aynı VM SQL veya Azure SQL / RDS |
| 3 | Secret store (Azure Key Vault, AWS SM, env-only) | README secret'ları repo dışı der |
| 4 | CI/CD pipeline tanımı | Repo'da GitHub Actions / Azure DevOps yok |
| 5 | Container strategy | Dockerfile yok; ileride eklenebilir |
| 6 | Query DB backup/restore runbook | CQRS-11A: operasyonel madde |
| 7 | Staging instance count | Tek instance önerilir; LB + multi-instance claiming stratejisi belirsiz |
| 8 | Staging veri kaynağı | Seed vs production anonymized copy |
| 9 | Pet projection staging default | `appsettings.Staging.json` → `PetProjection:Enabled=false`; payment search için **açılmalı** |
| 10 | Monitoring/alerting entegrasyonu | `cqrs-11c-monitoring-alerts.md` referans; staging alert owner belirsiz |
| 11 | TLS sertifika yönetimi | IIS/Let's Encrypt/cloud LB |
| 12 | Tenant-wide report perf baseline | CQRS-18A — staging'de büyük tenant fixture ile ölçüm planı |

**DEPLOY-0:** Commit atılmadı (kullanıcı talimatı).
