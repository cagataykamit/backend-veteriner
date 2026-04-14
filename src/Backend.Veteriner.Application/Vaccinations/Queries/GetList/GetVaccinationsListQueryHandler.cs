using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Vaccinations.Contracts.Dtos;
using Backend.Veteriner.Application.Vaccinations.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Vaccinations.Queries.GetList;

public sealed class GetVaccinationsListQueryHandler
    : IRequestHandler<GetVaccinationsListQuery, Result<PagedResult<VaccinationListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly ILogger<GetVaccinationsListQueryHandler> _logger;

    public GetVaccinationsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        ILogger<GetVaccinationsListQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _vaccinations = vaccinations;
        _pets = pets;
        _clients = clients;
        _logger = logger ?? NullLogger<GetVaccinationsListQueryHandler>.Instance;
    }

    public async Task<Result<PagedResult<VaccinationListItemDto>>> Handle(
        GetVaccinationsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<VaccinationListItemDto>>.Failure(
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

        var effectiveClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<VaccinationListItemDto>>.Failure(
                "Vaccinations.ClinicContextMismatch",
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
            MarkStep("searchPetIdsLookup");
        }

        var total = await _vaccinations.CountAsync(
            new VaccinationsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.Status,
                request.DueFromUtc,
                request.DueToUtc,
                request.AppliedFromUtc,
                request.AppliedToUtc,
                searchPattern,
                searchPetIds),
            ct);
        MarkStep("vaccinationsCount");

        var rows = await _vaccinations.ListAsync(
            new VaccinationsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.Status,
                request.DueFromUtc,
                request.DueToUtc,
                request.AppliedFromUtc,
                request.AppliedToUtc,
                page,
                pageSize,
                searchPattern,
                searchPetIds),
            ct);
        MarkStep("vaccinationsPage");

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
            .Select(v =>
            {
                petById.TryGetValue(v.PetId, out var pet);
                var petName = pet?.Name ?? string.Empty;
                var clientId = pet?.ClientId ?? Guid.Empty;
                var clientName = clientId != Guid.Empty && clientNameById.TryGetValue(clientId, out var cName)
                    ? cName
                    : string.Empty;

                return new VaccinationListItemDto(
                    v.Id,
                    v.PetId,
                    petName,
                    clientId,
                    clientName,
                    v.ClinicId,
                    v.ExaminationId,
                    v.VaccineName,
                    v.AppliedAtUtc,
                    v.DueAtUtc,
                    v.Status);
            })
            .ToList();

        _logger.LogInformation(
            "Vaccinations list generated. TenantId={TenantId} ClinicId={ClinicId} Page={Page} PageSize={PageSize} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            tenantId,
            effectiveClinicId,
            page,
            pageSize,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<PagedResult<VaccinationListItemDto>>.Success(
            PagedResult<VaccinationListItemDto>.Create(items, total, page, pageSize));
    }
}
