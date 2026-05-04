using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.AppointmentSettings.GetClinicAppointmentSettings;

public sealed record GetClinicAppointmentSettingsQuery(Guid ClinicId)
    : IRequest<Result<ClinicAppointmentSettingsDto>>;
