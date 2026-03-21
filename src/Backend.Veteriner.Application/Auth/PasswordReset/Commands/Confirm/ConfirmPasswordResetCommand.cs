using Backend.Veteriner.Application.Common.Abstractions;
using MediatR;

namespace Backend.Veteriner.Application.Auth.PasswordReset.Commands.Confirm;

public sealed record ConfirmPasswordResetCommand(string Token, string NewPassword)
    : IRequest<Unit>, IAuditableRequest
{
    public string AuditAction => "PasswordReset.Confirm";
    public string? AuditTarget => "Token=***";
}
