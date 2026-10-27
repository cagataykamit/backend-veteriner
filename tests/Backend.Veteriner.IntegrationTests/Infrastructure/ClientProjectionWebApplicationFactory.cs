using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Client projection testleri için ayrı command/query LocalDB veritabanları.
/// Hosted servisler kapalı; testler <see cref="Backend.Veteriner.Application.Projections.Clients.IClientProjectionProcessor.ProcessBatchAsync"/>
/// çağırır.
/// </summary>
public sealed class ClientProjectionWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public ClientProjectionWebApplicationFactory()
        : base("ClientProjectionTests")
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false",
        ["ClientProjection:Enabled"] = "false",
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
            o.BatchSize = 50;
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
