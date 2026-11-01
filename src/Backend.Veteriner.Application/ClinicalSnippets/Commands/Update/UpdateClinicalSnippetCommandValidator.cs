using Backend.Veteriner.Domain.Catalog;
using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalSnippets.Commands.Update;

public sealed class UpdateClinicalSnippetCommandValidator : AbstractValidator<UpdateClinicalSnippetCommand>
{
    public UpdateClinicalSnippetCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Shortcut).NotEmpty().MaximumLength(ClinicalSnippet.MaxShortcutLength + 1);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(ClinicalSnippet.MaxTitleLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ClinicalSnippet.MaxBodyLength);
    }
}
