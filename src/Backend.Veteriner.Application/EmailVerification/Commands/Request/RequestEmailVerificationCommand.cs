using Backend.Veteriner.Application.Common.Abstractions;
using Mediator;

namespace Backend.Veteriner.Application.EmailVerification.Commands.Request;

public sealed record RequestEmailVerificationCommand(string Email)
    : IRequest<Unit>, IAuditableRequest
{
    public string AuditAction => "EmailVerification.Request";
    public string AuditTarget => $"Email={Email}";
}
