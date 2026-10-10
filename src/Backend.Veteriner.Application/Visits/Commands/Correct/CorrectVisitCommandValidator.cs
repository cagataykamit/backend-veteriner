using FluentValidation;

namespace Backend.Veteriner.Application.Visits.Commands.Correct;

public sealed class CorrectVisitCommandValidator : AbstractValidator<CorrectVisitCommand>
{
    public CorrectVisitCommandValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
    }
}
