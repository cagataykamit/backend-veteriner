using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Files.Specs;
using Backend.Veteriner.Application.Imaging.Contracts.Dtos;
using Backend.Veteriner.Application.Imaging.Specs;
using Backend.Veteriner.Application.Pets.ReadModels;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Imaging.Queries.GetList;

/// <summary>Klinik kapsamlı sayfalı liste (Lab listesi deseni): klinik kapsamı olmadan listelenmez.</summary>
public sealed class GetImagingRecordsListQueryHandler
    : IRequestHandler<GetImagingRecordsListQuery, Result<PagedResult<ImagingRecordListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ImagingRecord> _records;
    private readonly IReadRepository<StoredFile> _files;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IPetReadModelLookupReader _petLookupReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;

    public GetImagingRecordsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ImagingRecord> records,
        IReadRepository<StoredFile> files,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IPetReadModelLookupReader petLookupReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _records = records;
        _files = files;
        _pets = pets;
        _clients = clients;
        _petLookupReader = petLookupReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
    }

    public async Task<Result<PagedResult<ImagingRecordListItemDto>>> Handle(
        GetImagingRecordsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Fail(
                "ImagingRecords.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (requestedClinicId is null)
        {
            return Fail(
                "ImagingRecords.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scope.IsSuccess)
            return Result<PagedResult<ImagingRecordListItemDto>>.Failure(scope.Error);

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);

        var normalized = ListQueryTextSearch.Normalize(request.PageRequest.Search);
        var searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);
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

        var filter = new ImagingRecordListFilter(
            tenantId,
            scope.Value!.SingleClinicId,
            scope.Value!.AccessibleClinicIds,
            request.PetId,
            request.ExaminationId,
            request.Modality,
            request.Status,
            request.DateFromUtc,
            request.DateToUtc,
            searchPattern,
            searchPetIds);

        var total = await _records.CountAsync(new ImagingRecordsFilteredCountSpec(filter), ct);
        var rows = await _records.ListAsync(
            new ImagingRecordsFilteredPagedSpec(
                filter, page, pageSize, ImagingRecordListSort.Catalog.Resolve(request.PageRequest)),
            ct);

        var recordIds = rows.Select(r => r.Id).ToArray();
        var fileOwnerIds = recordIds.Length == 0
            ? []
            : await _files.ListAsync(new StoredFileReadyOwnerIdsSpec(tenantId, StoredFileOwnerType.ImagingRecord, recordIds), ct);
        var fileCountByRecord = fileOwnerIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        var petIds = rows.Select(r => r.PetId).Distinct().ToArray();
        var pets = petIds.Length == 0 ? [] : await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct);
        var petById = pets.ToDictionary(p => p.Id);

        var clientIds = pets.Select(p => p.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0 ? [] : await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct);
        var clientNameById = clients.ToDictionary(c => c.Id, c => c.FullName);

        var items = rows
            .Select(r =>
            {
                petById.TryGetValue(r.PetId, out var pet);
                var clientId = pet?.ClientId ?? Guid.Empty;
                return new ImagingRecordListItemDto(
                    r.Id,
                    r.ClinicId,
                    r.PetId,
                    pet?.Name ?? string.Empty,
                    clientId,
                    clientNameById.GetValueOrDefault(clientId, string.Empty),
                    r.ExaminationId,
                    r.PerformedAtUtc,
                    r.Modality,
                    r.Description,
                    r.Status,
                    fileCountByRecord.GetValueOrDefault(r.Id),
                    r.AddedAfterExaminationFinalization);
            })
            .ToList();

        return Result<PagedResult<ImagingRecordListItemDto>>.Success(
            PagedResult<ImagingRecordListItemDto>.Create(items, total, page, pageSize));
    }

    private static Result<PagedResult<ImagingRecordListItemDto>> Fail(string code, string message)
        => Result<PagedResult<ImagingRecordListItemDto>>.Failure(code, message);
}
