using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
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
        var required = new List<string>();
        if (batch.CustomerCounts.Total > 0)
            required.Add(PermissionCatalog.Clients.Create);
        if (batch.PetCounts.Total > 0)
            required.Add(PermissionCatalog.Pets.Create);
        if (batch.VaccinationCounts.Total > 0)
            required.Add(PermissionCatalog.Vaccinations.Create);

        var missing = required.Where(p => !permissions.HasPermission(p)).ToList();
        return missing.Count == 0
            ? Result.Success()
            : Result.Failure("Imports.AccessDenied", $"Bu işlem için şu izinler de gerekir: {string.Join(", ", missing)}.");
    }
}
