using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.WorkingHours.UpdateClinicWorkingHours;

public sealed record UpdateClinicWorkingHoursCommand(Guid ClinicId, IReadOnlyList<ClinicWorkingHourDto> Items)
    : IRequest<Result<IReadOnlyList<ClinicWorkingHourDto>>>;
