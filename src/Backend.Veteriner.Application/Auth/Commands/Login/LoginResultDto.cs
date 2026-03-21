namespace Backend.Veteriner.Application.Auth.Commands.Login;

public sealed record LoginResultDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt
);