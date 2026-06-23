# Staging Config and Secrets Inventory

**Tür:** Staging deploy öncesi config / secret / feature flag envanteri (DEPLOY-3). **Production kod, test, appsettings, migration ve IIS config değişmedi.**

**Ön durum:**

| Faz | Doküman | Commit |
|---|---|---|
| DEPLOY-0 | [`staging-environment-design.md`](staging-environment-design.md) | `a5a681e` |
| DEPLOY-1 | [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) | `e8a0bf8` |
| DEPLOY-2 | [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md) | `ec458a1` |

**İlgili dokümanlar:** [`staging-environment-design.md`](staging-environment-design.md) · [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) · [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md) · [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md) · [`cqrs-17c-payment-cqrs-release-readiness.md`](../cqrs/cqrs-17c-payment-cqrs-release-readiness.md) · [`README.md`](../../README.md)

**Git doğrulama (2026-06-23, DEPLOY-3 başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** |
| DEPLOY-2 commit | `ec458a1` — `docs(deploy): add windows iis staging checklist` |
| DEPLOY-1 commit | `e8a0bf8` — `docs(deploy): add first staging deploy runbook` |
| DEPLOY-0 commit | `a5a681e` — `docs(deploy): add staging environment design` |

**Kapsam:** İlk staging deploy öncesi tüm config, environment variable, secret, connection string ve feature flag değerlerinin tek referans envanteri. **Gerçek secret/parola/JWT key yazılmaz.**

---

## Purpose

Bu doküman operatör ve deploy pipeline'ın staging ortamına geçmeden önce **hangi config değerlerinin nereden geldiğini**, **hangilerinin zorunlu olduğunu** ve **ilk deploy'da hangi feature flag'lerin kapalı kalacağını** tek yerde toplar.

Hedef okuyucu: staging host kurulumu ([`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md)) tamamlandıktan sonra API publish, DbMigrator ve smoke test öncesi config doğrulaması yapan operasyon ekibi.

**İlk staging yaklaşımı (özet):**

- Windows VPS/VM + IIS
- SQL Server aynı instance olabilir
- `VetinityCommandDb_Staging` + `VetinityQueryDb_Staging`
- `Backend.Veteriner.Api` tek instance
- `ASPNETCORE_ENVIRONMENT=Staging`
- Payment CQRS flag'leri ilk deploy'da **false**

---

## Configuration sources

ASP.NET Core configuration yükleme sırası (`WebApplicationBuilderExtensions.AddBackendAppConfiguration`):

| Öncelik (düşük → yüksek) | Kaynak | Staging notu |
|---|---|---|
| 1 | `appsettings.json` | Temel config; connection string ve JWT key **boş** |
| 2 | `appsettings.Staging.json` | Staging overlay; CQRS/projection default'ları; connection string **boş** |
| 3 | **Environment variables** / secret store | **En yüksek öncelik** — production-like deploy'da secret'lar buradan gelir |
| 4 | User Secrets | Yalnızca `Development` ortamında API tarafında (`IsDevelopment()`) |

DbMigrator (`Host.CreateApplicationBuilder`):

| Kaynak | Staging notu |
|---|---|
| `appsettings.json` | Boş connection string şablonu |
| `appsettings.{Environment}.json` | **`appsettings.Staging.json` repo'da yok** — Staging'de env var zorunlu |
| Environment variables | `DOTNET_ENVIRONMENT=Staging` + connection string env var'ları |
| User Secrets | Yalnızca `Development` |

**Secret kuralı:** Gerçek secret değerleri (SQL parola, JWT key, SMTP parola, billing API key'leri) **repo'ya yazılmaz**. Bu doküman yalnızca placeholder kullanır.

Config section → environment variable dönüşümü (.NET convention):

```text
Section:SubSection:Key  →  Section__SubSection__Key
ConnectionStrings:DefaultConnection  →  ConnectionStrings__DefaultConnection
```

---

## Required environment variables

Aşağıdaki tablo **ilk staging deploy** için minimum env var envanteridir. Key isimleri repo'daki gerçek config yollarından türetilmiştir; uydurma key yoktur.

### Zorunlu (ilk deploy)

| Key | Required? | Example placeholder | Used by | Notes |
|---|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | **Evet** | `Staging` | API | `appsettings.Staging.json` overlay'i yükler |
| `ConnectionStrings__DefaultConnection` | **Evet*** | `Server=<SQL_HOST>;Database=VetinityCommandDb_Staging;User Id=<USER>;Password=<PASSWORD>;TrustServerCertificate=<true\|false>;MultipleActiveResultSets=True` | API, DbMigrator | Command DB (`AppDbContext`). *`SqlServer` fallback kullanılıyorsa bu satır yerine aşağıdaki key |
| `ConnectionStrings__SqlServer` | Koşullu | Aynı Command placeholder | API, DbMigrator | `DefaultConnection` boşsa Infrastructure fallback (`DependencyInjection.cs`). Production şablonu bu key'i kullanır; staging'de **tek Command key** tutarlılığı önerilir |
| `ConnectionStrings__QueryConnection` | **Evet** | `Server=<SQL_HOST>;Database=VetinityQueryDb_Staging;User Id=<USER>;Password=<PASSWORD>;TrustServerCertificate=<true\|false>;MultipleActiveResultSets=True` | API, DbMigrator | Query DB (`QueryDbContext`). **Boş bırakılamaz** |
| `Jwt__Key` | **Evet** | `<STAGING_JWT_SIGNING_KEY_MIN_32_CHARS>` | API | Symmetric signing key. Staging'de production key **kullanılmamalı** |
| `App__BaseUrl` | **Evet** | `https://staging-api.<DOMAIN>` | API | Public API base URL (link/redirect üretimi) |
| `AllowedOrigins__0` | **Evet** | `https://staging-app.<DOMAIN>` | API | CORS `AllowFrontend` policy. Config section: `AllowedOrigins` (string array). Ek origin: `AllowedOrigins__1`, … |
| `PaymentProjection__Enabled` | **Evet** (değer) | `false` | API | Config section: `PaymentProjection`. İlk deploy **false** |
| `QueryReadModels__PaymentsListReadEnabled` | **Evet** (değer) | `false` | API | Config section: `QueryReadModels` |
| `QueryReadModels__DashboardRecentPaymentsReadEnabled` | **Evet** (değer) | `false` | API | |
| `QueryReadModels__ClientPaymentSummaryReadEnabled` | **Evet** (değer) | `false` | API | |
| `QueryReadModels__PaymentsReportReadEnabled` | **Evet** (değer) | `false` | API | |
| `QueryReadModels__PaymentsReportExportReadEnabled` | **Evet** (değer) | `false` | API | |
| `QueryReadModels__PaymentsGetByIdReadEnabled` | **Evet** (değer) | `false` | API | |

### DbMigrator ortamı

| Key | Required? | Example placeholder | Used by | Notes |
|---|---|---|---|---|
| `DOTNET_ENVIRONMENT` | **Evet** | `Staging` | DbMigrator | Host environment adı. `appsettings.Staging.json` DbMigrator projesinde **yok** — connection string env var zorunlu |
| `ConnectionStrings__DefaultConnection` | **Evet*** | (Command placeholder) | DbMigrator | `migrate`, `seed`, `all`, backfill komutları |
| `ConnectionStrings__QueryConnection` | **Evet** | (Query placeholder) | DbMigrator | `migrate-query`, backfill komutları |

DbMigrator çalıştırırken API ile **aynı** Command/Query connection string env var'ları set edilmelidir.

### Önerilen (repo default ile gelir; override opsiyonel)

| Key | Required? | Example placeholder | Used by | Notes |
|---|---|---|---|---|
| `Jwt__Issuer` | Hayır | `Backend.Veteriner` | API | `appsettings.json` default |
| `Jwt__Audience` | Hayır | `Backend.Veteriner.Audience` | API | Production şablonu farklı audience kullanabilir |
| `Jwt__ExpMinutes` | Hayır | `60` | API | Access token süresi (dakika) |
| `Jwt__RefreshExpDays` | Hayır | `7` | API | Refresh token ömrü (gün) |
| `RateLimiting__Enabled` | Hayır | `true` | API | `appsettings.Staging.json` overlay'de **true** |
| `RateLimiting__GlobalPermitLimit` | Hayır | `200` | API | Base `appsettings.json` |

### Feature kullanılıyorsa (ilk deploy zorunlu değil)

| Key | Required? | Example placeholder | Used by | Notes |
|---|---|---|---|---|
| `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Pass`, `Smtp__From` | Hayır | `<SMTP_*>` | API | Reminder/mail smoke için. Development'ta `Smtp:Pass` zorunlu; Staging'de env ile |
| `Billing__Stripe__SecretKey`, `Billing__Iyzico__ApiKey`, … | Hayır | `<BILLING_*>` | API | Billing/checkout test edilecekse sandbox değerleri |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Hayır | `http://localhost:4317` | API | OpenTelemetry; observability stack varsa |

---

## Connection strings

### Command vs Query ayrımı

| Rol | EF Context | Config key | Staging catalog (öneri) |
|---|---|---|---|
| **Command DB** | `AppDbContext` | `ConnectionStrings:DefaultConnection` (tercih) veya `ConnectionStrings:SqlServer` | `VetinityCommandDb_Staging` |
| **Query DB** | `QueryDbContext` | `ConnectionStrings:QueryConnection` | `VetinityQueryDb_Staging` |

Kaynak: `Infrastructure/DependencyInjection.cs` — Command için `DefaultConnection` yoksa `SqlServer` fallback; Query için **`QueryConnection` zorunlu** (boşsa startup exception).

### Placeholder örnekleri

**Command (`DefaultConnection` veya `SqlServer`):**

```text
Server=<SQL_HOST>;Database=VetinityCommandDb_Staging;User Id=<USER>;Password=<PASSWORD>;TrustServerCertificate=<true|false>;MultipleActiveResultSets=True
```

**Query (`QueryConnection`):**

```text
Server=<SQL_HOST>;Database=VetinityQueryDb_Staging;User Id=<USER>;Password=<PASSWORD>;TrustServerCertificate=<true|false>;MultipleActiveResultSets=True
```

Windows Authentication alternatifi (operasyon kararı):

```text
Server=<SQL_HOST>;Database=VetinityCommandDb_Staging;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True
```

### Kurallar

- **Command DB ve Query DB farklı catalog olmalı.** Aynı catalog kullanılırsa backfill servisleri reddeder (`PaymentReadModelBackfillService` vb.).
- **`QueryConnection` zorunlu** — API ve DbMigrator ikisi de Query connection olmadan başlamaz.
- Gerçek kullanıcı/parola **bu dokümanda ve repoda yazılmaz**.
- Staging şablonu (`appsettings.Staging.json`) `DefaultConnection` + `QueryConnection` kullanır (boş). Production şablonu `SqlServer` + `QueryConnection` kullanır — staging deploy'da **tercihen `DefaultConnection`** set edin veya `SqlServer` fallback'i bilinçli kullanın; **ikisini birden farklı değerlerle set etmeyin**.

---

## Authentication and JWT

Config section: `Jwt` (`JwtOptions` — `Issuer`, `Audience`, `Key`, `ExpMinutes`, `RefreshExpDays`).

| Kural | Açıklama |
|---|---|
| `Jwt__Key` staging için **production'dan farklı** olmalı | Production JWT ile imzalanmış token staging'de geçerli olmamalı |
| Key uzun, rastgele, repo dışında saklanmalı | Minimum pratik: 32+ karakter; secret store / IIS env var |
| Token doğrulama | `WebApplicationBuilderExtensions` — `ValidateIssuer`, `ValidateAudience`, `ValidateIssuerSigningKey`, `ValidateLifetime`; `ClockSkew = Zero` |
| Development vs Staging | Development'ta boş `Jwt:Key` startup exception; Staging'de env var ile doldurulmalı (Development guard'ları çalışmaz ama boş key runtime'da auth hatası verir) |
| Production JWT key staging'de **kullanılmamalı** | Staging/prod secret ayrımı zorunlu operasyon kuralı |

İlgili config (repo default, override opsiyonel):

```json
"Jwt": {
  "Issuer": "Backend.Veteriner",
  "Audience": "Backend.Veteriner.Audience",
  "Key": "",
  "ExpMinutes": 60,
  "RefreshExpDays": 7
}
```

Environment variable override örneği: `Jwt__Key=<STAGING_JWT_SIGNING_KEY_MIN_32_CHARS>`.

Cookie/session: `Session:SingleSessionPerUser` base config'de `false`; staging'de override opsiyonel (`Session__SingleSessionPerUser`).

---

## CORS and public URLs

| Config | Env var | Staging değeri |
|---|---|---|
| `App:BaseUrl` | `App__BaseUrl` | `https://staging-api.<DOMAIN>` — TLS sertifikası hostname ile uyumlu |
| `AllowedOrigins` (string[]) | `AllowedOrigins__0`, `AllowedOrigins__1`, … | Staging frontend origin, örn. `https://staging-app.<DOMAIN>` |

CORS policy adı: `AllowFrontend` (`WebApplicationBuilderExtensions`). `AllowCredentials()` açık — origin wildcard kullanılamaz; explicit origin listesi gerekir.

**localhost origin:** Config'de `AllowedOrigins` yoksa kod default'u `http://localhost:4200`'dür. Staging'de:

- **Bilinçli bırakılabilir** (geliştirici lokal frontend → staging API testi) — operasyon kararı
- **Kaldırılmalı** (yalnızca staging frontend origin) — daha sıkı güvenlik

**Swagger exposure:** Repo tüm ortamlarda Swagger açık (`AddSwaggerGen`). Staging'de public erişim **ayrı operasyon kararı** (IP allowlist, VPN, auth gateway). Config flag ile kapatma repo'da yok.

---

## CQRS and projection flags

### Payment CQRS — ilk deploy başlangıç değerleri

Tüm değerler **`false`** (repo `appsettings.Staging.json` ile uyumlu):

| Config yolu | Env var | İlk staging |
|---|---|---|
| `PaymentProjection:Enabled` | `PaymentProjection__Enabled` | **false** |
| `QueryReadModels:PaymentsListReadEnabled` | `QueryReadModels__PaymentsListReadEnabled` | **false** |
| `QueryReadModels:DashboardRecentPaymentsReadEnabled` | `QueryReadModels__DashboardRecentPaymentsReadEnabled` | **false** |
| `QueryReadModels:ClientPaymentSummaryReadEnabled` | `QueryReadModels__ClientPaymentSummaryReadEnabled` | **false** |
| `QueryReadModels:PaymentsReportReadEnabled` | `QueryReadModels__PaymentsReportReadEnabled` | **false** |
| `QueryReadModels:PaymentsReportExportReadEnabled` | `QueryReadModels__PaymentsReportExportReadEnabled` | **false** |
| `QueryReadModels:PaymentsGetByIdReadEnabled` | `QueryReadModels__PaymentsGetByIdReadEnabled` | **false** |

### Operasyon kuralları

- **İlk deploy'da Payment CQRS read flag'leri açılmayacak.** Production davranışı (Command DB path) korunur.
- Payment CQRS rollout [`cqrs-17a`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md) ve [`cqrs-17c`](../cqrs/cqrs-17c-payment-cqrs-release-readiness.md) runbook'larına göre **ayrı faz**.
- **Projection / backfill / parity geçmeden read flag açılmamalı.** Sıra: `PaymentProjection__Enabled=true` → client/pet backfill → `backfill-payment-read-models` (exit 0, parity InSync) → kademeli read flag açma.
- Flag değişikliği **process restart** gerektirir (`IOptions` startup bind).

### Diğer projection / QueryReadModels (staging şablonu)

`appsettings.Staging.json` overlay — ilk deploy referansı:

| Config yolu | Staging default | Not |
|---|---|---|
| `AppointmentProjection:Enabled` | **true** | |
| `ClientProjection:Enabled` | **true** | |
| `PetProjection:Enabled` | **false** | Payment search rollout öncesi açılmalı + `backfill-pet-projections` |
| `QueryReadModels:AppointmentsEnabled` | **false** | |
| `QueryReadModels:ClientsEnabled` | **false** | |
| `QueryReadModels:PetsEnabled` | **false** | |
| `QueryReadModels:PaymentsSearchLookupEnabled` | **false** | |
| `QueryReadModels:DashboardFinanceReadEnabled` | **false** | |
| Diğer `QueryReadModels:*` | **false** | Tam liste: `QueryReadModelsOptions.cs` |

Bu flag'ler Payment CQRS ilk deploy gate'i değildir; env var ile override edilebilir. Startup log'da `CqrsStartupConfigurationLogger` tüm efektif değerleri yazar.

---

## Logging and health

### Logging

| Kaynak | Staging davranışı |
|---|---|
| `Serilog` | Base `appsettings.json`: Console + File (`logs/log-.ndjson`, 14 gün retention). Staging overlay minimum level override |
| `Logging:LogLevel` | Base config; Microsoft.AspNetCore Warning |
| IIS stdout | Yalnızca kısa troubleshooting (`web.config`); kalıcı log değil |

Operasyon: log dosyası path'i publish klasörüne göre (`C:\inetpub\Vetinity\staging\api\current\logs\` veya overlay path). Event Viewer (ASP.NET Core Module) ikincil kaynak.

### Health endpoints

| Endpoint | Rol |
|---|---|
| `/health/live` | Liveness — predicate yok (her zaman 200) |
| `/health/ready` | Readiness — tüm check'ler; Degraded/Unhealthy → 503 |
| `/health` | Default health map |

Registered checks (`AddHealthChecks`):

| Check name | İlk staging beklentisi |
|---|---|
| `sql` | Command DB erişimi — Healthy |
| `query-sql` | Query DB erişimi — Healthy |
| `outbox` | Outbox worker — Healthy veya kabul edilebilir Degraded |
| `appointment-projection` | Projection enabled — izle |
| `client-projection` | Projection enabled — izle |
| `pet-projection` | Disabled — duruma göre |
| `payment-projection` | **Disabled** — projection kapalıysa drift değerlendirilmez |

### Startup config doğrulama

API startup sonrası `CqrsStartupConfigurationLogger` şunları loglar (secret/PII yok):

- `Environment` (Staging olmalı)
- Tüm CQRS / projection flag değerleri
- `CommandDbCatalog` / `QueryDbCatalog` — **farklı catalog adları** görünmeli

Örnek log alanları: `PaymentProjectionEnabled`, `PaymentsListReadEnabled`, …, `CommandDbCatalog`, `QueryDbCatalog`.

---

## DbMigrator configuration

DbMigrator, API ile **aynı** `AddInfrastructure(configuration)` connection string çözümlemesini kullanır.

### Gerekli config (komuta göre)

| Komut | Command connection | Query connection | Not |
|---|---|---|---|
| `migrate` | **Gerekli** | — | `AppDbContext` MigrateAsync |
| `migrate-query` | — | **Gerekli** | `QueryDbContext` MigrateAsync |
| `seed` | **Gerekli** | — | Command DB seed pipeline |
| `all` | **Gerekli** | — | migrate + seed; **Query içermez** |
| `backfill-client-projections` | **Gerekli** | **Gerekli** | İlk deploy'da çalıştırılmaz |
| `backfill-pet-projections` | **Gerekli** | **Gerekli** | İlk deploy'da çalıştırılmaz |
| `backfill-payment-finance-projections` | **Gerekli** | **Gerekli** | Payment CQRS fazında |
| `backfill-payment-read-models` | **Gerekli** | **Gerekli** | Payment CQRS fazında; parity exit 2 |

### İlk staging sırası

```powershell
$env:DOTNET_ENVIRONMENT = "Staging"
# ConnectionStrings__DefaultConnection ve ConnectionStrings__QueryConnection set edilmis olmali

dotnet Backend.Veteriner.DbMigrator.dll migrate
dotnet Backend.Veteriner.DbMigrator.dll migrate-query
dotnet Backend.Veteriner.DbMigrator.dll seed   # opsiyonel
```

**Payment backfill komutları ilk deploy'da çalıştırılmaz** — ayrı CQRS rollout fazı.

DbMigrator projesinde `appsettings.Staging.json` **yok**; Staging'de connection string'ler **yalnızca environment variable** (veya publish artifact yanına operatör tarafından eklenen local config — repo dışı) ile gelir.

Development'ta User Secrets desteklenir; Staging'de desteklenmez.

---

## IIS-specific notes

| Konu | Rehber |
|---|---|
| Env var enjeksiyonu | IIS Application Pool → Advanced Settings → **Environment Variables** (tercih) |
| Alternatif | Publish output `web.config` → `aspNetCore` → `environmentVariables` |
| Secret dosyada | `web.config` içine secret yazılacaksa dosya ACL sıkı olmalı; app pool identity dışında okuma yok |
| **Tercih sırası** | 1) Machine-level env var / secret store 2) Deploy pipeline secret injection 3) IIS app pool env var 4) web.config (son çare) |
| Recycle sonrası doğrulama | App pool recycle → startup log'da `Environment=Staging`, CQRS flags **false**, catalog adları doğru |
| API ortam değişkeni | `ASPNETCORE_ENVIRONMENT=Staging` (IIS app pool veya web.config) |
| DbMigrator ortam değişkeni | `DOTNET_ENVIRONMENT=Staging` (CLI session veya aynı machine env var) |

Detaylı IIS checklist: [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md).

---

## Secret handling rules

- [ ] Gerçek secret **commit etme** (connection string, JWT, SMTP, billing API key)
- [ ] `.env` dosyası **repo'ya alma** (`.gitignore` kontrolü)
- [ ] Connection string screenshot / ticket paylaşırken **parola maskele**
- [ ] JWT key **paylaşma** — secret store veya güvenli kanal
- [ ] SQL user **minimum yetki** — yalnız staging Command + Query catalog; sysadmin kullanma
- [ ] Staging / production secret **ayrı** — JWT key, SQL login, billing sandbox vs prod
- [ ] Backup dosyalarında secret sızıntısı **kontrolü** (`.bak`, config export, log dump)
- [ ] `appsettings.Development.json` gibi yerel dosyalar production/staging host'a **kopyalanmaz**
- [ ] Production şablonundaki (`appsettings.Production.json`) boş key placeholder'ları staging'de env ile doldur; dosyayı secret ile commit etme

---

## Validation checklist

Staging deploy öncesi / sonrası operatör kontrol listesi:

### Config ve secret

- [ ] `ASPNETCORE_ENVIRONMENT=Staging`
- [ ] Command DB catalog = `VetinityCommandDb_Staging` (veya operasyon onaylı ad)
- [ ] Query DB catalog = `VetinityQueryDb_Staging` (veya operasyon onaylı ad)
- [ ] **Cataloglar farklı** (startup log `CommandDbCatalog` ≠ `QueryDbCatalog`)
- [ ] `Jwt__Key` set edildi; production key değil
- [ ] `App__BaseUrl` staging API URL ile uyumlu
- [ ] `AllowedOrigins__0` staging frontend origin
- [ ] Tüm Payment CQRS env var'ları **false** (veya unset → staging overlay false)

### DbMigrator

- [ ] `DOTNET_ENVIRONMENT=Staging`
- [ ] `migrate` başarılı (exit 0)
- [ ] `migrate-query` başarılı (exit 0)
- [ ] (Opsiyonel) `seed` başarılı

### API runtime

- [ ] IIS app pool start / recycle sonrası API ayakta
- [ ] Startup log'da `Environment=Staging`
- [ ] Startup log'da Payment CQRS flags **false**
- [ ] `GET /health/ready` erişilebilir — `sql` + `query-sql` Healthy
- [ ] CORS: staging frontend'den preflight/API çağrısı kabul ediliyor
- [ ] JWT login/auth smoke başarılı (token al → korumalı endpoint 200)

### Bilinçli dışlama (ilk deploy)

- [ ] Payment CQRS read flag'leri **açılmadı**
- [ ] `backfill-payment-read-models` **çalıştırılmadı**
- [ ] `PaymentProjection__Enabled` **false** kaldı

---

## Open decisions

| # | Karar | Seçenekler |
|---|---|---|
| 1 | Command connection key | `DefaultConnection` (önerilen) vs `SqlServer` fallback |
| 2 | SQL auth model | Windows (app pool identity) vs SQL login |
| 3 | Secret store | IIS app pool env only, Azure Key Vault, DPAPI, pipeline secret |
| 4 | `AllowedOrigins` | Yalnızca staging frontend vs localhost dahil |
| 5 | Swagger erişimi | VPN, IP allowlist, public |
| 6 | JWT Audience/Issuer | Repo default vs staging-specific audience |
| 7 | SMTP / Billing config | İlk deploy'da boş vs sandbox değerleri |
| 8 | Serilog file path | Relative `logs/` vs absolute `C:\inetpub\...\logs\` |
| 9 | Seed stratejisi | DbMigrator `seed` vs anonymized DB restore |
| 10 | Payment CQRS faz tarihi | Host smoke sonrası 17A/17C |
| 11 | `PetProjection:Enabled` | Payment search öncesi ne zaman true |
| 12 | Health endpoint exposure | Public vs internal-only |

---

**DEPLOY-3:** Commit atılmadı (kullanıcı talimatı).
