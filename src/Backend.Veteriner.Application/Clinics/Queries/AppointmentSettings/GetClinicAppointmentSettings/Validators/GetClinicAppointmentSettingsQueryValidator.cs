using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Queries.AppointmentSettings.GetClinicAppointmentSettings.Validators;

public sealed class GetClinicAppointmentSettingsQueryValidator : AbstractValidator<GetClinicAppointmentSettingsQuery>
{
    public GetClinicAppointmentSettingsQueryValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
    }
}
