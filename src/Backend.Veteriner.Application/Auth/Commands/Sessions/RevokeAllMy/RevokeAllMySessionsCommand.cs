using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeAllMy;

public sealed record RevokeAllMySessionsCommand(Guid UserId)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Session.LogoutAll";
    public string AuditTarget => $"UserId={UserId}";
}
