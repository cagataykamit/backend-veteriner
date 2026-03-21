using Backend.Veteriner.Application.Auth.Commands.Login;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Refresh;

public sealed record RefreshCommand(string RefreshToken, Guid? TenantId = null)
    : IRequest<Result<LoginResultDto>>;
