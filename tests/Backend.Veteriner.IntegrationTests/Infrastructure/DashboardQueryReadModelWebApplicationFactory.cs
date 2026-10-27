using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Dashboard appointment Query DB parity testleri için factory.
/// </summary>
public sealed class DashboardQueryReadModelWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public DashboardQueryReadModelWebApplicationFactory()
        : base(AppointmentProjectionWebApplicationFactory.DatabaseKey)
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false",
        ["QueryReadModels:AppointmentsEnabled"] = "false"
    }.Concat(IntegrationTestAppConfiguration.DashboardAppointmentsQueryReadEnabled);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
    }
}
