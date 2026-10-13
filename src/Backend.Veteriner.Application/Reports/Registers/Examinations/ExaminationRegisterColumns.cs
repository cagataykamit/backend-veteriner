namespace Backend.Veteriner.Application.Reports.Registers.Examinations;

/// <summary>
/// Muayene defteri sütunlarının tek tanımı (CSV ve XLSX bunu okur). Resmî biçim doğrulandığında yalnız bu liste güncellenir.
/// </summary>
internal static class ExaminationRegisterColumns
{
    private const string Patient = "Hasta";

    public static readonly IReadOnlyList<RegisterColumn<ExaminationRegisterItemDto>> All =
    [
        RegisterColumn<ExaminationRegisterItemDto>.Number(null, "Sıra No", r => r.RowNumber),
        RegisterColumn<ExaminationRegisterItemDto>.Date(null, "Muayene Tarihi", r => r.ExaminedAtUtc),
        RegisterColumn<ExaminationRegisterItemDto>.Text(null, "Hasta Sahibinin Adı Soyadı", r => r.OwnerName),
        RegisterColumn<ExaminationRegisterItemDto>.Text(null, "Telefonu", r => r.OwnerPhone),
        RegisterColumn<ExaminationRegisterItemDto>.Text(null, "Adresi", r => r.OwnerAddress),
        RegisterColumn<ExaminationRegisterItemDto>.Text(Patient, "Ad", r => r.PetName),
        RegisterColumn<ExaminationRegisterItemDto>.Text(Patient, "Tür", r => r.Species),
        RegisterColumn<ExaminationRegisterItemDto>.Text(Patient, "Irk", r => r.Breed),
        RegisterColumn<ExaminationRegisterItemDto>.Text(Patient, "Cinsiyet", r => r.Gender),
        RegisterColumn<ExaminationRegisterItemDto>.Text(Patient, "Yaş", r => r.Age),
        RegisterColumn<ExaminationRegisterItemDto>.Text(Patient, "Kart No", r => r.CardNumber),
        RegisterColumn<ExaminationRegisterItemDto>.Text(Patient, "Çip No", r => r.MicrochipNumber),
        RegisterColumn<ExaminationRegisterItemDto>.Text(Patient, "Protokol No", r => r.ProtocolNumber),
        RegisterColumn<ExaminationRegisterItemDto>.Text(Patient, "e-Reçete Kimlik", r => r.EPrescriptionId),
        RegisterColumn<ExaminationRegisterItemDto>.Text(null, "Hastanın Şikayeti", r => r.Complaint),
        RegisterColumn<ExaminationRegisterItemDto>.Text(null, "Teşhis Edilen Hastalık", r => r.Diagnosis),
        RegisterColumn<ExaminationRegisterItemDto>.Text(null, "Tedavi Şekli", r => string.Join("; ", r.Treatments)),
        RegisterColumn<ExaminationRegisterItemDto>.Text(null, "Reçete Bilgileri", r => string.Join("; ", r.Prescriptions.Select(FormatPrescription))),
        RegisterColumn<ExaminationRegisterItemDto>.Text(null, "Satışa Eklenen İlaçlar", r => string.Join("; ", r.Administrations.Select(FormatAdministration))),
        RegisterColumn<ExaminationRegisterItemDto>.Text(null, "Veteriner Hekimin Adı Soyadı", r => r.VeterinarianName),
    ];

    /// <summary><c>Başlık: İlaç ×Miktar, İlaç</c>; satırsız (eski) reçetede yalnız başlık.</summary>
    private static string FormatPrescription(RegisterPrescriptionDto p)
    {
        if (p.Items.Count == 0)
            return p.Title;

        var items = p.Items.Select(i => i.Quantity is { } q ? $"{i.Name} ×{RegisterDisplay.Quantity(q)}" : i.Name);
        return $"{p.Title}: {string.Join(", ", items)}";
    }

    /// <summary><c>Ürün Miktar Birim</c> + sonradan eklenen için ek + iptal için ek.</summary>
    private static string FormatAdministration(RegisterAdministrationDto a)
        => $"{a.ProductName} {RegisterDisplay.Quantity(a.Quantity)} {a.Unit}"
           + (a.AddedAfterFinalization ? " (sonradan eklendi)" : string.Empty)
           + (a.IsVoided ? " (iptal)" : string.Empty);
}
