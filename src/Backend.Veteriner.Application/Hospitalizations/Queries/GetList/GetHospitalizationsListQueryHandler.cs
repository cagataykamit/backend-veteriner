using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Queries.GetList;

public sealed class GetHospitalizationsListQueryHandler
    : IRequestHandler<GetHospitalizationsListQuery, Result<PagedResult<HospitalizationListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetHospitalizationsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _hospitalizations = hospitalizations;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<PagedResult<HospitalizationListItemDto>>> Handle(
        GetHospitalizationsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<HospitalizationListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var effectiveClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<HospitalizationListItemDto>>.Failure(
                "Hospitalizations.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var normalized = ListQueryTextSearch.Normalize(request.PageRequest.Search);
        string? searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);
        Guid[] searchPetIds = [];
        if (searchPattern is not null)
        {
            searchPetIds = await ListSearchPetIds.ResolveForAggregateListAsync(
                tenantId,
                searchPattern,
                _clients,
                _pets,
                ct);
        }

        var total = await _hospitalizations.CountAsync(
            new HospitalizationsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.ActiveOnly,
                request.DateFromUtc,
                request.DateToUtc,
                searchPattern,
                searchPetIds),
            ct);

        var rows = await _hospitalizations.ListAsync(
            new HospitalizationsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.ActiveOnly,
                request.DateFromUtc,
                request.DateToUtc,
                page,
                pageSize,
                searchPattern,
                searchPetIds),
            ct);

        var petIds = rows.Select(x => x.PetId).Distinct().ToArray();
        var pets = petIds.Length == 0
            ? []
            : await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct);
        var petById = pets.ToDictionary(x => x.Id);

        var clientIds = pets.Select(x => x.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct);
        var clientNameById = clients.ToDictionary(x => x.Id, x => x.FullName);

        var items = rows
            .Select(h =>
            {
                petById.TryGetValue(h.PetId, out var pet);
                var petName = pet?.Name ?? string.Empty;
                var clientId = pet?.ClientId ?? Guid.Empty;
                var clientName = clientId != Guid.Empty && clientNameById.TryGetValue(clientId, out var cn)
                    ? cn
                    : string.Empty;

                var isActive = h.DischargedAtUtc is null;

                return new HospitalizationListItemDto(
                    h.Id,
                    h.ClinicId,
                    h.PetId,
                    petName,
                    clientId,
                    clientName,
                    h.ExaminationId,
                    h.AdmittedAtUtc,
                    h.PlannedDischargeAtUtc,
                    h.DischargedAtUtc,
                    h.Reason,
                    isActive);
            })
            .ToList();

        return Result<PagedResult<HospitalizationListItemDto>>.Success(
            PagedResult<HospitalizationListItemDto>.Create(items, total, page, pageSize));
    }
}
