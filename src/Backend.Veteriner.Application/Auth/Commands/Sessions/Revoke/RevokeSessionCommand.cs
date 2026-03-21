using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Commands.Sessions.Revoke;

public sealed record RevokeSessionCommand(Guid UserId, Guid RefreshTokenId)
    : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => "Session.Revoke";
    public string AuditTarget => $"UserId={UserId}, RefreshTokenId={RefreshTokenId}";
}
