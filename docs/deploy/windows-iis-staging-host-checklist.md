# Windows/IIS Staging Host Checklist

**Tür:** Operasyonel Windows/IIS staging host hazırlık checklist (DEPLOY-2). **Production kod, test, appsettings, migration, IIS config ve script oluşturulmadı.**

**Ön durum:**

| Faz | Doküman | Commit |
|---|---|---|
| DEPLOY-0 | [`staging-environment-design.md`](staging-environment-design.md) | `a5a681e` |
| DEPLOY-1 | [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) | `e8a0bf8` |
| Diagram normalize | ASCII diyagramlar | `abc0f9d` |

**İlgili dokümanlar:** [`staging-environment-design.md`](staging-environment-design.md) · [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) · [`cqrs-17a-payment-read-model-production-rollout-runbook.md`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md) · [`README.md`](../../README.md)

**Git doğrulama (2026-06-23, DEPLOY-2 başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** |
| DEPLOY-1 commit | `e8a0bf8` — `docs(deploy): add first staging deploy runbook` |
| DEPLOY-0 commit | `a5a681e` — `docs(deploy): add staging environment design` |

**Kapsam:** Windows Server + IIS üzerinde ilk staging host hazırlığı. **Payment CQRS read flag'leri ilk kurulumda açılmaz.**

---

## Target host shape

```text
+----------------------------------------------------------+
| Windows Server / Windows VPS (tek VM)                     |
|                                                           |
| IIS (reverse proxy + TLS termination)                     |
|   -> ASP.NET Core Module v2 -> Kestrel (in-process/out-of-process)
|                                                           |
| Backend.Veteriner.Api (tek instance, tek app pool)        |
| ASPNETCORE_ENVIRONMENT=Staging                            |
|                                                           |
| SQL Server (ayni makine veya erisilebilir host)           |
|   +- VetinityCommandDb_Staging   (Command DB)             |
|   +- VetinityQueryDb_Staging     (Query DB)               |
|                                                           |
| DbMigrator: deploy adiminda CLI (surekli servis degil)    |
+----------------------------------------------------------+
```

| Karar | Değer |
|---|---|
| OS | Windows Server veya desteklenen Windows VPS |
| Web sunucu | **IIS** + ASP.NET Core Hosting Bundle |
| API instance | **1** (projection `ClaimingEnabled=false`) |
| SQL | Ayni instance, **iki farkli catalog zorunlu** |
| Ortam | `Staging` -> `appsettings.Staging.json` overlay |

---

## Required software

Asagidaki yazilimlar staging host'ta kurulu veya erisilebilir olmalidir. **Surum numarasi uydurulmaz**; proje `net9.0` hedefler.

| # | Yazilim | Not |
|---|---|---|
| 1 | **Windows Server** veya Windows VPS | Tek VM baslangic icin yeterli |
| 2 | **IIS** (Web Server role) | ASP.NET Core Module icin gerekli rol ozellikleri |
| 3 | **ASP.NET Core Hosting Bundle** | Proje **TargetFramework `net9.0`** ile uyumlu bundle — [.NET 9 download](https://dotnet.microsoft.com/download/dotnet/9.0) Hosting Bundle |
| 4 | **SQL Server** | Ayni VM veya erisilebilir ayri host |
| 5 | **SSMS** veya **sqlcmd** alternatifi | DB olusturma, yetki, smoke SQL |
| 6 | **Git** | Opsiyonel — publish genelde build machine'den artifact ile |
| 7 | **TLS sertifikasi** | Staging domain icin (Let's Encrypt, internal CA, cloud cert) |
| 8 | **.NET SDK** (opsiyonel) | Sunucuda `dotnet run` ile DbMigrator calistirilacaksa; tercihen publish edilmis migrator DLL |

**Kurulum sonrasi dogrulama:**

- [ ] `dotnet --list-runtimes` ciktisinda **Microsoft.AspNetCore.App 9.x** gorunur (Hosting Bundle kurulumu)
- [ ] IIS Manager acilir; **AspNetCoreModuleV2** modulu mevcut
- [ ] SQL Server'a localhost veya planlanan host uzerinden baglanti test edildi

---

## Windows Server preparation

- [ ] Windows Update uygulandi (guvenlik yamalari)
- [ ] Sunucu saati / timezone UTC veya operasyon standardi ile uyumlu (NTP)
- [ ] Yonetici hesaplari sinirli; servis hesabi tanimlandi (app pool identity)
- [ ] Disk alani yeterli (publish output + log + SQL data/log dosyalari)
- [ ] Antivirus / real-time scan: IIS app klasoru ve SQL data path icin operasyon politikasi netlesti
- [ ] Uzaktan erisim: RDP yalnizca izinli IP/VPN (asagida §Firewall)
- [ ] Host adi / DNS staging icin planlandi

---

## IIS preparation

- [ ] **Server Manager** -> Add Roles and Features -> **Web Server (IIS)**
- [ ] Gerekli IIS ozellikleri (minimum):
  - Web Server
  - Application Development: **WebSocket** (gerekirse), **Application Initialization** (opsiyonel)
  - Security: **Request Filtering**, **Windows Authentication** (kullanilmiyorsa kapali kalabilir)
  - Management: IIS Management Console
- [ ] **ASP.NET Core Hosting Bundle** kuruldu ve **IIS restart** yapildi (`iisreset` veya reboot)
- [ ] Default Web Site devre disi birakilabilir (operasyon tercihi)
- [ ] Staging site icin bos site + binding planlandi

---

## ASP.NET Core Hosting Bundle

| Konu | Checklist |
|---|---|
| Surum | **.NET 9** Hosting Bundle — repo `Backend.Veteriner.Api.csproj` -> `net9.0` |
| Kurulum | Microsoft resmi installer; kurulum sonrasi IIS yeniden baslat |
| Dogrulama | `%ProgramFiles%\dotnet\shared\Microsoft.AspNetCore.App\9.*` mevcut |
| Modul | `AspNetCoreModuleV2` IIS Modules listesinde |
| web.config | Publish output otomatik uretir; elle repo'ya eklenmez |

**Not:** SDK sunucuda zorunlu degil; yalnizca publish edilmis `Backend.Veteriner.Api.dll` calistirilir.

---

## SQL Server preparation

| Kural | Aciklama |
|---|---|
| Ayni instance | **Yeterli** — tek SQL Server instance uzerinde iki database |
| Iki catalog **zorunlu** | `VetinityCommandDb_Staging` (Command) + `VetinityQueryDb_Staging` (Query) |
| QueryConnection | **Command DB ile ayni catalog olmamali** — backfill guard reddeder |
| Auth secimi | **Windows Authentication** veya **SQL login** — operasyon karari |
| Minimum yetki | App/migrator login: yalniz ilgili iki DB'de gerekli DDL/DML (operasyon standardina gore daralt) |

### On kontroller

- [ ] SQL Server servisi calisiyor
- [ ] TCP/IP etkin (uzak SQL ise; ayni VM'de shared memory/pipes genelde yeterli)
- [ ] Collation operasyon standardi ile uyumlu
- [ ] Backup klasoru / maintenance plani (staging icin operasyon karari)

---

## Database creation

Ornek operasyonel adimlar (SSMS veya sqlcmd). **Sunucu adi, dosya yolu ve login operatore birakilir.**

```sql
-- Ornek: catalog adlari sabit; path ve login operatore ozel
CREATE DATABASE VetinityCommandDb_Staging;
CREATE DATABASE VetinityQueryDb_Staging;
GO
```

- [ ] `VetinityCommandDb_Staging` olusturuldu
- [ ] `VetinityQueryDb_Staging` olusturuldu
- [ ] Catalog adlari **birbirinden farkli** dogrulandi
- [ ] App/migrator login olusturuldu ve iki DB'ye map edildi (veya Windows auth + app pool identity)
- [ ] **Secret connection string repoya yazilmadi**

---

## Application folder layout

**Oneri only** — repo'ya dosya eklenmez; operatör sunucuda olusturur.

```text
C:\inetpub\Vetinity\staging\
  api\
    current\          <- IIS physical path (aktif release)
    releases\
      yyyyMMdd-HHmm\  <- her deploy yeni klasor
  logs\               <- Serilog file sink (opsiyonel ek path)
  migrator\           <- publish edilmis DbMigrator artifact
```

Checklist:

- [ ] `C:\inetpub\Vetinity\staging\api\releases\` olusturuldu
- [ ] `C:\inetpub\Vetinity\staging\api\current\` ilk publish ile doldurulacak
- [ ] `C:\inetpub\Vetinity\staging\logs\` yazilabilir (app pool identity)
- [ ] `C:\inetpub\Vetinity\staging\migrator\` publish edilmis DbMigrator icin
- [ ] Rollback: onceki release klasoru saklanir; `current` kopya veya junction ile degistirilir (operasyon karari)

---

## Environment variables and secrets

Degerler **IIS Application Pool -> Advanced Settings -> Environment Variables** veya site `web.config` `environmentVariables` (publish output) uzerinden verilir. **Gercek secret yazilmaz.**

### Zorunlu

| Degisken | Deger (placeholder) |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` |
| `ConnectionStrings__DefaultConnection` | `Server=<SQL_HOST>;Database=VetinityCommandDb_Staging;User Id=<USER>;Password=<PASSWORD>;TrustServerCertificate=<true\|false>;MultipleActiveResultSets=True` |
| `ConnectionStrings__QueryConnection` | `Server=<SQL_HOST>;Database=VetinityQueryDb_Staging;User Id=<USER>;Password=<PASSWORD>;TrustServerCertificate=<true\|false>;MultipleActiveResultSets=True` |

**Alternatif:** Production sablonu `SqlServer` kullaniyorsa `ConnectionStrings__SqlServer` (Command); `DefaultConnection` yoksa Infrastructure fallback devreye girer. Staging'de **tek Command key** tutarliligi onerilir.

| Degisken | Deger (placeholder) |
|---|---|
| `Jwt__Key` | `<STAGING_JWT_SIGNING_KEY_MIN_32_CHARS>` |
| `App__BaseUrl` | `https://staging-api.<DOMAIN>` |
| `AllowedOrigins__0` | `https://staging-app.<DOMAIN>` |

### Payment CQRS — ilk deploy (false)

| Degisken | Ilk staging deger |
|---|---|
| `PaymentProjection__Enabled` | `false` |
| `QueryReadModels__PaymentsListReadEnabled` | `false` |
| `QueryReadModels__DashboardRecentPaymentsReadEnabled` | `false` |
| `QueryReadModels__ClientPaymentSummaryReadEnabled` | `false` |
| `QueryReadModels__PaymentsReportReadEnabled` | `false` |
| `QueryReadModels__PaymentsReportExportReadEnabled` | `false` |
| `QueryReadModels__PaymentsGetByIdReadEnabled` | `false` |

**Not:** `appsettings.Staging.json` repo sablonu zaten bu default'lari icerir; production-like deploy'da **env var overlay** tercih edilir (secret repo disinda).

Checklist:

- [ ] Tum zorunlu env var'lar set edildi
- [ ] Connection string'lerde **farkli Database=** catalog adlari
- [ ] JWT key repoya/commit'e yazilmadi
- [ ] Payment CQRS flag'leri ilk deploy'da **false**

---

## Publish artifact deployment

Build machine (repo root):

```powershell
dotnet restore Veteriner.sln
dotnet publish src/Backend.Veteriner.Api/Backend.Veteriner.Api.csproj -c Release -o .\publish\staging-api
dotnet publish src/Backend.Veteriner.DbMigrator/Backend.Veteriner.DbMigrator.csproj -c Release -o .\publish\staging-dbmigrator
```

Sunucuya kopyalama:

- [ ] `publish\staging-api\*` -> `C:\inetpub\Vetinity\staging\api\releases\<yyyyMMdd-HHmm>\`
- [ ] Aktif release -> `C:\inetpub\Vetinity\staging\api\current\` (kopya veya robocopy / junction)
- [ ] `publish\staging-dbmigrator\*` -> `C:\inetpub\Vetinity\staging\migrator\`
- [ ] `web.config` publish output ile geldi (AspNetCoreModuleV2 handler)

---

## DbMigrator execution

**API startup migration yapmaz.** Migrator, sunucuda veya build machine'den **Staging connection string'leri** ile calistirilir.

### On kosullar

- [ ] Publish edilmis migrator artifact hazir (`C:\inetpub\Vetinity\staging\migrator\`)
- [ ] `DOTNET_ENVIRONMENT=Staging` (veya env var'lar dogrudan set)
- [ ] `ConnectionStrings__DefaultConnection` ve `ConnectionStrings__QueryConnection` migrator process icin erisilebilir

### Ilk staging sira

```powershell
cd C:\inetpub\Vetinity\staging\migrator
$env:DOTNET_ENVIRONMENT = "Staging"
# ConnectionStrings env var'lari app pool ile ayni veya migrator'a ozel set edilmis olmali

dotnet Backend.Veteriner.DbMigrator.dll migrate
dotnet Backend.Veteriner.DbMigrator.dll migrate-query
dotnet Backend.Veteriner.DbMigrator.dll seed
```

| Komut | Repo adi | Rol |
|---|---|---|
| Command schema | `migrate` | `AppDbContext` MigrateAsync |
| Query schema | `migrate-query` | `QueryDbContext` MigrateAsync |
| Seed (opsiyonel) | `seed` | Permission/data seed |
| `all` | migrate + seed | **Query icermez** — `migrate-query` ayri zorunlu |

Alternatif (repo'dan build machine):

```powershell
$env:DOTNET_ENVIRONMENT = "Staging"
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
dotnet run --project src/Backend.Veteriner.DbMigrator -- seed
```

### Payment CQRS — **ilk deploy'da calistirilmaz**

Asagidakiler **ayri rollout fazinda** ([`cqrs-17a`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md)):

- `PaymentProjection__Enabled=true` -> API restart
- `backfill-client-projections` / `backfill-pet-projections`
- `backfill-payment-read-models` (exit 0, parity InSync)

Checklist:

- [ ] `migrate` basarili
- [ ] `migrate-query` basarili
- [ ] (Opsiyonel) `seed` basarili
- [ ] **Sonra** IIS app pool start / recycle
- [ ] Payment backfill **ilk kurulumda atlandi** (bilincli)

---

## IIS app pool and site settings

### Application Pool

| Ayar | Deger |
|---|---|
| .NET CLR version | **No Managed Code** |
| Managed pipeline mode | Integrated |
| Identity | ApplicationPoolIdentity veya ozel servis hesabi (SQL Windows auth icin) |
| Start Mode | AlwaysRunning (opsiyonel) |
| Idle Time-out | Operasyon karari (0 = idle kapanmaz) |
| Environment Variables | `ASPNETCORE_ENVIRONMENT=Staging` + ConnectionStrings + Jwt + CQRS flags |

### Site

| Ayar | Deger |
|---|---|
| Physical path | `C:\inetpub\Vetinity\staging\api\current` |
| Binding | HTTPS 443 + staging hostname; HTTP 80 redirect (operasyon karari) |
| Application | Root application, app pool yukaridaki pool |
| Hosting | ASP.NET Core Module v2 (`web.config` processPath=`dotnet`) |

### Permissions

- [ ] App pool identity: site klasorunde **Read & Execute**
- [ ] Log klasoru: **Modify** (Serilog file sink kullaniliyorsa)
- [ ] SQL: Windows auth ise app pool identity SQL login; SQL auth ise connection string login

### Troubleshooting (kisitli kullanim)

- [ ] `stdoutLogEnabled=true` yalnizca kisa sureli debug (web.config); production-like staging'de kapali tutulmasi onerilir
- [ ] Request limits / timeouts: buyuk export yok ilk deploy'da; bilincli ayar gerekirse IIS `maxAllowedContentLength` gozden gecirilir

### Swagger

- [ ] Repo **tum ortamlarda Swagger acik** — staging'de erisim **IP allowlist, VPN veya auth gateway** operasyon karari
- [ ] Public internet'e acik birakilmamali (varsayilan oneri)

---

## TLS and domain

- [ ] Staging DNS: `staging-api.<DOMAIN>` -> sunucu IP
- [ ] TLS sertifikasi binding'e baglandi (443)
- [ ] HTTP 80 -> HTTPS redirect karari uygulandi (opsiyonel ama onerilir)
- [ ] `App__BaseUrl` sertifika hostname ile uyumlu
- [ ] Frontend CORS: `AllowedOrigins__0` staging app URL ile eslesir

---

## Firewall checklist

| Kural | Oneri |
|---|---|
| **443** | Acik — staging API (kisitli kaynak IP/VPN tercih) |
| **80** | Redirect veya kapali |
| **1433 / SQL** | **Public acilmamali** — localhost veya private network |
| **RDP 3389** | Kisitli IP / VPN only |
| **/health/ready** | Public ise rate limit / IP kisiti; monitoring agindan erisim |
| WinRM / diger | Kapali veya kisitli |

- [ ] Windows Firewall kurallari dokumante edildi
- [ ] Cloud NSG / edge firewall (varsa) SQL portu disariya kapali

---

## Logging checklist

### Serilog

Base `appsettings.json`: Console + rolling file. Production sablonu Linux path kullanir; staging'de operatör path belirler veya platform log toplar.

- [ ] Log dizini app pool identity tarafindan yazilabilir
- [ ] Disk rotation / retention (base: 14 gun retained file count ornegi)
- [ ] Event Viewer: ASP.NET Core Module / Windows Logs -> Application hatalari izlenir

### Startup log (zorunlu dogrulama)

Deploy/restart sonrasi log'da tek satir:

```text
CQRS startup configuration. Environment=Staging ... PaymentProjectionEnabled=False ... PaymentsListReadEnabled=False ... CommandDbCatalog=VetinityCommandDb_Staging QueryDbCatalog=VetinityQueryDb_Staging
```

- [ ] `Environment=Staging`
- [ ] `CommandDbCatalog` != `QueryDbCatalog`
- [ ] `QueryDbCatalog=(not-configured)` **yok**
- [ ] Tum `Payments*ReadEnabled=False`
- [ ] `PaymentProjectionEnabled=False` (ilk deploy)

---

## Health check validation

| Endpoint | Beklenti |
|---|---|
| `GET /health/live` | 200 |
| `GET /health/ready` | 200 (sorun yoksa); Degraded/Unhealthy -> 503 |

Kontrol edilecek entry'ler:

- [ ] `sql` — Healthy
- [ ] `query-sql` — Healthy, bekleyen migration yok
- [ ] `outbox` — Healthy veya kabul edilebilir Degraded
- [ ] `payment-projection` — projection kapaliysa drift degerlendirilmez
- [ ] `client-projection` — staging sablonunda enabled

Ornek (PowerShell, sunucudan veya operator workstation):

```powershell
Invoke-RestMethod -Uri "https://staging-api.<DOMAIN>/health/ready" -Method Get
```

---

## Rollback checklist

### Uygulama rollback

- [ ] IIS app pool **Stop**
- [ ] `current` klasorunu onceki `releases\<timestamp>` ile degistir (veya junction)
- [ ] Env var / secret onceki degere dondur (CQRS flags **false**)
- [ ] App pool **Start** / recycle
- [ ] `GET /health/ready` Healthy
- [ ] Auth + temel endpoint smoke
- [ ] Startup log flag dogrulama

### Payment CQRS acildiysa

- [ ] Tum `QueryReadModels__*ReadEnabled` -> `false`
- [ ] `PaymentProjection__Enabled` -> `false` (opsiyonel; read flag'ler kapali olmali)
- [ ] Recycle -> Command DB path smoke

### Database rollback

- [ ] EF **down** otomasyonu repo'da yok
- [ ] Strateji: deploy oncesi backup veya bos staging'de drop/recreate + `migrate` + `migrate-query`
- [ ] Command ve Query **ayri ayri** restore

---

## Security checklist

- [ ] Connection string ve JWT **repo'ya yazilmadi**
- [ ] Secret store veya encrypted config (Azure Key Vault, DPAPI, IIS secret — operasyon karari)
- [ ] SQL login minimum privilege; sysadmin kullanilmiyor
- [ ] TLS aktif; zayif protokol kapali
- [ ] Swagger public exposure **bilincli karar** — varsayilan: kisitli
- [ ] Staging verisi: production copy ise **anonimlestirilmis**
- [ ] `/health/ready` gereksiz yere public degil
- [ ] RDP kisitli; guclu parola / MFA
- [ ] Rate limiting staging'de acik (`RateLimiting:Enabled=true` sablon)
- [ ] Duzenli backup (Command + Query) — operasyon plani

---

## Open decisions

| # | Karar | Secenekler |
|---|---|---|
| 1 | Staging domain | `staging-api.<domain>`, internal DNS |
| 2 | SQL auth model | Windows (app pool identity) vs SQL login |
| 3 | SQL konumu | Ayni VM vs ayri SQL host |
| 4 | Secret yonetimi | IIS env only, Key Vault, encrypted web.config |
| 5 | Release switching | Kopya vs junction vs symlink |
| 6 | HTTP -> HTTPS redirect | Evet / hayir |
| 7 | Swagger erisimi | VPN, IP allowlist, public |
| 8 | CI/CD | Manuel robocopy vs pipeline |
| 9 | Seed stratejisi | DbMigrator seed vs anonymized restore |
| 10 | Payment CQRS faz tarihi | Ilk host dogrulama sonrasi 17A |
| 11 | Log aggregation | Dosya only vs Application Insights / ELK |
| 12 | Backup sikligi | Gunluk full, snapshot, disposable staging |

---

## Final recommendation

| Soru | Oneri |
|---|---|
| **Windows/IIS baslangic icin uygun mu?** | **Evet** — repo Windows/IIS + Hosting Bundle desenine uygun; DEPLOY-0/1 ile hizali; tek VM staging icin pratik |
| **Tek VM yeterli mi?** | **Evet (baslangic)** — API tek instance + SQL ayni makine veya erisilebilir ayri SQL; Payment projection claiming kapali |
| **Ne zaman ayri SQL Server?** | SQL CPU/IO baskisi, staging/production izolasyonu, managed SQL (Azure SQL) gereksinimi, backup/HA ihtiyaci |
| **Ne zaman load balancer?** | API multi-instance + projection claiming acildiginda veya HA gerektiginde — **ilk staging'de gerekmez** |
| **Ilk kurulumdan once netlesmesi gerekenler** | Staging domain + TLS · SQL auth model · secret store · Swagger erisim politikasi · backup stratejisi · frontend `AllowedOrigins` · release klasor stratejisi |

**Sonraki adim:** Bu checklist tamamlandiktan sonra [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) deploy adimlari uygulanir; host dogrulandiktan sonra Payment CQRS icin [`cqrs-17a`](../cqrs/cqrs-17a-payment-read-model-production-rollout-runbook.md) ayri faz acilir.

**DEPLOY-2:** Commit atılmadı (kullanıcı talimatı).
