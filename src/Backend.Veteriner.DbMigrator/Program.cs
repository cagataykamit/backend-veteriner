using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
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
        case "seed":
            await RunSeedPipelineAsync(db, hasher, logger, CancellationToken.None);
            logger.LogInformation("Seed pipeline completed.");
            break;
        case "all":
            await db.Database.MigrateAsync();
            await RunSeedPipelineAsync(db, hasher, logger, CancellationToken.None);
            logger.LogInformation("Migrate + seed pipeline completed.");
            break;
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
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Backend.Veteriner.DbMigrator — şema ve seed (API startup dışında).

        Kullanım:
          dotnet run --project src/Backend.Veteriner.DbMigrator -- <komut>

        Komutlar:
          migrate   — EF Core MigrateAsync (bekleyen migrationları uygular)
          seed      — Permission/Data/AdminClaim/InviteAssignable seed zinciri
          all       — önce migrate, sonra seed

        Bağlantı: ConnectionStrings:DefaultConnection (API ile aynı UserSecretsId kullanılabilir).
        Şema için alternatif: dotnet ef database update --project src/Backend.Veteriner.Infrastructure --startup-project src/Backend.Veteriner.Api
        """);
}

public partial class Program;
