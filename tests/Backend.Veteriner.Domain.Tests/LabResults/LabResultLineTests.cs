using Backend.Veteriner.Domain.LabResults;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.LabResults;

/// <summary>REF-001 Dilim 3: değer satırı, hesaplanan işaret, karşılaştırılabilirlik ve satırlı sonuç kuralları.</summary>
public sealed class LabResultLineTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ResultDate = Now.AddHours(-2);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid PetId = Guid.NewGuid();

    private static LabResultLineDraft Line(string name = "ALT", decimal value = 45m, string? unit = "U/L", decimal? low = 10m, decimal? high = 100m)
        => new(name, value, unit, low, high);

    private static LabResult WithLines(string? text, params LabResultLineDraft[] lines)
        => new(TenantId, ClinicId, PetId, null, null, null, ResultDate, "Biyokimya", text, null, null, Now, lines);

    [Theory]
    [InlineData(5, 10, 100, LabValueFlag.Low)]
    [InlineData(10, 10, 100, LabValueFlag.Normal)]
    [InlineData(100, 10, 100, LabValueFlag.Normal)]
    [InlineData(100.01, 10, 100, LabValueFlag.High)]
    public void Evaluate_Should_Compare_Value_With_Both_Limits(decimal value, decimal low, decimal high, LabValueFlag expected)
        => LabResultLine.Evaluate(value, low, high).Should().Be(expected);

    [Fact]
    public void Evaluate_Should_Use_Single_Limit_And_Return_Null_Without_Range()
    {
        LabResultLine.Evaluate(3m, null, 5m).Should().Be(LabValueFlag.Normal);
        LabResultLine.Evaluate(6m, null, 5m).Should().Be(LabValueFlag.High);
        LabResultLine.Evaluate(1m, 2m, null).Should().Be(LabValueFlag.Low);
        LabResultLine.Evaluate(1m, null, null).Should().BeNull();
    }

    [Fact]
    public void Lines_Alone_Should_Make_Result_Resulted_And_Keep_Text_Empty()
    {
        var l = WithLines(null, Line(), Line("Glikoz", 140m, "mg/dL", 70m, 120m));

        l.Status.Should().Be(LabResultStatus.Resulted);
        l.ResultText.Should().BeNull();
        l.Lines.Should().HaveCount(2);
        l.Lines.Select(x => x.SortOrder).Should().Equal(1, 2);
        l.Lines[0].Flag.Should().Be(LabValueFlag.Normal);
        l.Lines[1].Flag.Should().Be(LabValueFlag.High);
        l.Lines[1].LabResultId.Should().Be(l.Id);
    }

    [Fact]
    public void Free_Text_Should_Be_Kept_Next_To_Lines()
    {
        var l = WithLines(" Rapor ekte. ", Line());

        l.ResultText.Should().Be("Rapor ekte.");
        l.Lines.Should().ContainSingle();
    }

    [Fact]
    public void Lines_Without_Result_Date_Should_Be_Rejected()
    {
        var check = LabResult.ValidateResult(null, null, null, [Line()]);

        check.IsSuccess.Should().BeFalse();
        check.Error.Code.Should().Be("LabResults.Validation");
    }

    [Fact]
    public void Same_Parameter_Twice_Should_Be_Rejected_Regardless_Of_Spelling()
    {
        var check = LabResult.ValidateResult(ResultDate, null, null, [Line("Glikoz"), Line(" GLİKOZ ")]);

        check.IsSuccess.Should().BeFalse();
        check.Error.Message.Should().Contain("Aynı parametre");
    }

    [Theory]
    [InlineData("", 1, null, null)]
    [InlineData("ALT", 1.00001, null, null)]
    [InlineData("ALT", 5, 10, 2)]
    public void Invalid_Line_Should_Be_Rejected(string name, decimal value, int? low, int? high)
    {
        var check = LabResult.ValidateResult(ResultDate, null, null, [new LabResultLineDraft(name, value, null, low, high)]);

        check.IsSuccess.Should().BeFalse();
        check.Error.Code.Should().Be("LabResults.Validation");
    }

    [Fact]
    public void More_Than_Max_Lines_Should_Be_Rejected()
    {
        var lines = Enumerable.Range(1, LabResultLine.MaxPerResult + 1).Select(i => Line($"P{i}")).ToList();

        LabResult.ValidateResult(ResultDate, null, null, lines).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Update_With_Null_Lines_Should_Keep_Lines_And_Empty_List_Should_Clear_Them()
    {
        var l = WithLines("Metin", Line());

        l.UpdateDetails(ClinicId, PetId, null, null, ResultDate, "Biyokimya", "Metin", null, null, Now).IsSuccess.Should().BeTrue();
        l.Lines.Should().ContainSingle();

        l.UpdateDetails(ClinicId, PetId, null, null, ResultDate, "Biyokimya", "Metin", null, null, Now, []).IsSuccess.Should().BeTrue();
        l.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Update_Should_Not_Remove_Last_Result_When_Text_And_Lines_Both_Go()
    {
        var l = WithLines(null, Line());

        var update = l.UpdateDetails(ClinicId, PetId, null, null, null, "Biyokimya", null, null, null, Now, []);

        update.IsSuccess.Should().BeFalse();
        l.Lines.Should().ContainSingle();
    }

    [Fact]
    public void Requested_Should_Become_Resulted_When_Lines_Are_Entered()
    {
        var l = new LabResult(TenantId, ClinicId, PetId, null, null, null, null, "Hemogram", null, null, null, Now);

        l.UpdateDetails(ClinicId, PetId, null, null, ResultDate, "Hemogram", null, null, null, Now, [Line("WBC", 12m, "10^9/L", 6m, 17m)])
            .IsSuccess.Should().BeTrue();

        l.Status.Should().Be(LabResultStatus.Resulted);
        l.Lines.Should().ContainSingle();
    }

    [Fact]
    public void Comparable_Should_Require_Same_Parameter_And_Unit_Ignoring_Spelling()
    {
        var current = WithLines(null, Line("Glikoz", 140m, "mg/dL")).Lines[0];

        current.IsComparableWith(WithLines(null, Line("GLİKOZ", 100m, "MG/DL")).Lines[0]).Should().BeTrue();
        current.IsComparableWith(WithLines(null, Line("Glikoz", 7m, "mmol/L")).Lines[0]).Should().BeFalse();
        current.IsComparableWith(WithLines(null, Line("Üre", 140m, "mg/dL")).Lines[0]).Should().BeFalse();
    }

    [Fact]
    public void TrendFrom_Should_Give_Direction()
    {
        var line = WithLines(null, Line(value: 50m)).Lines[0];

        line.TrendFrom(40m).Should().Be(LabValueTrend.Up);
        line.TrendFrom(60m).Should().Be(LabValueTrend.Down);
        line.TrendFrom(50m).Should().Be(LabValueTrend.Unchanged);
    }
}
