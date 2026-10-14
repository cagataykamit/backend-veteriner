namespace Backend.Veteriner.Domain.Imports;

/// <summary>İçe aktarmanın mantıksal tablosu; sayısal değerler kalıcıdır.</summary>
public enum ImportTable
{
    Customers = 1,
    Pets = 2,
    Vaccinations = 3,
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
