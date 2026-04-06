using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionSummary.Validators;

public sealed class GetTenantSubscriptionSummaryQueryValidator : AbstractValidator<GetTenantSubscriptionSummaryQuery>
{
    public GetTenantSubscriptionSummaryQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
