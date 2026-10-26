using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clinics.Commands.AppointmentSettings.UpdateClinicAppointmentSettings;

public sealed record UpdateClinicAppointmentSettingsCommand(
    Guid ClinicId,
    int DefaultAppointmentDurationMinutes,
    int SlotIntervalMinutes,
    bool AllowOverlappingAppointments)
    : IRequest<Result<ClinicAppointmentSettingsDto>>;
