using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Deactivate;

/// <summary>
/// Tenant-scoped klinik pasife alma (Faz 5A). Idempotent.
/// </summary>
public sealed record DeactivateClinicCommand(Guid Id)
    : IRequest<Result<DeactivateClinicResultDto>>;
