using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Rollback;

/// <summary>Geri alma girdisi: partinin bir satırı ve hedef kaydı (izlenmeyen tek sorgu).</summary>
public sealed record ImportRollbackRowInfo(
    Guid RowId,
    ImportTable Table,
    int RowNumber,
    ImportRowApplyStatus ApplyStatus,
    Guid? TargetId);

public enum ImportRollbackDecision
{
    /// <summary>Kayıt silinir.</summary>
    Delete = 1,

    /// <summary>Bağı olduğu için kalır (<c>Kept</c>).</summary>
    Keep = 2,

    /// <summary>Kayıt zaten yok (önceki denemede silinmiş); satır <c>RolledBack</c> sayılır.</summary>
    Gone = 3,
}

/// <summary>Bir hedef kaydın kararı ve (kalıyorsa) neden kodları.</summary>
public sealed record ImportRollbackItem(
    ImportRollbackRowInfo Row,
    ImportRollbackDecision Decision,
    IReadOnlyList<string> Reasons)
{
    public Guid TargetId => Row.TargetId!.Value;

    public string Entity => ImportRollbackPlanner.EntityOf(Row.Table);
}

/// <summary>Sahip birimi: bir müşteri ve ona bağlı partinin oluşturduğu hasta/aşı satırları (tek transaction).</summary>
public sealed record ImportRollbackUnit(Guid OwnerKey, IReadOnlyList<Guid> RowIds);

public sealed record ImportRollbackPlan(IReadOnlyList<ImportRollbackItem> Items, IReadOnlyList<ImportRollbackUnit> Units)
{
    public IEnumerable<ImportRollbackItem> Blocked => Items.Where(i => i.Decision == ImportRollbackDecision.Keep);

    public bool HasBlocked => Blocked.Any();

    public int Count(ImportTable table, ImportRollbackDecision decision)
        => Items.Count(i => i.Row.Table == table && i.Decision == decision);

    public int Total(ImportRollbackDecision decision) => Items.Count(i => i.Decision == decision);
}

/// <summary>
/// Geri alma silinebilirlik kuralının tek yeri (IMPORT_API_CONTRACT.md §9, B5 tablosu). Saf fonksiyondur: önizleme
/// (tüm parti), istek anındaki engel denetimi ve arka plan birim işlemi (birim, aynı transaction'da yeniden okunmuş
/// bağımlılıklarla) aynı kuralı çalıştırır. Yalnız <c>Created</c> (ve önceki kısmi geri almadan <c>Kept</c>) satırlar
/// değerlendirilir; eşleşen mevcut kayıt (<c>Matched</c>) hiçbir zaman hedef değildir.
/// </summary>
public static class ImportRollbackPlanner
{
    public const string ClientEntity = "Client";
    public const string PetEntity = "Pet";
    public const string VaccinationEntity = "Vaccination";

    public static string EntityOf(ImportTable table) => table switch
    {
        ImportTable.Customers => ClientEntity,
        ImportTable.Pets => PetEntity,
        _ => VaccinationEntity,
    };

    /// <summary>Geri almanın hedef satırı mı (partinin oluşturduğu kaydı taşıyor ve henüz geri alınmamış).</summary>
    public static bool IsTarget(ImportRollbackRowInfo row)
        => row.TargetId is not null && row.ApplyStatus is ImportRowApplyStatus.Created or ImportRowApplyStatus.Kept;

    public static ImportRollbackTargets Targets(IEnumerable<ImportRollbackRowInfo> rows)
    {
        var targets = rows.Where(IsTarget).ToList();
        Guid[] Ids(ImportTable table) => targets.Where(r => r.Table == table).Select(r => r.TargetId!.Value).Distinct().ToArray();
        return new ImportRollbackTargets(Ids(ImportTable.Customers), Ids(ImportTable.Pets), Ids(ImportTable.Vaccinations));
    }

    public static ImportRollbackPlan Build(IEnumerable<ImportRollbackRowInfo> rows, ImportRollbackFacts facts)
    {
        var targets = rows.Where(IsTarget).OrderBy(r => r.Table).ThenBy(r => r.RowNumber).ToList();
        var items = new List<ImportRollbackItem>(targets.Count);

        // Aşı: yalnız kendi bağları.
        var vaccinationRows = targets.Where(r => r.Table == ImportTable.Vaccinations).ToList();
        var batchVaccinations = vaccinationRows.Select(r => r.TargetId!.Value).ToHashSet();
        foreach (var row in vaccinationRows)
        {
            if (!facts.Vaccinations.TryGetValue(row.TargetId!.Value, out var v))
            {
                items.Add(Gone(row));
                continue;
            }

            items.Add(Decide(row,
                (v.LinkedToExamination, ImportRollbackReasons.LinkedToExamination),
                (v.HasAdministration, ImportRollbackReasons.HasAdministration),
                (v.Modified, ImportRollbackReasons.VaccinationModified)));
        }

        var keptVaccinations = KeptIds(items, ImportTable.Vaccinations);

        // Hasta: kendi bağları + kalan tek bağ partinin silinebilir aşıları olmalı.
        var petRows = targets.Where(r => r.Table == ImportTable.Pets).ToList();
        var batchPets = petRows.Select(r => r.TargetId!.Value).ToHashSet();
        foreach (var row in petRows)
        {
            var petId = row.TargetId!.Value;
            if (!facts.PetOwners.ContainsKey(petId))
            {
                items.Add(Gone(row));
                continue;
            }

            var reasons = new SortedSet<string>(facts.PetBlockers.GetValueOrDefault(petId) ?? new HashSet<string>(), StringComparer.Ordinal);
            var vaccinations = facts.PetVaccinations.GetValueOrDefault(petId) ?? [];
            if (vaccinations.Any(id => !batchVaccinations.Contains(id)))
                reasons.Add(ImportRollbackReasons.HasOtherVaccinations);
            if (vaccinations.Any(keptVaccinations.Contains))
                reasons.Add(ImportRollbackReasons.HasKeptVaccinations);

            items.Add(Decide(row, reasons));
        }

        var keptPets = KeptIds(items, ImportTable.Pets);

        // Müşteri: değiştirilmemiş, kendi bağı yok ve tüm hastaları partinin silinebilir hastaları.
        foreach (var row in targets.Where(r => r.Table == ImportTable.Customers))
        {
            var clientId = row.TargetId!.Value;
            if (!facts.ClientModified.TryGetValue(clientId, out var modified))
            {
                items.Add(Gone(row));
                continue;
            }

            var reasons = new SortedSet<string>(facts.ClientBlockers.GetValueOrDefault(clientId) ?? new HashSet<string>(), StringComparer.Ordinal);
            if (modified)
                reasons.Add(ImportRollbackReasons.ClientModified);
            var pets = facts.ClientPets.GetValueOrDefault(clientId) ?? [];
            if (pets.Any(id => !batchPets.Contains(id)))
                reasons.Add(ImportRollbackReasons.HasOtherPets);
            if (pets.Any(keptPets.Contains))
                reasons.Add(ImportRollbackReasons.HasKeptPets);

            items.Add(Decide(row, reasons));
        }

        return new ImportRollbackPlan(items, BuildUnits(items, facts));
    }

    /// <summary>
    /// <c>Fail</c> politikasında birimde engel varsa birimin hiçbir kaydı silinmez: silinecek kayıtlar
    /// <see cref="ImportRollbackReasons.OwnerUnitBlocked"/> ile kalır (zaten olmayanlar değişmez).
    /// </summary>
    public static ImportRollbackPlan KeepWholeUnitIfBlocked(ImportRollbackPlan plan)
    {
        if (!plan.HasBlocked)
            return plan;

        var items = plan.Items
            .Select(i => i.Decision == ImportRollbackDecision.Delete
                ? i with { Decision = ImportRollbackDecision.Keep, Reasons = [ImportRollbackReasons.OwnerUnitBlocked] }
                : i)
            .ToList();
        return plan with { Items = items };
    }

    /// <summary>
    /// Birim anahtarı hedefin sahibidir (müşteri; hasta için sahibi, aşı için hastasının sahibi). Sahibi bilinmeyen
    /// (zaten silinmiş) kayıt kendi biriminde kalır. Sıra: birimin ilk satırı (tablo, satır numarası).
    /// </summary>
    private static List<ImportRollbackUnit> BuildUnits(IReadOnlyList<ImportRollbackItem> items, ImportRollbackFacts facts)
    {
        Guid OwnerOf(ImportRollbackItem item)
        {
            var id = item.TargetId;
            return item.Row.Table switch
            {
                ImportTable.Customers => id,
                ImportTable.Pets => facts.PetOwners.TryGetValue(id, out var owner) ? owner : id,
                _ => facts.Vaccinations.TryGetValue(id, out var v) && facts.PetOwners.TryGetValue(v.PetId, out var petOwner)
                    ? petOwner
                    : id,
            };
        }

        return items
            .GroupBy(OwnerOf)
            .Select(g => (First: g.Min(i => ((int)i.Row.Table, i.Row.RowNumber)), Unit: new ImportRollbackUnit(g.Key, g.Select(i => i.Row.RowId).ToList())))
            .OrderBy(x => x.First)
            .Select(x => x.Unit)
            .ToList();
    }

    private static HashSet<Guid> KeptIds(IEnumerable<ImportRollbackItem> items, ImportTable table)
        => items.Where(i => i.Row.Table == table && i.Decision == ImportRollbackDecision.Keep).Select(i => i.TargetId).ToHashSet();

    private static ImportRollbackItem Gone(ImportRollbackRowInfo row) => new(row, ImportRollbackDecision.Gone, []);

    private static ImportRollbackItem Decide(ImportRollbackRowInfo row, params (bool Applies, string Reason)[] checks)
        => Decide(row, checks.Where(c => c.Applies).Select(c => c.Reason));

    private static ImportRollbackItem Decide(ImportRollbackRowInfo row, IEnumerable<string> reasons)
    {
        var list = reasons.Distinct(StringComparer.Ordinal).ToList();
        return new ImportRollbackItem(row, list.Count == 0 ? ImportRollbackDecision.Delete : ImportRollbackDecision.Keep, list);
    }
}
