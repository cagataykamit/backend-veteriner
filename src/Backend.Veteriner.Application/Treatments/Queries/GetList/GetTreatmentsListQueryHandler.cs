using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Treatments.Contracts.Dtos;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Treatments.Queries.GetList;

public sealed class GetTreatmentsListQueryHandler
    : IRequestHandler<GetTreatmentsListQuery, Result<PagedResult<TreatmentListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Treatment> _treatments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly ILogger<GetTreatmentsListQueryHandler> _logger;

    public GetTreatmentsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Treatment> treatments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        ILogger<GetTreatmentsListQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _treatments = treatments;
        _pets = pets;
        _clients = clients;
        _logger = logger ?? NullLogger<GetTreatmentsListQueryHandler>.Instance;
    }

    public async Task<Result<PagedResult<TreatmentListItemDto>>> Handle(
        GetTreatmentsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<TreatmentListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        var querySteps = 0;
        var slowestStep = string.Empty;
        long slowestMs = 0;

        void MarkStep(string name)
        {
            querySteps++;
            var elapsed = stepSw.ElapsedMilliseconds;
            if (elapsed > slowestMs)
            {
                slowestMs = elapsed;
                slowestStep = name;
            }

            stepSw.Restart();
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<TreatmentListItemDto>>.Failure(
                "Treatments.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<PagedResult<TreatmentListItemDto>>.Failure(scopeResult.Error);
        MarkStep("scopeResolve");

        var effectiveClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value!.AccessibleClinicIds;

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

        var total = await _treatments.CountAsync(
            new TreatmentsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.DateFromUtc,
                request.DateToUtc,
                searchPattern,
                searchPetIds,
                accessibleClinicIds),
            ct);
        MarkStep("treatmentsCount");

        var rows = await _treatments.ListAsync(
            new TreatmentsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.DateFromUtc,
                request.DateToUtc,
                page,
                pageSize,
                searchPattern,
                searchPetIds,
                accessibleClinicIds),
            ct);
        MarkStep("treatmentsPage");

        var petIds = rows.Select(x => x.PetId).Distinct().ToArray();
        var pets = petIds.Length == 0
            ? []
            : await _pets.ListAsync(new PetsByTenantIdsNameClientSpec(tenantId, petIds), ct);
        if (petIds.Length > 0)
            MarkStep("petsLookup");
        var petById = pets.ToDictionary(x => x.Id);

        var clientIds = pets.Select(x => x.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsNameSpec(tenantId, clientIds), ct);
        if (clientIds.Length > 0)
            MarkStep("clientsLookup");
        var clientNameById = clients.ToDictionary(x => x.Id, x => x.FullName);

        var items = rows
            .Select(t =>
            {
                petById.TryGetValue(t.PetId, out var pet);
                var petName = pet?.Name ?? string.Empty;
                var clientId = pet?.ClientId ?? Guid.Empty;
                var clientName = clientId != Guid.Empty && clientNameById.TryGetValue(clientId, out var cn)
                    ? cn
                    : string.Empty;

                return new TreatmentListItemDto(
                    t.Id,
                    t.ClinicId,
                    t.PetId,
                    petName,
                    clientId,
                    clientName,
                    t.TreatmentDateUtc,
                    t.Title,
                    t.ExaminationId,
                    t.FollowUpDateUtc);
            })
            .ToList();

        _logger.LogInformation(
            "Treatments list generated. TenantId={TenantId} ClinicId={ClinicId} Page={Page} PageSize={PageSize} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            tenantId,
            effectiveClinicId,
            page,
            pageSize,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<PagedResult<TreatmentListItemDto>>.Success(
            PagedResult<TreatmentListItemDto>.Create(items, total, page, pageSize));
    }
}
