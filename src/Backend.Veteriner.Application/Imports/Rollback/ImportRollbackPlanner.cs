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

/// <summary>
/// Geri alma birimi (tek transaction): bir müşteri ve ona bağlı partinin oluşturduğu hasta, aşı, klinik geçmiş, randevu ve
/// hesap hareketi satırları; ya da bir ürün satırı (ADR-048). Anahtar müşteri veya ürün kimliğidir.
/// </summary>
public sealed record ImportRollbackUnit(Guid OwnerKey, IReadOnlyList<Guid> RowIds);

/// <summary>
/// <see cref="OpeningBalances"/>: hedef müşterilere partinin açtığı devir ücreti ya da devir alacağı (müşteri → kayıt). Devir
/// satır değil müşterinin ekidir; müşteriyle aynı kararı alır (müşteri silinirse devir de silinir).
/// </summary>
public sealed record ImportRollbackPlan(IReadOnlyList<ImportRollbackItem> Items, IReadOnlyList<ImportRollbackUnit> Units)
{
    public IReadOnlyDictionary<Guid, ImportRollbackOpeningBalanceFact> OpeningBalances { get; init; } =
        new Dictionary<Guid, ImportRollbackOpeningBalanceFact>();

    public IEnumerable<ImportRollbackItem> Blocked => Items.Where(i => i.Decision == ImportRollbackDecision.Keep);

    public bool HasBlocked => Blocked.Any();

    public int Count(ImportTable table, ImportRollbackDecision decision)
        => Items.Count(i => i.Row.Table == table && i.Decision == decision);

    /// <summary>Kararı <paramref name="decision"/> olan müşterilerin devir ücreti ve devir alacağı sayısı.</summary>
    public int CountOpeningBalances(ImportRollbackDecision decision)
        => Items.Count(i => i.Row.Table == ImportTable.Customers && i.Decision == decision && OpeningBalances.ContainsKey(i.TargetId));

    public int Total(ImportRollbackDecision decision) => Items.Count(i => i.Decision == decision);
}

/// <summary>
/// Geri alma silinebilirlik kuralının tek yeri (IMPORT_API_CONTRACT.md §9, B5 tablosu; §12.9). Saf fonksiyondur: önizleme
/// (tüm parti), istek anındaki engel denetimi ve arka plan birim işlemi (birim, aynı transaction'da yeniden okunmuş
/// bağımlılıklarla) aynı kuralı çalıştırır. Yalnız <c>Created</c> (ve önceki kısmi geri almadan <c>Kept</c>) satırlar
/// değerlendirilir; eşleşen mevcut kayıt (<c>Matched</c>) hiçbir zaman hedef değildir. Partinin kendi kayıtları bağ sayılmaz,
/// diğer her bağ engeldir (ADR-048).
/// </summary>
public static class ImportRollbackPlanner
{
    public const string ClientEntity = "Client";
    public const string PetEntity = "Pet";
    public const string VaccinationEntity = "Vaccination";
    public const string ClinicalRecordEntity = "ClinicalRecord";
    public const string AccountEntryEntity = "AccountEntry";
    public const string AppointmentEntity = "Appointment";
    public const string ProductEntity = "Product";

    public static string EntityOf(ImportTable table) => table switch
    {
        ImportTable.Customers => ClientEntity,
        ImportTable.Pets => PetEntity,
        ImportTable.ClinicalRecords => ClinicalRecordEntity,
        ImportTable.AccountEntries => AccountEntryEntity,
        ImportTable.Appointments => AppointmentEntity,
        ImportTable.Products => ProductEntity,
        _ => VaccinationEntity,
    };

    /// <summary>Geri almanın hedef satırı mı (partinin oluşturduğu kaydı taşıyor ve henüz geri alınmamış).</summary>
    public static bool IsTarget(ImportRollbackRowInfo row)
        => row.TargetId is not null && row.ApplyStatus is ImportRowApplyStatus.Created or ImportRowApplyStatus.Kept;

    public static ImportRollbackTargets Targets(Guid batchId, IEnumerable<ImportRollbackRowInfo> rows)
    {
        var targets = rows.Where(IsTarget).ToList();
        Guid[] Ids(ImportTable table) => targets.Where(r => r.Table == table).Select(r => r.TargetId!.Value).Distinct().ToArray();
        return new ImportRollbackTargets(Ids(ImportTable.Customers), Ids(ImportTable.Pets), Ids(ImportTable.Vaccinations))
        {
            BatchId = batchId,
            ClinicalRecordIds = Ids(ImportTable.ClinicalRecords),
            AccountEntryIds = Ids(ImportTable.AccountEntries),
            AppointmentIds = Ids(ImportTable.Appointments),
            ProductIds = Ids(ImportTable.Products),
        };
    }

    public static ImportRollbackPlan Build(IEnumerable<ImportRollbackRowInfo> rows, ImportRollbackFacts facts)
    {
        var targets = rows.Where(IsTarget).OrderBy(r => r.Table).ThenBy(r => r.RowNumber).ToList();
        var items = new List<ImportRollbackItem>(targets.Count);
        List<ImportRollbackRowInfo> TargetsOf(ImportTable table) => targets.Where(r => r.Table == table).ToList();

        // Aşı: yalnız kendi bağları.
        var vaccinationRows = TargetsOf(ImportTable.Vaccinations);
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

        // Klinik geçmiş ve hesap hareketi: salt okunur, başka kayıt bağlanamaz; varsa silinir.
        var clinicalRecordRows = TargetsOf(ImportTable.ClinicalRecords);
        var batchClinicalRecords = clinicalRecordRows.Select(r => r.TargetId!.Value).ToHashSet();
        foreach (var row in clinicalRecordRows)
            items.Add(facts.ClinicalRecords.ContainsKey(row.TargetId!.Value) ? Decide(row, Array.Empty<string>()) : Gone(row));

        // Randevu: hâlâ planlı, değişmemiş, geliş/muayene bağlı değil.
        foreach (var row in TargetsOf(ImportTable.Appointments))
        {
            if (!facts.Appointments.TryGetValue(row.TargetId!.Value, out var a))
            {
                items.Add(Gone(row));
                continue;
            }

            items.Add(Decide(row,
                (a.Changed, ImportRollbackReasons.AppointmentChanged),
                (a.HasVisits, ImportRollbackReasons.HasVisits),
                (a.HasExaminations, ImportRollbackReasons.HasExaminations)));
        }

        var keptVaccinations = KeptIds(items, ImportTable.Vaccinations);
        var petsWithKeptAppointments = items
            .Where(i => i.Row.Table == ImportTable.Appointments && i.Decision == ImportRollbackDecision.Keep)
            .Select(i => facts.Appointments[i.TargetId].PetId)
            .ToHashSet();

        // Hasta: kendi bağları + kalan bağlar partinin silinebilir aşıları, klinik geçmiş kayıtları ve randevuları olmalı.
        var petRows = TargetsOf(ImportTable.Pets);
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
            if ((facts.PetClinicalRecords.GetValueOrDefault(petId) ?? []).Any(id => !batchClinicalRecords.Contains(id)))
                reasons.Add(ImportRollbackReasons.HasOtherClinicalRecords);
            if (petsWithKeptAppointments.Contains(petId))
                reasons.Add(ImportRollbackReasons.HasKeptAppointments);

            items.Add(Decide(row, reasons));
        }

        var keptPets = KeptIds(items, ImportTable.Pets);

        var accountEntryRows = TargetsOf(ImportTable.AccountEntries);
        var batchAccountEntries = accountEntryRows.Select(r => r.TargetId!.Value).ToHashSet();
        foreach (var row in accountEntryRows)
            items.Add(facts.AccountEntries.ContainsKey(row.TargetId!.Value) ? Decide(row, Array.Empty<string>()) : Gone(row));

        // Müşteri: değiştirilmemiş, kendi bağı yok, tüm hastaları partinin silinebilir hastaları; devir ücreti dokunulmamış.
        foreach (var row in TargetsOf(ImportTable.Customers))
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
            if ((facts.ClientAccountEntries.GetValueOrDefault(clientId) ?? []).Any(id => !batchAccountEntries.Contains(id)))
                reasons.Add(ImportRollbackReasons.HasOtherAccountEntries);
            if (facts.OpeningBalances.TryGetValue(clientId, out var balance) && balance.Changed)
                reasons.Add(ImportRollbackReasons.OpeningBalanceChanged);

            items.Add(Decide(row, reasons));
        }

        // Ürün: kendi birimi; bağ nedenleri okumadan gelir.
        foreach (var row in TargetsOf(ImportTable.Products))
        {
            items.Add(facts.Products.TryGetValue(row.TargetId!.Value, out var productBlockers)
                ? Decide(row, productBlockers.OrderBy(r => r, StringComparer.Ordinal))
                : Gone(row));
        }

        return new ImportRollbackPlan(items, BuildUnits(items, facts))
        {
            OpeningBalances = facts.OpeningBalances,
        };
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
    /// Birim anahtarı hedefin sahibidir (müşteri; hasta için sahibi; aşı, klinik geçmiş ve randevu için hastasının sahibi;
    /// hesap hareketi için müşterisi). Ürün kendi birimidir. Sahibi bilinmeyen (zaten silinmiş) kayıt kendi biriminde kalır.
    /// Sıra: birimin ilk satırı (tablo, satır numarası); ürün birimleri bu yüzden sonda.
    /// </summary>
    private static List<ImportRollbackUnit> BuildUnits(IReadOnlyList<ImportRollbackItem> items, ImportRollbackFacts facts)
    {
        Guid PetOwner(Guid? petId, Guid fallback)
            => petId is { } id && facts.PetOwners.TryGetValue(id, out var owner) ? owner : fallback;

        Guid OwnerOf(ImportRollbackItem item)
        {
            var id = item.TargetId;
            return item.Row.Table switch
            {
                ImportTable.Customers or ImportTable.Products => id,
                ImportTable.Pets => PetOwner(id, id),
                ImportTable.Vaccinations => PetOwner(facts.Vaccinations.TryGetValue(id, out var v) ? v.PetId : null, id),
                ImportTable.ClinicalRecords => PetOwner(facts.ClinicalRecords.TryGetValue(id, out var petId) ? petId : null, id),
                ImportTable.Appointments => PetOwner(facts.Appointments.TryGetValue(id, out var a) ? a.PetId : null, id),
                ImportTable.AccountEntries => facts.AccountEntries.TryGetValue(id, out var clientId) ? clientId : id,
                _ => id,
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
