using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Hbs.Contracts;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Hbs;

/// <summary>
/// HBS ekranında girilecek alanların tek tanımı (bölüm, etiket, kopyalanacak değer). Alanlar pilot hekimle doğrulanınca yalnız
/// bu sınıf değişir (sözleşme bölüm 4). Değerler HBS'e yapıştırılacak biçimdedir: tarih <c>dd.MM.yyyy</c> (İstanbul), miktar Türkçe ondalık.
/// </summary>
public static class HbsDataBuilder
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static HbsDataDto ForPrescription(HbsPrescriptionSource source, HbsEntryDto hbs, bool revealOwnerIdentity)
    {
        var (owner, masked) = OwnerSection(source.Owner, revealOwnerIdentity);
        var sections = new List<HbsDataSectionDto>
        {
            AnimalSection(source.Animal),
            owner,
            new("record", "Reçete",
            [
                new("record.date", "Reçete Tarihi", Day(source.PrescribedAtUtc)),
                new("record.veterinarian", "Veteriner Hekim", source.VeterinarianName),
            ]),
        };

        if (source.Items.Count == 0)
        {
            sections.Add(new HbsDataSectionDto("item1", "İlaç 1", [new("item1.content", "Reçete İçeriği", Text(source.Content))]));
        }
        else
        {
            for (var i = 0; i < source.Items.Count; i++)
            {
                var item = source.Items[i];
                var key = $"item{i + 1}";
                sections.Add(new HbsDataSectionDto(key, $"İlaç {i + 1}",
                [
                    new($"{key}.name", "İlaç Adı", Text(item.Name)),
                    new($"{key}.quantity", "Miktar", Number(item.Quantity)),
                    new($"{key}.unit", "Birim", Text(item.Unit)),
                    new($"{key}.dose", "Doz", Text(item.Dose)),
                    new($"{key}.instructions", "Kullanım", Text(item.Instructions)),
                    new($"{key}.lotNumber", "Parti/Seri No", Text(item.LotNumber)),
                ]));
            }
        }

        return Build(HbsRecordKind.Prescription, source.Id, hbs, masked, sections);
    }

    public static HbsDataDto ForVaccination(HbsVaccinationSource source, HbsEntryDto hbs, bool revealOwnerIdentity)
    {
        var (owner, masked) = OwnerSection(source.Owner, revealOwnerIdentity);
        var dose = source.DoseQuantity is { } quantity ? Text($"{Number(quantity)} {source.DoseUnit}") : null;
        var sections = new List<HbsDataSectionDto>
        {
            AnimalSection(source.Animal),
            owner,
            new("vaccine", "Aşı",
            [
                new("vaccine.name", "Aşı Adı", Text(source.VaccineName)),
                new("vaccine.lotNumber", "Parti/Seri No", Text(source.LotNumber)),
                new("vaccine.expiryDate", "Son Kullanma Tarihi", source.ExpiryDate?.ToString("dd.MM.yyyy", Turkish)),
                new("vaccine.dose", "Doz", dose),
                new("vaccine.appliedAt", "Uygulama Tarihi", source.AppliedAtUtc is { } applied ? Day(applied) : null),
                new("vaccine.veterinarian", "Veteriner Hekim", source.VeterinarianName),
            ]),
        };

        return Build(HbsRecordKind.Vaccination, source.Id, hbs, masked, sections);
    }

    /// <summary>Kaynak kaydın HBS alanlarından yanıt nesnesi.</summary>
    public static HbsEntryDto Entry(HbsEntrySource source, bool isTracked)
        => new(source.Status, source.ReferenceNumber, source.NotRequiredReason, source.MarkedAtUtc, source.MarkedByUserId, source.MarkedByName, isTracked);

    /// <summary>Dolu alanlar: bölüm başlığı satırı ve altında <c>Etiket: değer</c>; bölümler boş satırla ayrılır.</summary>
    public static string CopyAllText(IReadOnlyList<HbsDataSectionDto> sections)
    {
        var blocks = sections
            .Select(s => (s.Title, Lines: s.Fields.Where(f => f.Value is not null).Select(f => $"{f.Label}: {f.Value}").ToList()))
            .Where(b => b.Lines.Count > 0)
            .Select(b => b.Title + "\n" + string.Join('\n', b.Lines));

        return string.Join("\n\n", blocks);
    }

    private static HbsDataDto Build(HbsRecordKind kind, Guid id, HbsEntryDto hbs, bool masked, IReadOnlyList<HbsDataSectionDto> sections)
        => new(kind, id, hbs, masked, sections, CopyAllText(sections));

    private static HbsDataSectionDto AnimalSection(HbsAnimalSource animal)
        => new("animal", "Hasta",
        [
            new("animal.name", "Adı", Text(animal.Name)),
            new("animal.species", "Türü", Text(animal.Species)),
            new("animal.breed", "Irkı", Text(animal.Breed)),
            new("animal.microchip", "Mikroçip No", Text(animal.MicrochipNumber)),
            new("animal.hbsAnimalId", "HBS Hayvan Kimlik No", Text(animal.HbsAnimalId)),
        ]);

    /// <summary>
    /// Kimlik no kişi türünden: bireysel → TCKN, kurumsal → vergi no (VKN veya şahıs şirketi TCKN'si). Tam değer yalnız
    /// <paramref name="reveal"/> iken; aksi hâlde <see cref="TurkishTaxIdentity.Mask"/>.
    /// </summary>
    private static (HbsDataSectionDto Section, bool Masked) OwnerSection(HbsOwnerSource owner, bool reveal)
    {
        var (label, number) = owner.PersonType switch
        {
            ClientPersonType.Individual => ("T.C. Kimlik No", owner.NationalId),
            ClientPersonType.Corporate => ("Vergi No", owner.TaxNumber),
            _ => ("T.C. Kimlik No / Vergi No", null),
        };

        var masked = !reveal && !string.IsNullOrWhiteSpace(number);
        var value = masked ? TurkishTaxIdentity.Mask(number) : Text(number);

        return (new HbsDataSectionDto("owner", "Hasta Sahibi",
        [
            new("owner.name", "Adı Soyadı", Text(owner.FullName)),
            new("owner.identityNumber", label, value),
            new("owner.phone", "Telefon", Text(owner.Phone)),
        ]), masked);
    }

    private static string Day(DateTime utc) => OperationDayBounds.ToLocalDate(utc).ToString("dd.MM.yyyy", Turkish);

    private static string? Number(decimal? value) => value?.ToString("0.###", Turkish);

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
