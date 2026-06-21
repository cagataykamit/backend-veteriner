using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var cmd = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "help";

if (cmd is "help" or "-h" or "--help")
{
    PrintHelp();
    return 0;
}

var builder = Host.CreateApplicationBuilder(args);

if (builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddInfrastructure(builder.Configuration);

using var host = builder.Build();

using var scope = host.Services.CreateScope();
var sp = scope.ServiceProvider;
var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbMigrator");
var db = sp.GetRequiredService<AppDbContext>();
var hasher = sp.GetRequiredService<IPasswordHasher>();

try
{
    switch (cmd)
    {
        case "migrate":
            await db.Database.MigrateAsync();
            logger.LogInformation("EF Core MigrateAsync completed.");
            break;
        case "migrate-query":
            {
                var queryDb = sp.GetRequiredService<QueryDbContext>();
                await queryDb.Database.MigrateAsync();
                logger.LogInformation("Query DB MigrateAsync completed.");
                break;
            }
        case "seed":
            await RunSeedPipelineAsync(db, hasher, logger, CancellationToken.None);
            logger.LogInformation("Seed pipeline completed.");
            break;
        case "all":
            await db.Database.MigrateAsync();
            await RunSeedPipelineAsync(db, hasher, logger, CancellationToken.None);
            logger.LogInformation("Migrate + seed pipeline completed.");
            break;
        case "loadtest-seed":
            if (args.Length < 2)
            {
                Console.Error.WriteLine("loadtest-seed requires a profile argument (e.g. 'small').");
                PrintHelp();
                return 1;
            }

            var profile = args[1].Trim().ToLowerInvariant();
            if (profile != "small")
            {
                Console.Error.WriteLine(
                    $"Unknown load test profile: '{args[1]}'. Supported profiles: small");
                PrintHelp();
                return 1;
            }

            await LoadTestDataSeeder.SeedSmallAsync(db, logger, CancellationToken.None);
            logger.LogInformation("Load test seed ({Profile}) completed.", profile);
            break;
        case "rebuild-appointment-projections":
            {
                var batchSize = AppointmentProjectionRebuildService.DefaultBatchSize;
                for (var i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--batch-size"
                        && i + 1 < args.Length
                        && int.TryParse(args[i + 1], out var parsedBatchSize))
                    {
                        batchSize = Math.Max(1, parsedBatchSize);
                        i++;
                    }
                }

                var rebuild = sp.GetRequiredService<IAppointmentProjectionRebuildService>();
                var result = await rebuild.RebuildAsync(batchSize, CancellationToken.None);

                Console.WriteLine("Appointment projection rebuild completed successfully.");
                Console.WriteLine($"  Command appointments : {result.CommandAppointmentCount}");
                Console.WriteLine($"  Query appointments   : {result.QueryAppointmentCount}");
                Console.WriteLine($"  Pet activity rows    : {result.PetActivityCount}");
                Console.WriteLine($"  Client activity rows : {result.ClientActivityCount}");
                Console.WriteLine($"  Daily stats rows     : {result.DailyStatsCount}");
                Console.WriteLine($"  Pending outbox       : {result.PendingAppointmentOutboxCount}");
                Console.WriteLine($"  Dead-letter outbox   : {result.DeadLetterAppointmentOutboxCount}");
                Console.WriteLine($"  Duration             : {result.Duration.TotalSeconds:F2}s");
                logger.LogInformation(
                    "Appointment projection rebuild completed. Command={Command} Query={Query} DurationSec={DurationSec}",
                    result.CommandAppointmentCount,
                    result.QueryAppointmentCount,
                    result.Duration.TotalSeconds);
                break;
            }
        case "backfill-client-projections":
            {
                var batchSize = ClientReadModelBackfillService.DefaultBatchSize;
                Guid? tenantId = null;
                for (var i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--batch-size"
                        && i + 1 < args.Length
                        && int.TryParse(args[i + 1], out var parsedBatchSize))
                    {
                        batchSize = Math.Max(1, parsedBatchSize);
                        i++;
                    }
                    else if (args[i] == "--tenant"
                        && i + 1 < args.Length
                        && Guid.TryParse(args[i + 1], out var parsedTenant))
                    {
                        tenantId = parsedTenant;
                        i++;
                    }
                }

                var backfill = sp.GetRequiredService<IClientReadModelBackfillService>();
                var result = await backfill.BackfillAsync(tenantId, batchSize, CancellationToken.None);

                Console.WriteLine("Client read-model backfill completed successfully.");
                Console.WriteLine($"  Scope tenant         : {(result.ScopeTenantId?.ToString() ?? "<all tenants>")}");
                Console.WriteLine($"  Command clients      : {result.CommandClientCount}");
                Console.WriteLine($"  Query clients        : {result.QueryClientCount}");
                Console.WriteLine($"  Inserted             : {result.InsertedCount}");
                Console.WriteLine($"  Updated              : {result.UpdatedCount}");
                Console.WriteLine($"  Skipped (stale)      : {result.SkippedStaleCount}");
                Console.WriteLine($"  Parity in-sync       : {result.ParityInSync}");
                Console.WriteLine($"  Duration             : {result.Duration.TotalSeconds:F2}s");
                logger.LogInformation(
                    "Client read-model backfill completed. Command={Command} Query={Query} ParityInSync={ParityInSync} DurationSec={DurationSec}",
                    result.CommandClientCount,
                    result.QueryClientCount,
                    result.ParityInSync,
                    result.Duration.TotalSeconds);

                if (!result.ParityInSync)
                {
                    Console.Error.WriteLine(
                        "UYARI: Backfill sonrası parity in-sync değil. ClientsEnabled açmadan önce parity'yi doğrulayın.");
                    return 2;
                }

                break;
            }
        case "backfill-pet-projections":
            {
                var batchSize = PetReadModelBackfillService.DefaultBatchSize;
                Guid? tenantId = null;
                for (var i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--batch-size"
                        && i + 1 < args.Length
                        && int.TryParse(args[i + 1], out var parsedBatchSize))
                    {
                        batchSize = Math.Max(1, parsedBatchSize);
                        i++;
                    }
                    else if (args[i] == "--tenant"
                        && i + 1 < args.Length
                        && Guid.TryParse(args[i + 1], out var parsedTenant))
                    {
                        tenantId = parsedTenant;
                        i++;
                    }
                }

                var backfill = sp.GetRequiredService<IPetReadModelBackfillService>();
                var result = await backfill.BackfillAsync(tenantId, batchSize, CancellationToken.None);

                Console.WriteLine("Pet read-model backfill completed successfully.");
                Console.WriteLine($"  Scope tenant         : {(result.ScopeTenantId?.ToString() ?? "<all tenants>")}");
                Console.WriteLine($"  Command pets         : {result.CommandPetCount}");
                Console.WriteLine($"  Query pets           : {result.QueryPetCount}");
                Console.WriteLine($"  Inserted             : {result.InsertedCount}");
                Console.WriteLine($"  Updated              : {result.UpdatedCount}");
                Console.WriteLine($"  Skipped (stale)      : {result.SkippedStaleCount}");
                Console.WriteLine($"  Parity in-sync       : {result.ParityInSync}");
                Console.WriteLine($"  Duration             : {result.Duration.TotalSeconds:F2}s");
                logger.LogInformation(
                    "Pet read-model backfill completed. Command={Command} Query={Query} ParityInSync={ParityInSync} DurationSec={DurationSec}",
                    result.CommandPetCount,
                    result.QueryPetCount,
                    result.ParityInSync,
                    result.Duration.TotalSeconds);

                if (!result.ParityInSync)
                {
                    Console.Error.WriteLine(
                        "UYARI: Backfill sonrası parity in-sync değil. PetsEnabled açmadan önce parity'yi doğrulayın.");
                    return 2;
                }

                break;
            }
        case "backfill-payment-finance-projections":
            {
                var batchSize = PaymentFinanceBackfillService.DefaultBatchSize;
                Guid? tenantId = null;
                for (var i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--batch-size"
                        && i + 1 < args.Length
                        && int.TryParse(args[i + 1], out var parsedBatchSize))
                    {
                        batchSize = Math.Max(1, parsedBatchSize);
                        i++;
                    }
                    else if (args[i] == "--tenant"
                        && i + 1 < args.Length
                        && Guid.TryParse(args[i + 1], out var parsedTenant))
                    {
                        tenantId = parsedTenant;
                        i++;
                    }
                }

                var backfill = sp.GetRequiredService<IPaymentFinanceBackfillService>();
                var result = await backfill.BackfillAsync(tenantId, batchSize, CancellationToken.None);

                Console.WriteLine("Payment finance backfill completed successfully.");
                Console.WriteLine($"  Scope tenant         : {(result.ScopeTenantId?.ToString() ?? "<all tenants>")}");
                Console.WriteLine($"  Command payments     : {result.CommandPaymentCount}");
                Console.WriteLine($"  Query contributions  : {result.QueryContributionCount}");
                Console.WriteLine($"  Inserted             : {result.InsertedCount}");
                Console.WriteLine($"  Updated              : {result.UpdatedCount}");
                Console.WriteLine($"  Skipped (stale)      : {result.SkippedStaleCount}");
                Console.WriteLine($"  Recomputed buckets   : {result.RecomputedBucketCount}");
                Console.WriteLine($"  Count parity in-sync : {result.CountParityInSync}");
                Console.WriteLine($"  Daily bucket in-sync : {result.DailyBucketParityInSync}");
                Console.WriteLine($"  Duration             : {result.Duration.TotalSeconds:F2}s");
                logger.LogInformation(
                    "Payment finance backfill completed. Command={Command} QueryContribution={QueryContribution} CountParityInSync={CountParityInSync} DailyBucketParityInSync={DailyBucketParityInSync} DurationSec={DurationSec}",
                    result.CommandPaymentCount,
                    result.QueryContributionCount,
                    result.CountParityInSync,
                    result.DailyBucketParityInSync,
                    result.Duration.TotalSeconds);

                if (!result.CountParityInSync || !result.DailyBucketParityInSync)
                {
                    Console.Error.WriteLine(
                        "UYARI: Backfill sonrası parity in-sync değil. PaymentProjection açmadan önce parity'yi doğrulayın.");
                    return 2;
                }

                break;
            }
        default:
            Console.Error.WriteLine($"Unknown command: {args[0]}");
            PrintHelp();
            return 1;
    }

    return 0;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "DbMigrator failed.");
    return 1;
}

static async Task RunSeedPipelineAsync(
    AppDbContext db,
    IPasswordHasher hasher,
    ILogger logger,
    CancellationToken ct)
{
    // ConfigureBackendAsync ile aynı sıra (PermissionSeeder ayrıca DataSeeder içinde de çağrılır; davranış korunur).
    await PermissionSeeder.SeedAsync(db, logger, ct);
    await DataSeeder.SeedAsync(db, hasher, logger, ct);
    await AdminClaimSeeder.SeedAsync(db, ct);
    await InviteAssignableOperationClaimsSeeder.SeedAsync(db, ct);
    // Invite-assignable rollerin (Admin, ClinicAdmin, Veteriner, Sekreter, …) minimum permission bağlarını uygular.
    // Faz 4B-6: AdminClaimSeeder yalnızca PlatformAdmin claim'ine tüm permission'ları bağlar (admin@example.com platform).
    // RolePermissionBindingSeeder ise Map'teki rolleri seed eder ve "Admin" claim'i için whitelist-dışı (sistem)
    // permission bağlarını idempotent biçimde temizler — tenant Admin ile platform yöneticisi ayrımını korur.
    await RolePermissionBindingSeeder.SeedAsync(db, logger, ct);
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Backend.Veteriner.DbMigrator — şema ve seed (API startup dışında).

        Kullanım:
          dotnet run --project src/Backend.Veteriner.DbMigrator -- <komut>

        Komutlar:
          migrate        — EF Core MigrateAsync (command DB; bekleyen migrationları uygular)
          migrate-query  — QueryDbContext MigrateAsync (QueryConnection)
          seed           — Permission/Data/AdminClaim/InviteAssignable seed zinciri
          all            — önce migrate, sonra seed
          loadtest-seed  — yük testi sentetik veri (yalnızca VetinityCommandDb_LoadTest command DB; profil: small)
          rebuild-appointment-projections — Command DB randevularından Query read-model yeniden oluştur
                                            (--batch-size 1000 opsiyonel)
          backfill-client-projections     — Command DB client'larından Query ClientReadModels idempotent doldur
                                            (--batch-size 500 ve --tenant <guid> opsiyonel)
          backfill-pet-projections        — Command DB pet'lerinden Query PetReadModels idempotent doldur
                                            (--batch-size 500 ve --tenant <guid> opsiyonel)
          backfill-payment-finance-projections — Command DB payment'lerinden Query finance contribution +
                                            daily stats idempotent doldur (--batch-size 500 ve --tenant <guid> opsiyonel)

        Load test ortamı (DOTNET_ENVIRONMENT=LoadTest):
          Command DB : VetinityCommandDb_LoadTest  (DefaultConnection / migrate / loadtest-seed)
          Query DB   : VetinityQueryDb_LoadTest    (QueryConnection / migrate-query / rebuild hedefi)
          appsettings.LoadTest.json veya ortam değişkenleri ile sunucu/parola override edilebilir.

        Örnek:
          $env:DOTNET_ENVIRONMENT = "LoadTest"
          dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate
          dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
          dotnet run --project src/Backend.Veteriner.DbMigrator -- loadtest-seed small
          dotnet run --project src/Backend.Veteriner.DbMigrator -- rebuild-appointment-projections --batch-size 1000
          dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections --batch-size 500
          dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections --tenant 00000000-0000-0000-0000-000000000000
          dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections --batch-size 500
          dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-pet-projections --tenant 00000000-0000-0000-0000-000000000000
          dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-finance-projections --batch-size 500
          dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-finance-projections --tenant 00000000-0000-0000-0000-000000000000

        Bağlantı: ConnectionStrings:DefaultConnection (command), ConnectionStrings:QueryConnection (query).
        Şema için alternatif: dotnet ef database update --project src/Backend.Veteriner.Infrastructure --startup-project src/Backend.Veteriner.Api
        """);
}

public partial class Program;
