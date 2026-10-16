using FluentValidation;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPayments;

public sealed class GetPortalPaymentsQueryValidator : AbstractValidator<GetPortalPaymentsQuery>
{
    public GetPortalPaymentsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, OwnerPortalPaging.MaxPageSize);
    }
}
