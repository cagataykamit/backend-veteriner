using Backend.Veteriner.Domain.Hbs;

namespace Backend.Veteriner.Application.Reports.Registers.Hbs;

/// <summary>HBS takibi — reçete satırı (sözleşme: HBS_ASSISTANT_API_CONTRACT.md bölüm 6).</summary>
/// <param name="Medications">İlaçlar tek metin: <c>Ad ×Miktar Birim</c>, <c>; </c> ile; satırsız reçetede <c>Content</c>.</param>
public sealed record HbsPrescriptionRegisterItemDto(
    int RowNumber,
    Guid PrescriptionId,
    DateTime PrescribedAtUtc,
    string OwnerName,
    string? OwnerPhone,
    string PetName,
    string Species,
    string? MicrochipNumber,
    string? HbsAnimalId,
    string Title,
    string Medications,
    string? VeterinarianName,
    HbsEntryStatus HbsStatus,
    string? ReferenceNumber,
    DateTime? MarkedAtUtc);

/// <summary>HBS takibi — aşı satırı; seri no/SKT bağlı iptal edilmemiş uygulamanın partisinden.</summary>
public sealed record HbsVaccinationRegisterItemDto(
    int RowNumber,
    Guid VaccinationId,
    DateTime AppliedAtUtc,
    string OwnerName,
    string? OwnerPhone,
    string PetName,
    string Species,
    string? MicrochipNumber,
    string? HbsAnimalId,
    string VaccineName,
    string? LotNumber,
    DateOnly? ExpiryDate,
    string? VeterinarianName,
    HbsEntryStatus HbsStatus,
    string? ReferenceNumber,
    DateTime? MarkedAtUtc);
