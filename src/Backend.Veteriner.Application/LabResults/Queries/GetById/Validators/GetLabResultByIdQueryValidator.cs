using Backend.Veteriner.Application.LabResults.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.LabResults.Queries.GetById.Validators;

public sealed class GetLabResultByIdQueryValidator : AbstractValidator<GetLabResultByIdQuery>
{
    public GetLabResultByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
