# Staging Provisioning Action Plan

**Tür:** Operasyonel staging VPS/VM provisioning action plan (DEPLOY-5). **Production kod, test, appsettings, migration, script ve IIS config değişmedi.**

**Ön durum:**

| Faz | Doküman | Commit |
|---|---|---|
| DEPLOY-0 | [`staging-environment-design.md`](staging-environment-design.md) | `a5a681e` |
| DEPLOY-1 | [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) | `e8a0bf8` |
| DEPLOY-2 | [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md) | `ec458a1` |
| DEPLOY-3 | [`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md) | `2f841ed` |
| DEPLOY-4 | [`staging-publish-artifact-checklist.md`](staging-publish-artifact-checklist.md) | `118d252` |
| DEPLOY-4A | Publish artifact hygiene (csproj) | `e36c709` |

**İlgili dokümanlar:** [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) · [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md) · [`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md) · [`staging-publish-artifact-checklist.md`](staging-publish-artifact-checklist.md) · [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md)

**Git doğrulama (2026-06-24, DEPLOY-5 başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** |
| DEPLOY-4A commit | `e36c709` — `build(deploy): exclude non-staging appsettings from publish` |
| `dotnet build --no-restore` | **Başarılı** (0 uyarı, 0 hata) |

**Kapsam:** Gerçek staging host hazırlığına başlamadan önce operatörün uygulayacağı sıralı eylem planı. Bu faz **implementasyon değildir** — yalnızca dokümantasyon.

---

## Purpose

DEPLOY-0–4A ile staging deploy için repo tarafı tasarım, runbook, IIS checklist, secret envanteri ve publish artifact hijyeni tamamlandı. DEPLOY-5, operasyon ekibinin **VPS/VM sipariş etmeden önce** hangi kararları netleştirmesi ve hangi sırayla ilerlemesi gerektiğini tek sayfada toplar.

Hedef okuyucu: staging host kuracak operatör / platform mühendisi. İlk gerçek deploy adımları için detay: [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md).

---

## Current repo readiness

Repo, staging provisioning ve first deploy için **hazır** kabul edilir:

| Alan | Durum | Kanıt / kaynak |
|---|---|---|
| API publish dry-run | **Başarılı** | [`staging-publish-artifact-checklist.md`](staging-publish-artifact-checklist.md) |
| DbMigrator publish dry-run | **Başarılı** | Aynı |
| `web.config` üretimi | **Evet** | IIS publish otomatik; `hostingModel="inprocess"` |
| Staging API artifact appsettings | **Yalnızca** `appsettings.json` + `appsettings.Staging.json` | DEPLOY-4A; `-p:EnvironmentName=Staging` zorunlu |
| Production publish kontrolü | **`appsettings.Production.json` korunur** | `-p:EnvironmentName=Production` dry-run |
| Non-staging appsettings staging artifact dışı | **Evet** | Development, IntegrationTests, LoadTest her zaman exclude; Production Staging publish'te exclude |
| DbMigrator artifact appsettings | **Yalnızca** `appsettings.json` | Development/LoadTest publish exclude |
| Solution build | **Temiz** | `dotnet build --no-restore` |
| Payment CQRS flags default | **false** | `appsettings.Staging.json` — `PaymentProjection:Enabled=false`, tüm `QueryReadModels:Payments*ReadEnabled=false` |
| API auto-migrate | **Yok** | DbMigrator zorunlu |
| Health endpoints | **Mevcut** | `/health/live`, `/health/ready`, `/health` |
| Startup CQRS log | **Mevcut** | `CqrsStartupConfigurationLogger` — environment, flag'ler, catalog adları |
| Dockerfile / CI pipeline | **Yok** | Manuel publish veya operasyon kararı |
| Publish profile (`.pubxml`) | **Yok** | `dotnet publish` + `-p:EnvironmentName=Staging` |

**Operatör uyarısı:** Publish komutunda `-p:EnvironmentName=Staging` verilmezse Staging/Production overlay exclude koşulları çalışmayabilir; deploy pipeline'da property açık set edilmelidir.

---

## Decisions to make before provisioning

Aşağıdaki kararlar **host sipariş etmeden önce** netleştirilmelidir. Repo bu kararları sabitlemez.

| # | Karar | Seçenekler / not |
|---|---|---|
| 1 | **Windows Server / VPS sağlayıcısı** | Azure VM, Hetzner, OVH, on-prem hypervisor, mevcut Windows VPS |
| 2 | **Staging domain** | `staging-api.<domain>`, internal DNS, geçici IP + hosts |
| 3 | **TLS yöntemi** | Let's Encrypt (win-acme), internal CA, cloud-managed cert, geçici self-signed |
| 4 | **SQL auth modeli** | Windows Authentication (app pool identity) vs SQL login |
| 5 | **Secret storage yöntemi** | IIS app pool env var, Azure Key Vault, DPAPI, pipeline secret injection |
| 6 | **Backup strategy** | Günlük full backup, snapshot, disposable staging (backup yok) |
| 7 | **Staging data strategy** | **Empty + DbMigrator seed** vs anonymized production copy |
| 8 | **Swagger exposure** | Public, VPN-only, IP allowlist (repo Staging'de Swagger kapatmaz) |
| 9 | **Monitoring / logging target** | Serilog file only, Event Viewer, Application Insights, Prometheus |
| 10 | **CI/CD vs manuel deploy** | GitHub Actions artifact, Azure DevOps, build machine'den robocopy/scp |

**Minimum zorunlu kararlar (Go için):** #1 host, #2 domain (veya geçici erişim planı), #3 TLS, #4 SQL auth, #5 secret store, #6 backup (en az rollback klasörü).

---

## Ordered action plan

Operatör sırası — **20 adım**. Detaylı komutlar için [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md).

| # | Eylem | Çıktı / gate |
|---|---|---|
| 1 | **VPS/VM seç** | Windows Server/VPS; CPU/RAM/disk staging için yeterli; firewall planı |
| 2 | **DNS / staging domain ayarla** | Hostname → staging VM IP; TLS için hostname hazır |
| 3 | **Windows / IIS / .NET Hosting Bundle kur** | IIS + AspNetCoreModuleV2; ASP.NET Core **9** runtime ([`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md)) |
| 4 | **SQL Server kur veya erişimi hazırla** | Aynı VM veya erişilebilir host; operatör bağlantı testi |
| 5 | **Command + Query DB oluştur** | Örn. `VetinityCommandDb_Staging`, `VetinityQueryDb_Staging` — **farklı catalog zorunlu** |
| 6 | **SQL login / permissions ayarla** | Migration + runtime için Command ve Query DB yetkisi |
| 7 | **Secrets / env var değerlerini hazırla** | [`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md) — JWT, connection strings, CORS, `App__BaseUrl` |
| 8 | **API + DbMigrator publish artifact üret** | Build machine; `-p:EnvironmentName=Staging`; bilinen git SHA |
| 9 | **Artifact'ı staging host'a kopyala** | API → IIS physical path; DbMigrator → migration klasörü |
| 10 | **DbMigrator `migrate` çalıştır** | `DOTNET_ENVIRONMENT=Staging` + connection env var; exit 0 |
| 11 | **DbMigrator `migrate-query` çalıştır** | Query DB şema; exit 0. *(Opsiyonel hemen sonra: `seed` — data strategy kararı)* |
| 12 | **IIS site / app pool oluştur** | No Managed Code; physical path = API publish output |
| 13 | **Env var'ları IIS / app pool'a bağla** | `ASPNETCORE_ENVIRONMENT=Staging` + secrets |
| 14 | **API start / recycle** | App pool start; crash loop yok |
| 15 | **Startup log doğrula** | `Environment=Staging`; catalog adları; Payment CQRS flags **false** |
| 16 | **`GET /health/ready` doğrula** | HTTP 200; `sql` + `query-sql` Healthy |
| 17 | **Auth / login smoke** | `POST /api/v1/auth/login` → token → korumalı endpoint 200 |
| 18 | **Tenant / clinic smoke** | Scoped liste endpoint beklenen yanıt |
| 19 | **Payment CQRS flags false doğrula** | Startup log + runtime; read path Command DB |
| 20 | **Snapshot / backup al** | Post-deploy baseline; rollback klasörü yedekle |

---

## Server checklist

- [ ] Windows Server güncel patch
- [ ] IIS Web Server role + gerekli özellikler
- [ ] ASP.NET Core **9** Hosting Bundle kurulu (`dotnet --list-runtimes` → `Microsoft.AspNetCore.App 9.x`)
- [ ] AspNetCoreModuleV2 IIS'te mevcut
- [ ] Staging API physical path oluşturuldu (ör. `C:\inetpub\vetinity-staging-api\current\`)
- [ ] DbMigrator çalıştırma klasörü hazır
- [ ] Log klasörü yazma izni (Serilog file sink)
- [ ] Firewall: HTTPS (443), SQL yalnızca gerekli kaynaklardan, RDP kısıtlı
- [ ] TLS sertifikası binding'e bağlandı
- [ ] Rollback için önceki publish klasörü veya snapshot planı

Detay: [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md).

---

## SQL checklist

- [ ] SQL Server erişilebilir (local veya remote)
- [ ] `VetinityCommandDb_Staging` oluşturuldu
- [ ] `VetinityQueryDb_Staging` oluşturuldu
- [ ] **Command catalog ≠ Query catalog**
- [ ] App login/user Command + Query'de map edildi
- [ ] Migration yetkisi doğrulandı (DbMigrator dry-run veya `migrate` test)
- [ ] Backup stratejisi kayıt altında (full backup veya disposable onay)

---

## IIS checklist

- [ ] Application Pool: **No Managed Code**
- [ ] App pool identity uygun (SQL Windows auth kullanılıyorsa DB login map)
- [ ] Site binding: HTTPS + staging hostname
- [ ] Physical path = API publish output (`web.config` dahil)
- [ ] `ASPNETCORE_ENVIRONMENT=Staging` app pool veya `web.config` env
- [ ] Connection strings + JWT **repo dosyasına yazılmadı** — env var
- [ ] stdout log default kapalı; troubleshooting için geçici açılabilir

---

## Secrets checklist

[`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md) referans alın.

**API (IIS app pool / secret store):**

- [ ] `ASPNETCORE_ENVIRONMENT=Staging`
- [ ] `ConnectionStrings__DefaultConnection` (Command)
- [ ] `ConnectionStrings__QueryConnection` (Query — **zorunlu**)
- [ ] `Jwt__Key` (production key **değil**)
- [ ] `App__BaseUrl` (staging API URL)
- [ ] `AllowedOrigins__0` (staging frontend)
- [ ] Payment CQRS env var'ları **false** veya unset (overlay default false)

**DbMigrator (CLI session):**

- [ ] `DOTNET_ENVIRONMENT=Staging`
- [ ] Aynı Command + Query connection string env var'ları

**Asla:**

- [ ] Secret plaintext ticket/chat'e yazılmadı
- [ ] `appsettings.Development.json` staging host'a kopyalanmadı

---

## Publish package checklist

Build machine'de (repo root):

```powershell
dotnet publish src/Backend.Veteriner.Api/Backend.Veteriner.Api.csproj -c Release -p:EnvironmentName=Staging -o ./publish/staging-api
dotnet publish src/Backend.Veteriner.DbMigrator/Backend.Veteriner.DbMigrator.csproj -c Release -o ./publish/staging-dbmigrator
```

**API artifact doğrulama (kopyalamadan önce):**

- [ ] `Backend.Veteriner.Api.dll`, `Backend.Veteriner.Api.exe`
- [ ] `web.config` mevcut
- [ ] `appsettings.json` + `appsettings.Staging.json` **yalnızca bunlar**
- [ ] `appsettings.Development.json` **yok**
- [ ] `appsettings.Production.json` **yok**
- [ ] Pattern taraması: sandbox key / ngrok / dolu `Password=` / JWT key **yok**

**DbMigrator artifact doğrulama:**

- [ ] `Backend.Veteriner.DbMigrator.dll`
- [ ] `appsettings.json` **yalnızca**
- [ ] `appsettings.Development.json` **yok**

Detay: [`staging-publish-artifact-checklist.md`](staging-publish-artifact-checklist.md).

---

## First deploy checklist

Sıra kritik — API **migration çalıştırmaz**.

1. [ ] Go/No-Go geçildi (aşağıda)
2. [ ] Publish artifact host'a kopyalandı
3. [ ] DbMigrator env var set
4. [ ] `dotnet Backend.Veteriner.DbMigrator.dll migrate` → exit 0
5. [ ] `dotnet Backend.Veteriner.DbMigrator.dll migrate-query` → exit 0
6. [ ] (Opsiyonel) `seed`
7. [ ] IIS site/app pool + env var
8. [ ] API start — migration log **beklenmez**
9. [ ] Verification checklist (aşağı)

**Çalıştırılmayacak (first deploy):** `backfill-payment-read-models`, Payment CQRS read flag açma.

---

## Verification checklist

### Startup log (tek satır aranır)

```text
CQRS startup configuration. Environment=Staging ... PaymentProjectionEnabled=False ... PaymentsListReadEnabled=False ... CommandDbCatalog=VetinityCommandDb_Staging QueryDbCatalog=VetinityQueryDb_Staging
```

| Kontrol | Beklenen |
|---|---|
| `Environment` | `Staging` |
| `CommandDbCatalog` ≠ `QueryDbCatalog` | **Evet** |
| `QueryDbCatalog=(not-configured)` | **Olmamalı** |
| `PaymentProjectionEnabled` | **False** |
| Tüm `Payments*ReadEnabled` | **False** |

### Health

| Endpoint | Beklenen |
|---|---|
| `GET /health/live` | 200 |
| `GET /health/ready` | 200; `sql` + `query-sql` Healthy |
| `GET /health` | Tam rapor erişilebilir |

### Smoke

- [ ] Swagger erişilebilir (exposure kararı operatör)
- [ ] Login → token
- [ ] Authenticated istek 200
- [ ] Tenant/clinic scoped endpoint smoke
- [ ] Payment list Command path (Query path log yok)

---

## Rollback preparation

Deploy **başlamadan önce** hazırlanmalı:

| Öğe | Açıklama |
|---|---|
| **Rollback klasörü** | Önceki publish output yedeklenebilir path (ör. `release-N-1\`) |
| **DB backup** | İlk deploy öncesi boş DB snapshot veya post-migrate backup |
| **Config snapshot** | Env var / secret store değerlerinin güvenli kaydı (secret değeri plaintext log'a değil) |
| **Git SHA kaydı** | Deploy edilen commit (ör. `e36c709` veya tag) |
| **Rollback runbook** | [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) §Rollback |

**API rollback:** process durdur → önceki publish klasörü → env var geri → restart → `/health/ready`.

**DB rollback:** EF down yok — backup restore veya boş staging'de drop/recreate + migrate/migrate-query.

---

## Payment CQRS deferred rollout

**İlk staging deploy'da Payment CQRS read flag'leri AÇILMAZ.**

Repo default (`appsettings.Staging.json`):

- `PaymentProjection:Enabled=false`
- Tüm `QueryReadModels:Payments*ReadEnabled=false`

**Ne zaman açılmalı:** First deploy smoke + altyapı doğrulandıktan **sonra**, ayrı operasyon fazı olarak:

1. [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md)
2. [`cqrs-17c-payment-cqrs-release-readiness.md`](../cqrs/cqrs-17c-payment-cqrs-release-readiness.md)

**Faz 0 önkoşulları (staging):**

- `ClientProjection:Enabled=true` (staging'de zaten true)
- `PetProjection:Enabled=true` → restart → `backfill-pet-projections`
- `PaymentProjection:Enabled=true` → restart → `backfill-payment-read-models` (parity InSync, exit 0)
- `/health/ready` → `payment-projection` Healthy, drift 0

**Faz 1:** Kademeli read flag açma (List → Dashboard → … → GetById); her flag restart + smoke.

Production'a geçiş: Staging Faz 0 + Faz 1 temiz olmadan production flag açılmamalı.

---

## Go / No-Go before starting actual deploy

### Go — tüm maddeler sağlanmalı

| # | Kriter |
|---|---|
| 1 | **Host kararı net** — Windows VPS/VM seçildi, erişim var |
| 2 | **Domain / TLS net** — staging hostname + sertifika planı |
| 3 | **SQL erişimi net** — sunucu erişilebilir, operatör admin/DDL yetkisi |
| 4 | **Secrets hazır** — JWT, Command + Query connection string, CORS, BaseUrl |
| 5 | **Publish artifact temiz** — yalnızca `appsettings.json` + `appsettings.Staging.json` (API); Development overlay yok |
| 6 | **Rollback klasör / backup hazır** — önceki release yedek path veya DB snapshot planı |
| 7 | **Operatör health / log erişimine sahip** — IIS log, Serilog path, `/health/ready` |

### No-Go — herhangi biri varsa deploy ertelenir

| # | Engel |
|---|---|
| 1 | **Secret store belirsiz** — connection string / JWT nereye yazılacağı net değil |
| 2 | **Query DB connection yok** — `ConnectionStrings__QueryConnection` tanımsız |
| 3 | **Command / Query aynı catalog** — backfill guard ve CQRS health başarısız olur |
| 4 | **Backup yok** — disposable staging bile bilinçli onay gerektirir |
| 5 | **TLS / domain belirsiz** — HTTPS binding veya geçici erişim planı yok |
| 6 | **`appsettings.Development.json` artifact'a giriyor** — `-p:EnvironmentName=Staging` eksik veya csproj regresyon |
| 7 | **Rollback planı yok** — önceki klasör / restore stratejisi tanımsız |

---

**DEPLOY-5:** Commit atılmadı (kullanıcı talimatı).
