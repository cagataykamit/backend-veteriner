namespace Backend.Veteriner.Application.Reports.Registers.Prescriptions;

/// <summary>
/// Reçete defteri sütunlarının tek tanımı (CSV ve XLSX bunu okur). Resmî biçim doğrulandığında yalnız bu liste güncellenir.
/// </summary>
internal static class PrescriptionRegisterColumns
{
    private const string Prescription = "Reçetenin";
    private const string Veterinarian = "Veteriner Hekimin";
    private const string Owner = "Hayvan Sahibinin";
    private const string Animal = "Hayvanın";
    private const string Drug = "İlaç";

    public static readonly IReadOnlyList<RegisterColumn<PrescriptionRegisterLine>> All =
    [
        RegisterColumn<PrescriptionRegisterLine>.Number(null, "Sıra No", r => r.Entry.RowNumber),
        RegisterColumn<PrescriptionRegisterLine>.Date(Prescription, "Tarihi", r => r.Entry.PrescribedAtUtc),
        RegisterColumn<PrescriptionRegisterLine>.Text(Prescription, "Sınıfı", r => r.Entry.PrescriptionClass),
        RegisterColumn<PrescriptionRegisterLine>.Text(Prescription, "Cilt/Sıra No", r => r.Entry.VolumeSequenceNumber),
        RegisterColumn<PrescriptionRegisterLine>.Text(Veterinarian, "Adı Soyadı", r => r.Entry.VeterinarianName),
        RegisterColumn<PrescriptionRegisterLine>.Text(Veterinarian, "Adresi", r => r.Entry.VeterinarianAddress),
        RegisterColumn<PrescriptionRegisterLine>.Text(Owner, "Adı Soyadı", r => r.Entry.OwnerName),
        RegisterColumn<PrescriptionRegisterLine>.Text(Owner, "Adresi", r => r.Entry.OwnerAddress),
        RegisterColumn<PrescriptionRegisterLine>.Text(Animal, "Türü", r => r.Entry.Species),
        RegisterColumn<PrescriptionRegisterLine>.Text(Animal, "Kulak/İşletme No", r => r.Entry.EarTagNumber),
        RegisterColumn<PrescriptionRegisterLine>.Number(Animal, "Sayısı", r => r.Entry.AnimalCount),
        RegisterColumn<PrescriptionRegisterLine>.Text(Drug, "Adı", r => r.Item.Name),
        RegisterColumn<PrescriptionRegisterLine>.Text(Drug, "Farmakolojik Şekli", r => r.Item.PharmacologicalForm),
        RegisterColumn<PrescriptionRegisterLine>.Text(Drug, "Ambalajı", r => r.Item.Packaging),
        RegisterColumn<PrescriptionRegisterLine>.Number(Drug, "Adedi", r => r.Item.Quantity),
    ];
}
