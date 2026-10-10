using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Queries.GetAddenda.Validators;

public sealed class GetExaminationAddendaQueryValidator : AbstractValidator<GetExaminationAddendaQuery>
{
    public GetExaminationAddendaQueryValidator()
    {
        RuleFor(x => x.ExaminationId).NotEmpty();
    }
}
