using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Commands.LogoutAll;

public sealed record LogoutAllCommand(Guid UserId)
    : IRequest<Unit>, IAuditableRequest
{
    public string AuditAction => "Session.LogoutAll";
    public string AuditTarget => $"UserId={UserId}";
}
