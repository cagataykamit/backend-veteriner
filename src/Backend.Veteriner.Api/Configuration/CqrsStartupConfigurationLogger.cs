using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Infrastructure.Projections.Appointments;
using Backend.Veteriner.Infrastructure.Projections.Payments;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Backend.Veteriner.Api.Configuration;

/// <summary>
/// CQRS feature flag ve DB catalog bilgisini startup'ta PII/secret loglamadan yazar.
/// </summary>
internal static class CqrsStartupConfigurationLogger
{
    public static void LogEffectiveConfiguration(WebApplication app)
    {
        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CqrsStartup");

        var configuration = app.Configuration;
        var queryReadModels = app.Services.GetRequiredService<IOptions<QueryReadModelsOptions>>().Value;
        var projectionOptions = app.Services.GetRequiredService<IOptions<AppointmentProjectionOptions>>().Value;
        var paymentProjectionOptions = app.Services.GetRequiredService<IOptions<PaymentProjectionOptions>>().Value;

        var commandConnection =
            configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetConnectionString("SqlServer");
        var queryConnection = configuration.GetConnectionString("QueryConnection");

        logger.LogInformation(
            "CQRS startup configuration. Environment={Environment} ProjectionEnabled={ProjectionEnabled} PaymentProjectionEnabled={PaymentProjectionEnabled} AppointmentsQueryReadEnabled={AppointmentsQueryReadEnabled} DashboardQueryReadEnabled={DashboardQueryReadEnabled} DashboardFinanceReadEnabled={DashboardFinanceReadEnabled} ClientsQueryReadEnabled={ClientsQueryReadEnabled} PetsQueryReadEnabled={PetsQueryReadEnabled} SharedSearchLookupEnabled={SharedSearchLookupEnabled} PaymentsSearchLookupEnabled={PaymentsSearchLookupEnabled} CommandDbCatalog={CommandDbCatalog} QueryDbCatalog={QueryDbCatalog}",
            app.Environment.EnvironmentName,
            projectionOptions.Enabled,
            paymentProjectionOptions.Enabled,
            queryReadModels.AppointmentsEnabled,
            queryReadModels.DashboardAppointmentsEnabled,
            queryReadModels.DashboardFinanceReadEnabled,
            queryReadModels.ClientsEnabled,
            queryReadModels.PetsEnabled,
            queryReadModels.SharedSearchLookupEnabled,
            queryReadModels.PaymentsSearchLookupEnabled,
            TryGetDatabaseCatalog(commandConnection),
            TryGetDatabaseCatalog(queryConnection));
    }

    internal static string TryGetDatabaseCatalog(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "(not-configured)";

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.InitialCatalog)
                ? "(no-catalog)"
                : builder.InitialCatalog;
        }
        catch
        {
            var match = Regex.Match(connectionString, @"Database=([^;]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "(unparseable)";
        }
    }
}
