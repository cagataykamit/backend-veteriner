using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Clinics;

/// <summary>ADR-021 / ADR-024: klinik KVKK şablon metni (boş/uzunluk kuralı, tür).</summary>
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
}
