using Backend.Veteriner.Domain.Prescriptions;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Prescriptions;

/// <summary>ADR-036 Dilim 2: reçete kaleminde doz, birim ve parti/seri no yapılandırılmış alanlardır.</summary>
public sealed class PrescriptionItemStructureTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static Prescription NewPrescription()
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, DateTime.UtcNow, "Reçete", "İçerik", null, null, Now);

    [Fact]
    public void Structured_Fields_Should_Be_Trimmed_And_Blank_Stored_As_Null()
    {
        var p = NewPrescription();

        p.ReplaceItems([new PrescriptionItemDraft("Amoksisilin", null, null, 2m, " 1 tablet ", " kutu ", "   ")]).IsSuccess.Should().BeTrue();

        var item = p.Items.Single();
        item.Dose.Should().Be("1 tablet");
        item.Unit.Should().Be("kutu");
        item.LotNumber.Should().BeNull();
    }

    [Theory]
    [InlineData(PrescriptionItem.DoseMaxLength + 1, 0, 0)]
    [InlineData(0, PrescriptionItem.UnitMaxLength + 1, 0)]
    [InlineData(0, 0, PrescriptionItem.LotNumberMaxLength + 1)]
    public void Too_Long_Structured_Field_Should_Fail(int doseLength, int unitLength, int lotLength)
    {
        var p = NewPrescription();
        string? Text(int length) => length == 0 ? null : new string('a', length);

        var result = p.ReplaceItems([new PrescriptionItemDraft("İlaç", null, null, 1m, Text(doseLength), Text(unitLength), Text(lotLength))]);

        result.Error.Code.Should().Be("Prescriptions.Validation");
        p.Items.Should().BeEmpty();
    }
}
