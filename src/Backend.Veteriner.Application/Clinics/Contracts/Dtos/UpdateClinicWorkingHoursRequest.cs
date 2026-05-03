namespace Backend.Veteriner.Application.Clinics.Contracts.Dtos;

/// <summary>PUT çalışma saatleri istek gövdesi; tam 7 gün gerekir.</summary>
public sealed record UpdateClinicWorkingHoursRequest(IReadOnlyList<ClinicWorkingHourDto> Items);
