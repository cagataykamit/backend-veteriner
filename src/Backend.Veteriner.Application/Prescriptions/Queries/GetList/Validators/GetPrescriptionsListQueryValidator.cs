using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Prescriptions.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Prescriptions.Queries.GetList.Validators;

public sealed class GetPrescriptionsListQueryValidator : AbstractValidator<GetPrescriptionsListQuery>
{
    public GetPrescriptionsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);

        RuleFor(x => x)
            .Must(x => !x.DateFromUtc.HasValue || !x.DateToUtc.HasValue || x.DateFromUtc <= x.DateToUtc)
            .WithMessage("dateFromUtc, dateToUtc'den küçük veya eşit olmalıdır.");
    }
}
