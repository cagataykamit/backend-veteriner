using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Query read açıkken geçersiz Query DB bağlantısı ile health testleri.
/// </summary>
public sealed class AppointmentProjectionBrokenQueryReadEnabledWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public const string InvalidQueryConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityQueryDb_AppointmentProjectionBroken;Trusted_Connection=True;Connect Timeout=1;MultipleActiveResultSets=true";

    public AppointmentProjectionBrokenQueryReadEnabledWebApplicationFactory()
        : base(AppointmentProjectionWebApplicationFactory.DatabaseKey, InvalidQueryConnection)
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false",
        ["QueryReadModels:AppointmentsEnabled"] = "true",
        ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
    }
}

/// <summary>
/// Query read kapalıyken geçersiz Query DB bağlantısı ile health testleri.
/// </summary>
public sealed class AppointmentProjectionBrokenQueryReadDisabledWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public const string InvalidQueryConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityQueryDb_AppointmentProjectionBrokenOff;Trusted_Connection=True;Connect Timeout=1;MultipleActiveResultSets=true";

    public AppointmentProjectionBrokenQueryReadDisabledWebApplicationFactory()
        : base(AppointmentProjectionWebApplicationFactory.DatabaseKey, InvalidQueryConnection)
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false",
        ["QueryReadModels:AppointmentsEnabled"] = "false",
        ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
    }
}

/// <summary>
/// Geçersiz monitoring konfigürasyonu ile startup fail-fast testleri (host kurulamaz; veritabanı açılmaz).
/// </summary>
public sealed class AppointmentProjectionInvalidMonitoringWebApplicationFactory : WebApplicationFactory<global::Program>
{
    private static readonly string CommandConnection = IntegrationTestDatabaseNames.ConnectionString(
        IntegrationTestDatabaseNames.Command(AppointmentProjectionWebApplicationFactory.DatabaseKey));

    private static readonly string QueryConnection = IntegrationTestDatabaseNames.ConnectionString(
        IntegrationTestDatabaseNames.Query(AppointmentProjectionWebApplicationFactory.DatabaseKey));

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
                ["AppointmentProjectionMonitoring:WarningPendingAgeSeconds"] = "30",
                ["AppointmentProjectionMonitoring:CriticalPendingAgeSeconds"] = "10"
            }.Concat(IntegrationTestAppConfiguration.RateLimitingDisabled));
        });
    }
}
