# Load test database setup

Yük testi ortamı **ayrı** command ve query veritabanları kullanır; geliştirme veya entegrasyon test DB'lerine dokunmaz.

## Hedef isimler

| Rol | Veritabanı adı |
|-----|----------------|
| Command (operasyon + outbox) | `VetinityCommandDb_LoadTest` |
| Query (read-model / projection) | `VetinityQueryDb_LoadTest` |

Kaynak kontrollü şablon: `appsettings.LoadTest.json` (API ve DbMigrator).

Sunucu, kimlik bilgisi veya farklı host için **User Secrets** (API ile aynı `UserSecretsId`) veya ortam değişkenleri kullanın:

- `ConnectionStrings__DefaultConnection`
- `ConnectionStrings__QueryConnection`

## DbMigrator akışı

```powershell
$env:DOTNET_ENVIRONMENT = "LoadTest"

# 1) Command şema
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate

# 2) Query şema
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query

# 3) Sentetik operasyon verisi (yalnız command DB)
dotnet run --project src/Backend.Veteriner.DbMigrator -- loadtest-seed small

# 4) Command randevularından Query read-model yeniden oluştur
dotnet run --project src/Backend.Veteriner.DbMigrator -- rebuild-appointment-projections --batch-size 1000
```

`loadtest-seed` güvenlik kontrolü yalnızca **`VetinityCommandDb_LoadTest`** command DB'sinde çalışır (`LoadTestDataSeeder.RequiredDatabaseName`). Query DB seed edilmez.

`migrate-query` ve `rebuild-appointment-projections` **`VetinityQueryDb_LoadTest`** (`QueryConnection`) hedefler.

## API (LoadTest ortamı)

```powershell
$env:DOTNET_ENVIRONMENT = "LoadTest"
dotnet run --project src/Backend.Veteriner.Api
```

`appsettings.LoadTest.json` her iki connection string'i tanımlar. k6 senaryoları için token üretimi: `tests/load/tools/Prepare-LoadTestTokens.ps1`.

## Projection / CQRS notları

Load test sırasında projector açık olabilir (`AppointmentProjection:Enabled=true` şablonda). Tam rebuild öncesi projector'ı durdurmak önerilir — bkz. [`appointment-projection-operations.md`](appointment-projection-operations.md).

Query read-model bayrakları (`QueryReadModels:*`) load test'te varsayılan **false**; bayrak açıldığında Query DB zorunludur, command fallback yoktur.

## İlgili sabitler

- `LoadTestDataSeeder.RequiredDatabaseName` → `VetinityCommandDb_LoadTest`
- `LoadTestDataSeeder.QueryDatabaseName` → `VetinityQueryDb_LoadTest` (dokümantasyon; seeder query'ye yazmaz)
