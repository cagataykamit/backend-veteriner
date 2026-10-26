using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;

namespace Backend.Veteriner.Application.Auth.PasswordReset.Commands.Request;

public sealed record RequestPasswordResetCommand(string Email)
    : IRequest<Unit>, IAuditableRequest
{
    public string AuditAction => "PasswordReset.Request";
    public string AuditTarget => $"Email={Email}";
}
