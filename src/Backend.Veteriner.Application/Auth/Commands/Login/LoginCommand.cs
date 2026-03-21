using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password)
    : IRequest<Result<LoginResultDto>>;
