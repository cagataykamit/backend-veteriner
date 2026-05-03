using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.Update;

/// <summary>
/// Tenant-scoped klinik güncelleme. İletişim/profil alanları gövdeden tam set olarak yazılır (patch yok).
/// </summary>
public sealed record UpdateClinicCommand(
    Guid Id,
    string Name,
    string City,
    string? Phone = null,
    string? Email = null,
    string? Address = null,
    string? Description = null)
    : IRequest<Result<ClinicDetailDto>>;
