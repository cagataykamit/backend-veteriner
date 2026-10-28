using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Queries.GetDuplicatePayments;

/// <summary>Platform yöneticisi: çift ödemeler ve iade durumları (BILL-005). Kiracılar arası okuma bilinçli.</summary>
public sealed record GetDuplicatePaymentsQuery : IRequest<IReadOnlyList<DuplicatePaymentDto>>;

public sealed record DuplicatePaymentDto(
    Guid CheckoutSessionId,
    Guid TenantId,
    string? TenantName,
    string TargetPlanCode,
    string BillingPeriod,
    string Provider,
    long? AmountMinor,
    string CurrencyCode,
    DateTime? DetectedAtUtc,
    string RefundStatus,
    int RefundAttemptCount,
    string? RefundFailureReason,
    DateTime? RefundedAtUtc);

public sealed class GetDuplicatePaymentsQueryHandler : IRequestHandler<GetDuplicatePaymentsQuery, IReadOnlyList<DuplicatePaymentDto>>
{
    /// <summary>Çift ödeme nadirdir; liste sayfalanmaz.</summary>
    public const int MaxRows = 200;

    private readonly IReadRepository<BillingCheckoutSession> _sessions;
    private readonly IReadRepository<Tenant> _tenants;

    public GetDuplicatePaymentsQueryHandler(IReadRepository<BillingCheckoutSession> sessions, IReadRepository<Tenant> tenants)
    {
        _sessions = sessions;
        _tenants = tenants;
    }

    public async ValueTask<IReadOnlyList<DuplicatePaymentDto>> Handle(GetDuplicatePaymentsQuery request, CancellationToken ct)
    {
        var sessions = await _sessions.ListAsync(new DuplicatePaymentCheckoutSessionsSpec(MaxRows), ct);
        var tenantIds = sessions.Select(s => s.TenantId).Distinct().ToList();
        var names = tenantIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _tenants.ListAsync(new TenantNamesSpec(tenantIds), ct)).ToDictionary(t => t.Id, t => t.Name);

        return sessions
            .Select(s => new DuplicatePaymentDto(
                s.Id,
                s.TenantId,
                names.GetValueOrDefault(s.TenantId),
                SubscriptionPlanCatalog.ToApiCode(s.TargetPlanCode),
                s.BillingPeriod.ToString(),
                s.Provider.ToString(),
                s.GrossAmountMinor.HasValue ? s.GrossAmountMinor - (s.DiscountAmountMinor ?? 0) : null,
                "TRY",
                s.DuplicatePaymentDetectedAtUtc,
                s.RefundStatus.ToString(),
                s.RefundAttemptCount,
                s.RefundFailureReason,
                s.RefundedAtUtc))
            .ToList();
    }

    private sealed class TenantNamesSpec : Specification<Tenant>
    {
        public TenantNamesSpec(IReadOnlyCollection<Guid> ids)
        {
            Query.AsNoTracking();
            Query.Where(t => ids.Contains(t.Id));
        }
    }
}
