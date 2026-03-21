using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Appointments.Queries.GetById;

public sealed record GetAppointmentByIdQuery(Guid Id) : IRequest<Result<AppointmentDetailDto>>;
