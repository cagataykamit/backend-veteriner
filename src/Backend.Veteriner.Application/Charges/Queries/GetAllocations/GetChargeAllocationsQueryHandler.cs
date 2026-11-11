using Backend.Veteriner.Application.ClientCredits;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Charges.Queries.GetAllocations;

public sealed class GetChargeAllocationsQueryHandler
    : IRequestHandler<GetChargeAllocationsQuery, Result<IReadOnlyList<ChargeAllocationItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<PaymentAllocation> _allocations;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<ClientCredit> _credits;
    private readonly IReadRepository<ClientCreditApplication> _creditApplications;

    public GetChargeAllocationsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Charge> charges,
        IReadRepository<PaymentAllocation> allocations,
        IReadRepository<Payment> payments,
        IReadRepository<ClientCredit> credits,
        IReadRepository<ClientCreditApplication> creditApplications)
    {
        _credits = credits;
        _creditApplications = creditApplications;
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _charges = charges;
        _allocations = allocations;
        _payments = payments;
    }

    public async ValueTask<Result<IReadOnlyList<ChargeAllocationItemDto>>> Handle(
        GetChargeAllocationsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<ChargeAllocationItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var charge = await _charges.FirstOrDefaultAsync(new ChargeByIdSpec(tenantId, request.ChargeId), ct);
        if (charge is null
            || (_clinicContext.ClinicId is { } contextClinicId && charge.ClinicId != contextClinicId)
            || !(await _clinicScopeResolver.ResolveAsync(tenantId, charge.ClinicId, ct)).IsSuccess)
        {
            // Atanmamış klinikteki kayıt varlığı sızdırmaz.
            return Result<IReadOnlyList<ChargeAllocationItemDto>>.Failure("Charges.NotFound", "Ücret kaydı bulunamadı.");
        }

        var rows = await _allocations.ListAsync(new PaymentAllocationsByChargeIdSpec(tenantId, charge.Id), ct);
        var paymentIds = rows.Select(r => r.PaymentId).Distinct().ToArray();
        var payments = paymentIds.Length == 0
            ? new Dictionary<Guid, Payment>()
            : (await _payments.ListAsync(new PaymentsByIdsSpec(tenantId, paymentIds), ct))
                .ToDictionary(p => p.Id);

        var items = rows
            .Where(r => payments.ContainsKey(r.PaymentId))
            .Select(r => new ChargeAllocationItemDto(
                r.Id, r.PaymentId, payments[r.PaymentId].PaidAtUtc, payments[r.PaymentId].Method,
                r.Amount, r.CreatedAtUtc, r.CreatedByUserId))
            .ToList();

        // Krediden mahsuplar (FIN-005): ödeme alanları kredinin kaynak ödemesidir; devir alacağında (ödemesiz) boştur.
        var applications = await _creditApplications.ListAsync(new ActiveClientCreditApplicationsByChargeSpec(tenantId, charge.Id), ct);
        if (applications.Count > 0)
        {
            var credits = (await _credits.ListAsync(
                    new ClientCreditsByIdsSpec(tenantId, applications.Select(a => a.CreditId).Distinct().ToArray()), ct))
                .ToDictionary(c => c.Id);
            var sourceIds = credits.Values.Where(c => c.SourcePaymentId.HasValue).Select(c => c.SourcePaymentId!.Value).Distinct().ToArray();
            var sources = sourceIds.Length == 0
                ? new Dictionary<Guid, Payment>()
                : (await _payments.ListAsync(new PaymentsByIdsSpec(tenantId, sourceIds), ct)).ToDictionary(p => p.Id);
            items.AddRange(applications
                .Where(a => credits.TryGetValue(a.CreditId, out var c)
                            && (c.SourcePaymentId is not { } paymentId || sources.ContainsKey(paymentId)))
                .Select(a =>
                {
                    var source = credits[a.CreditId].SourcePaymentId is { } paymentId ? sources[paymentId] : null;
                    return new ChargeAllocationItemDto(
                        a.Id, source?.Id, source?.PaidAtUtc, source?.Method, a.Amount, a.CreatedAtUtc, a.CreatedByUserId,
                        ChargeAllocationKinds.Credit, a.CreditId);
                }));
        }

        return Result<IReadOnlyList<ChargeAllocationItemDto>>.Success(
            items.OrderBy(i => i.CreatedAtUtc).ThenBy(i => i.Id).ToList());
    }
}
