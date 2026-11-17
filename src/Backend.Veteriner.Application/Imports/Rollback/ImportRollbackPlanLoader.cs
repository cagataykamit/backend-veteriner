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
        var facts = await _dependencies.ReadAsync(tenantId, ImportRollbackPlanner.Targets(batchId, rows), ct);
        return new ImportRollbackSnapshot(rows, ImportRollbackPlanner.Build(rows, facts));
    }

    /// <summary>
    /// Engelli kayıtlar grup grup (geri alma birimi: müşteri ya da ürün, birim sırasıyla); grup içinde müşteri, hasta,
    /// randevu, aşı, klinik kayıt, hesap hareketi, sonra satır numarası. Böylece aynı kökten gelen zincir bir arada kalır ve
    /// en çok <paramref name="max"/> öğe kesilirken grup ortadan bölünmez (son grup hariç).
    /// </summary>
    public static IReadOnlyList<ImportRollbackItem> OrderedBlocked(ImportRollbackPlan plan, int max)
    {
        var groups = GroupsOf(plan);
        return plan.Blocked
            .OrderBy(i => groups.TryGetValue(i.Row.RowId, out var g) ? g.Index : int.MaxValue)
            .ThenBy(i => EntityRank(i.Row.Table))
            .ThenBy(i => i.Row.RowNumber)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// Engelli kayıt listesi; en çok <paramref name="max"/> öğe (<see cref="OrderedBlocked"/> sırası).
    /// <paramref name="records"/> verilirse kayıtları tanıtan bilgiler (kişisel veri) eklenir; verilmezse yalnız kimlik, satır
    /// ve neden kodları döner (hata yanıtı bu biçimdedir).
    /// </summary>
    public static IReadOnlyList<ImportRollbackBlockedItemDto> BlockedItems(
        ImportRollbackPlan plan, int max, IReadOnlyDictionary<Guid, ImportRollbackRecordDto>? records = null)
        => BlockedItems(plan, OrderedBlocked(plan, max), records);

    /// <inheritdoc cref="BlockedItems(ImportRollbackPlan, int, IReadOnlyDictionary{Guid, ImportRollbackRecordDto}?)"/>
    public static IReadOnlyList<ImportRollbackBlockedItemDto> BlockedItems(
        ImportRollbackPlan plan,
        IReadOnlyList<ImportRollbackItem> ordered,
        IReadOnlyDictionary<Guid, ImportRollbackRecordDto>? records = null)
    {
        var groups = GroupsOf(plan);
        return ordered
            .Select(i => new ImportRollbackBlockedItemDto(
                i.Entity,
                i.TargetId,
                ImportCanonicalNames.Key(i.Row.Table),
                i.Row.RowNumber,
                i.Reasons,
                groups.TryGetValue(i.Row.RowId, out var g) ? g.Key : null,
                records?.GetValueOrDefault(i.TargetId)))
            .ToList();
    }

    private static Dictionary<Guid, (int Index, Guid Key)> GroupsOf(ImportRollbackPlan plan)
    {
        var groups = new Dictionary<Guid, (int Index, Guid Key)>();
        for (var index = 0; index < plan.Units.Count; index++)
        {
            foreach (var rowId in plan.Units[index].RowIds)
                groups[rowId] = (index, plan.Units[index].OwnerKey);
        }

        return groups;
    }

    private static int EntityRank(ImportTable table) => table switch
    {
        ImportTable.Customers => 0,
        ImportTable.Pets => 1,
        ImportTable.Appointments => 2,
        ImportTable.Vaccinations => 3,
        ImportTable.ClinicalRecords => 4,
        ImportTable.AccountEntries => 5,
        _ => 6,
    };

    public static ImportRollbackCountsDto Counts(ImportRollbackPlan plan, ImportRollbackDecision decision)
        => new(
            plan.Count(ImportTable.Customers, decision),
            plan.Count(ImportTable.Pets, decision),
            plan.Count(ImportTable.Vaccinations, decision),
            plan.Count(ImportTable.ClinicalRecords, decision),
            plan.Count(ImportTable.AccountEntries, decision),
            plan.Count(ImportTable.Appointments, decision),
            plan.Count(ImportTable.Products, decision),
            plan.CountOpeningBalances(decision));
}
