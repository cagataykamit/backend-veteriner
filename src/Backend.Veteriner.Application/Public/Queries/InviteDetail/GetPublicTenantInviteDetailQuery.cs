using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Public.Queries.InviteDetail;

public sealed record GetPublicTenantInviteDetailQuery(string RawToken)
    : IRequest<Result<PublicTenantInviteDetailDto>>;
