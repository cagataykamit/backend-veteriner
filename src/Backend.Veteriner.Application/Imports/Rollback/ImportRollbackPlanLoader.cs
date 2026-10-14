using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Rollback;

/// <summary>Satırlar (hedef + eşleşen + geri alınmış) ve bunlardan kurulan plan.</summary>
public sealed record ImportRollbackSnapshot(IReadOnlyList<ImportRollbackRowInfo> Rows, ImportRollbackPlan Plan)
{
    public int CountRows(ImportTable table, ImportRowApplyStatus status)
        => Rows.Count(r => r.Table == table && r.ApplyStatus == status);
}

/// <summary>
/// Planı kurar: satırları (tek sorgu) ve hedeflerin bağımlılıklarını (<see cref="IImportRollbackDependencyReader"/>)
/// okuyup <see cref="ImportRollbackPlanner"/> çalıştırır. Önizleme, istek anı engel denetimi ve birim işlemi bunu kullanır.
/// </summary>
public sealed class ImportRollbackPlanLoader
{
    private readonly IReadRepository<ImportBatchRow> _rows;
    private readonly IImportRollbackDependencyReader _dependencies;

    public ImportRollbackPlanLoader(IReadRepository<ImportBatchRow> rows, IImportRollbackDependencyReader dependencies)
    {
        _rows = rows;
        _dependencies = dependencies;
    }

    public async Task<ImportRollbackSnapshot> LoadAsync(
        Guid tenantId, Guid batchId, IReadOnlyCollection<Guid>? rowIds, CancellationToken ct)
    {
        var rows = await _rows.ListAsync(new ImportRollbackRowsSpec(batchId, rowIds), ct);
        var facts = await _dependencies.ReadAsync(tenantId, ImportRollbackPlanner.Targets(rows), ct);
        return new ImportRollbackSnapshot(rows, ImportRollbackPlanner.Build(rows, facts));
    }

    /// <summary>Engelli kayıt listesi (kişisel veri yok: kimlik, satır, neden kodları); en çok <paramref name="max"/> öğe.</summary>
    public static IReadOnlyList<ImportRollbackBlockedItemDto> BlockedItems(ImportRollbackPlan plan, int max)
        => plan.Blocked
            .Take(max)
            .Select(i => new ImportRollbackBlockedItemDto(
                i.Entity, i.TargetId, ImportCanonicalNames.Key(i.Row.Table), i.Row.RowNumber, i.Reasons))
            .ToList();

    public static ImportRollbackCountsDto Counts(ImportRollbackPlan plan, ImportRollbackDecision decision)
        => new(
            plan.Count(ImportTable.Customers, decision),
            plan.Count(ImportTable.Pets, decision),
            plan.Count(ImportTable.Vaccinations, decision));
}
