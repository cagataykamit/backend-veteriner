using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Prescriptions.Contracts.Dtos;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Prescriptions.Queries.GetList;

public sealed class GetPrescriptionsListQueryHandler
    : IRequestHandler<GetPrescriptionsListQuery, Result<PagedResult<PrescriptionListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Prescription> _prescriptions;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetPrescriptionsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Prescription> prescriptions,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _prescriptions = prescriptions;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<PagedResult<PrescriptionListItemDto>>> Handle(
        GetPrescriptionsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<PrescriptionListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var effectiveClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<PrescriptionListItemDto>>.Failure(
                "Prescriptions.ClinicContextMismatch",
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

        var total = await _prescriptions.CountAsync(
            new PrescriptionsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.DateFromUtc,
                request.DateToUtc,
                searchPattern,
                searchPetIds),
            ct);

        var rows = await _prescriptions.ListAsync(
            new PrescriptionsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
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
            .Select(pr =>
            {
                petById.TryGetValue(pr.PetId, out var pet);
                var petName = pet?.Name ?? string.Empty;
                var clientId = pet?.ClientId ?? Guid.Empty;
                var clientName = clientId != Guid.Empty && clientNameById.TryGetValue(clientId, out var cn)
                    ? cn
                    : string.Empty;

                return new PrescriptionListItemDto(
                    pr.Id,
                    pr.ClinicId,
                    pr.PetId,
                    petName,
                    clientId,
                    clientName,
                    pr.PrescribedAtUtc,
                    pr.Title,
                    pr.ExaminationId,
                    pr.TreatmentId,
                    pr.FollowUpDateUtc);
            })
            .ToList();

        return Result<PagedResult<PrescriptionListItemDto>>.Success(
            PagedResult<PrescriptionListItemDto>.Create(items, total, page, pageSize));
    }
}
