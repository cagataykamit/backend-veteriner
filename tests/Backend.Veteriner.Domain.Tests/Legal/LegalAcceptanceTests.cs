using Backend.Veteriner.Domain.Legal;
using AwesomeAssertions;

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

    [Fact]
    public void RecordForOwnerAccount_Should_Store_OwnerAccount_Without_User_And_Tenant()
    {
        var ownerAccountId = Guid.NewGuid();
        var at = new DateTime(2026, 10, 16, 9, 30, 0, DateTimeKind.Utc);

        var acceptance = LegalAcceptance.RecordForOwnerAccount(ownerAccountId, LegalDocumentType.PortalTerms, "0.1", at);

        acceptance.OwnerAccountId.Should().Be(ownerAccountId);
        acceptance.UserId.Should().BeNull();
        acceptance.TenantId.Should().BeNull();
        acceptance.DocumentType.Should().Be(LegalDocumentType.PortalTerms);
        acceptance.AcceptedAtUtc.Should().Be(at);
    }

    [Fact]
    public void Record_Should_Not_Set_OwnerAccount()
        => LegalAcceptance.Record(UserId, TenantId, LegalDocumentType.Terms, "0.1", DateTime.UtcNow).OwnerAccountId.Should().BeNull();

    [Fact]
    public void RecordForOwnerAccount_Should_Throw_When_OwnerAccountId_Empty()
    {
        var act = () => LegalAcceptance.RecordForOwnerAccount(Guid.Empty, LegalDocumentType.PortalPrivacy, "0.1", DateTime.UtcNow);

        act.Should().Throw<ArgumentException>();
    }
}
