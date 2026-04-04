using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Queries.GetRelatedSummary.Validators;

public sealed class GetExaminationRelatedSummaryQueryValidator : AbstractValidator<GetExaminationRelatedSummaryQuery>
{
    public GetExaminationRelatedSummaryQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
