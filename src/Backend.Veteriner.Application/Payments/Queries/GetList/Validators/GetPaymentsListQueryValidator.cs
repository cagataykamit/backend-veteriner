using Backend.Veteriner.Application.Payments.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Payments.Queries.GetList.Validators;

public sealed class GetPaymentsListQueryValidator : AbstractValidator<GetPaymentsListQuery>
{
    public GetPaymentsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x)
            .Must(x => !x.PaidFromUtc.HasValue || !x.PaidToUtc.HasValue || x.PaidFromUtc <= x.PaidToUtc)
            .WithMessage("paidFromUtc, paidToUtc'den küçük veya eşit olmalıdır.");
    }
}
