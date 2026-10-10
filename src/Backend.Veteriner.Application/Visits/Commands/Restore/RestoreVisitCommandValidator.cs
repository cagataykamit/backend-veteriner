using FluentValidation;

namespace Backend.Veteriner.Application.Visits.Commands.Restore;

public sealed class RestoreVisitCommandValidator : AbstractValidator<RestoreVisitCommand>
{
    public RestoreVisitCommandValidator()
    {
        RuleFor(x => x.VisitId).NotEmpty();
    }
}
