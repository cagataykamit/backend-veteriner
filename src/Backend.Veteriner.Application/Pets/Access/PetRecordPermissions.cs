using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;

namespace Backend.Veteriner.Application.Pets.Access;

/// <summary>Kayıt türü → gereken okuma izni (history-summary ve timeline için tek kural).</summary>
public static class PetRecordPermissions
{
    public static string RequiredPermission(PetRecordKind kind) => kind switch
    {
        PetRecordKind.Visit => PermissionCatalog.Visits.Read,
        PetRecordKind.Examination => PermissionCatalog.Examinations.Read,
        PetRecordKind.Treatment => PermissionCatalog.Treatments.Read,
        PetRecordKind.Prescription => PermissionCatalog.Prescriptions.Read,
        PetRecordKind.LabResult => PermissionCatalog.LabResults.Read,
        PetRecordKind.Vaccination => PermissionCatalog.Vaccinations.Read,
        PetRecordKind.Hospitalization => PermissionCatalog.Hospitalizations.Read,
        PetRecordKind.Appointment => PermissionCatalog.Appointments.Read,
        PetRecordKind.Payment => PermissionCatalog.Payments.Read,
        PetRecordKind.Administration => PermissionCatalog.Administrations.Read,
        PetRecordKind.Imaging => PermissionCatalog.Imaging.Read,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Bilinmeyen kayıt türü.")
    };

    public static bool CanRead(this ICurrentUserPermissionChecker checker, PetRecordKind kind)
        => checker.HasPermission(RequiredPermission(kind));

    /// <summary>İstenen türleri izni olanlar ve olmayanlar olarak ayırır (sıra korunur).</summary>
    public static (IReadOnlyList<PetRecordKind> Allowed, IReadOnlyList<PetRecordKind> Omitted) Split(
        this ICurrentUserPermissionChecker checker,
        IEnumerable<PetRecordKind> requested)
    {
        var allowed = new List<PetRecordKind>();
        var omitted = new List<PetRecordKind>();
        foreach (var kind in requested)
            (checker.CanRead(kind) ? allowed : omitted).Add(kind);
        return (allowed, omitted);
    }
}
