using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Clinics;

/// <summary>ADR-021: klinik KVKK form şablon metni (boş/uzunluk kuralı).</summary>
public sealed class ClinicKvkkFormTemplateTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_Should_Trim_And_Keep_Placeholders()
    {
        var result = ClinicKvkkFormTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "  {{clinicName}} / {{clientName}} / {{date}}  ", UserId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Text.Should().Be("{{clinicName}} / {{clientName}} / {{date}}");
        result.Value.UpdatedByUserId.Should().Be(UserId);
        result.Value.UpdatedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Reject_Empty(string? text)
    {
        var result = ClinicKvkkFormTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), text, UserId);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.KvkkFormTemplate.Validation");
    }

    [Fact]
    public void Update_Should_Enforce_Max_Length_And_Keep_Old_Text_On_Failure()
    {
        var row = ClinicKvkkFormTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), "Eski metin", UserId).Value!;

        row.UpdateText(new string('a', ClinicKvkkFormTemplate.MaxTextLength), UserId).IsSuccess.Should().BeTrue();
        row.UpdatedAtUtc.Should().NotBeNull();

        var tooLong = row.UpdateText(new string('b', ClinicKvkkFormTemplate.MaxTextLength + 1), UserId);
        tooLong.IsSuccess.Should().BeFalse();
        tooLong.Error.Code.Should().Be("Clinics.KvkkFormTemplate.Validation");
        row.Text.Should().HaveLength(ClinicKvkkFormTemplate.MaxTextLength).And.StartWith("a");
    }
}
