using Backend.Veteriner.Application.Examinations.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Examinations.Queries.GetById.Validators;

public sealed class GetExaminationByIdQueryValidator : AbstractValidator<GetExaminationByIdQuery>
{
    public GetExaminationByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
