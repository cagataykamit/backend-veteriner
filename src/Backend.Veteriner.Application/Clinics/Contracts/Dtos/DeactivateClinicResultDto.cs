namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>
/// Klinik pasife alma sonucu. <c>AlreadyInactive = true</c> ise istek öncesinde klinik zaten pasifti (idempotent no-op).
/// </summary>
public sealed record DeactivateClinicResultDto(Guid ClinicId, bool AlreadyInactive);
