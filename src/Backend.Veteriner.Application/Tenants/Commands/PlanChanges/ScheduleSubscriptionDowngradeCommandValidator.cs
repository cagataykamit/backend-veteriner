using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.PlanChanges;

public sealed class ScheduleSubscriptionDowngradeCommandValidator : AbstractValidator<ScheduleSubscriptionDowngradeCommand>
{
    public ScheduleSubscriptionDowngradeCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.TargetPlanCode).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
