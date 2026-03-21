using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Logout;

public sealed record LogoutCommand(string RefreshToken)
    : IRequest<Unit>, IAuditableRequest
{
    public string AuditAction => "Session.Logout";
    public string AuditTarget => $"RefreshToken={RefreshToken}";
}
