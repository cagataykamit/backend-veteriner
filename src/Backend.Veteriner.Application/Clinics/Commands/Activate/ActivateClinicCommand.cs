using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Activate;

/// <summary>
/// Tenant-scoped klinik tekrar aktifleştirme (Faz 5A). Idempotent.
/// </summary>
public sealed record ActivateClinicCommand(Guid Id)
    : IRequest<Result<ActivateClinicResultDto>>;
