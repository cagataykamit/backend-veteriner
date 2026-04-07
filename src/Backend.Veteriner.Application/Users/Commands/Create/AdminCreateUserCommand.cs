using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Users.Commands.Create;

/// <summary>
/// Admin tarafından kullanıcı oluşturma.
/// </summary>
public sealed record AdminCreateUserCommand(
    string Email,
    string Password
) : IRequest<Result<Guid>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "User.Create";
    public string AuditTarget => $"Email={Email}";
}

