using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetOperationalAlerts;

public sealed record GetDashboardOperationalAlertsQuery : IRequest<Result<DashboardOperationalAlertsDto>>;
