using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Claim path integration testleri için <see cref="AppointmentProjectionOptions.ClaimingEnabled"/> açık factory.
/// </summary>
public sealed class AppointmentProjectionClaimEnabledWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public AppointmentProjectionClaimEnabledWebApplicationFactory()
        : base("AppointmentProjectionClaimTests")
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false",
        ["AppointmentProjection:ClaimingEnabled"] = "true",
        ["AppointmentProjection:ClaimBatchSize"] = "1",
        ["AppointmentProjection:LeaseDurationSeconds"] = "60",
        ["QueryReadModels:AppointmentsEnabled"] = "false",
        ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o =>
        {
            o.Enabled = false;
            o.ClaimingEnabled = true;
            o.ClaimBatchSize = 1;
            o.LeaseDurationSeconds = 60;
            o.ConsumerName = "appointment-read-model-v1";
        });
        services.PostConfigure<QueryReadModelsOptions>(o =>
        {
            o.AppointmentsEnabled = false;
            o.DashboardAppointmentsEnabled = false;
        });
    }
}
