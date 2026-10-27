using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Pet projection claim path testleri için <see cref="PetProjectionOptions.ClaimingEnabled"/> açık factory.
/// </summary>
public sealed class PetProjectionClaimEnabledWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public PetProjectionClaimEnabledWebApplicationFactory()
        : base("PetProjectionClaimTests")
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false",
        ["ClientProjection:Enabled"] = "false",
        ["PetProjection:Enabled"] = "false",
        ["PetProjection:ClaimingEnabled"] = "true",
        ["PetProjection:ClaimBatchSize"] = "1",
        ["PetProjection:LeaseDurationSeconds"] = "60",
        ["QueryReadModels:AppointmentsEnabled"] = "false",
        ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<ClientProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<PetProjectionOptions>(o =>
        {
            o.Enabled = false;
            o.ClaimingEnabled = true;
            o.ClaimBatchSize = 1;
            o.LeaseDurationSeconds = 60;
            o.ConsumerName = "pet-read-model-v1";
        });
        services.PostConfigure<QueryReadModelsOptions>(o =>
        {
            o.AppointmentsEnabled = false;
            o.DashboardAppointmentsEnabled = false;
        });
    }
}
