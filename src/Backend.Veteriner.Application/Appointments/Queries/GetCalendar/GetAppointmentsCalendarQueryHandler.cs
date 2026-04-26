using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Queries.GetCalendar;

public sealed class GetAppointmentsCalendarQueryHandler
    : IRequestHandler<GetAppointmentsCalendarQuery, Result<IReadOnlyList<AppointmentCalendarItemDto>>>
{
    private static readonly TimeSpan MaxWindow = TimeSpan.FromDays(45);

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetAppointmentsCalendarQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _pets = pets;
        _clients = clients;
    }

    public async Task<Result<IReadOnlyList<AppointmentCalendarItemDto>>> Handle(GetAppointmentsCalendarQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (!request.DateFromUtc.HasValue || !request.DateToUtc.HasValue)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Appointments.Calendar.DateWindowRequired",
                "dateFromUtc ve dateToUtc zorunludur.");
        }

        var dateFromUtc = NormalizeToUtc(request.DateFromUtc.Value);
        var dateToUtc = NormalizeToUtc(request.DateToUtc.Value);
        if (dateToUtc <= dateFromUtc)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Appointments.Calendar.InvalidDateWindow",
                "dateToUtc, dateFromUtc'den büyük olmalıdır.");
        }

        if (dateToUtc - dateFromUtc > MaxWindow)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Appointments.Calendar.DateWindowTooLarge",
                "Tarih aralığı en fazla 45 gün olabilir.");
        }

        var effectiveClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Failure(
                "Appointments.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        var rows = await _appointments.ListAsync(
            new AppointmentsCalendarSpec(
                tenantId,
                effectiveClinicId,
                dateFromUtc,
                dateToUtc,
                request.Status),
            ct);

        if (rows.Count == 0)
            return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Success(Array.Empty<AppointmentCalendarItemDto>());

        var petIds = rows.Select(r => r.PetId).Distinct().ToArray();
        var pets = await _pets.ListAsync(new PetsByTenantIdsNameClientSpec(tenantId, petIds), ct);
        var petById = pets.ToDictionary(p => p.Id);

        var clientIds = pets.Select(p => p.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? []
            : await _clients.ListAsync(new ClientsByTenantIdsNameSpec(tenantId, clientIds), ct);
        var clientNameById = clients.ToDictionary(c => c.Id, c => c.FullName);

        var items = rows.Select(r =>
        {
            petById.TryGetValue(r.PetId, out var pet);
            var clientId = pet?.ClientId ?? Guid.Empty;
            var clientName = clientId != Guid.Empty && clientNameById.TryGetValue(clientId, out var cName)
                ? cName
                : string.Empty;
            return new AppointmentCalendarItemDto(
                r.Id,
                r.ClinicId,
                r.PetId,
                clientId,
                r.ScheduledAtUtc,
                r.Status,
                r.AppointmentType,
                pet?.Name ?? string.Empty,
                clientName);
        }).ToList();

        return Result<IReadOnlyList<AppointmentCalendarItemDto>>.Success(items);
    }

    private static DateTime NormalizeToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
