using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public sealed class AppointmentProjectionOptionsValidator : IValidateOptions<AppointmentProjectionOptions>
{
    public ValidateOptionsResult Validate(string? name, AppointmentProjectionOptions options)
    {
        if (options.LeaseDurationSeconds <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"{AppointmentProjectionOptions.SectionName}:LeaseDurationSeconds must be greater than zero.");
        }

        if (options.ClaimBatchSize < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{AppointmentProjectionOptions.SectionName}:ClaimBatchSize must be at least 1.");
        }

        if (options.ClaimBatchSize > AppointmentProjectionOptions.MaxClaimBatchSize)
        {
            return ValidateOptionsResult.Fail(
                $"{AppointmentProjectionOptions.SectionName}:ClaimBatchSize must not exceed {AppointmentProjectionOptions.MaxClaimBatchSize}.");
        }

        return ValidateOptionsResult.Success;
    }
}
