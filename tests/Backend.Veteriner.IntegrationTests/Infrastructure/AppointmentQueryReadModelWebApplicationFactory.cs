using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Appointment projection DB + QueryReadModels:AppointmentsEnabled=true factory.
/// </summary>
public sealed class AppointmentQueryReadModelWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public AppointmentQueryReadModelWebApplicationFactory()
        : base(AppointmentProjectionWebApplicationFactory.DatabaseKey)
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false"
    }.Concat(IntegrationTestAppConfiguration.AppointmentsQueryReadEnabled);

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
    }
}
