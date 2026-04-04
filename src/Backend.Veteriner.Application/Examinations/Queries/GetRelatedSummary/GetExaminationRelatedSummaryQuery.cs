using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Examinations.Queries.GetRelatedSummary;

public sealed record GetExaminationRelatedSummaryQuery(Guid Id)
    : IRequest<Result<ExaminationRelatedSummaryDto>>;
