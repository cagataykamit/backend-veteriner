using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Payments.Contracts.Dtos;
using Backend.Veteriner.Application.Payments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Payments.Queries.GetList;

public sealed class GetPaymentsListQueryHandler
    : IRequestHandler<GetPaymentsListQuery, Result<PagedResult<PaymentListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Payment> _payments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetPaymentsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Payment> payments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _payments = payments;
        _pets = pets;
        _clients = clients;
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
        var effectiveClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<PaymentListItemDto>>.Failure(
                "Payments.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var total = await _payments.CountAsync(
            new PaymentsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                request.PaidFromUtc,
                request.PaidToUtc),
            ct);

        var rows = await _payments.ListAsync(
            new PaymentsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.ClientId,
                request.PetId,
                request.Method,
                request.PaidFromUtc,
                request.PaidToUtc,
                page,
                pageSize),
            ct);

        var clientIds = rows.Select(x => x.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct);
        var clientNameById = clients.ToDictionary(x => x.Id, x => x.FullName);

        var petIds = rows.Where(x => x.PetId.HasValue).Select(x => x.PetId!.Value).Distinct().ToArray();
        var pets = petIds.Length == 0
            ? []
            : await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct);
        var petById = pets.ToDictionary(x => x.Id);

        var items = rows
            .Select(p =>
            {
                var clientName = clientNameById.TryGetValue(p.ClientId, out var cn) ? cn : string.Empty;
                string petName = string.Empty;
                if (p.PetId is { } pid && petById.TryGetValue(pid, out var pet))
                    petName = pet.Name;

                return new PaymentListItemDto(
                    p.Id,
                    p.ClinicId,
                    p.ClientId,
                    clientName,
                    p.PetId,
                    petName,
                    p.Amount,
                    p.Currency,
                    p.Method,
                    p.PaidAtUtc);
            })
            .ToList();

        return Result<PagedResult<PaymentListItemDto>>.Success(
            PagedResult<PaymentListItemDto>.Create(items, total, page, pageSize));
    }
}
