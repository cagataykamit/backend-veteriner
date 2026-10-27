using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Catalog;

public sealed class ClinicalTermTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void CreateForTenant_Should_Collapse_Whitespace_And_Store_Turkish_Key()
    {
        var term = ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Diagnosis, "  KULAK   İltihabı ", Now);

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
        var term = ClinicalTerm.CreateSystem(ClinicalTermKind.Complaint, "Kusma", Now);

        term.TenantId.Should().BeNull();
        term.IsSystem.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Reject_Empty_Name(string name)
    {
        var act = () => ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Complaint, name, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Should_Reject_Too_Long_Name_And_Undefined_Kind()
    {
        var tooLong = () => ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Complaint, new string('a', ClinicalTerm.MaxNameLength + 1), Now);
        var badKind = () => ClinicalTerm.CreateForTenant(TenantId, (ClinicalTermKind)99, "Kusma", Now);
        var emptyTenant = () => ClinicalTerm.CreateForTenant(Guid.Empty, ClinicalTermKind.Complaint, "Kusma", Now);

        tooLong.Should().Throw<ArgumentException>();
        badKind.Should().Throw<ArgumentException>();
        emptyTenant.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_Should_Update_Name_Key_And_Timestamp()
    {
        var term = ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Diagnosis, "Otit", Now);

        var result = term.Rename("Otitis externa", Now);

        result.IsSuccess.Should().BeTrue();
        term.Name.Should().Be("Otitis externa");
        term.NormalizedName.Should().Be("otitis externa");
        term.UpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void Rename_Should_Fail_On_Empty_Name_Without_Change()
    {
        var term = ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Diagnosis, "Otit", Now);

        var result = term.Rename("  ", Now);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ClinicalTerms.Validation");
        term.Name.Should().Be("Otit");
    }

    [Fact]
    public void Deactivate_And_Activate_Should_Toggle_Tenant_Term()
    {
        var term = ClinicalTerm.CreateForTenant(TenantId, ClinicalTermKind.Complaint, "Kusma", Now);

        term.Deactivate(Now).IsSuccess.Should().BeTrue();
        term.IsActive.Should().BeFalse();
        term.Deactivate(Now).IsSuccess.Should().BeTrue();
        term.Activate(Now).IsSuccess.Should().BeTrue();
        term.IsActive.Should().BeTrue();
    }

    [Fact]
    public void System_Term_Should_Be_Read_Only()
    {
        var term = ClinicalTerm.CreateSystem(ClinicalTermKind.Complaint, "Kusma", Now);

        term.Rename("Kusma (akut)", Now).Error.Code.Should().Be("ClinicalTerms.SystemTermReadOnly");
        term.Deactivate(Now).Error.Code.Should().Be("ClinicalTerms.SystemTermReadOnly");
        term.Activate(Now).Error.Code.Should().Be("ClinicalTerms.SystemTermReadOnly");
        term.Name.Should().Be("Kusma");
        term.IsActive.Should().BeTrue();
    }
}
