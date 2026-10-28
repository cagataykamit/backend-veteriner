using Backend.Veteriner.Application.ChargeSuggestions.Contracts.Dtos;
using Backend.Veteriner.Application.ChargeSuggestions.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ChargeSuggestions.Queries.GetList;

public sealed class GetChargeSuggestionsQueryHandler
    : IRequestHandler<GetChargeSuggestionsQuery, Result<PagedResult<ChargeSuggestionDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ChargeSuggestion> _suggestions;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetChargeSuggestionsQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ChargeSuggestion> suggestions,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _suggestions = suggestions;
        _pets = pets;
        _clients = clients;
    }

    public async ValueTask<Result<PagedResult<ChargeSuggestionDto>>> Handle(GetChargeSuggestionsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ChargeSuggestionDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
        {
            return Result<PagedResult<ChargeSuggestionDto>>.Failure(ClinicContextRule.Mismatch("ChargeSuggestions"));
        }

        if ((request.ClinicId ?? _clinicContext.ClinicId) is not { } clinicId)
        {
            return Result<PagedResult<ChargeSuggestionDto>>.Failure(
                "ChargeSuggestions.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<ChargeSuggestionDto>>.Failure(scope.Error);

        // Müşteri süzgeci: kalemin müşterisi hayvanın güncel sahibi olduğundan müşterinin bugünkü hayvanları üzerinden süzülür.
        IReadOnlyCollection<Guid>? clientPetIds = null;
        if (request.ClientId is { } requestedClientId)
        {
            clientPetIds = (await _pets.ListAsync(new PetsByTenantClientIdSpec(tenantId, requestedClientId), ct))
                .Select(p => p.Id)
                .ToArray();
            if (clientPetIds.Count == 0)
            {
                return Result<PagedResult<ChargeSuggestionDto>>.Success(
                    PagedResult<ChargeSuggestionDto>.Create([], 0, request.Page, request.PageSize));
            }
        }

        var total = await _suggestions.CountAsync(
            new PendingChargeSuggestionsSpec(tenantId, clinicId, request.PetId, request.ExaminationId, request.VisitId, clientPetIds), ct);
        var rows = await _suggestions.ListAsync(
            new PendingChargeSuggestionsPagedSpec(
                tenantId, clinicId, request.PetId, request.ExaminationId, request.VisitId, request.Page, request.PageSize, clientPetIds), ct);

        var petIds = rows.Select(r => r.PetId).Distinct().ToArray();
        var pets = petIds.Length == 0
            ? new Dictionary<Guid, Pet>()
            : (await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct)).ToDictionary(p => p.Id);

        var clientIds = pets.Values.Select(p => p.ClientId).Distinct().ToArray();
        var clientNames = clientIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct))
                .ToDictionary(c => c.Id, c => c.FullName);

        var items = rows
            .Select(r =>
            {
                var pet = pets.GetValueOrDefault(r.PetId);
                var clientId = pet?.ClientId ?? Guid.Empty;
                return r.ToDto(clientId, clientNames.GetValueOrDefault(clientId, string.Empty), pet?.Name ?? string.Empty);
            })
            .ToList();

        return Result<PagedResult<ChargeSuggestionDto>>.Success(
            PagedResult<ChargeSuggestionDto>.Create(items, total, request.Page, request.PageSize));
    }
}
