# Staging Publish Artifact Checklist

**Tür:** Staging deploy öncesi publish artifact dry-run / paket denetimi (DEPLOY-4). **Production kod, test, appsettings, migration değişmedi.**

**Ön durum:**

| Faz | Doküman | Commit |
|---|---|---|
| DEPLOY-0 | [`staging-environment-design.md`](staging-environment-design.md) | `a5a681e` |
| DEPLOY-1 | [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) | `e8a0bf8` |
| DEPLOY-2 | [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md) | `ec458a1` |
| DEPLOY-3 | [`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md) | `2f841ed` |

**İlgili dokümanlar:** [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) · [`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md) · [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md)

**Git doğrulama (2026-06-23, DEPLOY-4 başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| DEPLOY-3 commit | `2f841ed` — `docs(deploy): add staging config secrets inventory` |
| `git status --short` (başlangıç) | **Temiz değildi** — `ClinicAssignmentAccessGuard.cs` + test dosyası modified (DEPLOY-4 dışı değişiklik) |
| Publish profile (`.pubxml`) | **Yok** |
| Dockerfile / publish script | **Yok** |

**Dry-run output klasörü:** `artifacts/deploy-dryrun/` (`.gitignore` satır 64: `artifacts/` — commitlenmez)

---

## API publish command

Repo root'tan Release publish:

```powershell
dotnet publish src/Backend.Veteriner.Api/Backend.Veteriner.Api.csproj -c Release -o artifacts/deploy-dryrun/api
```

**Proje:** `src/Backend.Veteriner.Api/Backend.Veteriner.Api.csproj` — `TargetFramework: net9.0`, `Microsoft.NET.Sdk.Web`

**Ön koşul:** `dotnet restore` (dry-run'da otomatik restore yapıldı).

**DEPLOY-4 dry-run:** **Başarılı** (exit 0).

---

## DbMigrator publish command

```powershell
dotnet publish src/Backend.Veteriner.DbMigrator/Backend.Veteriner.DbMigrator.csproj -c Release -o artifacts/deploy-dryrun/migrator
```

**Proje:** `src/Backend.Veteriner.DbMigrator/Backend.Veteriner.DbMigrator.csproj` — `OutputType: Exe`, `TargetFramework: net9.0`

**Csproj config copy:** `appsettings.json`, `appsettings.Development.json`, `appsettings.LoadTest.json` (`CopyToOutputDirectory: PreserveNewest`). **`appsettings.Staging.json` yok** — Staging'de env var zorunlu ([`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md)).

**DEPLOY-4 dry-run:** **Başarılı** (exit 0).

**DB bağlantısı gerektirmeyen doğrulama:**

```powershell
dotnet artifacts/deploy-dryrun/migrator/Backend.Veteriner.DbMigrator.dll help
```

Exit 0; komut listesi (`migrate`, `migrate-query`, `seed`, `all`, backfill komutları) yazdırıldı.

---

## Expected API artifact files

| Dosya / kalıp | Dry-run | Not |
|---|---|---|
| `Backend.Veteriner.Api.dll` | **Var** | Ana assembly |
| `Backend.Veteriner.Api.exe` | **Var** | Windows apphost |
| `web.config` | **Var** | IIS AspNetCoreModuleV2; `hostingModel="inprocess"` |
| `appsettings.json` | **Var** | Base config; connection string ve JWT key **boş** |
| `appsettings.Staging.json` | **Var** | Staging overlay; CQRS default'ları false |
| `Backend.Veteriner.Api.deps.json` | **Var** | Runtime dependency manifest |
| `Backend.Veteriner.Api.runtimeconfig.json` | **Var** | .NET 9 runtime config |
| `Backend.Veteriner.Application.dll` | **Var** | Transitive publish |
| `Backend.Veteriner.Infrastructure.dll` | **Var** | Transitive publish |
| NuGet runtime DLL'leri | **Var** | ~107 dosya toplam (publish output) |

### Ek olarak publish'e dahil olan dosyalar (staging deploy'da dikkat)

| Dosya | Risk | Not |
|---|---|---|
| `appsettings.Development.json` | **Orta** | Dev makine adı, e-posta, Iyzico sandbox key'leri, ngrok URL — bkz. §Secret leak checks |
| `appsettings.Production.json` | Düşük | Şablon; `Password=` ve `Jwt:Key` boş |
| `appsettings.IntegrationTests.json` | Düşük | Test overlay |
| `appsettings.LoadTest.json` | Düşük | Load test overlay |
| `*.pdb` | Düşük | Debug symbols; staging'de opsiyonel |

**Staging runtime:** `ASPNETCORE_ENVIRONMENT=Staging` ile yalnızca `appsettings.json` + `appsettings.Staging.json` overlay yüklenir; Development dosyası **runtime'da okunmaz**. Dosya diskte kalır — operasyonel hijyen için sunucuya kopyalamadan önce silinebilir (open issue).

---

## Expected DbMigrator artifact files

| Dosya / kalıp | Dry-run | Not |
|---|---|---|
| `Backend.Veteriner.DbMigrator.dll` | **Var** | Ana assembly |
| `Backend.Veteriner.DbMigrator.exe` | **Var** | Windows apphost |
| `appsettings.json` | **Var** | Boş connection string şablonu |
| `appsettings.Development.json` | **Var** | Dev makine connection string (Trusted_Connection) |
| `appsettings.LoadTest.json` | **Var** | Load test şablonu |
| `Backend.Veteriner.DbMigrator.deps.json` | **Var** | |
| `Backend.Veteriner.DbMigrator.runtimeconfig.json` | **Var** | |
| `Backend.Veteriner.Infrastructure.dll` | **Var** | Migration/backfill için gerekli |
| NuGet runtime DLL'leri | **Var** | ~94 dosya toplam |

**Staging'de çalıştırma (sunucuda env var set edildikten sonra):**

```powershell
$env:DOTNET_ENVIRONMENT = "Staging"
dotnet Backend.Veteriner.DbMigrator.dll migrate
dotnet Backend.Veteriner.DbMigrator.dll migrate-query
dotnet Backend.Veteriner.DbMigrator.dll seed   # opsiyonel
```

**DEPLOY-4'te çalıştırılmadı:** `migrate`, `migrate-query`, backfill (DB bağlantısı gerektirir).

---

## IIS notes

`web.config` publish tarafından otomatik üretilir (elle repo'ya eklenmez):

```xml
<aspNetCore processPath="dotnet"
            arguments=".\Backend.Veteriner.Api.dll"
            stdoutLogEnabled="false"
            stdoutLogFile=".\logs\stdout"
            hostingModel="inprocess" />
```

| Konu | Checklist |
|---|---|
| Hosting model | **inprocess** (AspNetCoreModuleV2) |
| Physical path | Publish output klasörü (`current\`) |
| Env var | IIS app pool: `ASPNETCORE_ENVIRONMENT=Staging` + connection strings + JWT ([`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md)) |
| .NET runtime | ASP.NET Core **9** Hosting Bundle sunucuda kurulu olmalı |
| stdout log | Default kapalı; troubleshooting için geçici açılabilir |

Detaylı IIS checklist: [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md).

---

## Secret leak checks

Dry-run publish output'ta pattern taraması (`Password=`, `Jwt__Key`, sandbox key kalıpları, dev hostname).

### Staging-relevant dosyalar (temiz)

| Dosya | Sonuç |
|---|---|
| `appsettings.json` | Connection string boş; `Jwt:Key` boş |
| `appsettings.Staging.json` | Connection string boş; Payment CQRS flags false |
| `appsettings.Production.json` | `Password=` boş; `Jwt:Key` boş (şablon) |
| `web.config` | Secret yok |

### Bulgu: Development overlay artifact'ta mevcut

`appsettings.Development.json` **hem API hem DbMigrator** publish output'una kopyalanır (Sdk/content default davranışı / DbMigrator csproj `CopyToOutputDirectory`).

API `appsettings.Development.json` içeriği (özet — **staging sunucuya taşınmamalı / silinmeli**):

| Alan | Değer tipi | Staging riski |
|---|---|---|
| `ConnectionStrings` | Dev makine adı (`DESKTOP-*`), Trusted_Connection | Ortam fingerprint; parola yok |
| `Smtp:User` / `From` | Gerçek e-posta adresi | PII sızıntısı |
| `Billing:Iyzico:ApiKey` / `SecretKey` | Iyzico **sandbox** API credential | Gerçek sandbox secret (repo kaynak dosyasında) |
| `Billing:Iyzico:CallbackUrl` | ngrok URL | Dev tunnel fingerprint |
| `Billing:Iyzico:SandboxBuyerIp` | Gerçek IP | PII/fingerprint |

DbMigrator `appsettings.Development.json`: dev makine connection string (Trusted_Connection, parola yok).

### Staging runtime etkisi

`ASPNETCORE_ENVIRONMENT=Staging` / `DOTNET_ENVIRONMENT=Staging` ile Development overlay **yüklenmez**. Risk: yanlış ortam değişkeni, dosya incelemesi, backup sızıntısı.

### JWT / SQL parola

Publish output'ta **JWT signing key yok** (boş). SQL **Password=** dolu connection string yok (Production şablonu boş; Development Trusted_Connection).

**Özet:** Staging deploy paketi **üretilebilir**; operatör sunucuya kopyalamadan önce `appsettings.Development.json` (ve isteğe bağlı diğer ortam overlay'leri) **silmeli** veya deploy pipeline'da exclude etmeli (repo değişikliği — open issue).

---

## Git hygiene

| Kontrol | Sonuç |
|---|---|
| `.gitignore` `artifacts/` | **Evet** (satır 64) |
| `git status` publish sonrası | `artifacts/deploy-dryrun/` **görünmedi** (ignore altında) |
| Publish output commit | **Yapılmamalı** — dry-run klasörü geçici |
| `.gitignore` değişikliği | **Yapılmadı** (DEPLOY-4 kısıtı) |

Dry-run klasörü deploy sonrası silinebilir:

```powershell
Remove-Item -Recurse -Force artifacts/deploy-dryrun
```

---

## Dry-run result

| Adım | Sonuç | Tarih |
|---|---|---|
| API `dotnet publish -c Release` | **Başarılı** | 2026-06-23 |
| DbMigrator `dotnet publish -c Release` | **Başarılı** | 2026-06-23 |
| API beklenen dosyalar | **Tam** (dll, exe, web.config, appsettings, deps, runtimeconfig) | |
| DbMigrator beklenen dosyalar | **Tam** | |
| DbMigrator `help` (DB yok) | **Başarılı** (exit 0) | |
| Secret leak (staging config) | **Temiz** | |
| Secret leak (Development overlay diskte) | **Bulgu** — sandbox key / dev PII; runtime Staging'de aktif değil | |
| Git artifact leak | **Yok** (`artifacts/` ignored) | |
| `dotnet build --no-restore` | **Başarılı** (0 uyarı, 0 hata) | |

**Sonuç:** Local build makinesinde staging deploy paketi **üretilebilir**. Sunucuya geçmeden önce env var injection + Development overlay temizliği önerilir.

---

## Open issues

| # | Konu | Öneri |
|---|---|---|
| 1 | Publish tüm `appsettings.*.json` dosyalarını API output'a kopyalar | Staging deploy pipeline'da `appsettings.Development.json` (ve gereksiz overlay'ler) **exclude** veya sunucuda sil |
| 2 | DbMigrator'da `appsettings.Staging.json` yok | Staging'de connection string **yalnızca env var** — [`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md) ile uyumlu; dokümante edildi |
| 3 | DbMigrator publish `appsettings.Development.json` taşır | Migrator çalıştırırken `DOTNET_ENVIRONMENT=Staging` zorunlu; dev dosyası yüklenmez |
| 4 | Publish profile / CI script yok | Manuel `dotnet publish` veya pipeline adımı operasyon kararı |
| 5 | `.pdb` staging'de gerekli mi? | Opsiyonel; güvenlik/hardening kararı |
| 6 | Repo `appsettings.Development.json` içinde sandbox credential | Kaynak dosya hijyeni ayrı tech debt (DEPLOY-4 kapsamı dışı — kod değiştirilmedi) |
| 7 | DEPLOY-4 başlangıç working tree | Modified `ClinicAssignmentAccessGuard` dosyaları — deploy audit'ten bağımsız; commit öncesi temizlenmeli |

---

**DEPLOY-4:** Commit atılmadı (kullanıcı talimatı).
