using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Vaccinations.Queries.GetList;
using FluentValidation;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetList.Validators;

public sealed class GetVaccinationsListQueryValidator : AbstractValidator<GetVaccinationsListQuery>
{
    public GetVaccinationsListQueryValidator()
    {
        RuleFor(x => x.PageRequest).NotNull();
        RuleFor(x => x.PageRequest.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageRequest.PageSize).InclusiveBetween(1, 200);

        RuleFor(x => x.PageRequest.Search)
            .MaximumLength(ListQueryTextSearch.MaxTermLength)
            .When(x => x.PageRequest.Search != null);

        RuleFor(x => x)
            .Must(x => !x.DueFromUtc.HasValue || !x.DueToUtc.HasValue || x.DueFromUtc <= x.DueToUtc)
            .WithMessage("dueFromUtc, dueToUtc'den küçük veya eşit olmalıdır.");

        RuleFor(x => x)
            .Must(x => !x.AppliedFromUtc.HasValue || !x.AppliedToUtc.HasValue || x.AppliedFromUtc <= x.AppliedToUtc)
            .WithMessage("appliedFromUtc, appliedToUtc'den küçük veya eşit olmalıdır.");

        RuleFor(x => x)
            .Must(x => !x.DateFromUtc.HasValue || !x.DateToUtc.HasValue || x.DateFromUtc <= x.DateToUtc)
            .WithMessage("dateFromUtc, dateToUtc'den küçük veya eşit olmalıdır.");

        RuleFor(x => x.ClientId)
            .Must(id => !id.HasValue || id.Value != Guid.Empty)
            .WithMessage("clientId is invalid.");

        RuleFor(x => x.PageRequest.Sort)
            .Must(VaccinationListSort.Catalog.IsAllowedSort)
            .WithMessage(VaccinationListSort.Catalog.InvalidSortMessage);

        RuleFor(x => x.PageRequest.Order)
            .Must(ListSortOrder.IsAllowed)
            .WithMessage(ListSortOrder.InvalidMessage);
    }
}
