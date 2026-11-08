using Backend.Veteriner.Application.Appointments.Calendar;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalAppointmentCalendar;

/// <summary>
/// Randevularım'daki ".ics indir" (TIER-020; docs/APPOINTMENT_OWNER_LINK_API_CONTRACT.md §8): bağın müşterisinin planlı ve saati
/// gelmemiş randevusu için takvim dosyası. Değilse <see cref="OwnerPortalErrors.AppointmentNotFound"/> (sebep ayırt edilmez).
/// </summary>
public sealed record GetPortalAppointmentCalendarQuery(Guid OwnerAccountId, Guid LinkId, Guid AppointmentId)
    : IRequest<Result<AppointmentCalendarFile>>, IAuditableRequest
{
    public string AuditAction => "Portal.Read.AppointmentCalendar";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId};AppointmentId={AppointmentId}";
}

public sealed class GetPortalAppointmentCalendarQueryHandler
    : IRequestHandler<GetPortalAppointmentCalendarQuery, Result<AppointmentCalendarFile>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly TimeProvider _timeProvider;

    public GetPortalAppointmentCalendarQueryHandler(
        OwnerPortalAccess access,
        IReadRepository<Pet> pets,
        IReadRepository<Appointment> appointments,
        IReadRepository<Clinic> clinics,
        TimeProvider timeProvider)
    {
        _access = access;
        _pets = pets;
        _appointments = appointments;
        _clinics = clinics;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentCalendarFile>> Handle(GetPortalAppointmentCalendarQuery request, CancellationToken ct)
    {
        var scopeResult = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scopeResult.IsSuccess)
            return Result<AppointmentCalendarFile>.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var appointment = await _appointments.FirstOrDefaultAsync(new AppointmentByIdSpec(scope.TenantId, request.AppointmentId), ct);
        if (appointment is null || !appointment.CanOwnerRespond(utcNow))
            return Result<AppointmentCalendarFile>.Failure(OwnerPortalErrors.AppointmentNotFound);

        var pet = (await _pets.ListAsync(new PortalPetNamesByClientSpec(scope.TenantId, scope.ClientId), ct))
            .FirstOrDefault(p => p.Id == appointment.PetId);
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(scope.TenantId, appointment.ClinicId), ct);
        if (pet is null || clinic is null)
            return Result<AppointmentCalendarFile>.Failure(OwnerPortalErrors.AppointmentNotFound);

        return Result<AppointmentCalendarFile>.Success(AppointmentCalendar.File(
            AppointmentCalendarEvent.Create(appointment.Id, appointment.ScheduledAtUtc, appointment.DurationMinutes, clinic, pet.Name),
            utcNow));
    }
}
