using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

public sealed class CancelSubscriptionCheckoutCommandValidator : AbstractValidator<CancelSubscriptionCheckoutCommand>
{
    public CancelSubscriptionCheckoutCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CheckoutSessionId).NotEmpty();
    }
}
