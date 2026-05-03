using FluentValidation;

namespace Backend.Veteriner.Application.Clinics.Queries.WorkingHours.GetClinicWorkingHours.Validators;

public sealed class GetClinicWorkingHoursQueryValidator : AbstractValidator<GetClinicWorkingHoursQuery>
{
    public GetClinicWorkingHoursQueryValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
    }
}
