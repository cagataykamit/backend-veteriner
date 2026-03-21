using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password, Guid? TenantId = null)
    : IRequest<Result<LoginResultDto>>;
