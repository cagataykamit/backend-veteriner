using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.IntegrationTests.Projections.Appointments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Claim path testleri için instrumented claim repository decorator kaydı.
/// </summary>
public sealed class AppointmentProjectionClaimInstrumentedWebApplicationFactory : DedicatedDatabaseWebApplicationFactory
{
    public AppointmentProjectionClaimInstrumentedWebApplicationFactory()
        : base("AppointmentProjectionClaimInstrumentedTests")
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
        services.RemoveAll<IAppointmentOutboxClaimRepository>();
        services.AddScoped<TestAppointmentOutboxClaimRepository>();
        services.AddScoped<IAppointmentOutboxClaimRepository>(sp =>
            sp.GetRequiredService<TestAppointmentOutboxClaimRepository>());
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
