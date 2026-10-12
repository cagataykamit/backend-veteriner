using FluentValidation;

namespace Backend.Veteriner.Application.LabResults.Commands.Cancel;

public sealed class CancelLabResultCommandValidator : AbstractValidator<CancelLabResultCommand>
{
    public CancelLabResultCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
