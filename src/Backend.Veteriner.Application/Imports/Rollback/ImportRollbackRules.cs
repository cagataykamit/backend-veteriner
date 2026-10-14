namespace Backend.Veteriner.Application.Imports.Rollback;

/// <summary>Engelli kayıt politikası (ADR-019 K7; IMPORT_API_CONTRACT.md §7.12).</summary>
public static class ImportRollbackPolicy
{
    /// <summary>Varsayılan: engelli tek kayıt varsa hiçbir şey silinmez.</summary>
    public const string Fail = "Fail";

    /// <summary>Bağı olmayanlar silinir, bağı olanlar kalır (<c>PartiallyRolledBack</c>).</summary>
    public const string DeleteUnblockedOnly = "DeleteUnblockedOnly";

    public static readonly IReadOnlyList<string> All = [Fail, DeleteUnblockedOnly];
}

/// <summary>Silinmeyen (<c>Kept</c>) kaydın neden kodları (IMPORT_API_CONTRACT.md §9, B5 tablosu). Kod tek yerde.</summary>
public static class ImportRollbackReasons
{
    // Aşı
    public const string LinkedToExamination = "LinkedToExamination";
    public const string HasAdministration = "HasAdministration";
    public const string VaccinationModified = "VaccinationModified";

    // Hasta
    public const string HasAppointments = "HasAppointments";
    public const string HasVisits = "HasVisits";
    public const string HasExaminations = "HasExaminations";
    public const string HasTreatments = "HasTreatments";
    public const string HasPrescriptions = "HasPrescriptions";
    public const string HasLabResults = "HasLabResults";
    public const string HasHospitalizations = "HasHospitalizations";
    public const string HasAdministrations = "HasAdministrations";
    public const string HasCareInstructions = "HasCareInstructions";
    public const string HasChargeSuggestions = "HasChargeSuggestions";
    public const string HasOtherVaccinations = "HasOtherVaccinations";
    public const string HasKeptVaccinations = "HasKeptVaccinations";

    // Hasta ve müşteri
    public const string HasCharges = "HasCharges";
    public const string HasPayments = "HasPayments";

    // Müşteri
    public const string ClientModified = "ClientModified";
    public const string HasSales = "HasSales";
    public const string HasOtherPets = "HasOtherPets";
    public const string HasKeptPets = "HasKeptPets";

    /// <summary><c>Fail</c> politikasında iş sırasında birimde engel doğdu; birimin silinebilir kaydı da bırakıldı.</summary>
    public const string OwnerUnitBlocked = "OwnerUnitBlocked";
}
