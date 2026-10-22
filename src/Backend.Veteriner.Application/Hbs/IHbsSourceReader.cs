using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hbs;

namespace Backend.Veteriner.Application.Hbs;

public sealed record HbsAnimalSource(string Name, string Species, string? Breed, string? MicrochipNumber, string? HbsAnimalId);

/// <summary>Sahip; kimlik numaraları tam değerdir, maskeleme kararı çağıranındır.</summary>
public sealed record HbsOwnerSource(string FullName, string? Phone, ClientPersonType? PersonType, string? NationalId, string? TaxNumber);

public sealed record HbsEntrySource(
    HbsEntryStatus Status,
    string? ReferenceNumber,
    string? NotRequiredReason,
    DateTime? MarkedAtUtc,
    Guid? MarkedByUserId,
    string? MarkedByName);

public sealed record HbsPrescriptionItemSource(
    string Name,
    decimal? Quantity,
    string? Unit,
    string? Dose,
    string? Instructions,
    string? LotNumber);

public sealed record HbsPrescriptionSource(
    Guid Id,
    Guid ClinicId,
    DateTime PrescribedAtUtc,
    string Content,
    IReadOnlyList<HbsPrescriptionItemSource> Items,
    HbsAnimalSource Animal,
    HbsOwnerSource Owner,
    string? VeterinarianName,
    HbsEntrySource Hbs);

/// <param name="LotNumber">Bağlı iptal edilmemiş uygulamanın partisinden; ürünsüz aşıda null.</param>
/// <param name="DoseQuantity">Bağlı uygulamanın miktarı (ürünün stok birimiyle).</param>
public sealed record HbsVaccinationSource(
    Guid Id,
    Guid ClinicId,
    string VaccineName,
    DateTime? AppliedAtUtc,
    bool IsTracked,
    string? LotNumber,
    DateOnly? ExpiryDate,
    decimal? DoseQuantity,
    string? DoseUnit,
    HbsAnimalSource Animal,
    HbsOwnerSource Owner,
    string? VeterinarianName,
    HbsEntrySource Hbs);

/// <summary>
/// HBS hazır verisinin kaynağı (komut veritabanı, yalnız verilen kiracıda). Hekim kuralı defterlerle aynı: bağlı muayenenin geliş
/// kaydındaki sorumlu hekim, yoksa null. Klinik erişim denetimi çağıranındır.
/// </summary>
public interface IHbsSourceReader
{
    Task<HbsPrescriptionSource?> GetPrescriptionAsync(Guid tenantId, Guid prescriptionId, CancellationToken ct = default);

    Task<HbsVaccinationSource?> GetVaccinationAsync(Guid tenantId, Guid vaccinationId, CancellationToken ct = default);
}
