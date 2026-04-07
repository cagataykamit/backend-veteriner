using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Permissions.Delete;

public sealed record DeletePermissionCommand(Guid Id)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Permission.Delete";
    public string AuditTarget => $"Id={Id}";
}
