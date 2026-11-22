using Backend.Veteriner.Application.DrugGuide.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DrugGuide.Queries.GetSummary;

public sealed record GetDrugGuideSummaryQuery : IRequest<Result<DrugGuideSummaryDto>>;
