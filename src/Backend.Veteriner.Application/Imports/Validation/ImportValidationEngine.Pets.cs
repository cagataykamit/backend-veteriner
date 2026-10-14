using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;

namespace Backend.Veteriner.Application.Imports.Validation;

public sealed partial class ImportValidationEngine
{
    private const int MaxPetNameLength = 200;
    private const int MaxBreedTextLength = 150;
    private const int MaxPassportLength = 50;
    private const int MaxPetNotesLength = 2000;
    private const int MaxBirthDateAgeYears = 40;

    /// <summary>Hayvan bağlantı noktası: dosyada yeni/eşleşen (<c>file:satır</c>) veya mevcut kayıt (<c>db:id</c>).</summary>
    private sealed record PetRef(string Key, Guid? PetId, Guid SpeciesId, DateOnly? BirthDate, OwnerRef Owner);

    private sealed class PetNode
    {
        public PetNode(int row) => Row = row;

        public int Row { get; }
        public ImportRowValidationStatus Status { get; set; }

        /// <summary>Aşıların bağlanacağı hayvan; hatalı veya bilinçli atlanan satırda null.</summary>
        public PetRef? Ref { get; set; }

        /// <summary>Tür bilinçli atlandı (<c>targetId = null</c>); bağlı aşılar da atlanır.</summary>
        public bool SkippedByUser { get; set; }
    }

    private readonly Dictionary<string, PetNode> _petByRef = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string OwnerKey, string Name, Guid SpeciesId), PetNode> _petByIdentity = new();
    private readonly Dictionary<string, (string PetKey, int Row)> _fileChips = new(StringComparer.Ordinal);

    /// <summary>MusteriNo bağlantısı yalnız müşteri tablosu varsa ve orada MusteriNo eşlenmişse geçerlidir.</summary>
    private bool CustomerReferenceUsable
        => _input.Tables.TryGetValue(ImportTable.Customers, out var rows) && rows.Count > 0
           && _input.Mapping.ColumnsOf(ImportTable.Customers).ContainsKey("MusteriNo");

    private ImportRowResult ProcessPet(ImportRowReader reader)
    {
        var issues = new List<ImportIssue>();
        var node = new PetNode(reader.RowNumber);

        var reference = ImportValueParsers.NormalizeText(reader.Raw("HayvanNo"));
        var name = ImportValueParsers.NormalizeText(reader.Raw("HayvanAdi"));
        if (name is null)
            issues.Add(ImportRules.RequiredMissing.At("HayvanAdi"));
        else if (name.Length > MaxPetNameLength)
            issues.Add(ImportRules.FieldTooLong.At("HayvanAdi", $"Hayvan adı en fazla {MaxPetNameLength} karakter olabilir."));

        var owner = ResolveOwnerForPet(reader, issues);

        // Tür
        Guid? speciesId = null;
        var speciesSkipped = false;
        var rawSpecies = reader.Raw("Tur");
        var species = _resolver.Resolve(ImportValueKind.Species, rawSpecies);
        switch (species.Kind)
        {
            case ImportValueResolutionKind.Resolved:
                speciesId = species.TargetId;
                break;
            case ImportValueResolutionKind.Skipped:
                speciesSkipped = true;
                break;
            case ImportValueResolutionKind.Unmapped:
                issues.Add(ImportRules.SpeciesUnmapped.At("Tur", value: rawSpecies));
                break;
            default:
                issues.Add(ImportRules.RequiredMissing.At("Tur"));
                break;
        }

        // Irk (türe bağlı; eşlenmezse serbest metin)
        Guid? breedId = null;
        string? breedText = null;
        if (reader.Raw("Irk") is { } rawBreed)
        {
            var breed = speciesId is null
                ? ImportValueResolution.Unmapped
                : _resolver.Resolve(ImportValueKind.Breed, rawBreed, speciesId);
            if (breed.Kind == ImportValueResolutionKind.Resolved)
            {
                if (_breedsById.TryGetValue(breed.TargetId!.Value, out var entry) && entry.ParentId == speciesId)
                    breedId = breed.TargetId;
                else
                    issues.Add(ImportRules.BreedSpeciesMismatch.At("Irk", value: rawBreed));
            }
            else
            {
                breedText = rawBreed;
                issues.Add(ImportRules.BreedFreeText.At("Irk", value: rawBreed));
                if (breedText.Length > MaxBreedTextLength)
                    issues.Add(ImportRules.FieldTooLong.At("Irk", $"Irk en fazla {MaxBreedTextLength} karakter olabilir."));
            }
        }

        // Cinsiyet, kısır
        var rawGender = reader.Raw("Cinsiyet");
        var (gender, genderRecognized) = ImportValueParsers.ParseGender(rawGender);
        if (!genderRecognized)
            issues.Add(ImportRules.GenderUnrecognized.At("Cinsiyet", value: rawGender));

        var rawNeutered = reader.Raw("Kisir");
        var (neutered, neuteredRecognized) = ImportValueParsers.ParseNeutered(rawNeutered);
        if (!neuteredRecognized)
            issues.Add(ImportRules.NeuteredUnrecognized.At("Kisir", value: rawNeutered));

        // Doğum tarihi
        var (birthDate, _) = ReadDate(reader, "DogumTarihi", issues);
        if (birthDate is { } birth)
        {
            if (birth > _today)
            {
                issues.Add(ImportRules.BirthDateInFuture.At("DogumTarihi", value: reader.Raw("DogumTarihi")));
                birthDate = null;
            }
            else if (birth < _today.AddYears(-MaxBirthDateAgeYears))
            {
                issues.Add(ImportRules.BirthDateImplausible.At("DogumTarihi", value: reader.Raw("DogumTarihi")));
            }
        }

        // Mikroçip biçimi (çakışma kimlik çözüldükten sonra)
        var rawChip = reader.Raw("Mikrocip");
        var chip = ImportValueParsers.ParseMicrochip(rawChip);
        switch (chip.Problem)
        {
            case ImportMicrochipProblem.ScientificNotation:
                issues.Add(ImportRules.MicrochipScientificNotation.At("Mikrocip", value: rawChip));
                break;
            case ImportMicrochipProblem.Invalid:
                issues.Add(ImportRules.MicrochipInvalid.At("Mikrocip", value: rawChip));
                break;
            default:
                if (chip.Value is { Length: not ImportValueParsers.StandardMicrochipLength })
                    issues.Add(ImportRules.MicrochipLength.At("Mikrocip", value: rawChip));
                break;
        }

        // Renk (katalogda yoksa not)
        Guid? colorId = null;
        string? colorNote = null;
        if (reader.Raw("Renk") is { } rawColor)
        {
            var color = _resolver.Resolve(ImportValueKind.Color, rawColor);
            if (color.Kind == ImportValueResolutionKind.Resolved)
            {
                colorId = color.TargetId;
            }
            else
            {
                colorNote = $"Renk: {rawColor}";
                issues.Add(ImportRules.ColorFreeText.At("Renk", value: rawColor));
            }
        }

        // Ağırlık
        var rawWeight = reader.Raw("Agirlik");
        var (weight, weightValid) = ImportValueParsers.ParseWeight(rawWeight);
        if (!weightValid)
            issues.Add(ImportRules.WeightInvalid.At("Agirlik", value: rawWeight));

        var passport = reader.Raw("KarneNo");
        if (passport is { Length: > MaxPassportLength })
            issues.Add(ImportRules.FieldTooLong.At("KarneNo", $"Karne numarası en fazla {MaxPassportLength} karakter olabilir."));

        var notes = string.Join('\n', new[] { colorNote, reader.Raw("Not") }.Where(s => s is not null));
        if (notes.Length > MaxPetNotesLength)
            issues.Add(ImportRules.FieldTooLong.At("Not", $"Not (renk satırıyla birlikte) en fazla {MaxPetNotesLength} karakter olabilir."));

        // Kayıt anahtarı: tekrarlanan numara
        if (reference is not null)
        {
            if (_petByRef.TryGetValue(reference, out var first))
            {
                issues.Add(ImportRules.DuplicateReference.At(
                    "HayvanNo", $"Bu hayvan numarası {first.Row}. satırda da kullanılmış.", reference));
            }
            else
            {
                _petByRef[reference] = node;
            }
        }

        var normalized = new ImportNormalizedPet(
            owner?.Key, reference, name, speciesId, breedId, breedText, gender, birthDate, neutered,
            chip.Value, colorId, weight, passport, notes.Length == 0 ? null : notes);

        if (HasError(issues) || owner is null)
            return Complete(node, Finish(ImportTable.Pets, reader.RowNumber, issues, normalized));

        // Tür bilinçli atlandı: satır ve bağlı aşılar atlanır.
        if (speciesSkipped)
        {
            issues.Add(ImportRules.ValueSkipped.At("Tur", value: rawSpecies));
            node.SkippedByUser = true;
            return Complete(node, Finish(ImportTable.Pets, reader.RowNumber, issues, normalized, ImportRowAction.Skip));
        }

        var nameKey = PetNameKey(name!);
        var identity = (owner.Key, nameKey, speciesId!.Value);

        // Dosya içi mükerrer (aynı sahip + ad + tür): ilk görülen kazanır.
        if (_petByIdentity.TryGetValue(identity, out var canonical))
        {
            node.Ref = canonical.Ref;
            issues.Add(ImportRules.DuplicateInFile.At(null, $"Aynı hayvan {canonical.Row}. satırda da var; bu satır atlanacak."));
            return Complete(node, Finish(
                ImportTable.Pets, reader.RowNumber, issues, normalized, ImportRowAction.Skip, mergedIntoRow: canonical.Row));
        }

        // Kiracıda mevcut hayvan (aynı müşteri + küçük harf ad + tür).
        ImportExistingPet? existing = null;
        if (owner.ClientId is { } clientId)
            _dbPetByIdentity.TryGetValue((clientId, nameKey, speciesId.Value), out existing);

        // Mikroçip çakışması (K10): başka hayvanda dosyada veya kiracıda.
        var petKey = existing is not null ? $"db:{existing.Id}" : $"file:{reader.RowNumber}";
        if (chip.Value is { } chipValue)
        {
            var conflict = FindChipConflict(chipValue, petKey, existing?.Id);
            if (conflict is not null)
            {
                issues.Add(ImportRules.MicrochipConflict.At("Mikrocip", conflict, rawChip));
                return Complete(node, Finish(ImportTable.Pets, reader.RowNumber, issues, normalized));
            }

            _fileChips[chipValue] = (petKey, reader.RowNumber);
        }

        node.Ref = new PetRef(petKey, existing?.Id, speciesId.Value, birthDate ?? existing?.BirthDate, owner);
        _petByIdentity[identity] = node;

        if (existing is not null)
        {
            issues.Add(ImportRules.MatchedExisting.At());
            return Complete(node, Finish(ImportTable.Pets, reader.RowNumber, issues, normalized, ImportRowAction.Match, existing.Id));
        }

        return Complete(node, Finish(ImportTable.Pets, reader.RowNumber, issues, normalized, ImportRowAction.Create));
    }

    private static ImportRowResult Complete(PetNode node, ImportRowResult result)
    {
        node.Status = result.Status;
        if (result.Status == ImportRowValidationStatus.Error)
            node.Ref = null;
        return result;
    }

    /// <summary>Çakışma varsa kullanıcıya gösterilecek açıklama, yoksa null.</summary>
    private string? FindChipConflict(string chip, string petKey, Guid? matchedPetId)
    {
        if (_fileChips.TryGetValue(chip, out var seen) && seen.PetKey != petKey)
            return $"Aynı mikroçip {seen.Row}. satırdaki başka bir hayvanda da var.";

        if (_dbPetsByChip.TryGetValue(chip, out var dbPets) && dbPets.Any(p => p.Id != matchedPetId))
            return "Aynı mikroçip kayıtlı başka bir hayvanda var.";

        return null;
    }

    /// <summary>
    /// Hasta satırının sahibini çözer (bölüm 6.4 bağlantı çözümü). Çözülemezse nedeni <paramref name="issues"/>'a ekler.
    /// </summary>
    private OwnerRef? ResolveOwnerForPet(ImportRowReader reader, List<ImportIssue> issues)
    {
        var reference = ImportValueParsers.NormalizeText(reader.Raw("MusteriNo"));
        if (reference is not null && CustomerReferenceUsable)
        {
            if (!_customerByRef.TryGetValue(reference, out var node))
            {
                issues.Add(ImportRules.OwnerNotFound.At(
                    "MusteriNo", $"'{reference}' numaralı müşteri dosyada bulunamadı.", reference));
                return null;
            }

            if (node.Owner is null)
            {
                issues.Add(ImportRules.OwnerRowInvalid.At("MusteriNo", $"Müşteri satırı ({node.Row}) hatalı.", reference));
                return null;
            }

            return node.Owner;
        }

        return FindOwnerByNameAndPhone(reader, issues, ImportRules.OwnerNotFound);
    }

    /// <summary>
    /// Sahibi <c>SahipAdSoyad + SahipTelefon</c> ile çözer: telefon geçerliyse dosyadaki sonra kiracıdaki müşteri
    /// (ad + telefon); telefon boş/geçersizse dosyadaki aynı adlı <b>tek</b> müşteri.
    /// </summary>
    private OwnerRef? FindOwnerByNameAndPhone(ImportRowReader reader, List<ImportIssue> issues, ImportRule notFound)
    {
        var fullName = ImportValueParsers.NormalizeText(reader.Raw("SahipAdSoyad"));
        if (fullName is null)
        {
            issues.Add(notFound.At("SahipAdSoyad", "Sahip adı boş; sahip bulunamadı."));
            return null;
        }

        var rawPhone = reader.Raw("SahipTelefon");
        var (phone, phoneInvalid) = ImportValueParsers.ParsePhone(rawPhone);
        if (phoneInvalid)
            issues.Add(ImportRules.PhoneInvalid.At("SahipTelefon", value: rawPhone));

        var nameKey = NameKey(fullName);
        if (phone is not null)
        {
            if (_customerByNamePhone.TryGetValue((nameKey, phone), out var node) && node.Owner is { } fileOwner)
                return fileOwner;

            if (_dbClientByNamePhone.TryGetValue((nameKey, phone), out var existing))
                return new OwnerRef($"db:{existing.Id}", existing.Id, existing.Email is not null);

            issues.Add(notFound.At("SahipAdSoyad", "Sahip dosyada veya kayıtlı müşterilerde bulunamadı.", fullName));
            return null;
        }

        if (_customerByName.TryGetValue(nameKey, out var sameName))
        {
            var owners = sameName.Where(n => n.Owner is not null).Select(n => n.Owner!).DistinctBy(o => o.Key).ToList();
            if (owners.Count == 1)
                return owners[0];

            issues.Add(notFound.At("SahipAdSoyad", "Telefon olmadığı için sahip ayırt edilemedi (aynı adlı birden fazla müşteri var).", fullName));
            return null;
        }

        issues.Add(notFound.At("SahipAdSoyad", "Sahip dosyadaki müşteriler arasında bulunamadı (telefon yok/geçersiz).", fullName));
        return null;
    }
}
