using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Hospitalizations.Queries.GetList;

public sealed class GetHospitalizationsListQueryHandler
    : IRequestHandler<GetHospitalizationsListQuery, Result<PagedResult<HospitalizationListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Hospitalization> _hospitalizations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IPetReadModelLookupReader _petLookupReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;

    public GetHospitalizationsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Hospitalization> hospitalizations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IPetReadModelLookupReader petLookupReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _hospitalizations = hospitalizations;
        _pets = pets;
        _clients = clients;
        _petLookupReader = petLookupReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
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
        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<HospitalizationListItemDto>>.Failure(
                "Hospitalizations.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;

        // Güvenlik: açık bir klinik kapsamı (request.ClinicId veya aktif clinic context) yoksa
        // tüm kiracı yatış kayıtlarını DÖNDÜRME. Tenant-wide kullanıcılar dahil, kapsamsız list/okuma engellenir.
        if (requestedClinicId is null)
        {
            return Result<PagedResult<HospitalizationListItemDto>>.Failure(
                "Hospitalizations.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi. Yatışlar klinik kapsamı olmadan listelenemez.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PagedResult<HospitalizationListItemDto>>.Failure(scopeResult.Error);

        var effectiveClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value!.AccessibleClinicIds;

        var normalized = ListQueryTextSearch.Normalize(request.PageRequest.Search);
        string? searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);
        Guid[] searchPetIds = [];
        if (searchPattern is not null)
        {
            searchPetIds = await SharedSearchPetIdsLookup.ResolveAsync(
                tenantId,
                searchPattern,
                _queryReadModelsOptions.SharedSearchLookupEnabled,
                _petLookupReader,
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
                searchPetIds,
                accessibleClinicIds),
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
                searchPetIds,
                accessibleClinicIds),
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
