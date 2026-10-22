using Backend.Veteriner.Application.Hbs;
using Backend.Veteriner.Application.Hbs.Contracts;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hbs;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Hbs;

/// <summary>ADR-036 Dilim 2: HBS hazır veri alanları, sahip kimlik no maskesi, satırsız reçete ve tümünü kopyala metni.</summary>
public sealed class HbsDataBuilderTests
{
    private const string Tckn = "10000000146";

    private static readonly HbsAnimalSource Animal = new("Pamuk", "Kedi", "Tekir", "900123456789012", null);
    private static readonly HbsEntrySource NotEntered = new(HbsEntryStatus.NotEntered, null, null, null, null, null);

    // 21.10.2026 23:30 UTC = 22.10.2026 02:30 İstanbul
    private static readonly DateTime LateUtc = new(2026, 10, 21, 23, 30, 0, DateTimeKind.Utc);

    private static HbsOwnerSource Owner(ClientPersonType? type = ClientPersonType.Individual)
        => new("Ayşe Yılmaz", "905321110001", type, type == ClientPersonType.Individual ? Tckn : null, type == ClientPersonType.Corporate ? "1234567890" : null);

    private static HbsPrescriptionSource Prescription(IReadOnlyList<HbsPrescriptionItemSource> items, HbsOwnerSource? owner = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), LateUtc, "Serbest metin içerik", items, Animal, owner ?? Owner(), "Dr. Ali Veli", NotEntered);

    private static HbsDataFieldDto Field(HbsDataDto dto, string key)
        => dto.Sections.SelectMany(s => s.Fields).Single(f => f.Key == key);

    [Fact]
    public void Prescription_Should_List_Animal_Owner_Record_And_Item_Fields_In_Hbs_Format()
    {
        var dto = HbsDataBuilder.ForPrescription(
            Prescription([new HbsPrescriptionItemSource("Amoksisilin 250 mg", 0.5m, "kutu", "1 tablet", "Günde 2 kez", null)]),
            HbsDataBuilder.Entry(NotEntered, true),
            revealOwnerIdentity: true);

        dto.RecordKind.Should().Be(HbsRecordKind.Prescription);
        dto.Sections.Select(s => s.Key).Should().Equal("animal", "owner", "record", "item1");
        Field(dto, "animal.microchip").Value.Should().Be("900123456789012");
        Field(dto, "animal.hbsAnimalId").Value.Should().BeNull("boş alan da listelenir");
        Field(dto, "record.date").Value.Should().Be("22.10.2026", "tarih İstanbul günüdür");
        Field(dto, "record.veterinarian").Value.Should().Be("Dr. Ali Veli");
        Field(dto, "item1.quantity").Value.Should().Be("0,5");
        Field(dto, "item1.unit").Value.Should().Be("kutu");
        Field(dto, "item1.dose").Value.Should().Be("1 tablet");
        Field(dto, "owner.identityNumber").Value.Should().Be(Tckn);
        Field(dto, "owner.identityNumber").Label.Should().Be("T.C. Kimlik No");
        dto.OwnerIdentityMasked.Should().BeFalse();
    }

    [Fact]
    public void Owner_Identity_Should_Be_Masked_Without_Reveal_Permission()
    {
        var dto = HbsDataBuilder.ForPrescription(Prescription([]), HbsDataBuilder.Entry(NotEntered, true), revealOwnerIdentity: false);

        Field(dto, "owner.identityNumber").Value.Should().Be("100******46");
        dto.OwnerIdentityMasked.Should().BeTrue();
        dto.CopyAllText.Should().NotContain(Tckn);
    }

    [Fact]
    public void Corporate_Owner_Should_Use_Tax_Number_Label_And_Missing_Type_Should_Be_Empty()
    {
        var corporate = HbsDataBuilder.ForPrescription(Prescription([], Owner(ClientPersonType.Corporate)), HbsDataBuilder.Entry(NotEntered, true), true);
        Field(corporate, "owner.identityNumber").Label.Should().Be("Vergi No");
        Field(corporate, "owner.identityNumber").Value.Should().Be("1234567890");

        var unknown = HbsDataBuilder.ForPrescription(Prescription([], Owner(null)), HbsDataBuilder.Entry(NotEntered, true), false);
        Field(unknown, "owner.identityNumber").Value.Should().BeNull();
        unknown.OwnerIdentityMasked.Should().BeFalse();
    }

    [Fact]
    public void Prescription_Without_Items_Should_Expose_Content_As_Single_Medication_Section()
    {
        var dto = HbsDataBuilder.ForPrescription(Prescription([]), HbsDataBuilder.Entry(NotEntered, true), true);

        dto.Sections.Last().Fields.Should().ContainSingle()
            .Which.Should().Be(new HbsDataFieldDto("item1.content", "Reçete İçeriği", "Serbest metin içerik"));
    }

    [Fact]
    public void Vaccination_Should_Include_Lot_Expiry_Dose_And_Date()
    {
        var source = new HbsVaccinationSource(
            Guid.NewGuid(), Guid.NewGuid(), "Kuduz Aşısı", LateUtc, true, "LOT-9", new DateOnly(2027, 3, 1), 1m, "doz",
            Animal, Owner(), null, NotEntered);

        var dto = HbsDataBuilder.ForVaccination(source, HbsDataBuilder.Entry(NotEntered, source.IsTracked), true);

        dto.RecordKind.Should().Be(HbsRecordKind.Vaccination);
        Field(dto, "vaccine.lotNumber").Value.Should().Be("LOT-9");
        Field(dto, "vaccine.expiryDate").Value.Should().Be("01.03.2027");
        Field(dto, "vaccine.dose").Value.Should().Be("1 doz");
        Field(dto, "vaccine.appliedAt").Value.Should().Be("22.10.2026");
        Field(dto, "vaccine.veterinarian").Value.Should().BeNull();
    }

    [Fact]
    public void CopyAllText_Should_Contain_Only_Filled_Fields_Grouped_By_Section()
    {
        var sections = new List<HbsDataSectionDto>
        {
            new("a", "Hasta", [new("a.name", "Adı", "Pamuk"), new("a.chip", "Mikroçip No", null)]),
            new("b", "Boş", [new("b.x", "X", null)]),
            new("c", "Aşı", [new("c.name", "Aşı Adı", "Kuduz")]),
        };

        HbsDataBuilder.CopyAllText(sections).Should().Be("Hasta\nAdı: Pamuk\n\nAşı\nAşı Adı: Kuduz");
    }
}
