using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Queries.GetById;

public sealed record GetExaminationByIdQuery(Guid Id) : IRequest<Result<ExaminationDetailDto>>;
