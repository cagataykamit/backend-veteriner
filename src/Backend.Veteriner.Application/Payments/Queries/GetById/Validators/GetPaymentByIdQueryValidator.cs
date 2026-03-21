using Backend.Veteriner.Application.Payments.Queries.GetById;
using FluentValidation;

namespace Backend.Veteriner.Application.Payments.Queries.GetById.Validators;

public sealed class GetPaymentByIdQueryValidator : AbstractValidator<GetPaymentByIdQuery>
{
    public GetPaymentByIdQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
