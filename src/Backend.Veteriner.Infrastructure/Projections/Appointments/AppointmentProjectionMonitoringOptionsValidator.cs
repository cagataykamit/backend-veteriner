using Backend.Veteriner.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionMonitoringOptionsValidator
    : IValidateOptions<AppointmentProjectionMonitoringOptions>
{
    public ValidateOptionsResult Validate(string? name, AppointmentProjectionMonitoringOptions options)
    {
        if (options.WarningPendingAgeSeconds <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"{AppointmentProjectionMonitoringOptions.SectionName}:WarningPendingAgeSeconds must be greater than zero.");
        }

        if (options.CriticalPendingAgeSeconds <= options.WarningPendingAgeSeconds)
        {
            return ValidateOptionsResult.Fail(
                $"{AppointmentProjectionMonitoringOptions.SectionName}:CriticalPendingAgeSeconds must be greater than WarningPendingAgeSeconds.");
        }

        if (options.ParityCheckIntervalSeconds <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"{AppointmentProjectionMonitoringOptions.SectionName}:ParityCheckIntervalSeconds must be greater than zero.");
        }

        return ValidateOptionsResult.Success;
    }
}
