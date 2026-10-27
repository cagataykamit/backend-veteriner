using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Clients;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Backend.Veteriner.Infrastructure.Projections.Pets;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Payment finance projection testleri için ayrı command/query LocalDB veritabanları.
/// </summary>
public sealed class PaymentProjectionWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public PaymentProjectionWebApplicationFactory()
        : base("PaymentProjectionTests")
    {
    }

    protected override IEnumerable<KeyValuePair<string, string?>> Settings => new Dictionary<string, string?>
    {
        ["Outbox:Enabled"] = "false",
        ["AppointmentProjection:Enabled"] = "false",
        ["ClientProjection:Enabled"] = "false",
        ["PetProjection:Enabled"] = "false",
        ["PaymentProjection:Enabled"] = "false",
        ["QueryReadModels:AppointmentsEnabled"] = "false",
        ["QueryReadModels:DashboardAppointmentsEnabled"] = "false"
    };

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<OutboxOptions>(o => o.Enabled = false);
        services.PostConfigure<AppointmentProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<ClientProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<PetProjectionOptions>(o => o.Enabled = false);
        services.PostConfigure<PaymentProjectionOptions>(o =>
        {
            o.Enabled = false;
            o.BatchSize = 50;
            o.ConsumerName = "payment-finance-v1";
        });
    }
}
