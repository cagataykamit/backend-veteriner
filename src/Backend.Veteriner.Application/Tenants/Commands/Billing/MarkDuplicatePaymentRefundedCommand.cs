using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Commands.Billing;

/// <summary>
/// Platform yöneticisi otomatik iadesi başarısız çift ödemeyi sağlayıcı panelinden iade etti (BILL-005). Tekrarında değişiklik yok.
/// </summary>
public sealed record MarkDuplicatePaymentRefundedCommand(Guid CheckoutSessionId)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "BillingDuplicatePayment.MarkRefunded";
    public string? AuditTarget => $"CheckoutSessionId={CheckoutSessionId}";
}

public sealed class MarkDuplicatePaymentRefundedCommandValidator : AbstractValidator<MarkDuplicatePaymentRefundedCommand>
{
    public MarkDuplicatePaymentRefundedCommandValidator()
    {
        RuleFor(x => x.CheckoutSessionId).NotEmpty();
    }
}
