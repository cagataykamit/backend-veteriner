# First Staging Deployment Runbook

**Tür:** Operasyonel first staging deploy runbook (DEPLOY-1). **Production kod, test, appsettings, migration ve config değişmedi.**

**Ön durum:** [`staging-environment-design.md`](staging-environment-design.md) (DEPLOY-0) tamamlandı.

**İlgili dokümanlar:** [`staging-environment-design.md`](staging-environment-design.md) · [`cqrs-18z-payment-cqrs-closure-handoff.md`](../cqrs/cqrs-18z-payment-cqrs-closure-handoff.md) · [`cqrs-17c-payment-cqrs-release-readiness.md`](../cqrs/cqrs-17c-payment-cqrs-release-readiness.md) · [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md) · [`README.md`](../../README.md)

**Git doğrulama (2026-06-23, DEPLOY-1 başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** |
| DEPLOY-0 commit | `a5a681e` — `docs(deploy): add staging environment design` |
| CQRS-18Z commit | `9113bbc` — `docs(cqrs): close payment cqrs handoff` |

**Kapsam:** İlk staging API deploy + DB şema + temel doğrulama. **Payment CQRS read flag'leri bu deploy'da açılmaz** — hazırlık ayrı faz ([§Payment CQRS staging preparation](#payment-cqrs-staging-preparation)).

---

## Target architecture

Başlangıç için önerilen şekil (DEPLOY-0 ile uyumlu):

```text
+----------------------------------------------------------+
| Tek VPS / VM                                              |
|                                                           |
| ASPNETCORE_ENVIRONMENT = Staging                          |
|                                                           |
| +--------------------------------------------------------+|
| | Reverse proxy (tercihen TLS)                           ||
| | * Windows: IIS + ASP.NET Core Hosting Bundle           ||
| | * Linux: nginx / Caddy -> Kestrel                      ||
| +---------------------------+----------------------------+|
|                             |                             |
| +---------------------------v----------------------------+|
| | Backend.Veteriner.Api (tek instance)                   ||
| | * Outbox worker (hosted service)                       ||
| | * Projection workers (appointment/client/pet/           ||
| |   payment - config'e bağlı)                            ||
| +---------------------------+----------------------------+|
|                             |                             |
| +---------------------------v----------------------------+|
| | SQL Server (aynı instance)                             ||
| | +- VetinityCommandDb_Staging   (Command DB)            ||
| | +- VetinityQueryDb_Staging     (Query DB)              ||
| +--------------------------------------------------------+|
|                                                           |
| DbMigrator: deploy sırasında CLI (sürekli servis değil)   |
+----------------------------------------------------------+
```

| Bileşen | Karar |
|---|---|
| API instance sayısı | **1** (projection `ClaimingEnabled=false` — multi-instance öncesi değerlendirme gerekir) |
| Command / Query DB | **Farklı catalog zorunlu**; aynı SQL Server instance **yeterli** |
| Ortam adı | `Staging` → `appsettings.Staging.json` overlay |
| Docker | Repo'da **Dockerfile yok** — bare-metal / VM deploy |
| API auto-migrate | **Yok** — DbMigrator zorunlu |

---

## Prerequisites

Deploy öncesi operatör / platform ekibi hazırlığı:

| # | Gereksinim | Not |
|---|---|---|
| 1 | **.NET 9 runtime** | API target: `net9.0` |
| 2 | **ASP.NET Core Hosting Bundle** (IIS kullanılıyorsa) | Windows IIS reverse proxy için |
| 3 | **SQL Server erişimi** | DB oluşturma + migration çalıştırma yetkisi |
| 4 | **DbMigrator çalıştırma** | Build edilmiş `Backend.Veteriner.DbMigrator` veya repo'dan `dotnet run` |
| 5 | **TLS sertifikası veya geçici staging domain** | HTTPS önerilir |
| 6 | **Log erişimi** | Serilog file/console; operatör log okuyabilmeli |
| 7 | **Secret/config yönetimi** | Connection string, JWT key repo **dışında** |
| 8 | **Firewall portları** | HTTPS (443), SQL (yalnızca gerekli kaynaklardan), opsiyonel RDP/SSH |
| 9 | **Backup/restore stratejisi** | İlk deploy öncesi boş DB; ileride Command+Query yedek planı |
| 10 | **Deploy edilecek commit/tag** | Bilinen git SHA (ör. DEPLOY-0 sonrası HEAD) |
| 11 | **Rollback klasörü** | Önceki publish output yedeklenebilir olmalı |

**Repo'da yok (operasyon kararı):** CI/CD pipeline, publish profile (`.pubxml`), IaC, staging DNS kaydı.

---

## Server preparation

### Windows (IIS) — özet adımlar

1. Windows Server güncel patch seviyesi
2. SQL Server kurulu ve çalışır durumda (aynı VM veya erişilebilir host)
3. [.NET 9 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/9.0) kurulumu
4. IIS: Application Pool — **No Managed Code**, identity uygun servis hesabı
5. Site binding: HTTPS + staging hostname
6. Publish output klasörü (ör. `C:\inetpub\vetinity-staging-api`) — IIS site physical path
7. Ortam değişkeni: `ASPNETCORE_ENVIRONMENT=Staging` (App Pool veya `web.config` / sistem env)
8. Log klasörü yazma izni (Serilog file sink kullanılacaksa)

### Linux (Kestrel + reverse proxy) — özet adımlar

1. .NET 9 runtime kurulumu
2. SQL Server erişimi (Linux'ta SQL Server container veya remote Windows SQL)
3. systemd unit veya supervisor ile Kestrel
4. nginx/Caddy TLS termination → `http://127.0.0.1:<port>`
5. `ASPNETCORE_ENVIRONMENT=Staging`
6. Log dizini izinleri

**Hangi OS?** → [§Open decisions](#open-decisions)

---

## Database preparation

### Kurallar (repo zorunluluğu)

| Kural | Açıklama |
|---|---|
| **Farklı catalog** | `VetinityCommandDb_Staging` ≠ `VetinityQueryDb_Staging` |
| **Aynı instance OK** | Tek SQL Server instance yeterli |
| **QueryConnection zorunlu** | Query DB olmadan API başlar ama `/health/ready` ve CQRS başarısız olur |
| **Backfill guard** | Aynı catalog → backfill **InvalidOperationException** |
| **Config hardcode yok** | DB adları connection string'de; repo `appsettings.Staging.json` boş placeholder |

### Operasyonel DB oluşturma (örnek — SQL Server)

Operatör SQL Server'da iki boş database oluşturur. Örnek isimler (değiştirilebilir):

```sql
-- Operatör: sunucu adı, collation ve file path ortama göre ayarlanır
CREATE DATABASE VetinityCommandDb_Staging;
CREATE DATABASE VetinityQueryDb_Staging;
GO
```

### SQL kullanıcı (minimum yetki prensibi)

| Database | Minimum yetki (EF migration + runtime) |
|---|---|
| Command | `db_owner` veya migration için DDL + runtime DML (operasyon standardına göre daraltılabilir) |
| Query | Aynı — ayrı login veya aynı login iki DB'de map |

**Not:** Migration sırasında DbMigrator ve runtime API aynı credential'ları kullanır (`ConnectionStrings`).

---

## Configuration and secrets

Staging config kaynağı: `appsettings.json` + `appsettings.Staging.json` overlay + **ortam değişkenleri / secret store** (override).

### Zorunlu ortam

```text
ASPNETCORE_ENVIRONMENT=Staging
```

DbMigrator için (aynı overlay):

```text
DOTNET_ENVIRONMENT=Staging
```

### Connection strings

| Key | Zorunlu | Placeholder örneği |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | **Evet** (tercih) | `Server=<SQL_HOST>;Database=VetinityCommandDb_Staging;User Id=<USER>;Password=<PASSWORD>;TrustServerCertificate=<true\|false>;MultipleActiveResultSets=True` |
| `ConnectionStrings:SqlServer` | Alternatif | Production şablonu `SqlServer` kullanıyorsa; `DefaultConnection` yoksa fallback |
| `ConnectionStrings:QueryConnection` | **Evet** | `Server=<SQL_HOST>;Database=VetinityQueryDb_Staging;User Id=<USER>;Password=<PASSWORD>;TrustServerCertificate=<true\|false>;MultipleActiveResultSets=True` |

Environment variable formu:

```text
ConnectionStrings__DefaultConnection=<...>
ConnectionStrings__QueryConnection=<...>
```

### JWT / authentication

| Key | Zorunlu | Not |
|---|---|---|
| `Jwt:Key` | **Evet** | Signing key — **repo'ya yazılmaz** |
| `Jwt:Issuer` | Base'den devralınır | Staging'de override opsiyonel |
| `Jwt:Audience` | Base'den devralınır | Staging audience önerilir |

```text
Jwt__Key=<STAGING_JWT_SIGNING_KEY>
```

Development'ta boş JWT key startup exception verir; Staging/Production'da farklı validation — yine de **staging'de key zorunlu** (auth çalışması için).

### CORS

Config section: `AllowedOrigins` (string array). Default yoksa `http://localhost:4200`.

Staging için staging frontend origin set edilmeli:

```text
AllowedOrigins__0=https://staging-app.<DOMAIN>
```

Kaynak: `WebApplicationBuilderExtensions.cs` — policy adı `AllowFrontend`.

### App URL

```text
App__BaseUrl=https://staging-api.<DOMAIN>
```

### Logging

Base `appsettings.json`: Serilog Console + file. Production şablonu Linux path kullanır; staging'de operatör log path belirler veya platform log toplar.

Staging overlay minimum level override içerir; WriteTo base'den devralınabilir.

### CQRS flags — ilk deploy başlangıç değerleri

**Repo şablonu (`appsettings.Staging.json`) — değiştirmeyin, overlay ile override:**

| Config | İlk staging deploy |
|---|---|
| `PaymentProjection:Enabled` | **false** |
| `QueryReadModels:PaymentsListReadEnabled` | **false** |
| `QueryReadModels:DashboardRecentPaymentsReadEnabled` | **false** |
| `QueryReadModels:ClientPaymentSummaryReadEnabled` | **false** |
| `QueryReadModels:PaymentsReportReadEnabled` | **false** |
| `QueryReadModels:PaymentsReportExportReadEnabled` | **false** |
| `QueryReadModels:PaymentsGetByIdReadEnabled` | **false** |

Diğer projection/read flag'ler staging şablonunda: `AppointmentProjection:Enabled=true`, `ClientProjection:Enabled=true`, **`PetProjection:Enabled=false`** — payment search hazırlığında pet projection ayrıca açılacak.

### Opsiyonel (özellik kullanımına bağlı)

| Alan | Not |
|---|---|
| `Smtp:*` | Mail/reminder test edilecekse |
| `Billing:*` | Ödeme entegrasyonu staging sandbox |
| `RateLimiting:Enabled` | Staging şablonunda **true** |

**Asla repo'ya commit etmeyin:** parola, JWT key, connection string secret'ları.

---

## Publish and deploy API

Repo'da **publish profile (`.pubxml`) tanımlı değil**. Genel akış:

### Build machine'de (repo root)

```powershell
dotnet restore Veteriner.sln
dotnet publish src/Backend.Veteriner.Api/Backend.Veteriner.Api.csproj -c Release -o ./publish/staging-api
dotnet publish src/Backend.Veteriner.DbMigrator/Backend.Veteriner.DbMigrator.csproj -c Release -o ./publish/staging-dbmigrator
```

Linux/macOS'ta path ayırıcıları ortama göre uyarlanır; `-o` çıktı dizini operatör tarafından seçilir.

### Deploy adımları

1. `publish/staging-api` içeriğini sunucu hedef klasörüne kopyala (robocopy, scp, rsync, CI artifact)
2. `publish/staging-dbmigrator` içeriğini sunucuda migration çalıştırma konumuna kopyala (veya build machine'den remote SQL'e bağlanarak çalıştır)
3. Secret/config değerlerini platformda set et (**appsettings dosyasını repo'dan secret ile doldurmayın** — env var tercih)
4. API process'i henüz başlatma — önce [§Run DbMigrator](#run-dbmigrator)

### IIS notu

Publish output'ta `web.config` otomatik üretilir. Application Pool: No Managed Code. `ASPNETCORE_ENVIRONMENT=Staging` App Pool environment veya `web.config` `environmentVariables` ile.

---

## Run DbMigrator

**Sıra kritik.** API startup migration **yapmaz**.

### Ortam

Deploy/build machine'de veya sunucuda:

```powershell
$env:DOTNET_ENVIRONMENT = "Staging"
# ConnectionStrings env var'ları bu noktada set edilmiş olmalı
```

### Adım 1 — Command DB şema

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate
```

Publish edilmiş DbMigrator kullanılıyorsa:

```powershell
cd <publish/staging-dbmigrator>
$env:DOTNET_ENVIRONMENT = "Staging"
dotnet Backend.Veteriner.DbMigrator.dll migrate
```

Beklenen log: `EF Core MigrateAsync completed.`

### Adım 2 — Query DB şema

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
```

Beklenen: `Query DB MigrateAsync completed.` — `PaymentReadModels` tablosu dahil Query migration'lar uygulanır.

### Adım 3 — (İsteğe bağlı) Seed

İlk staging'de test kullanıcı/izin için:

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- seed
```

Tek komut (migrate + seed, Query hariç):

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- all
# Not: `all` migrate-query ÇALIŞTIRMAZ — Query için ayrı migrate-query zorunlu
```

### Adım 4 — API başlat

DbMigrator tamamlandıktan **sonra** API start/restart.

### Adım 5 — Payment CQRS (ayrı faz, ilk deploy'da zorunlu değil)

İlk deploy'da **read flag açılmaz**. Payment CQRS hazırlığı için bkz. [§Payment CQRS staging preparation](#payment-cqrs-staging-preparation).

**DbMigrator exit code:** `0` başarı, `1` hata, `2` backfill parity mismatch (backfill komutları için).

---

## Start API

### Kestrel (doğrudan)

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Staging"
cd <publish/staging-api>
dotnet Backend.Veteriner.Api.dll
```

Production-like: systemd / IIS / Windows Service ile sürekli çalıştırma.

### Kontrol

- Process ayakta
- Port dinleniyor (launchSettings Development: 5018/7173 — **staging port operatör kararı**)
- İlk request öncesi startup log akışı başladı

---

## Verify startup logs

Deploy/restart sonrası log'da **tek satır** aranır (`CqrsStartupConfigurationLogger`):

```text
CQRS startup configuration. Environment=Staging ... PaymentProjectionEnabled=False ... PaymentsListReadEnabled=False ... PaymentsGetByIdReadEnabled=False ... CommandDbCatalog=VetinityCommandDb_Staging QueryDbCatalog=VetinityQueryDb_Staging
```

### Checklist

| Kontrol | Beklenen |
|---|---|
| `Environment` | `Staging` |
| `CommandDbCatalog` | Staging command DB adı (PII yok) |
| `QueryDbCatalog` | Staging query DB adı |
| `QueryDbCatalog=(not-configured)` | **Olmamalı** |
| `CommandDbCatalog` ≠ `QueryDbCatalog` | **Evet** |
| `PaymentProjectionEnabled` | **False** (ilk deploy) |
| Tüm `Payments*ReadEnabled` | **False** |
| Migration/seed mesajı | API log'da **olmamalı** — `API startup does not run EF migrations...` bilgi satırı olabilir |

---

## Verify health checks

### Endpoint'ler

| URL | Amaç |
|---|---|
| `GET /health/live` | Liveness |
| `GET /health/ready` | **Readiness (operatör gate)** |
| `GET /health` | Tam rapor |

### İlk deploy sonrası beklenen (`/health/ready`)

HTTP **200** veya sorun varsa **503** (Degraded/Unhealthy).

Kontrol edilecek entry'ler:

| Entry | İlk deploy beklentisi |
|---|---|
| `sql` | Healthy — Command DB bağlantısı |
| `query-sql` | Healthy — Query DB bağlantısı, **bekleyen migration yok** |
| `outbox` | Healthy veya Degraded (veri yokken genelde OK) |
| `appointment-projection` | Projection enabled ise kuyruk sinyalleri |
| `client-projection` | Staging'de client projection **enabled** |
| `pet-projection` | Staging'de pet projection **disabled** — kuyruk sinyalleri exposed |
| `payment-projection` | `PaymentProjection:Enabled=false` — drift değerlendirilmez |

Örnek istek (operatör):

```powershell
Invoke-RestMethod -Uri "https://staging-api.<DOMAIN>/health/ready" -Method Get
```

JSON: `status`, `results.<name>.status`, `results.<name>.data` (ör. `readModelCountDrift`, `deadLetterCount` — payment projection açıkken anlamlı).

---

## Payment CQRS staging preparation

**Bu first-deploy runbook kapsamında Payment CQRS read flag'leri AÇILMAZ.**

Staging deploy doğrulandıktan **sonra**, ayrı operasyon fazı olarak [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md) ve [`cqrs-17c-payment-cqrs-release-readiness.md`](../cqrs/cqrs-17c-payment-cqrs-release-readiness.md) uygulanır.

### Faz 0 — Altyapı (read flag'ler kapalı)

```text
1. ClientProjection:Enabled=true (staging'de zaten true)
2. PetProjection:Enabled=true → restart (staging şablonunda false — açılmalı)
3. backfill-client-projections
4. backfill-pet-projections
5. PaymentProjection:Enabled=true → restart
6. backfill-payment-read-models  (exit 0, Parity in-sync)
7. GetClinicParityAsync → InSync
8. GET /health/ready → payment-projection Healthy, readModelCountDrift=0
```

Komutlar (repo adları):

```powershell
$env:DOTNET_ENVIRONMENT = "Staging"
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models
```

### Faz 1 — Kademeli read flag'ler (staging smoke)

Her flag: config true → **restart** → startup log → per-flag smoke → 5–15 dk gözlem.

Sıra (17A): List → Dashboard recent → Client summary → Report JSON → Export → GetById (+ IDOR smoke).

**Production'a geçiş:** Staging Faz 0 + Faz 1 temiz olmadan production flag açılmamalı.

---

## Smoke test checklist

İlk staging deploy doğrulama (Payment CQRS flag'leri **kapalı**):

### Altyapı

- [ ] API process ayakta, crash loop yok
- [ ] `GET /health/live` erişilebilir
- [ ] `GET /health/ready` — `sql` + `query-sql` Healthy
- [ ] Startup log: `Environment=Staging`, catalog adları doğru
- [ ] Startup log: tüm `Payments*ReadEnabled=False`, `PaymentProjectionEnabled=False`

### API yüzeyi

- [ ] Swagger UI erişilebilir (`/swagger/index.html`) — **repo tüm ortamlarda Swagger açık**; erişim kısıtlama operasyon kararı
- [ ] Örnek public endpoint veya versioned API yanıt veriyor
- [ ] HTTPS/TLS çalışıyor (reverse proxy arkasında)

### Auth / tenant

- [ ] `POST /api/v1/auth/login` (seed kullanıcı ile) → token
- [ ] Authenticated istek → 200
- [ ] Tenant/clinic context gerektiren basit liste endpoint (ör. clients/payments) → 200 veya beklenen scope hatası

### Database

- [ ] Command DB tabloları mevcut (migration uygulandı)
- [ ] Query DB tabloları mevcut (`PaymentReadModels` dahil — migrate-query)
- [ ] `query-sql` health: pending migration **yok**

### CQRS baseline (flag kapalı)

- [ ] Payment list → Command DB path (Query path log **yok**)
- [ ] Mevcut davranış development/production baseline ile uyumlu (regression yok)

### Opsiyonel targeted test (build machine)

```powershell
dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~Health"
# Operatör: staging'e karşı smoke script yoksa manuel checklist yeterli
```

---

## Rollback checklist

### API / config rollback (tercih edilen)

```text
1. API process durdur
2. Önceki publish klasörüne geri dön (yedeklenmiş release-N-1)
   VEYA git tag'den yeniden publish
3. Secret/config önceki değerlere döndür (flag'ler dahil false)
4. API restart
5. GET /health/ready → Healthy
6. Auth + temel endpoint smoke
7. Startup log flag doğrulama
```

### Flag rollback (Payment CQRS açıldıysa)

```text
1. QueryReadModels:*ReadEnabled = false
2. PaymentProjection:Enabled = false (opsiyonel — read flag'ler zaten kapalı olmalı)
3. Restart
4. Command DB path smoke
```

### Database rollback

- EF migration **down** otomasyonu repo'da yok
- Strateji: **deploy öncesi backup** veya boş staging'de DB drop/recreate + migrate/migrate-query
- Production-like staging'de: Command + Query **ayrı ayrı** restore

### Log / incident

- [ ] Rollback zamanı kaydedildi
- [ ] Hata log snippet arşivlendi
- [ ] Health check son durumu dokümante edildi

---

## Security checklist

- [ ] Connection string **repo'ya veya ticket'a plaintext yazılmadı**
- [ ] JWT signing key **repo dışında** (secret store / env)
- [ ] Staging `AllowedOrigins` production frontend origin'lerinden **ayrı**
- [ ] **TLS** kullanılıyor (staging'de bile self-signed minimum; tercihen geçerli sertifika)
- [ ] SQL login: yalnız gerekli DB'lere erişim; mümkünse app-specific login (sysadmin değil)
- [ ] Swagger public exposure **bilinçli karar** — repo Staging'de Swagger'ı kapatmaz; firewall/IP allowlist veya auth gateway düşünün
- [ ] Staging verisi: production copy ise **anonymize**; gerçek PII minimum
- [ ] `/health/ready` operatör/monitoring ağına kısıtlı (public internet opsiyonel değil)
- [ ] RDP/SSH: key-based auth, güçlü parola politikası
- [ ] Rate limiting staging'de açık (`RateLimiting:Enabled=true`)

---

## Open decisions

Repo'da tanımlı değil — deploy öncesi operasyon ekibi karar vermeli:

| # | Karar | Seçenekler |
|---|---|---|
| 1 | **OS / host** | Windows + IIS vs Linux + systemd + nginx |
| 2 | **Staging domain** | `staging-api.<domain>`, internal DNS, geçici IP |
| 3 | **Secret store** | Azure Key Vault, AWS SM, HashiCorp Vault, env-only |
| 4 | **SQL Server sürüm/edisyon** | Express, Standard, Azure SQL, container |
| 5 | **CI/CD vs manuel publish** | GitHub Actions, Azure DevOps, rsync/scp |
| 6 | **Backup/restore** | Günlük full backup, snapshot, yok (disposable staging) |
| 7 | **Monitoring/alert** | Application Insights, Prometheus, log-only |
| 8 | **Swagger exposure** | Public, VPN-only, IP allowlist |
| 9 | **Seed stratejisi** | DbMigrator seed vs anonymized prod restore |
| 10 | **SMTP staging** | Gerçek mail vs sink/mock |
| 11 | **Frontend staging URL** | CORS `AllowedOrigins` için |
| 12 | **Payment CQRS faz tarihi** | First deploy sonrası hangi sprint'te 17A uygulanacak |

---

## Quick reference — komut özeti

```powershell
# Ortam
$env:ASPNETCORE_ENVIRONMENT = "Staging"
$env:DOTNET_ENVIRONMENT = "Staging"

# Publish (repo root)
dotnet publish src/Backend.Veteriner.Api/Backend.Veteriner.Api.csproj -c Release -o ./publish/staging-api
dotnet publish src/Backend.Veteriner.DbMigrator/Backend.Veteriner.DbMigrator.csproj -c Release -o ./publish/staging-dbmigrator

# DbMigrator sırası (first deploy)
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
dotnet run --project src/Backend.Veteriner.DbMigrator -- seed          # opsiyonel

# API start (publish output)
dotnet Backend.Veteriner.Api.dll

# Doğrulama
# GET /health/ready
# Log: CQRS startup configuration ...
```

**DEPLOY-1:** Commit atılmadı (kullanıcı talimatı).
