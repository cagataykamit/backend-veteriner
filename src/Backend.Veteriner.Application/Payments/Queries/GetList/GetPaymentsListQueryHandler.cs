using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Queries.GetList;

public sealed class GetPaymentsListQueryHandler
    : IRequestHandler<GetPaymentsListQuery, Result<PagedResult<PaymentListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Payment> _payments;

    public GetPaymentsListQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Payment> payments)
    {
        _tenantContext = tenantContext;
        _payments = payments;
    }

    public async Task<Result<PagedResult<PaymentListItemDto>>> Handle(
        GetPaymentsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<PaymentListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var total = await _payments.CountAsync(
            new PaymentsFilteredCountSpec(
                tenantId,
                request.ClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                request.PaidFromUtc,
                request.PaidToUtc),
            ct);

        var rows = await _payments.ListAsync(
            new PaymentsFilteredPagedSpec(
                tenantId,
                request.ClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                request.PaidFromUtc,
                request.PaidToUtc,
                page,
                pageSize),
            ct);

        var items = rows
            .Select(p => new PaymentListItemDto(
                p.Id,
                p.ClinicId,
                p.ClientId,
                p.PetId,
                p.Amount,
                p.Currency,
                p.Method,
                p.PaidAtUtc))
            .ToList();

        return Result<PagedResult<PaymentListItemDto>>.Success(
            PagedResult<PaymentListItemDto>.Create(items, total, page, pageSize));
    }
}
