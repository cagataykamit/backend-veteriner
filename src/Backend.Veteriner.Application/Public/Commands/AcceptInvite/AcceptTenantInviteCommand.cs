using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Public.Commands.AcceptInvite;

public sealed record AcceptTenantInviteCommand(string RawToken, Guid CurrentUserId)
    : IRequest<Result<TenantInviteAcceptResultDto>>, IIgnoreTenantWriteSubscriptionGuard;
