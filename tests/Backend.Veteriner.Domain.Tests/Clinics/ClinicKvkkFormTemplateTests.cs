using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Clinics;

/// <summary>ADR-021 / ADR-024: klinik KVKK şablon metni (boş/uzunluk kuralı, tür); ADR-032: metin sürümü ve özeti.</summary>
public sealed class ClinicKvkkFormTemplateTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Theory]
    [InlineData(ClinicKvkkFormKind.Consent)]
    [InlineData(ClinicKvkkFormKind.Disclosure)]
    public void Create_Should_Trim_Keep_Placeholders_And_Kind(ClinicKvkkFormKind kind)
    {
        var result = ClinicKvkkFormTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(), kind, "  {{clinicName}} / {{clientName}} / {{date}}  ", UserId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Text.Should().Be("{{clinicName}} / {{clientName}} / {{date}}");
        result.Value.Kind.Should().Be(kind);
        result.Value.UpdatedByUserId.Should().Be(UserId);
        result.Value.UpdatedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Reject_Empty(string? text)
    {
        var result = ClinicKvkkFormTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), ClinicKvkkFormKind.Disclosure, text, UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.KvkkFormTemplate.Validation");
    }

    [Fact]
    public void Create_Should_Throw_On_Undefined_Kind()
    {
        var act = () => ClinicKvkkFormTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), (ClinicKvkkFormKind)99, "Metin", UserId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_Should_Enforce_Max_Length_And_Keep_Old_Text_On_Failure()
    {
        var row = ClinicKvkkFormTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), ClinicKvkkFormKind.Consent, "Eski metin", UserId).Value!;

        row.UpdateText(new string('a', ClinicKvkkFormTemplate.MaxTextLength), UserId).IsSuccess.Should().BeTrue();
        row.UpdatedAtUtc.Should().NotBeNull();

        var tooLong = row.UpdateText(new string('b', ClinicKvkkFormTemplate.MaxTextLength + 1), UserId);
        tooLong.IsSuccess.Should().BeFalse();
        tooLong.Error.Code.Should().Be("Clinics.KvkkFormTemplate.Validation");
        row.Text.Should().HaveLength(ClinicKvkkFormTemplate.MaxTextLength).And.StartWith("a");
    }

    [Fact]
    public void Create_Should_Start_At_Version_One_With_Hash()
    {
        var row = ClinicKvkkFormTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), ClinicKvkkFormKind.Consent, "  Rıza metni  ", UserId).Value!;

        row.TextVersion.Should().Be(1);
        row.TextHash.Should().Be(ClinicKvkkFormTemplate.ComputeTextHash("Rıza metni"));
    }

    [Fact]
    public void Update_Should_Increment_Version_Only_When_Text_Changes()
    {
        var row = ClinicKvkkFormTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), ClinicKvkkFormKind.Consent, "Metin A", UserId).Value!;

        row.UpdateText("Metin B", UserId).IsSuccess.Should().BeTrue();
        row.TextVersion.Should().Be(2);
        row.TextHash.Should().Be(ClinicKvkkFormTemplate.ComputeTextHash("Metin B"));

        // Aynı metin (baş/son boşluk kırpıldıktan sonra) sürümü artırmaz.
        row.UpdateText("  Metin B ", UserId).IsSuccess.Should().BeTrue();
        row.TextVersion.Should().Be(2);

        // Geçersiz metin sürüme dokunmaz.
        row.UpdateText("   ", UserId).IsSuccess.Should().BeFalse();
        row.TextVersion.Should().Be(2);

        // Eski metne dönmek de bir değişikliktir.
        row.UpdateText("Metin A", UserId).IsSuccess.Should().BeTrue();
        row.TextVersion.Should().Be(3);
        row.TextHash.Should().Be(ClinicKvkkFormTemplate.ComputeTextHash("Metin A"));
    }

    [Fact]
    public void ComputeTextHash_Should_Be_Lowercase_Hex_Sha256_Of_Utf8()
    {
        // SHA-256("abc") bilinen değer.
        ClinicKvkkFormTemplate.ComputeTextHash("abc")
            .Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        ClinicKvkkFormTemplate.ComputeTextHash("Açık rıza").Should().HaveLength(ClinicKvkkFormTemplate.TextHashLength);
        ClinicKvkkFormTemplate.DefaultTextVersion.Should().Be(0);
    }
}
