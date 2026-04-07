using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Sessions.RevokeMy;

public sealed record RevokeMySessionCommand(Guid Id)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Session.Revoke";
    public string AuditTarget => $"SessionId={Id}";
}
