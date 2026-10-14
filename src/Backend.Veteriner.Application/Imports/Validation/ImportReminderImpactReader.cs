using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Reminders.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed record ImportedVaccinationDue(Guid PetId, DateTime? DueAtUtc);
public sealed record ImportPetOwner(Guid PetId, Guid ClientId);
public sealed record ImportClientEmail(Guid ClientId, string? Email);

/// <summary>
/// Hatırlatma etkisi (<c>reminderImpact</c>, IMPORT_API_CONTRACT.md §6.3). Uygulama başlamadan önce normalize satırlardan
/// (tahmin), başladıktan sonra partinin gerçek <c>ImportBatchId</c>'li aşılarından ve hastanın bugünkü sahibinin
/// e-postasından hesaplanır; <c>release-reminders</c> onayı bu ikinci sayıyla karşılaştırılır. Sayım kuralı
/// <see cref="ImportPreviewAggregator.ReminderImpactOf"/>'tadır.
/// </summary>
public sealed class ImportReminderImpactReader
{
    /// <summary>Kiracı ayarı kaydı yoksa varsayılan hatırlatma penceresi (gün).</summary>
    public const int DefaultReminderWindowDays = 3;

    private readonly IReadRepository<ImportBatchRow> _rows;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<TenantReminderSettings> _reminderSettings;

    public ImportReminderImpactReader(
        IReadRepository<ImportBatchRow> rows,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<TenantReminderSettings> reminderSettings)
    {
        _rows = rows;
        _vaccinations = vaccinations;
        _pets = pets;
        _clients = clients;
        _reminderSettings = reminderSettings;
    }

    /// <summary>Uygulama başladı mı (gerçek kayıtlardan hesaplanır).</summary>
    public static bool UsesImportedRecords(ImportBatch batch)
        => batch.Status is not (ImportBatchStatus.Validated or ImportBatchStatus.ValidationFailed);

    public async Task<ImportReminderImpactDto> ReadAsync(ImportBatch batch, DateTime nowUtc, CancellationToken ct)
    {
        var settings = await _reminderSettings.FirstOrDefaultAsync(new TenantReminderSettingsByTenantSpec(batch.TenantId), ct);
        var windowDays = settings?.VaccinationReminderDaysBefore ?? DefaultReminderWindowDays;
        var enabled = settings?.VaccinationRemindersEnabled ?? false;

        if (UsesImportedRecords(batch))
            return ImportPreviewAggregator.ReminderImpactOf(await ImportedCandidatesAsync(batch, ct), nowUtc, windowDays, enabled);

        var json = batch.VaccinationCounts.Total > 0 && batch.PayloadPurgedAtUtc is null
            ? await _rows.ListAsync(new ImportVaccinationNormalizedJsonSpec(batch.Id), ct)
            : [];
        return ImportPreviewAggregator.ReminderImpact(json, nowUtc, windowDays, enabled);
    }

    private async Task<IReadOnlyList<ImportReminderCandidate>> ImportedCandidatesAsync(ImportBatch batch, CancellationToken ct)
    {
        var dues = await _vaccinations.ListAsync(new ImportedVaccinationDueSpec(batch.TenantId, batch.Id), ct);
        if (dues.Count == 0)
            return [];

        var owners = await _pets.ListAsync(new ImportPetOwnersSpec(batch.TenantId, dues.Select(d => d.PetId).Distinct().ToArray()), ct);
        var clientByPet = owners.ToDictionary(o => o.PetId, o => o.ClientId);

        var emails = await _clients.ListAsync(new ImportClientEmailsSpec(batch.TenantId, owners.Select(o => o.ClientId).Distinct().ToArray()), ct);
        var withEmail = emails.Where(e => !string.IsNullOrWhiteSpace(e.Email)).Select(e => e.ClientId).ToHashSet();

        return dues
            .Select(d => clientByPet.TryGetValue(d.PetId, out var clientId)
                ? new ImportReminderCandidate(d.DueAtUtc, clientId.ToString(), withEmail.Contains(clientId))
                : new ImportReminderCandidate(d.DueAtUtc, null, false))
            .ToList();
    }
}
