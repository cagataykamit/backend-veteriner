using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Client projection claim path testleri için <see cref="ClientProjectionOptions.ClaimingEnabled"/> açık factory.
/// </summary>
public sealed class ClientProjectionClaimEnabledWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public ClientProjectionClaimEnabledWebApplicationFactory()
        : base("ClientProjectionClaimTests")
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false",
        ["ClientProjection:Enabled"] = "false",
        ["ClientProjection:ClaimingEnabled"] = "true",
        ["ClientProjection:ClaimBatchSize"] = "1",
        ["ClientProjection:LeaseDurationSeconds"] = "60",
        ["PetProjection:Enabled"] = "false",
        ["QueryReadModels:AppointmentsEnabled"] = "false",
        ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<ClientProjectionOptions>(o =>
        {
            o.Enabled = false;
            o.ClaimingEnabled = true;
            o.ClaimBatchSize = 1;
            o.LeaseDurationSeconds = 60;
            o.ConsumerName = "client-read-model-v1";
        });
        services.PostConfigure<PetProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<QueryReadModelsOptions>(o =>
        {
            o.AppointmentsEnabled = false;
            o.DashboardAppointmentsEnabled = false;
        });
    }
}
