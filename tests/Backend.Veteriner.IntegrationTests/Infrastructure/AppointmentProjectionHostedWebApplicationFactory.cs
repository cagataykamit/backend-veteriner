using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Appointment projection hosted service polling testleri için factory.
/// </summary>
public sealed class AppointmentProjectionHostedWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public AppointmentProjectionHostedWebApplicationFactory()
        : base("AppointmentProjectionHostedTests")
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "true",
        ["AppointmentProjection:LoopIntervalSeconds"] = "1",
        ["AppointmentProjection:BatchSize"] = "20",
        ["QueryReadModels:AppointmentsEnabled"] = "false",
        ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o =>
        {
            o.Enabled = true;
            o.LoopIntervalSeconds = 1;
            o.BatchSize = 20;
            o.ConsumerName = "appointment-read-model-v1";
        });
        services.PostConfigure<QueryReadModelsOptions>(o =>
        {
            o.AppointmentsEnabled = false;
            o.DashboardAppointmentsEnabled = false;
        });
    }
}
