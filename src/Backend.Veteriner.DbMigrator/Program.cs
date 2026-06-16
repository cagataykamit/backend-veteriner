using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
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
          loadtest-seed  — yük testi sentetik veri (yalnızca VetinityLoadTestDb; profil: small)
          rebuild-appointment-projections — Command DB randevularından Query read-model yeniden oluştur
                                            (--batch-size 1000 opsiyonel)

        Örnek:
          dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
          dotnet run --project src/Backend.Veteriner.DbMigrator -- loadtest-seed small
          dotnet run --project src/Backend.Veteriner.DbMigrator -- rebuild-appointment-projections --batch-size 1000

        Bağlantı: ConnectionStrings:DefaultConnection (command), ConnectionStrings:QueryConnection (query).
        Şema için alternatif: dotnet ef database update --project src/Backend.Veteriner.Infrastructure --startup-project src/Backend.Veteriner.Api
        """);
}

public partial class Program;
