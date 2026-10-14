using FluentValidation;

namespace Backend.Veteriner.Application.ChargeSuggestions.Commands.Convert;

public sealed class ConvertChargeSuggestionCommandValidator : AbstractValidator<ConvertChargeSuggestionCommand>
{
    public ConvertChargeSuggestionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RequestId).NotEqual(Guid.Empty).When(x => x.RequestId.HasValue);
    }
}
