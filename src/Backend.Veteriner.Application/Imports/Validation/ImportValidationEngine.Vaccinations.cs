using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed partial class ImportValidationEngine
{
    private const int MaxVaccinationNotesLength = 4000;

    private readonly HashSet<(string PetKey, Guid DefinitionId, DateOnly Date)> _fileVaccinations = new();

    /// <summary>HayvanNo bağlantısı yalnız hasta tablosu varsa ve orada HayvanNo eşlenmişse geçerlidir.</summary>
    private bool PetReferenceUsable
        => _input.Tables.TryGetValue(ImportTable.Pets, out var rows) && rows.Count > 0
           && _input.Mapping.ColumnsOf(ImportTable.Pets).ContainsKey("HayvanNo");

    private ImportRowResult ProcessVaccination(ImportRowReader reader)
    {
        var issues = new List<ImportIssue>();

        var (pet, petSkippedByUser) = ResolvePetForVaccination(reader, issues);

        // Aşı tanımı (hastanın türüne göre)
        Guid? definitionId = null;
        string? vaccineName = null;
        var vaccineSkipped = false;
        var rawVaccine = reader.Raw("AsiAdi");
        var vaccine = _resolver.Resolve(ImportValueKind.Vaccine, rawVaccine, pet?.SpeciesId);
        switch (vaccine.Kind)
        {
            case ImportValueResolutionKind.Resolved:
                var entry = _vaccinesById[vaccine.TargetId!.Value];
                if (pet is not null && entry.ParentId is { } vaccineSpecies && vaccineSpecies != pet.SpeciesId)
                {
                    issues.Add(ImportRules.VaccineInvalidSpecies.At("AsiAdi", value: rawVaccine));
                    break;
                }

                definitionId = entry.Id;
                vaccineName = entry.Name;
                break;
            case ImportValueResolutionKind.Skipped:
                vaccineSkipped = true;
                break;
            case ImportValueResolutionKind.Unmapped:
                issues.Add(ImportRules.VaccineUnmapped.At("AsiAdi", value: rawVaccine));
                break;
            default:
                issues.Add(ImportRules.RequiredMissing.At("AsiAdi"));
                break;
        }

        // Tarihler
        var rawApplied = reader.Raw("UygulamaTarihi");
        DateOnly? appliedAt = null;
        if (rawApplied is null)
        {
            issues.Add(ImportRules.RequiredMissing.At("UygulamaTarihi"));
        }
        else
        {
            appliedAt = ReadDate(reader, "UygulamaTarihi", issues).Value;
            if (appliedAt is { } applied)
            {
                if (applied > _today)
                {
                    issues.Add(ImportRules.AppliedAtInFuture.At("UygulamaTarihi", value: rawApplied));
                    appliedAt = null;
                }
                else if (applied < ImportValueParsers.MinVaccinationDate)
                {
                    issues.Add(ImportRules.DateOutOfRange.At("UygulamaTarihi", value: rawApplied));
                    appliedAt = null;
                }
                else if (pet?.BirthDate is { } birth && applied < birth)
                {
                    issues.Add(ImportRules.AppliedBeforeBirth.At("UygulamaTarihi", value: rawApplied));
                }
            }
        }

        var rawDue = reader.Raw("SonrakiAsiTarihi");
        var (dueAt, _) = ReadDate(reader, "SonrakiAsiTarihi", issues);
        DateTime? appliedAtUtc = appliedAt is { } a ? OperationDayBounds.NoonUtcForLocalDate(a) : null;
        DateTime? dueAtUtc = null;
        if (dueAt is { } due)
        {
            dueAtUtc = OperationDayBounds.NoonUtcForLocalDate(due);
            if (due > _today.AddYears(VaccinationDueAtWindow.MaxFutureYears))
            {
                issues.Add(ImportRules.DueTooFarInFuture.At("SonrakiAsiTarihi", value: rawDue));
                dueAtUtc = null;
            }
            else if (appliedAtUtc is not null)
            {
                // Durum/tarih tutarlılığı mevcut kuraldan (Applied dalı); bugün 12:00 "gelecek" sayılmasın diye +1 gün.
                var consistency = VaccinationStatusDateRules.Validate(
                    VaccinationStatus.Applied, appliedAtUtc, dueAtUtc, _input.NowUtc.AddDays(1));
                if (!consistency.IsSuccess)
                {
                    issues.Add(ImportRules.DueNotAfterApplied.At("SonrakiAsiTarihi", value: rawDue));
                    dueAtUtc = null;
                }
            }
        }

        // Not: Seri / Uygulayan / Not birleşimi (aşı varlığında ayrı alan yok)
        var notes = BuildNotes(reader.Raw("Seri"), reader.Raw("Uygulayan"), reader.Raw("Not"));
        if (notes is { Length: > MaxVaccinationNotesLength })
            issues.Add(ImportRules.FieldTooLong.At("Not", $"Not (seri/uygulayan ile birlikte) en fazla {MaxVaccinationNotesLength} karakter olabilir."));

        var normalized = new ImportNormalizedVaccination(
            pet?.Key,
            pet?.Owner.Key,
            pet?.Owner.HasEmail ?? false,
            definitionId,
            vaccineName,
            appliedAt,
            dueAt,
            appliedAtUtc,
            dueAtUtc,
            notes);

        if (HasError(issues))
            return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized);

        // Bilinçli atlama: hasta veya aşı değeri kullanıcı tarafından atlandı.
        if (petSkippedByUser)
        {
            issues.Add(ImportRules.ParentSkipped.At());
            return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        if (vaccineSkipped)
        {
            issues.Add(ImportRules.ValueSkipped.At("AsiAdi", value: rawVaccine));
            return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        // Mükerrer aşı: aynı hasta + aynı tanım + aynı uygulama günü (dosyada veya kiracıda).
        var key = (pet!.Key, definitionId!.Value, appliedAt!.Value);
        var existsInDb = pet.PetId is { } petId && _dbVaccinations.Contains((petId, key.Item2, key.Item3));
        if (existsInDb || !_fileVaccinations.Add(key))
        {
            issues.Add(ImportRules.DuplicateVaccination.At());
            return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized, ImportRowAction.Skip);
        }

        return Finish(ImportTable.Vaccinations, reader.RowNumber, issues, normalized, ImportRowAction.Create);
    }

    /// <summary>
    /// Aşı satırının hayvanını çözer: <c>HayvanNo</c> doluysa dosyadaki hasta; yoksa sahip + hayvan adı + tür ile
    /// dosyadaki sonra sahibin kiracıdaki hastaları.
    /// </summary>
    private (PetRef? Pet, bool SkippedByUser) ResolvePetForVaccination(ImportRowReader reader, List<ImportIssue> issues)
    {
        var reference = ImportValueParsers.NormalizeText(reader.Raw("HayvanNo"));
        if (reference is not null && PetReferenceUsable)
        {
            if (!_petByRef.TryGetValue(reference, out var node))
            {
                issues.Add(ImportRules.PetNotFound.At("HayvanNo", $"'{reference}' numaralı hasta dosyada bulunamadı.", reference));
                return (null, false);
            }

            if (node.SkippedByUser)
                return (null, true);

            if (node.Ref is null)
            {
                issues.Add(ImportRules.PetRowInvalid.At("HayvanNo", $"Hasta satırı ({node.Row}) hatalı.", reference));
                return (null, false);
            }

            return (node.Ref, false);
        }

        var owner = FindOwnerByNameAndPhone(reader, issues, ImportRules.PetNotFound);
        if (owner is null)
            return (null, false);

        var petName = ImportValueParsers.NormalizeText(reader.Raw("HayvanAdi"));
        if (petName is null)
        {
            issues.Add(ImportRules.PetNotFound.At("HayvanAdi", "Hasta adı boş; hasta bulunamadı."));
            return (null, false);
        }

        var rawSpecies = reader.Raw("Tur");
        var species = _resolver.Resolve(ImportValueKind.Species, rawSpecies);
        if (species.Kind == ImportValueResolutionKind.Skipped)
            return (null, true);

        if (species.Kind != ImportValueResolutionKind.Resolved)
        {
            issues.Add(species.Kind == ImportValueResolutionKind.Unmapped
                ? ImportRules.SpeciesUnmapped.At("Tur", value: rawSpecies)
                : ImportRules.PetNotFound.At("Tur", "Hayvan türü boş; hasta bulunamadı."));
            return (null, false);
        }

        var speciesId = species.TargetId!.Value;
        var nameKey = PetNameKey(petName);
        if (_petByIdentity.TryGetValue((owner.Key, nameKey, speciesId), out var filePet))
            return (filePet.Ref, false);

        if (owner.ClientId is { } clientId && _dbPetByIdentity.TryGetValue((clientId, nameKey, speciesId), out var dbPet))
            return (new PetRef($"db:{dbPet.Id}", dbPet.Id, dbPet.SpeciesId, dbPet.BirthDate, owner), false);

        issues.Add(ImportRules.PetNotFound.At("HayvanAdi", "Hasta dosyada veya sahibinin kayıtlı hastaları arasında bulunamadı.", petName));
        return (null, false);
    }

    /// <summary><c>Seri: …; Uygulayan: …; &lt;Not&gt;</c> (dolu olanlar); hepsi boşsa null.</summary>
    private static string? BuildNotes(string? serial, string? administeredBy, string? note)
    {
        var parts = new List<string>();
        if (serial is not null)
            parts.Add($"Seri: {serial}");
        if (administeredBy is not null)
            parts.Add($"Uygulayan: {administeredBy}");
        if (note is not null)
            parts.Add(note);

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}
