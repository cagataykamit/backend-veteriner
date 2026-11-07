using System.Globalization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.RecordHistory;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Veteriner.Infrastructure.RecordHistory;

/// <summary>
/// Klinik kayıt değişiklik geçmişini (RECORD-002) kaydetme anında, aynı transaction'da yazar: hangi kaydın hangi alanı,
/// kim tarafından, neyden neye çevrildi. Kural tek yerde: izlenen türler ve dışlanan teknik alanlar burada.
/// Kullanıcısız (sistem/arka plan, ör. içe aktarma) değişiklikler kaydedilmez.
/// </summary>
public sealed class RecordHistorySaveChangesInterceptor : SaveChangesInterceptor
{
    /// <summary>Reçete satırları reçetenin tek bir alanı gibi izlenir.</summary>
    public const string PrescriptionItemsField = "items";

    private static readonly IReadOnlyDictionary<Type, string> TrackedTypes = new Dictionary<Type, string>
    {
        [typeof(Examination)] = RecordHistoryTypes.Examination,
        [typeof(Treatment)] = RecordHistoryTypes.Treatment,
        [typeof(Prescription)] = RecordHistoryTypes.Prescription,
        [typeof(Vaccination)] = RecordHistoryTypes.Vaccination,
        [typeof(LabResult)] = RecordHistoryTypes.LabResult,
        [typeof(Administration)] = RecordHistoryTypes.Administration,
        [typeof(Hospitalization)] = RecordHistoryTypes.Hospitalization,
    };

    /// <summary>Kimlik, kiracı, eşzamanlılık ve zaman damgası gibi klinik anlamı olmayan alanlar.</summary>
    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
    {
        "Id", "TenantId", "RowVersion", "CreatedAtUtc", "UpdatedAtUtc", "ChangedAtUtc", "CreatedByUserId", "RequestId", "ImportBatchId",
    };

    private readonly IClientContext _client;
    private readonly TimeProvider _timeProvider;

    public RecordHistorySaveChangesInterceptor(IClientContext client, TimeProvider timeProvider)
    {
        _client = client;
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null || _client.UserId is not { } actorUserId)
            return;

        context.ChangeTracker.DetectChanges();
        var entries = context.ChangeTracker.Entries().ToList();
        var itemChanges = PrescriptionItemChanges(entries);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var rows = new List<RecordChange>();

        foreach (var entry in entries)
        {
            if (!TrackedTypes.TryGetValue(entry.Entity.GetType(), out var recordType)
                || entry.Entity is not (ITenantOwned owned and IClinicScoped scoped))
            {
                continue;
            }

            var recordId = (Guid)entry.Property("Id").CurrentValue!;
            var kind = entry.State switch
            {
                EntityState.Added => RecordChangeKind.Created,
                EntityState.Deleted => RecordChangeKind.Deleted,
                _ => RecordChangeKind.Updated,
            };

            var changes = new List<RecordFieldChange>();
            if (kind == RecordChangeKind.Updated)
            {
                changes.AddRange(FieldChanges(entry));
                if (itemChanges.TryGetValue(recordId, out var items))
                    changes.Add(items);
                if (changes.Count == 0)
                    continue;
            }

            rows.Add(new RecordChange(owned.TenantId, scoped.ClinicId, recordType, recordId, kind, actorUserId, nowUtc, changes));
        }

        if (rows.Count > 0)
            context.Set<RecordChange>().AddRange(rows);
    }

    private static IEnumerable<RecordFieldChange> FieldChanges(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified)
            yield break;

        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsShadowProperty() || IgnoredProperties.Contains(property.Metadata.Name))
                continue;
            if (Equals(property.OriginalValue, property.CurrentValue))
                continue;

            yield return new RecordFieldChange(property.Metadata.Name, Format(property.OriginalValue), Format(property.CurrentValue));
        }
    }

    /// <summary>
    /// Satırı eklenen, silinen veya değişen reçeteler için satır listesinin önceki ve yeni metni. Satırlar silinip
    /// yeniden eklense de metin aynıysa değişiklik sayılmaz.
    /// </summary>
    private static Dictionary<Guid, RecordFieldChange> PrescriptionItemChanges(IReadOnlyList<EntityEntry> entries)
    {
        var result = new Dictionary<Guid, RecordFieldChange>();
        var groups = entries
            .Where(e => e.Entity is PrescriptionItem)
            .GroupBy(e => ((PrescriptionItem)e.Entity).PrescriptionId);

        foreach (var group in groups)
        {
            if (group.All(e => e.State == EntityState.Unchanged))
                continue;

            var before = ItemsText(group.Where(e => e.State != EntityState.Added), current: false);
            var after = ItemsText(group.Where(e => e.State != EntityState.Deleted), current: true);
            if (!string.Equals(before, after, StringComparison.Ordinal))
                result[group.Key] = new RecordFieldChange(PrescriptionItemsField, before, after);
        }

        return result;
    }

    private static string? ItemsText(IEnumerable<EntityEntry> items, bool current)
    {
        var lines = items
            .Select(e => current ? e.CurrentValues : e.OriginalValues)
            .OrderBy(v => v.GetValue<int>(nameof(PrescriptionItem.SortOrder)))
            .Select(ItemLine)
            .ToList();
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string ItemLine(PropertyValues v)
    {
        var parts = new List<string> { v.GetValue<string>(nameof(PrescriptionItem.Name)) };
        if (v.GetValue<decimal?>(nameof(PrescriptionItem.Quantity)) is { } quantity)
        {
            var unit = v.GetValue<string?>(nameof(PrescriptionItem.Unit));
            parts.Add($"× {Format(quantity)}{(unit is null ? string.Empty : " " + unit)}");
        }

        foreach (var name in new[] { nameof(PrescriptionItem.Dose), nameof(PrescriptionItem.Instructions), nameof(PrescriptionItem.LotNumber) })
        {
            if (v.GetValue<string?>(name) is { } text)
                parts.Add(text);
        }

        return string.Join(" · ", parts);
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        DateTime d => (d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime())
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        // Veritabanından gelen ölçek (14.000) ile yeni değer (14) aynı biçimde yazılsın.
        decimal m => m.ToString("G29", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
