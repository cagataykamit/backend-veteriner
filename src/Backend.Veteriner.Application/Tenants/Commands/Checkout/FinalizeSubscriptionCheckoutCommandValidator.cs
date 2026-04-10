using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

public sealed class FinalizeSubscriptionCheckoutCommandValidator : AbstractValidator<FinalizeSubscriptionCheckoutCommand>
{
    public FinalizeSubscriptionCheckoutCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CheckoutSessionId).NotEmpty();
        RuleFor(x => x.ExternalReference).MaximumLength(250);
    }
}

