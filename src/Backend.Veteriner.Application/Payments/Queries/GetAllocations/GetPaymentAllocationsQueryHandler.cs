using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Queries.GetAllocations;

public sealed class GetPaymentAllocationsQueryHandler
    : IRequestHandler<GetPaymentAllocationsQuery, Result<IReadOnlyList<PaymentAllocationItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<PaymentAllocation> _allocations;
    private readonly IReadRepository<Charge> _charges;

    public GetPaymentAllocationsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Payment> payments,
        IReadRepository<PaymentAllocation> allocations,
        IReadRepository<Charge> charges)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _payments = payments;
        _allocations = allocations;
        _charges = charges;
    }

    public async Task<Result<IReadOnlyList<PaymentAllocationItemDto>>> Handle(
        GetPaymentAllocationsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<PaymentAllocationItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var payment = await _payments.FirstOrDefaultAsync(new PaymentByIdSpec(tenantId, request.PaymentId), ct);
        if (payment is null
            || (_clinicContext.ClinicId is { } contextClinicId && payment.ClinicId != contextClinicId)
            || !(await _clinicScopeResolver.ResolveAsync(tenantId, payment.ClinicId, ct)).IsSuccess)
        {
            // Atanmamış klinikteki kayıt varlığı sızdırmaz.
            return Result<IReadOnlyList<PaymentAllocationItemDto>>.Failure("Payments.NotFound", "Ödeme kaydı bulunamadı.");
        }

        var rows = await _allocations.ListAsync(new PaymentAllocationsByPaymentIdSpec(tenantId, payment.Id), ct);
        var chargeIds = rows.Select(r => r.ChargeId).Distinct().ToArray();
        var descriptions = chargeIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _charges.ListAsync(new ChargesByIdsSpec(tenantId, chargeIds), ct))
                .ToDictionary(c => c.Id, c => c.Description);

        IReadOnlyList<PaymentAllocationItemDto> items = rows
            .Select(r => new PaymentAllocationItemDto(
                r.Id, r.ChargeId, descriptions.GetValueOrDefault(r.ChargeId, string.Empty),
                r.Amount, r.CreatedAtUtc, r.CreatedByUserId))
            .ToList();
        return Result<IReadOnlyList<PaymentAllocationItemDto>>.Success(items);
    }
}
