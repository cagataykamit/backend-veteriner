using FluentValidation;

namespace Backend.Veteriner.Application.ExaminationTemplates.Queries.GetById;

public sealed class GetExaminationTemplateByIdQueryValidator : AbstractValidator<GetExaminationTemplateByIdQuery>
{
    public GetExaminationTemplateByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
