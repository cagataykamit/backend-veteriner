using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

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
    private readonly IReadRepository<Species> _species;

    public GetAppointmentsListQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Clinic> clinics,
        IReadRepository<Species> species)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _pets = pets;
        _clients = clients;
        _clinics = clinics;
        _species = species;
    }

    public async Task<Result<PagedResult<AppointmentListItemDto>>> Handle(
        GetAppointmentsListQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<AppointmentListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kirac� ba�lam� yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var effectiveClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<PagedResult<AppointmentListItemDto>>.Failure(
                "Appointments.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var total = await _appointments.CountAsync(
            new AppointmentsFilteredCountSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.Status,
                request.DateFromUtc,
                request.DateToUtc),
            ct);

        var rows = await _appointments.ListAsync(
            new AppointmentsFilteredPagedSpec(
                tenantId,
                effectiveClinicId,
                request.PetId,
                request.Status,
                request.DateFromUtc,
                request.DateToUtc,
                page,
                pageSize),
            ct);

        var petIds = rows.Select(x => x.PetId).Distinct().ToArray();
        var clinicIds = rows.Select(x => x.ClinicId).Distinct().ToArray();

        var pets = petIds.Length == 0
            ? []
            : await _pets.ListAsync(new PetsByTenantIdsSpec(tenantId, petIds), ct);
        var petById = pets.ToDictionary(x => x.Id);

        var speciesIds = pets.Select(x => x.SpeciesId).Distinct().ToArray();
        var speciesRows = speciesIds.Length == 0
            ? []
            : await _species.ListAsync(new SpeciesByIdsSpec(speciesIds), ct);
        var speciesNameById = speciesRows.ToDictionary(x => x.Id, x => x.Name);

        var clientIds = pets.Select(x => x.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct);
        var clientNameById = clients.ToDictionary(x => x.Id, x => x.FullName);

        var clinics = clinicIds.Length == 0
            ? []
            : await _clinics.ListAsync(new ClinicsByTenantIdsSpec(tenantId, clinicIds), ct);
        var clinicNameById = clinics.ToDictionary(x => x.Id, x => x.Name);

        var items = rows
            .Select(a =>
            {
                petById.TryGetValue(a.PetId, out var pet);
                var clientName = pet is not null && clientNameById.TryGetValue(pet.ClientId, out var cName)
                    ? cName
                    : string.Empty;
                var petName = pet?.Name ?? string.Empty;
                var speciesId = pet?.SpeciesId ?? Guid.Empty;
                var speciesName = speciesId != Guid.Empty && speciesNameById.TryGetValue(speciesId, out var sName)
                    ? sName
                    : string.Empty;
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
                    clientName,
                    a.ScheduledAtUtc,
                    a.Status,
                    a.Notes);
            })
            .ToList();

        return Result<PagedResult<AppointmentListItemDto>>.Success(
            PagedResult<AppointmentListItemDto>.Create(items, total, page, pageSize));
    }
}