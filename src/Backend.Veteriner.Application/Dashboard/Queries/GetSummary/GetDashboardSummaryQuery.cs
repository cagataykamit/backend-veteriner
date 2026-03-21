using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetSummary;

public sealed record GetDashboardSummaryQuery : IRequest<Result<DashboardSummaryDto>>;
