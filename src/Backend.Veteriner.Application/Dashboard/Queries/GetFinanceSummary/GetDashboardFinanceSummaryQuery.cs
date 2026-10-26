using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetFinanceSummary;

public sealed record GetDashboardFinanceSummaryQuery : IRequest<Result<DashboardFinanceSummaryDto>>;
