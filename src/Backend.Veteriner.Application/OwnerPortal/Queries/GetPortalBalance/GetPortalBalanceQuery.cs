using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clients.Queries.GetBalance;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalBalance;

/// <summary>
/// Bağın kliniğindeki bakiye: yalnız tutar/durum (ADR-031 AK-9; docs/PORTAL_API_CONTRACT.md §6.5). Hesap klinik paneli
/// bakiyesiyle aynı sorgular ve aynı kuraldır (<see cref="ClientBalanceTotals"/>); kalem ve açıklama dönmez.
/// </summary>
public sealed record GetPortalBalanceQuery(Guid OwnerAccountId, Guid LinkId) : IRequest<Result<PortalBalanceDto>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.Balance";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

public sealed class GetPortalBalanceQueryHandler : IRequestHandler<GetPortalBalanceQuery, Result<PortalBalanceDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Payment> _payments;

    public GetPortalBalanceQueryHandler(
        OwnerPortalAccess access,
        IReadRepository<Charge> charges,
        IReadRepository<Payment> payments)
    {
        _access = access;
        _charges = charges;
        _payments = payments;
    }

    public async ValueTask<Result<PortalBalanceDto>> Handle(GetPortalBalanceQuery request, CancellationToken ct)
    {
        var scopeResult = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PortalBalanceDto>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var charges = await _charges.ListAsync(
            new ChargesForClientBalanceSpec(scope.TenantId, scope.ClientId, scope.ClinicId, accessibleClinicIds: null), ct);
        var unallocated = await _payments.ListAsync(
            new UnallocatedPaymentsForClientSpec(scope.TenantId, scope.ClientId, scope.ClinicId, accessibleClinicIds: null), ct);
        var lastPayment = await _payments.FirstOrDefaultAsync(
            new PortalPaymentsSpec(scope.TenantId, scope.ClinicId, scope.ClientId, page: 1, pageSize: 1), ct);

        var totals = ClientBalanceTotals.From(charges, unallocated);
        return Result<PortalBalanceDto>.Success(new PortalBalanceDto(
            scope.ClinicId,
            Charge.DefaultCurrency,
            totals.ChargedAmount,
            totals.AllocatedAmount,
            totals.OutstandingAmount,
            totals.UnallocatedPaymentsAmount,
            lastPayment?.PaidAtUtc));
    }
}
