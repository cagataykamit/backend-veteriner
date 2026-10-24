using FluentValidation;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.SetWardActive;

public sealed class SetWardActiveCommandValidator : AbstractValidator<SetWardActiveCommand>
{
    public SetWardActiveCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.WardId).NotEmpty();
    }
}
