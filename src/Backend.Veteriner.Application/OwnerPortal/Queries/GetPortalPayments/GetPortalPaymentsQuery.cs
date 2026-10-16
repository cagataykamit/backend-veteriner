using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalPayments;

/// <summary>Bağın kliniğindeki ödeme geçmişi: yalnız tarih, tutar, yöntem; en yeni önce (docs/PORTAL_API_CONTRACT.md §6.5).</summary>
public sealed record GetPortalPaymentsQuery(Guid OwnerAccountId, Guid LinkId, int Page, int PageSize)
    : IRequest<Result<PagedResult<PortalPaymentDto>>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.Payments";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

public sealed class GetPortalPaymentsQueryHandler : IRequestHandler<GetPortalPaymentsQuery, Result<PagedResult<PortalPaymentDto>>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Payment> _payments;

    public GetPortalPaymentsQueryHandler(OwnerPortalAccess access, IReadRepository<Payment> payments)
    {
        _access = access;
        _payments = payments;
    }

    public async Task<Result<PagedResult<PortalPaymentDto>>> Handle(GetPortalPaymentsQuery request, CancellationToken ct)
    {
        var scopeResult = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PagedResult<PortalPaymentDto>>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var total = await _payments.CountAsync(new PortalPaymentsSpec(scope.TenantId, scope.ClinicId, scope.ClientId), ct);
        var items = total == 0
            ? []
            : await _payments.ListAsync(
                new PortalPaymentsSpec(scope.TenantId, scope.ClinicId, scope.ClientId, request.Page, request.PageSize), ct);

        return Result<PagedResult<PortalPaymentDto>>.Success(
            PagedResult<PortalPaymentDto>.Create(items, total, request.Page, request.PageSize));
    }
}
