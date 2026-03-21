using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.EmailVerification.Commands.Confirm;

public sealed record ConfirmEmailVerificationCommand(string Token)
    : IRequest<Unit>, IAuditableRequest
{
    public string AuditAction => "EmailVerification.Confirm";
    public string? AuditTarget => "Token=***";
}
