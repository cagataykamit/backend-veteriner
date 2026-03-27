using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Logout;

public sealed record LogoutCommand(string RefreshToken)
    : IRequest<Unit>, IAuditableRequest
{
    string IAuditableRequest.AuditAction => "Session.Logout";
    string IAuditableRequest.AuditTarget => $"RefreshToken={RefreshToken}";
}
