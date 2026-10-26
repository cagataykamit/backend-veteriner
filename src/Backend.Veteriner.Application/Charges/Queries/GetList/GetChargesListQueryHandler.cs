using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Charges.Queries.GetList;

public sealed class GetChargesListQueryHandler : IRequestHandler<GetChargesListQuery, Result<PagedResult<ChargeDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;

    public GetChargesListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Charge> charges,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _charges = charges;
        _clients = clients;
        _pets = pets;
    }

    public async ValueTask<Result<PagedResult<ChargeDto>>> Handle(GetChargesListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ChargeDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue
            && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<ChargeDto>>.Failure(
                "Charges.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        // Kapsamsız kiracı geneli liste yoktur (ödeme listesiyle aynı kural).
        if ((request.ClinicId ?? _clinicContext.ClinicId) is not { } clinicId)
        {
            return Result<PagedResult<ChargeDto>>.Failure(
                "Charges.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<ChargeDto>>.Failure(scope.Error);

        var total = await _charges.CountAsync(
            new ChargesFilteredSpec(tenantId, clinicId, request.ClientId, request.PetId, request.VisitId, request.ExaminationId), ct);
        var rows = await _charges.ListAsync(
            new ChargesFilteredPagedSpec(
                tenantId, clinicId, request.ClientId, request.PetId, request.VisitId, request.ExaminationId,
                request.Page, request.PageSize), ct);

        var clientIds = rows.Select(r => r.ClientId).Distinct().ToArray();
        var clientNames = clientIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct))
                .ToDictionary(c => c.Id, c => c.FullName);

        var petIds = rows.Where(r => r.PetId.HasValue).Select(r => r.PetId!.Value).Distinct().ToArray();
        var petNames = petIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct))
                .ToDictionary(p => p.Id, p => p.Name);

        var items = rows
            .Select(r => r.ToDto(
                clientNames.GetValueOrDefault(r.ClientId, string.Empty),
                r.PetId is { } pid ? petNames.GetValueOrDefault(pid) : null))
            .ToList();

        return Result<PagedResult<ChargeDto>>.Success(
            PagedResult<ChargeDto>.Create(items, total, request.Page, request.PageSize));
    }
}
