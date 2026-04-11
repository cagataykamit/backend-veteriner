using Backend.Veteriner.Domain.Tenants;
using FluentValidation;

namespace Backend.Veteriner.Application.Tenants.Commands.Billing;

public sealed class ProcessBillingWebhookCommandValidator : AbstractValidator<ProcessBillingWebhookCommand>
{
    public ProcessBillingWebhookCommandValidator()
    {
        RuleFor(x => x.Provider)
            .NotEqual(BillingProvider.None)
            .WithMessage("Provider gerekli.");

        RuleFor(x => x.RawBody)
            .NotNull()
            .WithMessage("Webhook gövdesi gerekli.");

        RuleFor(x => x.Headers)
            .NotNull()
            .WithMessage("Webhook başlıkları gerekli.");
    }
}
