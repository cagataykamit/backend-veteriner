using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.LegalAcceptances.Queries.GetCurrentVersions;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Domain.Legal;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.OwnerPortal;

/// <summary>ADR-031 K4: portal belgeleri ADR-029 kataloğunda, personel kuralı ve listesi değişmeden; izin yönetici düzeyi.</summary>
public sealed class PortalLegalCatalogTests
{
    [Fact]
    public void Portal_Documents_Should_Be_Draft_0_1()
    {
        LegalDocumentCatalog.RequiredForOwnerAccount.Should().Equal(LegalDocumentType.PortalTerms, LegalDocumentType.PortalPrivacy);
        LegalDocumentCatalog.CurrentVersion(LegalDocumentType.PortalTerms).Should().Be("0.1");
        LegalDocumentCatalog.CurrentVersion(LegalDocumentType.PortalPrivacy).Should().Be("0.1");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Veteriner")]
    public void Staff_Requirement_Should_Not_Include_Portal_Documents(string claim)
        => LegalDocumentCatalog.RequiredFor([claim]).Should()
            .NotContain([LegalDocumentType.PortalTerms, LegalDocumentType.PortalPrivacy]);

    [Fact]
    public async Task Public_Staff_List_Should_Stay_Staff_Only_And_Portal_List_Portal_Only()
    {
        var handler = new GetCurrentLegalDocumentVersionsQueryHandler();

        (await handler.Handle(new GetCurrentLegalDocumentVersionsQuery(), CancellationToken.None))
            .Select(d => d.DocumentType)
            .Should().Equal(LegalDocumentType.Terms, LegalDocumentType.Privacy, LegalDocumentType.DataProcessing);
        (await handler.Handle(new GetCurrentLegalDocumentVersionsQuery(OwnerPortal: true), CancellationToken.None))
            .Select(d => d.DocumentType)
            .Should().Equal(LegalDocumentType.PortalTerms, LegalDocumentType.PortalPrivacy);
    }

    [Fact]
    public void Owner_Acceptances_Should_Carry_OwnerAccount_And_Current_Version()
    {
        var ownerAccountId = Guid.NewGuid();

        var rows = LegalDocumentCatalog.CreateOwnerAccountAcceptances(
            ownerAccountId, LegalDocumentCatalog.RequiredForOwnerAccount, DateTime.UtcNow);

        rows.Should().HaveCount(2).And.OnlyContain(r => r.OwnerAccountId == ownerAccountId && r.UserId == null && r.Version == "0.1");
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Owner", true)]
    [InlineData("ClinicAdmin", true)]
    [InlineData("Veteriner", false)]
    [InlineData("Sekreter", false)]
    public void OwnerPortal_Manage_Should_Be_Bound_To_Admin_Level_Roles_Only(string role, bool expected)
        => RolePermissionBindings.Map[role].Contains(PermissionCatalog.OwnerPortal.Manage).Should().Be(expected);
}
