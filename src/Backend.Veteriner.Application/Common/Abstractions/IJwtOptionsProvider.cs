namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IJwtOptionsProvider
{
    int AccessTokenMinutes { get; }
    int RefreshTokenDays { get; }
}
