using Backend.Veteriner.Domain.Legal;
using FluentValidation;

namespace Backend.Veteriner.Application.LegalAcceptances.Commands.AcceptMy;

public sealed class AcceptMyLegalDocumentsCommandValidator : AbstractValidator<AcceptMyLegalDocumentsCommand>
{
    public AcceptMyLegalDocumentsCommandValidator()
    {
        RuleFor(x => x.Documents).NotEmpty();
        RuleFor(x => x.Documents!)
            .Must(d => d.Select(x => x.DocumentType).Distinct().Count() == d.Count)
            .WithMessage("Aynı belge birden fazla gönderilemez.")
            .When(x => x.Documents is { Count: > 0 });
        RuleForEach(x => x.Documents).ChildRules(d =>
        {
            d.RuleFor(x => x.DocumentType).IsInEnum();
            d.RuleFor(x => x.Version).NotEmpty().MaximumLength(LegalAcceptance.MaxVersionLength);
        });
    }
}
