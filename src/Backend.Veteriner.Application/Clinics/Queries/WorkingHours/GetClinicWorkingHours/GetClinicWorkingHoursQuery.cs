using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.WorkingHours.GetClinicWorkingHours;

public sealed record GetClinicWorkingHoursQuery(Guid ClinicId)
    : IRequest<Result<IReadOnlyList<ClinicWorkingHourDto>>>;
