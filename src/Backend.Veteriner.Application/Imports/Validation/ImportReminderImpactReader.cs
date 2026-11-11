using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Reminders.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed record ImportedVaccinationDue(Guid PetId, DateTime? DueAtUtc);
public sealed record ImportedAppointmentStart(Guid PetId, DateTime ScheduledAtUtc);
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
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<TenantReminderSettings> _reminderSettings;

    public ImportReminderImpactReader(
        IReadRepository<ImportBatchRow> rows,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Appointment> appointments,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<TenantReminderSettings> reminderSettings)
    {
        _rows = rows;
        _vaccinations = vaccinations;
        _appointments = appointments;
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
        var appointmentWindowHours = settings?.AppointmentReminderHoursBefore ?? ImportReminderImpactDto.DefaultAppointmentWindowHours;
        var appointmentsEnabled = settings?.AppointmentRemindersEnabled ?? false;

        ImportReminderImpactDto impact;
        IEnumerable<ImportAppointmentReminderCandidate> appointments;
        if (UsesImportedRecords(batch))
        {
            impact = ImportPreviewAggregator.ReminderImpactOf(await ImportedCandidatesAsync(batch, ct), nowUtc, windowDays, enabled);
            appointments = await ImportedAppointmentCandidatesAsync(batch, ct);
        }
        else
        {
            var json = batch.VaccinationCounts.Total > 0 && batch.PayloadPurgedAtUtc is null
                ? await _rows.ListAsync(new ImportVaccinationNormalizedJsonSpec(batch.Id), ct)
                : [];
            impact = ImportPreviewAggregator.ReminderImpact(json, nowUtc, windowDays, enabled);

            var appointmentJson = batch.AppointmentCounts.Total > 0 && batch.PayloadPurgedAtUtc is null
                ? await _rows.ListAsync(new ImportVaccinationNormalizedJsonSpec(batch.Id, ImportTable.Appointments), ct)
                : [];
            appointments = ImportPreviewAggregator.AppointmentCandidates(appointmentJson);
        }

        return ImportPreviewAggregator.WithAppointments(impact, appointments, nowUtc, appointmentWindowHours, appointmentsEnabled);
    }

    /// <summary>Partinin içe aktardığı planlı randevular ve hastanın bugünkü sahibinin e-postası.</summary>
    private async Task<IReadOnlyList<ImportAppointmentReminderCandidate>> ImportedAppointmentCandidatesAsync(ImportBatch batch, CancellationToken ct)
    {
        if (batch.AppointmentCounts.Total == 0)
            return [];

        var starts = await _appointments.ListAsync(new ImportedAppointmentStartSpec(batch.TenantId, batch.Id), ct);
        if (starts.Count == 0)
            return [];

        var owners = await _pets.ListAsync(new ImportPetOwnersSpec(batch.TenantId, starts.Select(s => s.PetId).Distinct().ToArray()), ct);
        var clientByPet = owners.ToDictionary(o => o.PetId, o => o.ClientId);
        var emails = await _clients.ListAsync(new ImportClientEmailsSpec(batch.TenantId, owners.Select(o => o.ClientId).Distinct().ToArray()), ct);
        var withEmail = emails.Where(e => !string.IsNullOrWhiteSpace(e.Email)).Select(e => e.ClientId).ToHashSet();

        return starts
            .Select(s => new ImportAppointmentReminderCandidate(
                s.ScheduledAtUtc, clientByPet.TryGetValue(s.PetId, out var clientId) && withEmail.Contains(clientId)))
            .ToList();
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
