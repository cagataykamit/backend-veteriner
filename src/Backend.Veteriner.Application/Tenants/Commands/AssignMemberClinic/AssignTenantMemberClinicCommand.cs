using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.AssignMemberClinic;

/// <summary>
/// Tenant paneli: mevcut bir üyeye bu kiracının kliniğini atar (Faz 4B).
/// <c>TenantId</c> JWT ile eşleşmelidir; üye kiracıda değilse 404 <c>Members.NotFound</c>.
/// Klinik bu kiracıda yoksa <c>Clinics.NotFound</c>, pasifse <c>Clinics.Inactive</c>.
/// Idempotent: ilişki zaten varsa <c>AlreadyAssigned = true</c> döner.
/// </summary>
public sealed record AssignTenantMemberClinicCommand(Guid TenantId, Guid MemberId, Guid ClinicId)
    : IRequest<Result<AssignTenantMemberClinicResultDto>>;
