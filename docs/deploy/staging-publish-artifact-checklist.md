# Staging Publish Artifact Checklist

**Tür:** Staging deploy öncesi publish artifact dry-run / paket denetimi (DEPLOY-4 / DEPLOY-4A). **Production kod, test, appsettings içeriği, migration değişmedi.**

**DEPLOY-4A (2026-06-24):** Publish artifact hygiene — csproj `CopyToPublishDirectory=Never` ile gereksiz `appsettings.*.json` dosyaları publish output'tan exclude edildi. Staging/Production overlay exclude **`EnvironmentName` MSBuild property** ile koşullu (`-p:EnvironmentName=Staging` / `Production`).

**Ön durum:**

| Faz | Doküman | Commit |
|---|---|---|
| DEPLOY-0 | [`staging-environment-design.md`](staging-environment-design.md) | `a5a681e` |
| DEPLOY-1 | [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) | `e8a0bf8` |
| DEPLOY-2 | [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md) | `ec458a1` |
| DEPLOY-3 | [`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md) | `2f841ed` |

**İlgili dokümanlar:** [`staging-first-deploy-runbook.md`](staging-first-deploy-runbook.md) · [`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md) · [`windows-iis-staging-host-checklist.md`](windows-iis-staging-host-checklist.md)

**Git doğrulama (2026-06-24, DEPLOY-4A başlangıcı):**

| Kontrol | Sonuç |
|---|---|
| DEPLOY-4 commit | `118d252` — `docs(deploy): add staging publish artifact checklist` |
| `git status --short` (başlangıç) | **Temiz** — yalnızca DEPLOY-4A csproj değişiklikleri |
| Publish profile (`.pubxml`) | **Yok** |
| Dockerfile / publish script | **Yok** |

**Dry-run output klasörü:** `artifacts/deploy-dryrun/` (`.gitignore` satır 64: `artifacts/` — commitlenmez)

---

## API publish command

Repo root'tan Release publish — **`-p:EnvironmentName=Staging` zorunlu** (staging artifact için doğru appsettings exclude):

```powershell
dotnet publish src/Backend.Veteriner.Api/Backend.Veteriner.Api.csproj -c Release -p:EnvironmentName=Staging -o artifacts/deploy-dryrun/api
```

**Production publish (ileride):**

```powershell
dotnet publish src/Backend.Veteriner.Api/Backend.Veteriner.Api.csproj -c Release -p:EnvironmentName=Production -o <output>
```

**Proje:** `src/Backend.Veteriner.Api/Backend.Veteriner.Api.csproj` — `TargetFramework: net9.0`, `Microsoft.NET.Sdk.Web`

**Ön koşul:** `dotnet restore` (dry-run'da otomatik restore yapıldı).

**`EnvironmentName` uyarısı:** MSBuild property verilmezse Staging/Production overlay exclude koşulları çalışmaz; hem `appsettings.Staging.json` hem `appsettings.Production.json` publish output'a girebilir. Deploy publish komutlarında **`-p:EnvironmentName` açıkça set edilmelidir.**

**DEPLOY-4A dry-run:** **Başarılı** (exit 0).

---

## DbMigrator publish command

```powershell
dotnet publish src/Backend.Veteriner.DbMigrator/Backend.Veteriner.DbMigrator.csproj -c Release -o artifacts/deploy-dryrun/migrator
```

**Proje:** `src/Backend.Veteriner.DbMigrator/Backend.Veteriner.DbMigrator.csproj` — `OutputType: Exe`, `TargetFramework: net9.0`

**Csproj config copy:** `appsettings.json`, `appsettings.Development.json`, `appsettings.LoadTest.json` (`CopyToOutputDirectory: PreserveNewest`; Development/LoadTest **`CopyToPublishDirectory: Never`** — DEPLOY-4A). **`appsettings.Staging.json` yok** — Staging'de env var zorunlu ([`staging-config-secrets-inventory.md`](staging-config-secrets-inventory.md)).

**DEPLOY-4A dry-run:** **Başarılı** (exit 0).

---

## Publish appsettings hygiene (DEPLOY-4A)

`CopyToPublishDirectory=Never` ile publish artifact'ta yalnızca deploy hedefi için gerekli config dosyaları kalır. Kaynak dosyalar repo'da durur; local `dotnet run` / Debug output etkilenmez.

### API — staging artifact (`-p:EnvironmentName=Staging`)

| Dosya | Publish artifact |
|---|---|
| `appsettings.json` | **Evet** |
| `appsettings.Staging.json` | **Evet** |
| `appsettings.Development.json` | **Hayır** (her zaman) |
| `appsettings.IntegrationTests.json` | **Hayır** (her zaman) |
| `appsettings.LoadTest.json` | **Hayır** (her zaman) |
| `appsettings.Production.json` | **Hayır** (Staging publish'te exclude) |

### API — production artifact (`-p:EnvironmentName=Production`, ileride)

| Dosya | Publish artifact |
|---|---|
| `appsettings.json` | **Evet** |
| `appsettings.Production.json` | **Evet** |
| `appsettings.Staging.json` | **Hayır** (Production publish'te exclude) |
| `appsettings.Development.json` | **Hayır** (her zaman) |
| `appsettings.IntegrationTests.json` | **Hayır** (her zaman) |
| `appsettings.LoadTest.json` | **Hayır** (her zaman) |

Kaynak: `Backend.Veteriner.Api.csproj` — `Content Update` + koşullu `CopyToPublishDirectory=Never`.

### DbMigrator publish artifact — beklenen `appsettings*.json`

| Dosya | Publish artifact |
|---|---|
| `appsettings.json` | **Evet** (boş connection string şablonu) |
| `appsettings.Development.json` | **Hayır** |
| `appsettings.LoadTest.json` | **Hayır** |

Staging'de connection string **yalnızca env var** (`DOTNET_ENVIRONMENT=Staging`; `appsettings.Staging.json` repo'da yok).

---

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

**Publish dışı (DEPLOY-4A, `-p:EnvironmentName=Staging`):** `appsettings.Development.json`, `appsettings.IntegrationTests.json`, `appsettings.LoadTest.json`, `appsettings.Production.json`.

**Staging runtime:** `ASPNETCORE_ENVIRONMENT=Staging` ile yalnızca `appsettings.json` + `appsettings.Staging.json` overlay yüklenir.

---

## Expected DbMigrator artifact files

| Dosya / kalıp | Dry-run | Not |
|---|---|---|
| `Backend.Veteriner.DbMigrator.dll` | **Var** | Ana assembly |
| `Backend.Veteriner.DbMigrator.exe` | **Var** | Windows apphost |
| `appsettings.json` | **Var** | Boş connection string şablonu |
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

### Staging-relevant dosyalar (temiz — DEPLOY-4A dry-run)

| Dosya | Sonuç |
|---|---|
| `appsettings.json` | Connection string boş; `Jwt:Key` boş; `Billing:Iyzico` section adı var (değer yok) |
| `appsettings.Staging.json` | Connection string boş; Payment CQRS flags false |
| `web.config` | Secret yok |

**Publish artifact'ta yok (DEPLOY-4A):** `appsettings.Development.json`, `appsettings.IntegrationTests.json`, `appsettings.LoadTest.json`, `appsettings.Production.json` (API); DbMigrator'da Development/LoadTest yok.

### Önceki bulgu (DEPLOY-4) — mitigated

DEPLOY-4'te `appsettings.Development.json` publish output'ta sandbox key / dev PII taşıyordu. **DEPLOY-4A:** csproj `CopyToPublishDirectory=Never` ile publish artifact'tan exclude edildi. Kaynak dosya repo'da kalır (local dev); staging sunucuya kopyalanmaz.

### JWT / SQL parola

Publish output'ta **JWT signing key yok** (boş). SQL **Password=** dolu connection string yok. Pattern taraması (`Iyzico`, `ngrok`, `Password=`, `Jwt__Key`, `User Id=`): API'de yalnızca `appsettings.json` içinde boş `Billing:Iyzico` section adı eşleşti; DbMigrator **temiz**.

**Özet:** Staging deploy paketi publish aşamasında hijyenik; operatör env var injection ile devam eder.

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
| API `dotnet publish -c Release` | **Başarılı** | 2026-06-24 (DEPLOY-4A) |
| DbMigrator `dotnet publish -c Release` | **Başarılı** | 2026-06-24 (DEPLOY-4A) |
| API beklenen dosyalar | **Tam** — yalnızca `appsettings.json` + `appsettings.Staging.json` | |
| DbMigrator beklenen dosyalar | **Tam** — yalnızca `appsettings.json` | |
| DbMigrator `help` (DB yok) | **Başarılı** (exit 0) | DEPLOY-4 |
| Secret leak (publish artifact) | **Temiz** — Development overlay publish dışı | DEPLOY-4A |
| Git artifact leak | **Yok** (`artifacts/` ignored) | |
| `dotnet build --no-restore` | **Başarılı** (0 uyarı, 0 hata) | 2026-06-24 |

**Sonuç:** Staging deploy paketi publish aşamasında hijyenik; env var injection ile sunucuya deploy edilebilir.

---

## Open issues

| # | Konu | Durum |
|---|---|---|
| 1 | Publish tüm `appsettings.*.json` dosyalarını API output'a kopyalar | **Resolved (DEPLOY-4A)** — csproj exclude + `-p:EnvironmentName` |
| 2 | DbMigrator'da `appsettings.Staging.json` yok | **By design** — env var zorunlu |
| 3 | DbMigrator publish `appsettings.Development.json` taşır | **Resolved (DEPLOY-4A)** — csproj exclude |
| 4 | Publish profile / CI script yok | Açık — operasyon kararı |
| 5 | `.pdb` staging'de gerekli mi? | Açık — opsiyonel hardening |
| 6 | Repo `appsettings.Development.json` içinde sandbox credential | Açık — kaynak dosya tech debt (içerik değiştirilmedi) |

---

**DEPLOY-4 / DEPLOY-4A:** Commit atılmadı (kullanıcı talimatı).
