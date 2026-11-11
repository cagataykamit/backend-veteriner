using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imports.Access;

/// <summary>
/// Yetki yükseltmesini önler (IMPORT_API_CONTRACT.md §2): partide bulunan her tablo için ilgili oluşturma izni de gerekir.
/// Uygulama (commit) ve geri alma (rollback) aynı kuralı kullanır.
/// </summary>
public static class ImportWritePermissions
{
    public static Result Check(ImportBatch batch, ICurrentUserPermissionChecker permissions)
    {
        var required = Required(batch);
        var missing = required.Where(p => !permissions.HasPermission(p)).ToList();
        return missing.Count == 0
            ? Result.Success()
            : Result.Failure("Imports.AccessDenied", $"Bu işlem için şu izinler de gerekir: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// Partide satırı olan tabloların oluşturma izinleri (tekilleştirilmiş). Devir bakiyesi ücret açtığından müşteri
    /// tablosunda <c>AcikBakiye</c> eşlenmişse <c>Charges.Create</c> de gerekir (ADR-048).
    /// </summary>
    public static IReadOnlyList<string> Required(ImportBatch batch)
    {
        var required = new List<string>();
        foreach (var table in ImportTables.All.Where(t => batch.CountsOf(t).Total > 0))
        {
            required.Add(table switch
            {
                ImportTable.Customers => PermissionCatalog.Clients.Create,
                ImportTable.Pets => PermissionCatalog.Pets.Create,
                ImportTable.Vaccinations => PermissionCatalog.Vaccinations.Create,
                ImportTable.ClinicalRecords => PermissionCatalog.Examinations.Create,
                ImportTable.AccountEntries => PermissionCatalog.Charges.Create,
                ImportTable.Appointments => PermissionCatalog.Appointments.Create,
                ImportTable.Products => PermissionCatalog.Products.Create,
                _ => throw new ArgumentOutOfRangeException(nameof(table)),
            });
        }

        if (batch.CustomerCounts.Total > 0
            && ImportMappingDocument.Parse(batch.MappingJson).ColumnsOf(ImportTable.Customers).ContainsKey("AcikBakiye"))
        {
            required.Add(PermissionCatalog.Charges.Create);
        }

        return required.Distinct(StringComparer.Ordinal).ToList();
    }
}
