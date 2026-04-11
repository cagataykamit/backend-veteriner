using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetPendingPlanChange;

public sealed class GetPendingSubscriptionPlanChangeQueryValidator : AbstractValidator<GetPendingSubscriptionPlanChangeQuery>
{
    public GetPendingSubscriptionPlanChangeQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
