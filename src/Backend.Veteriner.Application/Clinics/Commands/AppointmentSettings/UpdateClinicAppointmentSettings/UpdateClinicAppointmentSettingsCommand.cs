using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clinics.Commands.AppointmentSettings.UpdateClinicAppointmentSettings;

/// <summary><see cref="OwnerCancelLinkEnabled"/> boşsa mevcut değer (kayıt yoksa varsayılan) korunur.</summary>
public sealed record UpdateClinicAppointmentSettingsCommand(
    Guid ClinicId,
    int DefaultAppointmentDurationMinutes,
    int SlotIntervalMinutes,
    bool AllowOverlappingAppointments,
    bool? OwnerCancelLinkEnabled = null)
    : IRequest<Result<ClinicAppointmentSettingsDto>>;
