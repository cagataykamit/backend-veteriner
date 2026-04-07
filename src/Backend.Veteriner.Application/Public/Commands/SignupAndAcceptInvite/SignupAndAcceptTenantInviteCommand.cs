using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite;

public sealed record SignupAndAcceptTenantInviteCommand(string RawToken, string Password)
    : IRequest<Result<TenantInviteAcceptResultDto>>;
