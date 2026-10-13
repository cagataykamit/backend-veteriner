namespace Backend.Veteriner.Application.Reports.Registers.Examinations;

/// <summary>Muayene defteri satırı (bir muayene). <c>CardNumber</c> ve <c>EPrescriptionId</c> eksik alanlardır, her zaman null.</summary>
public sealed record ExaminationRegisterItemDto(
    int RowNumber,
    Guid ExaminationId,
    DateTime ExaminedAtUtc,
    bool IsFinalized,
    string OwnerName,
    string? OwnerPhone,
    string? OwnerAddress,
    string PetName,
    string Species,
    string? Breed,
    string? Gender,
    string? Age,
    string? CardNumber,
    string? MicrochipNumber,
    string? ProtocolNumber,
    string? EPrescriptionId,
    string Complaint,
    string? Diagnosis,
    IReadOnlyList<string> Treatments,
    IReadOnlyList<RegisterPrescriptionDto> Prescriptions,
    IReadOnlyList<RegisterAdministrationDto> Administrations,
    string? VeterinarianName);

public sealed record RegisterPrescriptionDto(string Title, IReadOnlyList<RegisterPrescriptionLineDto> Items);

public sealed record RegisterPrescriptionLineDto(string Name, decimal? Quantity, string? Instructions);

/// <param name="AddedAfterFinalization">Muayene kesinleştikten sonra eklendi (ADR-013).</param>
/// <param name="IsVoided">İptal edilmiş; yalnız <c>includeVoided=true</c> iken gelir.</param>
public sealed record RegisterAdministrationDto(
    string ProductName,
    decimal Quantity,
    string Unit,
    bool AddedAfterFinalization,
    bool IsVoided);
