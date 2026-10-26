using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Auth.Commands.ChangePassword;

public sealed record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword)
    : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Account.ChangePassword";
    public string AuditTarget => "SelfService";
}
