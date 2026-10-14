using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;

namespace Backend.Veteriner.Application.Imports.Validation;

/// <summary>Hatırlatma etkisi adayı: sonraki aşı tarihi, sahip anahtarı ve sahibin e-postası var mı.</summary>
public sealed record ImportReminderCandidate(DateTime? DueAtUtc, string? OwnerKey, bool OwnerHasEmail);

/// <summary>
/// Önizleme toplulaştırması (IMPORT_API_CONTRACT.md §7.6): saklanan satır sonuçlarından (<c>IssuesJson</c>,
/// <c>NormalizedJson</c>) türetilir; saat bağımlı değerler (hatırlatma penceresi) çağrı anındaki saate göredir.
/// </summary>
public static class ImportPreviewAggregator
{
    private static readonly Dictionary<string, (ImportValueKind Kind, bool Blocking)> UnmappedCodes = new(StringComparer.Ordinal)
    {
        [ImportRules.SpeciesUnmapped.Code] = (ImportValueKind.Species, true),
        [ImportRules.VaccineUnmapped.Code] = (ImportValueKind.Vaccine, true),
        [ImportRules.BreedFreeText.Code] = (ImportValueKind.Breed, false),
        [ImportRules.ColorFreeText.Code] = (ImportValueKind.Color, false),
    };

    /// <summary>Eşlenemeyen değerler ile sorun kodu sayıları (her ikisi de satır başına bir kez sayar).</summary>
    public static (IReadOnlyList<ImportUnmappedValueDto> Unmapped, IReadOnlyList<ImportIssueCountDto> Counts) AggregateIssues(
        IEnumerable<string?> issuesJsonPerRow)
    {
        var unmapped = new Dictionary<(ImportValueKind Kind, string Key), (string Source, bool Blocking, int Rows)>();
        var counts = new Dictionary<string, (ImportIssueSeverity Severity, int Rows)>(StringComparer.Ordinal);

        foreach (var json in issuesJsonPerRow)
        {
            var issues = ImportIssueJson.Parse(json);
            foreach (var code in issues.Select(i => i.Code).Distinct())
            {
                var severity = issues.First(i => i.Code == code).Severity;
                counts[code] = counts.TryGetValue(code, out var c) ? (c.Severity, c.Rows + 1) : (severity, 1);
            }

            var seenInRow = new HashSet<(ImportValueKind, string)>();
            foreach (var issue in issues)
            {
                if (!UnmappedCodes.TryGetValue(issue.Code, out var info) || issue.Value is not { } value)
                    continue;

                var key = (info.Kind, ImportTextFolding.Fold(value));
                if (!seenInRow.Add(key))
                    continue;

                unmapped[key] = unmapped.TryGetValue(key, out var u)
                    ? (u.Source, u.Blocking, u.Rows + 1)
                    : (value, info.Blocking, 1);
            }
        }

        var unmappedDtos = unmapped
            .OrderBy(kv => kv.Key.Kind)
            .ThenByDescending(kv => kv.Value.Rows)
            .ThenBy(kv => kv.Value.Source, StringComparer.Ordinal)
            .Select(kv => new ImportUnmappedValueDto(ImportValueKinds.Key(kv.Key.Kind), kv.Value.Source, kv.Value.Rows, kv.Value.Blocking))
            .ToList();

        var countDtos = counts
            .OrderByDescending(kv => kv.Value.Rows)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new ImportIssueCountDto(kv.Key, kv.Value.Severity.ToString(), kv.Value.Rows))
            .ToList();

        return (unmappedDtos, countDtos);
    }

    /// <summary>
    /// Yalnız <c>Create</c> eylemli hatasız aşı satırları. Geçmişte kalan sonraki-aşı tarihi hatırlatma üretmez;
    /// pencere: <c>(şimdi, şimdi + windowDays]</c> (mevcut hatırlatma işçisinin adayları).
    /// </summary>
    public static ImportReminderImpactDto ReminderImpact(
        IEnumerable<string?> vaccinationNormalizedJson,
        DateTime nowUtc,
        int windowDays,
        bool tenantRemindersEnabled)
        => ReminderImpactOf(
            vaccinationNormalizedJson
                .Select(ImportNormalizedJson.ParseVaccination)
                .Where(row => row is { Action: ImportRowAction.Create })
                .Select(row => new ImportReminderCandidate(row!.DueAtUtc, row.OwnerKey, row.OwnerHasEmail)),
            nowUtc,
            windowDays,
            tenantRemindersEnabled);

    /// <summary>
    /// Sayım kuralının tek yeri: önizlemede normalize satırlardan, uygulama sonrası gerçek içe aktarılmış aşılardan
    /// (<see cref="ImportReminderImpactReader"/>) gelen adaylar aynı tanımla sayılır.
    /// </summary>
    public static ImportReminderImpactDto ReminderImpactOf(
        IEnumerable<ImportReminderCandidate> candidates,
        DateTime nowUtc,
        int windowDays,
        bool tenantRemindersEnabled)
    {
        var windowEnd = nowUtc.AddDays(windowDays);
        var withDue = 0;
        var future = 0;
        var past = 0;
        var inWindow = 0;
        var owners = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in candidates)
        {
            if (row.DueAtUtc is not { } due)
                continue;

            withDue++;
            if (due <= nowUtc)
            {
                past++;
                continue;
            }

            future++;
            if (!row.OwnerHasEmail)
                continue;

            if (row.OwnerKey is not null)
                owners.Add(row.OwnerKey);
            if (due <= windowEnd)
                inWindow++;
        }

        return new ImportReminderImpactDto(withDue, future, past, owners.Count, inWindow, windowDays, tenantRemindersEnabled, ExcludedByDefault: true);
    }

    /// <summary>Bloklayan eşlenmemiş değer kodları (tür/aşı) — yalnız satırı olanlar.</summary>
    public static IReadOnlyList<string> BlockingCodes(IReadOnlyList<ImportUnmappedValueDto> unmapped)
        => unmapped
            .Where(u => u is { Blocking: true, RowCount: > 0 })
            .Select(u => u.Kind == ImportValueKinds.Key(ImportValueKind.Species)
                ? ImportRules.SpeciesUnmapped.Code
                : ImportRules.VaccineUnmapped.Code)
            .Distinct()
            .ToList();
}
