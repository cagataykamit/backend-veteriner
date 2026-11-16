using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Appointments.Queries.GetCalendar;

/// <param name="VeterinarianUserId">Yalnız bu hekimin randevuları (filtre, §6).</param>
/// <param name="WithoutVeterinarian">Yalnız hekimsiz randevular; <paramref name="VeterinarianUserId"/> ile birlikte verilemez.</param>
/// <param name="AppointmentTypes">Yalnız bu türler; boşsa tümü.</param>
/// <param name="States">Yalnız bu durumlar (bugün, gelecek, gecikmiş, tamamlanan); boşsa tümü.</param>
public sealed record GetAppointmentsCalendarQuery(
    DateTime? DateFromUtc,
    DateTime? DateToUtc,
    Guid? ClinicId = null,
    AppointmentStatus? Status = null,
    Guid? VeterinarianUserId = null,
    bool WithoutVeterinarian = false,
    IReadOnlyList<AppointmentType>? AppointmentTypes = null,
    IReadOnlyList<CalendarItemState>? States = null)
    : IRequest<Result<IReadOnlyList<AppointmentCalendarItemDto>>>;
