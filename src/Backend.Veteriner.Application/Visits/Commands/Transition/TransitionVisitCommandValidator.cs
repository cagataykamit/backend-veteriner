using Backend.Veteriner.Domain.Visits;
using FluentValidation;

namespace Backend.Veteriner.Application.Visits.Commands.Transition;

public sealed class TransitionVisitCommandValidator : AbstractValidator<TransitionVisitCommand>
{
    public TransitionVisitCommandValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();

        RuleFor(x => x.Target)
            .Must(t => t is VisitCareStatus.InProgress or VisitCareStatus.Completed)
            .WithMessage("Hedef durum InProgress veya Completed olmalıdır.");
    }
}
