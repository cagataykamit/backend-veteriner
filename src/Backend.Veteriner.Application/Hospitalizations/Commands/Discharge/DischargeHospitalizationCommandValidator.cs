using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Discharge;

public sealed class DischargeHospitalizationCommandValidator : AbstractValidator<DischargeHospitalizationCommand>
{
    public DischargeHospitalizationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DischargedAtUtc).NotEqual(default(DateTime));
        RuleFor(x => x.Notes)
            .MaximumLength(4000)
            .When(x => x.Notes != null);
    }
}
