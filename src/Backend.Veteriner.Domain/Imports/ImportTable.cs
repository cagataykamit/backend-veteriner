namespace Backend.Veteriner.Domain.Imports;

/// <summary>
/// İçe aktarmanın mantıksal tablosu; sayısal değerler kalıcıdır ve işlem sırasıdır (doğrulama ve uygulama bu sırayla
/// yürür). 4–7 tam geçiş tablolarıdır (ADR-048).
/// </summary>
public enum ImportTable
{
    Customers = 1,
    Pets = 2,
    Vaccinations = 3,
    ClinicalRecords = 4,
    AccountEntries = 5,
    Appointments = 6,
    Products = 7,
}

/// <summary>Tablo kümeleri; tek yer.</summary>
public static class ImportTables
{
    /// <summary>Tüm tablolar, işlem sırasıyla.</summary>
    public static readonly IReadOnlyList<ImportTable> All =
    [
        ImportTable.Customers, ImportTable.Pets, ImportTable.Vaccinations, ImportTable.ClinicalRecords,
        ImportTable.AccountEntries, ImportTable.Appointments, ImportTable.Products,
    ];

    /// <summary>Satırları partinin kliniğine yazılan tablolar; partide varsa <c>clinicId</c> zorunludur.</summary>
    public static readonly IReadOnlySet<ImportTable> ClinicScoped = new HashSet<ImportTable>
    {
        ImportTable.Vaccinations, ImportTable.ClinicalRecords, ImportTable.AccountEntries, ImportTable.Appointments,
    };
}

/// <summary>Satırın doğrulama (kuru çalıştırma) sonucu.</summary>
public enum ImportRowValidationStatus
{
    Pending = 0,
    Valid = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>Satırın uygulama ve geri alma sonucu.</summary>
public enum ImportRowApplyStatus
{
    Pending = 0,
    Created = 1,
    Matched = 2,
    Skipped = 3,
    Failed = 4,
    RolledBack = 5,
    Kept = 6,
}
