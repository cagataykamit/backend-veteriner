using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Pet projection testleri için ayrı command/query LocalDB veritabanları.
/// Hosted servisler kapalı; testler <see cref="Backend.Veteriner.Application.Projections.Pets.IPetProjectionProcessor.ProcessBatchAsync"/>
/// çağırır.
/// </summary>
public sealed class PetProjectionWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public PetProjectionWebApplicationFactory()
        : base("PetProjectionTests")
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
        services.PostConfigure<ClientProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<PetProjectionOptions>(o =>
        {
            o.Enabled = false;
            o.BatchSize = 50;
            o.ConsumerName = "pet-read-model-v1";
        });
        services.PostConfigure<QueryReadModelsOptions>(o =>
        {
            o.AppointmentsEnabled = false;
            o.DashboardAppointmentsEnabled = false;
        });
    }
}
