using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Permissions.Update;

public sealed record UpdatePermissionCommand(Guid Id, string Code, string? Description)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Permission.Update";
    public string AuditTarget => $"Id={Id}, Code={Code}";
}
