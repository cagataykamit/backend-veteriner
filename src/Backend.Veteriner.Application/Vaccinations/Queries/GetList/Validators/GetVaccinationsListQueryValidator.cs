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

        RuleFor(x => x)
            .Must(x => !x.DueFromUtc.HasValue || !x.DueToUtc.HasValue || x.DueFromUtc <= x.DueToUtc)
            .WithMessage("dueFromUtc, dueToUtc'den küçük veya eşit olmalıdır.");

        RuleFor(x => x)
            .Must(x => !x.AppliedFromUtc.HasValue || !x.AppliedToUtc.HasValue || x.AppliedFromUtc <= x.AppliedToUtc)
            .WithMessage("appliedFromUtc, appliedToUtc'den küçük veya eşit olmalıdır.");
    }
}
