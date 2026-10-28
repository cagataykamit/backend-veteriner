using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Tests.TestHelpers;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Clinics.Access;

public sealed class ClinicContextRuleTests
{
    private static readonly Guid ClinicA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClinicB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static TheoryData<Guid?, Guid?, bool> Cases => new()
    {
        { null, null, false },
        { ClinicA, null, false },
        { null, ClinicA, false },
        { ClinicA, ClinicA, false },
        { ClinicA, ClinicB, true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Conflicts_Should_BeTrue_Only_When_Both_Present_And_Different(Guid? requested, Guid? context, bool expected)
    {
        ClinicContextRule.Conflicts(requested, context).Should().Be(expected);
    }

    [Fact]
    public void Mismatch_Should_Keep_Module_Prefixed_Code_And_Default_Message()
    {
        var error = ClinicContextRule.Mismatch("Payments");

        error.Code.Should().Be("Payments.ClinicContextMismatch");
        error.Message.Should().Be("İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
    }

    [Fact]
    public void Mismatch_Should_Use_Custom_Message_When_Given()
    {
        var error = ClinicContextRule.Mismatch("Treatments", "Tedavi kaydı sadece aktif clinic bağlamında güncellenebilir.");

        error.Code.Should().Be("Treatments.ClinicContextMismatch");
        error.Message.Should().Be("Tedavi kaydı sadece aktif clinic bağlamında güncellenebilir.");
    }

    /// <summary>Kural tek yerde yaşar: kod metni <see cref="ClinicContextRule"/> dışında elle yazılmaz.</summary>
    [Fact]
    public void ClinicContextMismatch_Code_Should_Only_Be_Built_In_ClinicContextRule()
    {
        var handWritten = RepositoryPaths.SourceFiles()
            .Where(f => File.ReadAllText(f).Contains("ClinicContextMismatch\""))
            .Select(RepositoryPaths.RelativeToSrc)
            .Where(f => f != "Backend.Veteriner.Application/Clinics/Access/ClinicContextRule.cs")
            .ToList();

        handWritten.Should().BeEmpty("clinicId/bağlam çelişkisi ClinicContextRule.Conflicts + Mismatch ile denetlenir");
    }
}
