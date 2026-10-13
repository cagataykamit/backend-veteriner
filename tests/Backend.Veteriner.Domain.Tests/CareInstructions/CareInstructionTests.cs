using Backend.Veteriner.Domain.CareInstructions;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.CareInstructions;

public sealed class CareInstructionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid PetId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static CareInstruction Create(string text = " Günde iki kez su verin ")
        => new(TenantId, ClinicId, PetId, null, null, null, UserId, text);

    [Fact]
    public void Ctor_Should_Trim_Text_And_Record_Author()
    {
        var hospitalizationId = Guid.NewGuid();
        var examinationId = Guid.NewGuid();
        var visitId = Guid.NewGuid();

        var c = new CareInstruction(TenantId, ClinicId, PetId, hospitalizationId, examinationId, visitId, UserId, "  Dinlendirin ");

        c.Text.Should().Be("Dinlendirin");
        c.CreatedByUserId.Should().Be(UserId);
        c.HospitalizationId.Should().Be(hospitalizationId);
        c.ExaminationId.Should().Be(examinationId);
        c.VisitId.Should().Be(visitId);
        c.UpdatedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_Should_Reject_Blank_Text(string text)
    {
        var act = () => Create(text);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Ctor_Should_Reject_Too_Long_Text()
    {
        var act = () => Create(new string('a', CareInstruction.MaxTextLength + 1));

        act.Should().Throw<ArgumentException>();
        Create(new string('a', CareInstruction.MaxTextLength)).Text.Should().HaveLength(CareInstruction.MaxTextLength);
    }

    [Fact]
    public void UpdateText_Should_Change_Text_And_Keep_Links()
    {
        var visitId = Guid.NewGuid();
        var c = new CareInstruction(TenantId, ClinicId, PetId, null, null, visitId, UserId, "Eski");
        var editor = Guid.NewGuid();

        var result = c.UpdateText(editor, " Yeni ");

        result.IsSuccess.Should().BeTrue();
        c.Text.Should().Be("Yeni");
        c.UpdatedByUserId.Should().Be(editor);
        c.UpdatedAtUtc.Should().NotBeNull();
        c.VisitId.Should().Be(visitId);
    }

    [Fact]
    public void UpdateText_Should_Reject_Blank_Text_Without_Changing()
    {
        var c = Create("Eski");

        var result = c.UpdateText(UserId, "  ");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CareInstructions.Validation");
        c.Text.Should().Be("Eski");
    }
}
