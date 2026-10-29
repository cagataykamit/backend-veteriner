namespace Backend.Veteriner.Application.Reports.Registers.Prescriptions;

/// <summary>
/// Reçete defteri sütunlarının tek tanımı (CSV, XLSX ve PDF bunu okur). Resmî biçim doğrulandığında yalnız bu liste güncellenir.
/// </summary>
internal static class PrescriptionRegisterColumns
{
    private const string Prescription = "Reçetenin";
    private const string Veterinarian = "Veteriner Hekimin";
    private const string Owner = "Hayvan Sahibinin";
    private const string Animal = "Hayvanın";

    public static readonly IReadOnlyList<RegisterColumn<PrescriptionRegisterEntryDto>> All =
    [
        RegisterColumn<PrescriptionRegisterEntryDto>.Number(null, "Sıra No", r => r.RowNumber),
        RegisterColumn<PrescriptionRegisterEntryDto>.Date(Prescription, "Tarihi", r => r.PrescribedAtUtc),
        RegisterColumn<PrescriptionRegisterEntryDto>.Text(Prescription, "Sınıfı", r => r.PrescriptionClass),
        RegisterColumn<PrescriptionRegisterEntryDto>.Text(Prescription, "Cilt/Sıra No", r => r.VolumeSequenceNumber),
        RegisterColumn<PrescriptionRegisterEntryDto>.Text(Veterinarian, "Adı Soyadı", r => r.VeterinarianName),
        RegisterColumn<PrescriptionRegisterEntryDto>.Text(Veterinarian, "Adresi", r => r.VeterinarianAddress),
        RegisterColumn<PrescriptionRegisterEntryDto>.Text(Owner, "Adı Soyadı", r => r.OwnerName),
        RegisterColumn<PrescriptionRegisterEntryDto>.Text(Owner, "Adresi", r => r.OwnerAddress),
        RegisterColumn<PrescriptionRegisterEntryDto>.Text(Animal, "Türü", r => r.Species),
        RegisterColumn<PrescriptionRegisterEntryDto>.Text(Animal, "Kulak/İşletme No", r => r.EarTagNumber),
        RegisterColumn<PrescriptionRegisterEntryDto>.Number(Animal, "Sayısı", r => r.AnimalCount),
        RegisterColumn<PrescriptionRegisterEntryDto>.Text(null, "İlacın Adı, Farmakolojik Şekli, Ambalajı, Adedi", r => string.Join("; ", r.Items.Select(FormatItem))),
    ];

    /// <summary>PDF dışa aktarma tanımı (CSV/XLSX işleyicileri sütunları doğrudan kullanır).</summary>
    public static readonly RegisterExportSpec<PrescriptionRegisterEntryDto> Export =
        new(All, "Recete Defteri", "recete-defteri", "Reçete Kayıt Defteri");

    /// <summary><c>Ad (farmakolojik şekil, ambalaj) ×Adet</c>; boş parçalar atlanır.</summary>
    private static string FormatItem(PrescriptionRegisterItemDto item)
    {
        var details = new[] { item.PharmacologicalForm, item.Packaging }.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        var text = details.Count == 0 ? item.Name : $"{item.Name} ({string.Join(", ", details)})";
        return item.Quantity is { } q ? $"{text} ×{RegisterDisplay.Quantity(q)}" : text;
    }
}
