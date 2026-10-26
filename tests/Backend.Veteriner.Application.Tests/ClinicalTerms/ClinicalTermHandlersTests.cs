using Backend.Veteriner.Application.ClinicalTerms.Commands.Create;
using Backend.Veteriner.Application.ClinicalTerms.Commands.SetActive;
using Backend.Veteriner.Application.ClinicalTerms.Commands.Update;
using Backend.Veteriner.Application.ClinicalTerms.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Catalog;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.ClinicalTerms;

public sealed class ClinicalTermHandlersTests
{
    private static readonly string ValidRowVersion = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 7 });

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IRepository<ClinicalTerm>> _terms = new();

    public ClinicalTermHandlersTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _terms.Setup(r => r.AddAsync(It.IsAny<ClinicalTerm>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicalTerm t, CancellationToken _) => t);
        _terms.Setup(r => r.UpdateAsync(It.IsAny<ClinicalTerm>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _terms.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        NameTaken(false);
    }

    private void NameTaken(bool taken)
        => _terms.Setup(r => r.AnyAsync(It.IsAny<ClinicalTermNameTakenSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(taken);

    private ClinicalTerm Existing(ClinicalTerm term)
    {
        _terms.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicalTermForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(term);
        _terms.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicalTermVisibleByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(term);
        return term;
    }

    private ClinicalTerm TenantTerm(string name = "Otit")
        => Existing(ClinicalTerm.CreateForTenant(_tenantId, ClinicalTermKind.Diagnosis, name));

    private CreateClinicalTermCommandHandler CreateHandler() => new(_tenantContext.Object, _terms.Object);
    private UpdateClinicalTermCommandHandler UpdateHandler() => new(_tenantContext.Object, _terms.Object);
    private SetClinicalTermActiveCommandHandler SetActiveHandler() => new(_tenantContext.Object, _terms.Object);

    private static DbUpdateException UniqueViolation()
        => new("save failed", new Exception(
            "Cannot insert duplicate key row in object 'dbo.ClinicalTerms' with unique index 'IX_ClinicalTerms_TenantId_Kind_NormalizedName'."));

    [Fact]
    public async Task Create_Should_Add_Tenant_Term()
    {
        ClinicalTerm? added = null;
        _terms.Setup(r => r.AddAsync(It.IsAny<ClinicalTerm>(), It.IsAny<CancellationToken>()))
            .Callback<ClinicalTerm, CancellationToken>((t, _) => added = t)
            .ReturnsAsync((ClinicalTerm t, CancellationToken _) => t);

        var result = await CreateHandler().Handle(
            new CreateClinicalTermCommand(ClinicalTermKind.Complaint, " Kanlı  ishal "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Kanlı ishal");
        result.Value.Kind.Should().Be(ClinicalTermKind.Complaint);
        result.Value.IsSystem.Should().BeFalse();
        result.Value.IsActive.Should().BeTrue();
        added!.TenantId.Should().Be(_tenantId);
        added.NormalizedName.Should().Be("kanli ishal");
    }

    [Fact]
    public async Task Create_Should_Return_Duplicate_When_Name_Exists_In_Visible_List()
    {
        NameTaken(true);

        var result = await CreateHandler().Handle(
            new CreateClinicalTermCommand(ClinicalTermKind.Complaint, "KUSMA"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ClinicalTerms.DuplicateName");
        _terms.Verify(r => r.AddAsync(It.IsAny<ClinicalTerm>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_Map_Unique_Index_Race_To_Duplicate()
    {
        _terms.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(UniqueViolation());

        var result = await CreateHandler().Handle(
            new CreateClinicalTermCommand(ClinicalTermKind.Complaint, "Kusma"), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalTerms.DuplicateName");
    }

    [Fact]
    public async Task Create_Should_Fail_Without_Tenant_Context()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            new CreateClinicalTermCommand(ClinicalTermKind.Complaint, "Kusma"), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Update_Should_Rename_With_Expected_RowVersion()
    {
        var term = TenantTerm();

        var result = await UpdateHandler().Handle(
            new UpdateClinicalTermCommand(term.Id, "Otitis externa", ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Otitis externa");
        term.RowVersion.Should().Equal(Convert.FromBase64String(ValidRowVersion));
        _terms.Verify(r => r.UpdateAsync(term, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_Return_NotFound_When_Missing()
    {
        _terms.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicalTermForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicalTerm?)null);

        var result = await UpdateHandler().Handle(
            new UpdateClinicalTermCommand(Guid.NewGuid(), "X", ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalTerms.NotFound");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64")]
    public async Task Update_Should_Require_Valid_RowVersion(string? rowVersion)
    {
        var term = TenantTerm();

        var result = await UpdateHandler().Handle(
            new UpdateClinicalTermCommand(term.Id, "Otitis externa", rowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalTerms.Validation");
        _terms.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Reject_System_Term()
    {
        var term = Existing(ClinicalTerm.CreateSystem(ClinicalTermKind.Diagnosis, "Gastroenterit"));

        var result = await UpdateHandler().Handle(
            new UpdateClinicalTermCommand(term.Id, "Gastroenterit (akut)", ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalTerms.SystemTermReadOnly");
        _terms.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Return_Duplicate_When_New_Name_Taken()
    {
        var term = TenantTerm();
        NameTaken(true);

        var result = await UpdateHandler().Handle(
            new UpdateClinicalTermCommand(term.Id, "Gastroenterit", ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalTerms.DuplicateName");
        _terms.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Map_Concurrency_To_Conflict()
    {
        var term = TenantTerm();
        _terms.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await UpdateHandler().Handle(
            new UpdateClinicalTermCommand(term.Id, "Otitis externa", ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalTerms.ConcurrencyConflict");
    }

    [Fact]
    public async Task Deactivate_Should_Save_Tenant_Term()
    {
        var term = TenantTerm();

        var result = await SetActiveHandler().Handle(new SetClinicalTermActiveCommand(term.Id, false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        term.IsActive.Should().BeFalse();
        _terms.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deactivate_Should_Reject_System_Term()
    {
        var term = Existing(ClinicalTerm.CreateSystem(ClinicalTermKind.Complaint, "Kusma"));

        var result = await SetActiveHandler().Handle(new SetClinicalTermActiveCommand(term.Id, false), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalTerms.SystemTermReadOnly");
        term.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SetActive_Should_Return_NotFound_When_Missing()
    {
        _terms.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicalTermVisibleByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicalTerm?)null);

        var result = await SetActiveHandler().Handle(new SetClinicalTermActiveCommand(Guid.NewGuid(), true), CancellationToken.None);

        result.Error.Code.Should().Be("ClinicalTerms.NotFound");
    }

    [Fact]
    public void Search_Should_Build_Turkish_Insensitive_Word_Patterns()
    {
        ClinicalTermSearch.BuildTokenPatterns("  KANLI   İshal ").Should().Equal("%kanli%", "%ishal%");
        ClinicalTermSearch.BuildTokenPatterns("100%_[x]").Should().Equal("%100[%][_][[]x]%");
        ClinicalTermSearch.BuildTokenPatterns("   ").Should().BeEmpty();
        ClinicalTermSearch.BuildTokenPatterns("a b c d e f g").Should().HaveCount(ClinicalTermSearch.MaxTokenCount);
    }
}
