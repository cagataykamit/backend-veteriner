using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Payment finance projection testleri için ayrı command/query LocalDB veritabanları.
/// </summary>
public sealed class PaymentProjectionWebApplicationFactory : WebApplicationFactory<global::Program>
{
    public const string CommandDatabaseName = "VetinityCommandDb_PaymentProjectionTests";
    public const string QueryDatabaseName = "VetinityQueryDb_PaymentProjectionTests";

    public const string CommandConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityCommandDb_PaymentProjectionTests;Trusted_Connection=True;MultipleActiveResultSets=true";

    public const string QueryConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityQueryDb_PaymentProjectionTests;Trusted_Connection=True;MultipleActiveResultSets=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = CommandConnection,
                ["ConnectionStrings:SqlServer"] = CommandConnection,
                ["ConnectionStrings:QueryConnection"] = QueryConnection,
                ["Outbox:Enabled"] = "false",
                ["AppointmentProjection:Enabled"] = "false",
                ["ClientProjection:Enabled"] = "false",
                ["PetProjection:Enabled"] = "false",
                ["PaymentProjection:Enabled"] = "false",
                ["QueryReadModels:AppointmentsEnabled"] = "false",
                ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
            }.Concat(IntegrationTestAppConfiguration.RateLimitingDisabled));
        });

        builder.ConfigureServices(services =>
        {
            IntegrationTestDbContextOverride.UseDedicatedDatabase(services, CommandConnection);
            IntegrationTestDbContextOverride.UseDedicatedQueryDatabase(services, QueryConnection);

            using var scope = services.BuildServiceProvider().CreateScope();
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queryDb = scope.ServiceProvider.GetRequiredService<QueryDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            IntegrationTestDatabaseGuard.EnsureSafeDatabase(
                commandDb.Database.GetConnectionString(),
                allowedPrefix: CommandDatabaseName);
            IntegrationTestDatabaseGuard.EnsureSafeDatabase(
                queryDb.Database.GetConnectionString(),
                allowedPrefix: QueryDatabaseName);

            IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection).GetAwaiter().GetResult();
            commandDb.Database.EnsureCreated();
            TestDataSeeder.Seed(commandDb, hasher);
            PermissionSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            AdminClaimSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            InviteAssignableOperationClaimsSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            RolePermissionBindingSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();

            IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb).GetAwaiter().GetResult();
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSenderImmediate>();
            services.AddScoped<IEmailSenderImmediate, NoOpEmailSenderImmediate>();
            services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
            services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
            services.PostConfigure<ClientProjectionOptions>(o => o.Enabled = false);
            services.PostConfigure<PetProjectionOptions>(o => o.Enabled = false);
            services.PostConfigure<PaymentProjectionOptions>(o =>
            {
                o.Enabled = false;
                o.BatchSize = 50;
                o.ConsumerName = "payment-finance-v1";
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection);
        await IntegrationTestDatabaseReset.EnsureDroppedAsync(QueryConnection);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection).GetAwaiter().GetResult();
            IntegrationTestDatabaseReset.EnsureDroppedAsync(QueryConnection).GetAwaiter().GetResult();
        }
    }
}
