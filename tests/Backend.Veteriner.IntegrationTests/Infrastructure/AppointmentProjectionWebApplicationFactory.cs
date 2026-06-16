using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Appointment projection testleri için ayrı command/query LocalDB veritabanları.
/// Hosted servisler kapalı; testler <see cref="IAppointmentProjectionProcessor.ProcessBatchAsync"/> çağırır.
/// </summary>
public sealed class AppointmentProjectionWebApplicationFactory : WebApplicationFactory<global::Program>
{
    public const string CommandDatabaseName = "VetinityDb_AppointmentProjectionTests";
    public const string QueryDatabaseName = "VetinityQueryDb_AppointmentProjectionTests";

    public const string CommandConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityDb_AppointmentProjectionTests;Trusted_Connection=True;MultipleActiveResultSets=true";

    public const string QueryConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityQueryDb_AppointmentProjectionTests;Trusted_Connection=True;MultipleActiveResultSets=true";

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
                ["AppointmentProjection:Enabled"] = "false"
            });
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

            IntegrationTestDatabaseReset.ResetAndMigrateAsync(queryDb).GetAwaiter().GetResult();
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSenderImmediate>();
            services.AddScoped<IEmailSenderImmediate, NoOpEmailSenderImmediate>();
            services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
            services.PostConfigure<AppointmentProjectionOptions>(o =>
            {
                o.Enabled = false;
                o.BatchSize = 50;
                o.ConsumerName = "appointment-read-model-v1";
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
