using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalSnippets.Commands.SetActive;

public sealed class SetClinicalSnippetActiveCommandValidator : AbstractValidator<SetClinicalSnippetActiveCommand>
{
    public SetClinicalSnippetActiveCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
