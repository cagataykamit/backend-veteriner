using FluentValidation;

namespace Backend.Veteriner.Application.ExaminationTemplates.Queries.GetList;

public sealed class GetExaminationTemplatesListQueryValidator : AbstractValidator<GetExaminationTemplatesListQuery>
{
    public GetExaminationTemplatesListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);
    }
}
