using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Queries.GetSubscriptionCheckout;

public sealed class GetSubscriptionCheckoutQueryValidator : AbstractValidator<GetSubscriptionCheckoutQuery>
{
    public GetSubscriptionCheckoutQueryValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CheckoutSessionId).NotEmpty();
    }
}

