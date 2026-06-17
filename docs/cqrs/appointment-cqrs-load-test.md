# CQRS appointment load test (CQRS-10)

Fiziksel CQRS geçişinde **Command Read (A)**, **Appointment Query Read (B)** ve **Full Appointment Query Read (C)** modlarını aynı veri seti ve workload ile karşılaştırır. Projection lag ayrı düşük yoğunluklu senaryoda ölçülür.

## Hedef veritabanları

| Rol | Veritabanı |
|-----|------------|
| Command | `VetinityCommandDb_LoadTest` |
| Query | `VetinityQueryDb_LoadTest` |

Eski adlar (`VetinityDb`, `VetinityLoadTestDb`, `VetinityQueryLoadTestDb`) kullanılmaz.

```powershell
$env:DOTNET_ENVIRONMENT = "LoadTest"
$env:ASPNETCORE_ENVIRONMENT = "LoadTest"
```

`appsettings.json` içindeki boş connection string birleşmesi nedeniyle DbMigrator/API için catalog override gerekebilir (secret loglamadan):

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=VetinityCommandDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
$env:ConnectionStrings__QueryConnection = "Server=localhost;Database=VetinityQueryDb_LoadTest;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=120"
```

LoadTest ortamında `Jwt:Key` boş olabilir; API başlatmadan önce (user secrets yerine) ortam override kullanın — **token üretimi ile aynı key olmalı**:

```powershell
$env:Jwt__Key = "loadtest-local-jwt-signing-key-32chars-min"
```

100 VU load testlerinde global rate limiter (varsayılan 200 istek/dk/IP) 429 üretir. LoadTest koşularında devre dışı bırakın (`appsettings.LoadTest.json` veya ortam):

```powershell
$env:RateLimiting__Enabled = "false"
```

Effective catalog doğrulama (secret loglamadan):

```powershell
sqlcmd -S localhost -Q "SELECT DB_NAME()" -d VetinityCommandDb_LoadTest -h -1 -W
sqlcmd -S localhost -Q "SELECT DB_NAME()" -d VetinityQueryDb_LoadTest -h -1 -W
```

User Secrets connection string override ediyorsa `ConnectionStrings__DefaultConnection` / `ConnectionStrings__QueryConnection` ortam değişkenleriyle LoadTest şablonunu zorlayın.

## DB hazırlama (API ve projector kapalı)

```powershell
$env:DOTNET_ENVIRONMENT = "LoadTest"

dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
dotnet run --project src/Backend.Veteriner.DbMigrator -- seed
dotnet run --project src/Backend.Veteriner.DbMigrator -- loadtest-seed small
dotnet run --project src/Backend.Veteriner.DbMigrator -- rebuild-appointment-projections --batch-size 1000
```

Rebuild öncesi `/health/ready` veya DbMigrator çıktısında:

- pending appointment outbox = 0
- dead-letter appointment outbox = 0

## Veri eşitliği (A/B/C arası)

Tercih sırası:

1. Aynı SQL backup’tan Command + Query restore
2. Deterministik reset + seed + rebuild (`Invoke-CqrsLoadDataReset.ps1`)
3. Mevcut güvenli reset (K6 notu temizliği + rebuild)

```powershell
# Tam hazırlık
.\tests\load\tools\Invoke-CqrsLoadDataReset.ps1 -Method full

# Modlar arası (write-mix sonrası)
.\tests\load\tools\Invoke-CqrsLoadDataReset.ps1 -Method rebuild-only
```

Her mod öncesi raporlanan sayılar: Tenants, Clinics, Clients, Pets, Appointments, AppointmentReadModels.

## Token hazırlama

```powershell
.\tests\load\tools\Prepare-LoadTestTokens.ps1 -AllowInsecureLocalhostCertificate -Force
```

Çıktı: `tests/load/.tokens/clinic-tokens.json` (gitignore).

## API modları (config dosyası düzenleme yok)

API’yi her mod değişiminde yeniden başlatın:

```powershell
dotnet run --project src/Backend.Veteriner.Api --launch-profile https
```

Base URL: `https://localhost:7173`

### A — command

```powershell
$env:QueryReadModels__AppointmentsEnabled = "false"
$env:QueryReadModels__DashboardAppointmentsEnabled = "false"
$env:AppointmentProjection__Enabled = "true"
```

### B — appointment-query

```powershell
$env:QueryReadModels__AppointmentsEnabled = "true"
$env:QueryReadModels__DashboardAppointmentsEnabled = "false"
$env:AppointmentProjection__Enabled = "true"
```

### C — full-query

```powershell
$env:QueryReadModels__AppointmentsEnabled = "true"
$env:QueryReadModels__DashboardAppointmentsEnabled = "true"
$env:AppointmentProjection__Enabled = "true"
```

## Preflight

```powershell
.\tests\load\tools\Test-CqrsLoadPreflight.ps1 `
  -BaseUrl https://localhost:7173 `
  -Mode full-query `
  -TokenFile tests\load\.tokens\clinic-tokens.json
```

Kontroller: localhost guard, token dosyası, `/health/ready`, `appointment-projection` (pending/retry/dead=0, projectionEnabled=true), `query-sql` Healthy, mode flag eşleşmesi.

## Test matrisi

| Aşama | Mode | Workload | VU | Süre |
|-------|------|----------|-----|------|
| Warm-up | her mod veya ilk run | read | 50 | 2m |
| Read | A command | read | 100 | 5m |
| Read | B appointment-query | read | 100 | 5m |
| Read | C full-query | read | 100 | 5m |
| Write-mix | A command | write-mix | 100 | 5m |
| Write-mix | C full-query | write-mix | 100 | 5m |
| Projection lag | C full-query | projection-lag | 2 | 5m |
| Soak | C full-query | write-mix | 100 | 15m |

Koşu örneği:

```powershell
.\tests\load\tools\Run-CqrsLoadCase.ps1 `
  -Mode full-query `
  -Workload read `
  -Vus 100 `
  -Duration 5m `
  -BaseUrl https://localhost:7173 `
  -TokenFile tests\load\.tokens\clinic-tokens.json
```

## Workload scriptleri

| Workload | Script |
|----------|--------|
| read | `tests/load/cqrs-read.js` |
| write-mix | `tests/load/panel-appointment-write-mix.js` |
| projection-lag | `tests/load/appointment-projection-lag.js` |

Read metrikleri: `appointment_list_duration`, `appointment_calendar_duration`, `dashboard_duration`.

Projection lag metrikleri: `appointment_projection_create_lag_ms`, `appointment_projection_reschedule_lag_ms`, `appointment_projection_cancel_lag_ms`, `appointment_projection_timeout_rate`, `appointment_projection_wrong_state_rate`, `appointment_projection_poll_requests`.

Lag ortam değişkenleri: `PROJECTION_POLL_INTERVAL_MS=200`, `PROJECTION_TIMEOUT_MS=10000`.

## Smoke (uzun testlerden önce)

```powershell
.\tests\load\tools\Test-CqrsLoadSmoke.ps1 -BaseUrl https://localhost:7173
```

## Sonuç karşılaştırma

```powershell
.\tests\load\tools\Compare-CqrsLoadResults.ps1 `
  -ResultsDirectory tests\load\results `
  -OutputPath tests\load\results\comparison.csv
```

## Perf ve Query Store

Koşu sırasında veya sonrasında:

```powershell
.\tests\load\tools\Collect-CqrsLoadPerf.ps1 -DurationSeconds 300 -OutputPath tests\load\results\perf-last.json
```

Query Store / DMV:

```powershell
sqlcmd -S localhost -d VetinityCommandDb_LoadTest -i tests\load\sql\cqrs-top-queries.sql -v DatabaseName=VetinityCommandDb_LoadTest
sqlcmd -S localhost -d VetinityQueryDb_LoadTest -i tests\load\sql\cqrs-top-queries.sql -v DatabaseName=VetinityQueryDb_LoadTest
```

## Production guard

- Preflight yalnızca localhost / `CQRS_LOAD_ALLOWED_HOST` ile izin verilen host’larda çalışır.
- Production `appsettings` ve bayrakları değiştirilmez; testler LoadTest + ortam override ile yapılır.
- Token, parola ve connection string loglanmaz.

## Rollback

1. API’yi durdurun.
2. LoadTest ortam değişkenlerini temizleyin.
3. Development veya Production profiline dönün.
4. Gerekirse backup’tan LoadTest DB restore.
5. `tests/load/results/` ve `.tokens/` commitlenmez.

## Sonuç dosyaları

`tests/load/results/` gitignore altındadır; **commit atmayın**.
