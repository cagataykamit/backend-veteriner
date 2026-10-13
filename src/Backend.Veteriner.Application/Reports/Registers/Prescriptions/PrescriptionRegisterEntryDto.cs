namespace Backend.Veteriner.Application.Reports.Registers.Prescriptions;

/// <summary>
/// Reçete defteri kaydı (bir reçete). <c>PrescriptionClass</c>, <c>VolumeSequenceNumber</c> ve <c>EarTagNumber</c> eksik alanlardır, her zaman null.
/// <c>VeterinarianAddress</c> kliniğin adresidir.
/// </summary>
public sealed record PrescriptionRegisterEntryDto(
    int RowNumber,
    Guid PrescriptionId,
    DateTime PrescribedAtUtc,
    string? PrescriptionClass,
    string? VolumeSequenceNumber,
    string? VeterinarianName,
    string? VeterinarianAddress,
    string OwnerName,
    string? OwnerAddress,
    string Species,
    string? EarTagNumber,
    int AnimalCount,
    IReadOnlyList<PrescriptionRegisterItemDto> Items);

/// <summary><c>PharmacologicalForm</c> ve <c>Packaging</c> eksik alanlardır, her zaman null.</summary>
public sealed record PrescriptionRegisterItemDto(
    string Name,
    string? PharmacologicalForm,
    string? Packaging,
    decimal? Quantity);

/// <summary>Dışa aktarma satırı: reçete bilgisi + bir ilaç satırı (ekran ile aynı veriden türetilir).</summary>
public sealed record PrescriptionRegisterLine(PrescriptionRegisterEntryDto Entry, PrescriptionRegisterItemDto Item)
{
    /// <summary>Her reçeteyi ilaç satırı başına bir satıra açar; reçetenin sırası korunur. Reader satırsız reçeteye tek satır verir.</summary>
    public static IEnumerable<PrescriptionRegisterLine> Flatten(IEnumerable<PrescriptionRegisterEntryDto> entries)
        => entries.SelectMany(e => e.Items.Select(i => new PrescriptionRegisterLine(e, i)));
}
