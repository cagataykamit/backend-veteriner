using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.CreateInvite;

public sealed record CreateTenantInviteCommand(
    Guid TenantId,
    string Email,
    Guid ClinicId,
    Guid OperationClaimId,
    DateTime? ExpiresAtUtc) : IRequest<Result<CreateTenantInviteResultDto>>;
