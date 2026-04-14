using Backend.Veteriner.Application.Appointments;
using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Appointments.Queries.GetList;

public sealed class GetAppointmentsListQueryHandler
    : IRequestHandler<GetAppointmentsListQuery, Result<PagedResult<AppointmentListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly ILogger<GetAppointmentsListQueryHandler> _logger;

    public GetAppointmentsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Clinic> clinics,
        ILogger<GetAppointmentsListQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _pets = pets;
        _clients = clients;
        _clinics = clinics;
        _logger = logger ?? NullLogger<GetAppointmentsListQueryHandler>.Instance;
    }

    public async Task<Result<PagedResult<AppointmentListItemDto>>> Handle(
        GetAppointmentsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<AppointmentListItemDto>>.Failure(
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
            return Result<PagedResult<AppointmentListItemDto>>.Failure(
                "Appointments.ClinicContextMismatch",
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

        var scheduledAtDescending = AppointmentListSort.ResolveScheduledAtDescending(request.PageRequest);

        var total = await _appointments.CountAsync(
            new AppointmentsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.Status,
                request.DateFromUtc,
                request.DateToUtc,
                searchPattern,
                searchPetIds),
            ct);
        MarkStep("appointmentsCount");

        var rows = await _appointments.ListAsync(
            new AppointmentsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.Status,
                request.DateFromUtc,
                request.DateToUtc,
                page,
                pageSize,
                searchPattern,
                searchPetIds,
                scheduledAtDescending),
            ct);
        MarkStep("appointmentsPage");

        var petIds = rows.Select(x => x.PetId).Distinct().ToArray();
        var clinicIds = rows.Select(x => x.ClinicId).Distinct().ToArray();

        var pets = petIds.Length == 0
            ? []
            : await _pets.ListAsync(new PetsByTenantIdsNameClientSpeciesSpec(tenantId, petIds), ct);
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

        var clinics = clinicIds.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsByTenantIdsNameSpec(tenantId, clinicIds), ct);
        if (clinicIds.Length > 0)
            MarkStep("clinicsLookup");
        var clinicNameById = clinics.ToDictionary(x => x.Id, x => x.Name);

        var items = rows
            .Select(a =>
            {
                petById.TryGetValue(a.PetId, out var pet);
                var clientId = pet?.ClientId ?? Guid.Empty;
                var clientName = clientId != Guid.Empty && clientNameById.TryGetValue(clientId, out var cName)
                    ? cName
                    : string.Empty;
                var petName = pet?.Name ?? string.Empty;
                var speciesId = pet?.SpeciesId ?? Guid.Empty;
                var speciesName = pet?.SpeciesName ?? string.Empty;
                var clinicName = clinicNameById.TryGetValue(a.ClinicId, out var clName)
                    ? clName
                    : string.Empty;

                return new AppointmentListItemDto(
                    a.Id,
                    a.TenantId,
                    a.ClinicId,
                    clinicName,
                    a.PetId,
                    petName,
                    speciesId,
                    speciesName,
                    a.AppointmentType,
                    clientId,
                    clientName,
                    a.ScheduledAtUtc,
                    a.Status,
                    a.Notes);
            })
            .ToList();

        _logger.LogInformation(
            "Appointments list generated. TenantId={TenantId} ClinicId={ClinicId} Page={Page} PageSize={PageSize} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            tenantId,
            effectiveClinicId,
            page,
            pageSize,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<PagedResult<AppointmentListItemDto>>.Success(
            PagedResult<AppointmentListItemDto>.Create(items, total, page, pageSize));
    }
}
