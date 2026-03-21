using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Users.Contracts.Dtos;
using MediatR;

namespace Backend.Veteriner.Application.Users.Queries.Claims.GetByUserId;

/// <summary>
/// Admin: kullanıcının rolleri (operation claim) listesi.
/// </summary>
public sealed record AdminGetUserClaimsQuery(Guid UserId)
    : IRequest<IReadOnlyList<UserOperationClaimDetailDto>>;
