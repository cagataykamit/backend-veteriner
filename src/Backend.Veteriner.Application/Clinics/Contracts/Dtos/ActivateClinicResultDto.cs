namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>
/// Klinik aktifleştirme sonucu. <c>AlreadyActive = true</c> ise istek öncesinde klinik zaten aktifti (idempotent no-op).
/// </summary>
public sealed record ActivateClinicResultDto(Guid ClinicId, bool AlreadyActive);
