using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Queries.GetAddenda;

public sealed record GetExaminationAddendaQuery(Guid ExaminationId)
    : IRequest<Result<IReadOnlyList<ExaminationAddendumDto>>>;
