using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Domain.Legal;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.LegalAcceptances;

public sealed class LegalDocumentCatalogTests
{
    [Fact]
    public void CurrentVersions_Should_Cover_Every_DocumentType()
    {
        LegalDocumentCatalog.CurrentVersions.Keys.Should().BeEquivalentTo(Enum.GetValues<LegalDocumentType>());
        LegalDocumentCatalog.CurrentVersions.Values.Should().OnlyContain(v => v.Length <= LegalAcceptance.MaxVersionLength);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("Owner")]
    public void RequiredFor_ClinicAdministrator_Should_Include_DataProcessing(string claimName)
    {
        LegalDocumentCatalog.RequiredFor([claimName]).Should().BeEquivalentTo(
            [LegalDocumentType.Terms, LegalDocumentType.Privacy, LegalDocumentType.DataProcessing]);
    }

    [Theory]
    [InlineData("ClinicAdmin")]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    [InlineData("PlatformAdmin")]
    public void RequiredFor_OtherRoles_Should_Be_Terms_And_Privacy(string claimName)
    {
        LegalDocumentCatalog.RequiredFor([claimName]).Should().BeEquivalentTo(
            [LegalDocumentType.Terms, LegalDocumentType.Privacy]);
    }

    [Fact]
    public void RequiredFor_NoClaims_Should_Be_Terms_And_Privacy()
    {
        LegalDocumentCatalog.RequiredFor(null).Should().BeEquivalentTo([LegalDocumentType.Terms, LegalDocumentType.Privacy]);
    }

    [Fact]
    public void Pending_Should_Ignore_Acceptances_Of_Older_Versions()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var acceptances = new[]
        {
            LegalAcceptance.Record(userId, tenantId, LegalDocumentType.Terms, LegalDocumentCatalog.CurrentVersion(LegalDocumentType.Terms), DateTime.UtcNow),
            LegalAcceptance.Record(userId, tenantId, LegalDocumentType.Privacy, "0.0", DateTime.UtcNow),
        };

        var pending = LegalDocumentCatalog.Pending([LegalDocumentType.Terms, LegalDocumentType.Privacy], acceptances);

        pending.Should().Equal(LegalDocumentType.Privacy);
    }

    [Fact]
    public void EnsureAcceptsCurrent_Should_Succeed_And_Ignore_NotRequired_Documents()
    {
        var submitted = new[]
        {
            new LegalDocumentAcceptanceInput(LegalDocumentType.Terms, "0.1"),
            new LegalDocumentAcceptanceInput(LegalDocumentType.Privacy, " 0.1 "),
            new LegalDocumentAcceptanceInput(LegalDocumentType.DataProcessing, "0.0"),
        };

        LegalDocumentCatalog.EnsureAcceptsCurrent([LegalDocumentType.Terms, LegalDocumentType.Privacy], submitted)
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureAcceptsCurrent_Should_Return_Required_When_Document_Missing()
    {
        var result = LegalDocumentCatalog.EnsureAcceptsCurrent(
            [LegalDocumentType.Terms, LegalDocumentType.Privacy],
            [new LegalDocumentAcceptanceInput(LegalDocumentType.Terms, "0.1")]);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LegalAcceptances.Required");
    }

    [Fact]
    public void EnsureAcceptsCurrent_Should_Return_Required_When_Nothing_Submitted()
    {
        LegalDocumentCatalog.EnsureAcceptsCurrent([LegalDocumentType.Terms], null)
            .Error.Code.Should().Be("LegalAcceptances.Required");
    }

    [Fact]
    public void EnsureAcceptsCurrent_Should_Return_VersionOutdated_When_Version_Not_Current()
    {
        var result = LegalDocumentCatalog.EnsureAcceptsCurrent(
            [LegalDocumentType.Terms],
            [new LegalDocumentAcceptanceInput(LegalDocumentType.Terms, "0.0")]);

        result.Error.Code.Should().Be("LegalAcceptances.VersionOutdated");
    }

    [Fact]
    public void EnsureAcceptsCurrent_Should_Succeed_When_Nothing_Required()
    {
        LegalDocumentCatalog.EnsureAcceptsCurrent([], null).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CreateAcceptances_Should_Use_Current_Versions()
    {
        var rows = LegalDocumentCatalog.CreateAcceptances(
            Guid.NewGuid(), Guid.NewGuid(), [LegalDocumentType.Terms, LegalDocumentType.DataProcessing], DateTime.UtcNow);

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.Version == LegalDocumentCatalog.CurrentVersion(r.DocumentType));
    }
}
