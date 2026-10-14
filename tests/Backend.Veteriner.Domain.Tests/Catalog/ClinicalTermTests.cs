using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Catalog;

public sealed class ClinicalTermTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void CreateForTenant_Should_Collapse_Whitespace_And_Store_Turkish_Key()
    {
        var term = ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Diagnosis, "  KULAK   İltihabı ");

        term.TenantId.Should().Be(TenantId);
        term.Kind.Should().Be(ClinicalTermKind.Diagnosis);
        term.Name.Should().Be("KULAK İltihabı");
        term.NormalizedName.Should().Be("kulak iltihabi");
        term.IsActive.Should().BeTrue();
        term.IsSystem.Should().BeFalse();
    }

    [Theory]
    [InlineData("IŞIK", "ışık")]
    [InlineData("Çöğüş", "cogus")]
    [InlineData("İshal", "ISHAL")]
    public void Turkish_Variants_Should_Share_The_Same_Key(string a, string b)
        => TurkishTextKey.Normalize(a).Should().Be(TurkishTextKey.Normalize(b));

    [Fact]
    public void CreateSystem_Should_Have_No_Tenant()
    {
        var term = ClinicalTerm.CreateSystem(ClinicalTermKind.Complaint, "Kusma");

        term.TenantId.Should().BeNull();
        term.IsSystem.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Reject_Empty_Name(string name)
    {
        var act = () => ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Complaint, name);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Should_Reject_Too_Long_Name_And_Undefined_Kind()
    {
        var tooLong = () => ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Complaint, new string('a', ClinicalTerm.MaxNameLength + 1));
        var badKind = () => ClinicalTerm.CreateForTenant(TenantId, (ClinicalTermKind)99, "Kusma");
        var emptyTenant = () => ClinicalTerm.CreateForTenant(Guid.Empty, ClinicalTermKind.Complaint, "Kusma");

        tooLong.Should().Throw<ArgumentException>();
        badKind.Should().Throw<ArgumentException>();
        emptyTenant.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_Should_Update_Name_Key_And_Timestamp()
    {
        var term = ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Diagnosis, "Otit");

        var result = term.Rename("Otitis externa");

        result.IsSuccess.Should().BeTrue();
        term.Name.Should().Be("Otitis externa");
        term.NormalizedName.Should().Be("otitis externa");
        term.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Rename_Should_Fail_On_Empty_Name_Without_Change()
    {
        var term = ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Diagnosis, "Otit");

        var result = term.Rename("  ");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ClinicalTerms.Validation");
        term.Name.Should().Be("Otit");
    }

    [Fact]
    public void Deactivate_And_Activate_Should_Toggle_Tenant_Term()
    {
        var term = ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Complaint, "Kusma");

        term.Deactivate().IsSuccess.Should().BeTrue();
        term.IsActive.Should().BeFalse();
        term.Deactivate().IsSuccess.Should().BeTrue();
        term.Activate().IsSuccess.Should().BeTrue();
        term.IsActive.Should().BeTrue();
    }

    [Fact]
    public void System_Term_Should_Be_Read_Only()
    {
        var term = ClinicalTerm.CreateSystem(ClinicalTermKind.Complaint, "Kusma");

        term.Rename("Kusma (akut)").Error.Code.Should().Be("ClinicalTerms.SystemTermReadOnly");
        term.Deactivate().Error.Code.Should().Be("ClinicalTerms.SystemTermReadOnly");
        term.Activate().Error.Code.Should().Be("ClinicalTerms.SystemTermReadOnly");
        term.Name.Should().Be("Kusma");
        term.IsActive.Should().BeTrue();
    }
}
