using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Payments.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Payments.Queries.GetList.Validators;

public sealed class GetPaymentsListQueryValidator : AbstractValidator<GetPaymentsListQuery>
{
    public GetPaymentsListQueryValidator()
    {
        RuleFor(x => x.Paging).NotNull();
        RuleFor(x => x.Paging.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Paging.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.Search != null);

        RuleFor(x => x)
            .Must(x => !x.PaidFromUtc.HasValue || !x.PaidToUtc.HasValue || x.PaidFromUtc <= x.PaidToUtc)
            .WithMessage("paidFromUtc, paidToUtc'den küçük veya eşit olmalıdır.");
    }
}
