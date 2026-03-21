namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

public sealed record DashboardRecentClientDto(Guid Id, string FullName, string? Phone);
