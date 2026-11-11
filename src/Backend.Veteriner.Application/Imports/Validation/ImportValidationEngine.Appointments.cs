using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed partial class ImportValidationEngine
{
    private static readonly Dictionary<string, AppointmentType> AppointmentTypeValues = new(StringComparer.Ordinal)
    {
        ["muayene"] = AppointmentType.Examination,
        ["asi"] = AppointmentType.Vaccination,
        ["kontrol"] = AppointmentType.Checkup,
        ["ameliyat"] = AppointmentType.Surgery,
        ["operasyon"] = AppointmentType.Surgery,
        ["bakim"] = AppointmentType.Grooming,
        ["tiras"] = AppointmentType.Grooming,
        ["kuafor"] = AppointmentType.Grooming,
        ["danisma"] = AppointmentType.Consultation,
        ["konsultasyon"] = AppointmentType.Consultation,
        ["diger"] = AppointmentType.Other,
    };

    private readonly HashSet<(string PetKey, DateTime StartUtc)> _fileAppointments = new();
    private readonly HashSet<(Guid PetId, DateTime StartUtc)> _dbAppointments = new();
    private readonly Dictionary<string, List<Guid>> _veterinariansByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Guid>> _reasonsByName = new(StringComparer.Ordinal);

    private ImportRowResult ProcessAppointment(ImportRowReader reader)
    {
        var issues = new List<ImportIssue>();
        var (pet, petSkippedByUser) = ResolvePetLink(reader, issues);

        // Başlangıç: tarih + saat (Saat sütunu veya tarih hücresindeki saat), İstanbul yerel → UTC.
        DateTime? scheduledAtUtc = null;
        var rawDate = reader.Raw("Tarih");
        if (rawDate is null)
        {
            issues.Add(ImportRules.RequiredMissing.At("Tarih"));
        }
        else if (ReadDate(reader, "Tarih", issues).Value is { } date)
        {
            var rawTime = reader.Raw("Saat");
            var time = rawTime is not null ? ImportValueParsers.ParseTimeOfDay(rawTime) : ImportValueParsers.TimeOfDayIn(rawDate);
            if (time is { } t)
                scheduledAtUtc = OperationDayBounds.FromLocal(date.ToDateTime(t));
            else
                issues.Add(ImportRules.TimeInvalid.At(rawTime is not null ? "Saat" : "Tarih", value: rawTime ?? rawDate));
        }

        var rawDuration = reader.Raw("Sure");
        var (minutes, minutesValid) = ImportValueParsers.ParseMinutes(rawDuration);
        var duration = minutes ?? Appointment.DefaultDurationMinutes;
        if (!minutesValid || !Appointment.IsValidDurationMinutes(duration))
        {
            issues.Add(ImportRules.DurationInvalid.At("Sure", value: rawDuration));
            duration = Appointment.DefaultDurationMinutes;
        }

        var type = AppointmentType.Other;
        if (reader.Raw("RandevuTuru") is { } rawType)
        {
            if (AppointmentTypeValues.TryGetValue(ImportTextFolding.Fold(rawType), out var known))
                type = known;
            else
                issues.Add(ImportRules.AppointmentTypeDefaulted.At("RandevuTuru", value: rawType));
        }

        var notes = new List<string>();
        if (reader.Raw("Not") is { } note)
            notes.Add(note);

        Guid? veterinarianId = null;
        if (reader.Raw("Hekim") is { } rawVeterinarian)
        {
            veterinarianId = UniqueByName(_veterinariansByName, rawVeterinarian);
            if (veterinarianId is null)
            {
                issues.Add(ImportRules.VeterinarianUnmatched.At("Hekim", value: rawVeterinarian));
                notes.Add($"Hekim: {rawVeterinarian}");
            }
        }

        Guid? reasonId = null;
        if (reader.Raw("Neden") is { } rawReason)
        {
            reasonId = UniqueByName(_reasonsByName, rawReason);
            if (reasonId is null)
            {
                issues.Add(ImportRules.ReasonFreeText.At("Neden", value: rawReason));
                notes.Add($"Neden: {rawReason}");
            }
        }

        var notesText = notes.Count == 0 ? null : string.Join('\n', notes);
        if (notesText is { Length: > Appointment.MaxNotesLength })
            issues.Add(ImportRules.FieldTooLong.At("Not", $"Not (hekim/neden ekleriyle birlikte) en fazla {Appointment.MaxNotesLength} karakter olabilir."));

        var normalized = new ImportNormalizedAppointment(
            pet?.Key, pet?.Owner.Key, pet?.Owner.HasEmail ?? false, scheduledAtUtc, duration, type, veterinarianId, reasonId, notesText);

        if (HasError(issues))
            return Finish(ImportTable.Appointments, reader.RowNumber, issues, normalized);

        if (petSkippedByUser)
        {
            issues.Add(ImportRules.ParentSkipped.At());
            return Finish(ImportTable.Appointments, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        // Yalnız gelecek randevu (ADR-048 T5); geçmiş satır hata değildir, atlanır.
        if (scheduledAtUtc!.Value <= _input.NowUtc)
        {
            issues.Add(ImportRules.PastAppointmentSkipped.At("Tarih"));
            return Finish(ImportTable.Appointments, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        var existsInDb = pet!.PetId is { } petId && _dbAppointments.Contains((petId, scheduledAtUtc.Value));
        if (existsInDb || !_fileAppointments.Add((pet.Key, scheduledAtUtc.Value)))
        {
            issues.Add(ImportRules.DuplicateAppointment.At());
            return Finish(ImportTable.Appointments, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        return Finish(ImportTable.Appointments, reader.RowNumber, issues, normalized, ImportRowAction.Create);
    }

    /// <summary>Katlanmış ad dizininde tek eşleşme; yoksa veya belirsizse null.</summary>
    private static Guid? UniqueByName(Dictionary<string, List<Guid>> index, string name)
        => index.TryGetValue(ImportTextFolding.Fold(name), out var ids) && ids.Count == 1 ? ids[0] : null;

    private static void IndexByName(Dictionary<string, List<Guid>> index, IEnumerable<ImportCatalogEntry> entries)
    {
        foreach (var entry in entries)
        {
            var key = ImportTextFolding.Fold(entry.Name);
            if (key.Length == 0)
                continue;
            if (!index.TryGetValue(key, out var ids))
                index[key] = ids = new List<Guid>();
            if (!ids.Contains(entry.Id))
                ids.Add(entry.Id);
        }
    }
}
