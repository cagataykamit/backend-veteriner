using FluentValidation;

namespace Backend.Veteriner.Application.Visits.Commands.SetUrgency;

public sealed class SetVisitUrgencyCommandValidator : AbstractValidator<SetVisitUrgencyCommand>
{
    public SetVisitUrgencyCommandValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
    }
}
