namespace Backend.Veteriner.Application.Auth.Queries.Me;

public sealed record MeDto(
    Guid UserId,
    string Email,
    bool EmailConfirmed,
    string[] Roles
);
