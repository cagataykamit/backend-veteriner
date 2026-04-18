using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.RemoveMemberClinic;

/// <summary>
/// Tenant paneli: mevcut bir üyeden klinik üyeliğini kaldırır (Faz 4B).
/// Idempotent: ilişki zaten yoksa <c>AlreadyRemoved = true</c> döner.
/// Self-protect: çağıran kullanıcı kendi üzerinden klinik üyeliği kaldıramaz (<c>Clinics.SelfClinicRemoveForbidden</c>).
/// </summary>
public sealed record RemoveTenantMemberClinicCommand(Guid TenantId, Guid MemberId, Guid ClinicId)
    : IRequest<Result<RemoveTenantMemberClinicResultDto>>;
