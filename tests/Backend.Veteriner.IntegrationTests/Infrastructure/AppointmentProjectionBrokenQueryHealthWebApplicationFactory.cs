using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Query read açıkken geçersiz Query DB bağlantısı ile health testleri.
/// </summary>
public sealed class AppointmentProjectionBrokenQueryReadEnabledWebApplicationFactory
    : WebApplicationFactory<global::Program>
{
    public const string CommandConnection = AppointmentProjectionWebApplicationFactory.CommandConnection;

    public const string InvalidQueryConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityQueryDb_AppointmentProjectionBroken;Trusted_Connection=True;Connect Timeout=1;MultipleActiveResultSets=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = CommandConnection,
                ["ConnectionStrings:SqlServer"] = CommandConnection,
                ["ConnectionStrings:QueryConnection"] = InvalidQueryConnection,
                ["Outbox:Enabled"] = "false",
                ["AppointmentProjection:Enabled"] = "false",
                ["QueryReadModels:AppointmentsEnabled"] = "true",
                ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
            }.Concat(IntegrationTestAppConfiguration.RateLimitingDisabled));
        });

        builder.ConfigureServices(services =>
        {
            IntegrationTestDbContextOverride.UseDedicatedDatabase(services, CommandConnection);
            IntegrationTestDbContextOverride.UseDedicatedQueryDatabase(services, InvalidQueryConnection);

            using var scope = services.BuildServiceProvider().CreateScope();
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            IntegrationTestDatabaseGuard.EnsureSafeDatabase(
                commandDb.Database.GetConnectionString(),
                allowedPrefix: AppointmentProjectionWebApplicationFactory.CommandDatabaseName);

            IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection).GetAwaiter().GetResult();
            commandDb.Database.EnsureCreated();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            TestDataSeeder.Seed(commandDb, hasher);
            PermissionSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            AdminClaimSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            InviteAssignableOperationClaimsSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            RolePermissionBindingSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSenderImmediate>();
            services.AddScoped<IEmailSenderImmediate, NoOpEmailSenderImmediate>();
            services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
            services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection).GetAwaiter().GetResult();
    }
}

/// <summary>
/// Query read kapalıyken geçersiz Query DB bağlantısı ile health testleri.
/// </summary>
public sealed class AppointmentProjectionBrokenQueryReadDisabledWebApplicationFactory
    : WebApplicationFactory<global::Program>
{
    public const string CommandConnection = AppointmentProjectionWebApplicationFactory.CommandConnection;

    public const string InvalidQueryConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityQueryDb_AppointmentProjectionBrokenOff;Trusted_Connection=True;Connect Timeout=1;MultipleActiveResultSets=true";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = CommandConnection,
                ["ConnectionStrings:SqlServer"] = CommandConnection,
                ["ConnectionStrings:QueryConnection"] = InvalidQueryConnection,
                ["Outbox:Enabled"] = "false",
                ["AppointmentProjection:Enabled"] = "false",
                ["QueryReadModels:AppointmentsEnabled"] = "false",
                ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
            }.Concat(IntegrationTestAppConfiguration.RateLimitingDisabled));
        });

        builder.ConfigureServices(services =>
        {
            IntegrationTestDbContextOverride.UseDedicatedDatabase(services, CommandConnection);
            IntegrationTestDbContextOverride.UseDedicatedQueryDatabase(services, InvalidQueryConnection);

            using var scope = services.BuildServiceProvider().CreateScope();
            var commandDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection).GetAwaiter().GetResult();
            commandDb.Database.EnsureCreated();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            TestDataSeeder.Seed(commandDb, hasher);
            PermissionSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            AdminClaimSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            InviteAssignableOperationClaimsSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
            RolePermissionBindingSeeder.SeedAsync(commandDb).GetAwaiter().GetResult();
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSenderImmediate>();
            services.AddScoped<IEmailSenderImmediate, NoOpEmailSenderImmediate>();
            services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
            services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            IntegrationTestDatabaseReset.EnsureDroppedAsync(CommandConnection).GetAwaiter().GetResult();
    }
}

/// <summary>
/// Geçersiz monitoring konfigürasyonu ile startup fail-fast testleri.
/// </summary>
public sealed class AppointmentProjectionInvalidMonitoringWebApplicationFactory
    : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = AppointmentProjectionWebApplicationFactory.CommandConnection,
                ["ConnectionStrings:SqlServer"] = AppointmentProjectionWebApplicationFactory.CommandConnection,
                ["ConnectionStrings:QueryConnection"] = AppointmentProjectionWebApplicationFactory.QueryConnection,
                ["AppointmentProjectionMonitoring:WarningPendingAgeSeconds"] = "30",
                ["AppointmentProjectionMonitoring:CriticalPendingAgeSeconds"] = "10"
            }.Concat(IntegrationTestAppConfiguration.RateLimitingDisabled));
        });
    }
}
