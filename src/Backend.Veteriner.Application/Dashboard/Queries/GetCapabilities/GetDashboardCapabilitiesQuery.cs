using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetCapabilities;

public sealed record GetDashboardCapabilitiesQuery : IRequest<Result<DashboardCapabilitiesDto>>;
