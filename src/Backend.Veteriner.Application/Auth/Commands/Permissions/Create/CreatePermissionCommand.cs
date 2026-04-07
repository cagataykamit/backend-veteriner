using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Permissions.Create;

public sealed record CreatePermissionCommand(string Code, string? Description)
    : IRequest<Result<Guid>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Permission.Create";
    public string AuditTarget => $"Code={Code}";
}
