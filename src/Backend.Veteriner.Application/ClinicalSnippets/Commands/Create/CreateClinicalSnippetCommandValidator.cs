using Backend.Veteriner.Domain.Catalog;
using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalSnippets.Commands.Create;

public sealed class CreateClinicalSnippetCommandValidator : AbstractValidator<CreateClinicalSnippetCommand>
{
    public CreateClinicalSnippetCommandValidator()
    {
        RuleFor(x => x.Shortcut).NotEmpty().MaximumLength(ClinicalSnippet.MaxShortcutLength + 1);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(ClinicalSnippet.MaxTitleLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ClinicalSnippet.MaxBodyLength);
    }
}
