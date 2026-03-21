namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

public sealed record DashboardRecentPetDto(Guid Id, Guid ClientId, string Name, string Species);
