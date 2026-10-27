using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Appointment projection testleri için ayrı command/query LocalDB veritabanları.
/// Hosted servisler kapalı; testler <see cref="IAppointmentProjectionProcessor.ProcessBatchAsync"/> çağırır.
/// </summary>
public sealed class AppointmentProjectionWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    /// <summary>Randevu okuma modeli, pano ve sağlık fabrikaları da bu veritabanlarını kullanır.</summary>
    public const string DatabaseKey = "AppointmentProjectionTests";

    public AppointmentProjectionWebApplicationFactory()
        : base(DatabaseKey)
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
        services.PostConfigure<AppointmentProjectionOptions>(o =>
        {
            o.Enabled = false;
            o.BatchSize = 50;
            o.ConsumerName = "appointment-read-model-v1";
        });
        services.PostConfigure<ClientProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<PetProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<QueryReadModelsOptions>(o =>
        {
            o.AppointmentsEnabled = false;
            o.DashboardAppointmentsEnabled = false;
        });
    }
}
