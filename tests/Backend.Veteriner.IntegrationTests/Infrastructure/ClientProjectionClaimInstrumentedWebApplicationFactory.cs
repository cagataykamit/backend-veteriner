using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Clients;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Backend.IntegrationTests.Projections.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Client claim path testleri için instrumented claim repository decorator kaydı.
/// </summary>
public sealed class ClientProjectionClaimInstrumentedWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public ClientProjectionClaimInstrumentedWebApplicationFactory()
        : base("ClientProjectionClaimInstrumentedTests")
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
        services.RemoveAll<IClientOutboxClaimRepository>();
        services.AddScoped<TestClientOutboxClaimRepository>();
        services.AddScoped<IClientOutboxClaimRepository>(sp =>
            sp.GetRequiredService<TestClientOutboxClaimRepository>());
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
