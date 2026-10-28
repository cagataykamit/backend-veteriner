using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Commands.Billing;

public sealed class MarkDuplicatePaymentRefundedCommandHandler : IRequestHandler<MarkDuplicatePaymentRefundedCommand, Result>
{
    private readonly IRepository<BillingCheckoutSession> _sessions;
    private readonly TimeProvider _timeProvider;

    public MarkDuplicatePaymentRefundedCommandHandler(IRepository<BillingCheckoutSession> sessions, TimeProvider timeProvider)
    {
        _sessions = sessions;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(MarkDuplicatePaymentRefundedCommand request, CancellationToken ct)
    {
        var session = await _sessions.FirstOrDefaultAsync(new BillingCheckoutSessionByIdSpec(request.CheckoutSessionId), ct);
        if (session is null || session.RefundStatus == BillingCheckoutRefundStatus.None)
            return Result.Failure("Billing.DuplicatePaymentNotFound", "Çift ödeme kaydı bulunamadı.");

        if (session.RefundStatus == BillingCheckoutRefundStatus.Pending)
        {
            return Result.Failure(
                "Billing.DuplicatePaymentRefundInProgress",
                "Otomatik iade sürüyor; sonucu beklenmeden elle işaretlenemez.");
        }

        session.MarkRefundedManually(_timeProvider.GetUtcNow().UtcDateTime);
        await _sessions.UpdateAsync(session, ct);
        await _sessions.SaveChangesAsync(ct);
        return Result.Success();
    }
}
