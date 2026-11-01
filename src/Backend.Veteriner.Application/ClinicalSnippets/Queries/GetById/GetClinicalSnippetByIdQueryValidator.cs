using FluentValidation;

namespace Backend.Veteriner.Application.ClinicalSnippets.Queries.GetById;

public sealed class GetClinicalSnippetByIdQueryValidator : AbstractValidator<GetClinicalSnippetByIdQuery>
{
    public GetClinicalSnippetByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
