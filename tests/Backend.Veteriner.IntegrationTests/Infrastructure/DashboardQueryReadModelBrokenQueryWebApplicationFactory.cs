using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Dashboard testleri için geçersiz Query DB connection string factory.
/// </summary>
public sealed class DashboardQueryReadModelBrokenQueryWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public const string InvalidQueryConnection =
        "Server=(localdb)\\mssqllocaldb;Database=VetinityQueryDb_DashboardBroken_Invalid;Trusted_Connection=True;Connect Timeout=1;MultipleActiveResultSets=true";

    public DashboardQueryReadModelBrokenQueryWebApplicationFactory()
        : base(AppointmentProjectionWebApplicationFactory.DatabaseKey, InvalidQueryConnection)
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false",
        ["QueryReadModels:AppointmentsEnabled"] = "false"
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
    }
}
