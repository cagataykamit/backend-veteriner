using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.Checkout;

public sealed class StartSubscriptionCheckoutCommandValidator : AbstractValidator<StartSubscriptionCheckoutCommand>
{
    public StartSubscriptionCheckoutCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TargetPlanCode).NotEmpty().MaximumLength(30);
    }
}

