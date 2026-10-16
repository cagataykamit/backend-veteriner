using Backend.Veteriner.Domain.Legal;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Legal;

public sealed class LegalAcceptanceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Record_Should_Store_User_Tenant_Document_TrimmedVersion_And_UtcTime()
    {
        var at = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);

        var acceptance = LegalAcceptance.Record(UserId, TenantId, LegalDocumentType.DataProcessing, " 0.1 ", at);

        acceptance.Id.Should().NotBeEmpty();
        acceptance.UserId.Should().Be(UserId);
        acceptance.TenantId.Should().Be(TenantId);
        acceptance.DocumentType.Should().Be(LegalDocumentType.DataProcessing);
        acceptance.Version.Should().Be("0.1");
        acceptance.AcceptedAtUtc.Should().Be(at);
        acceptance.AcceptedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Record_Should_Create_NewRow_Each_Time()
    {
        var now = DateTime.UtcNow;

        var first = LegalAcceptance.Record(UserId, TenantId, LegalDocumentType.Terms, "0.1", now);
        var second = LegalAcceptance.Record(UserId, TenantId, LegalDocumentType.Terms, "0.1", now);

        second.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public void Record_Should_Throw_When_UserId_Empty()
    {
        var act = () => LegalAcceptance.Record(Guid.Empty, TenantId, LegalDocumentType.Terms, "0.1", DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_Should_Throw_When_TenantId_Empty()
    {
        var act = () => LegalAcceptance.Record(UserId, Guid.Empty, LegalDocumentType.Terms, "0.1", DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("012345678901234567890")]
    public void Record_Should_Throw_When_Version_Invalid(string version)
    {
        var act = () => LegalAcceptance.Record(UserId, TenantId, LegalDocumentType.Terms, version, DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_Should_Throw_When_DocumentType_Undefined()
    {
        var act = () => LegalAcceptance.Record(UserId, TenantId, (LegalDocumentType)99, "0.1", DateTime.UtcNow);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
