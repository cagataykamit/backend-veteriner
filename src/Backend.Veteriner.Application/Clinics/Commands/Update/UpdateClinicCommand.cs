using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Update;

/// <summary>
/// Tenant-scoped klinik güncelleme (Faz 5A). Mevcut <c>ClinicDetailDto</c> contract'ını korur.
/// </summary>
public sealed record UpdateClinicCommand(Guid Id, string Name, string City)
    : IRequest<Result<ClinicDetailDto>>;
