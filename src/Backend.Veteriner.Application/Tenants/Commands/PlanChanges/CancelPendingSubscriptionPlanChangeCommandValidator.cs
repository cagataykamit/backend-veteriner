using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.PlanChanges;

public sealed class CancelPendingSubscriptionPlanChangeCommandValidator : AbstractValidator<CancelPendingSubscriptionPlanChangeCommand>
{
    public CancelPendingSubscriptionPlanChangeCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
